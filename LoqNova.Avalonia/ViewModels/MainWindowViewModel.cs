using System.Threading.Tasks;
using System;
using System.Collections.ObjectModel;
using LoqNova.Avalonia.Services;
using LoqNova.Avalonia.ViewModels.Pages;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;

    /// <summary>True while a modal dialog is presented, used to show the scrim.</summary>
    [ObservableProperty]
    private bool _isDialogOpen;

    private readonly INavigationService _navigationService;
    private readonly ISettingsService _settingsService;
    private readonly IPerformanceService _performanceService;
    private readonly IRgbService _rgbService;
    private readonly IThermalService _thermalService;
    private readonly IBatteryService _batteryService;

    [ObservableProperty]
    /// <summary>Machine type as reported by the machine information source, or empty until loaded.</summary>
    private string _deviceModel = "";

    /// <summary>Real product version, read from the running assembly.</summary>
    public string AppVersion { get; } =
        typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    
    [ObservableProperty]
    private bool _isTracing = false;
    
    [ObservableProperty]
    private bool _isVantageActive = true;
    
    [ObservableProperty]
    private bool _isLegionZoneActive = true;
    
    [ObservableProperty]
    private bool _isFnKeysActive = true;
    
    /// <summary>
    /// Single source of truth for navigation. <see cref="PrimaryNavigationItems"/>
    /// and <see cref="FooterNavigationItems"/> are views onto the same
    /// <see cref="NavigationItemViewModel"/> instances, so the sidebar can
    /// split the list visually without duplicating any navigation state.
    /// </summary>
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new();

    public ObservableCollection<NavigationItemViewModel> PrimaryNavigationItems { get; } = new();

    public ObservableCollection<NavigationItemViewModel> FooterNavigationItems { get; } = new();
    
    public MainWindowViewModel(
        INavigationService navigationService,
        ISettingsService settingsService,
        IPerformanceService performanceService,
        IRgbService rgbService,
        IThermalService thermalService,
        IBatteryService batteryService,
        IDialogService dialogService)
    {
        _navigationService = navigationService;
        _settingsService = settingsService;
        _performanceService = performanceService;
        _rgbService = rgbService;
        _thermalService = thermalService;
        _batteryService = batteryService;
        _dialogService = dialogService;

        // A dialog dismisses itself through its own Close command, so the scrim is
        // cleared from the service's notification rather than by the presenter.
        _dialogService.Closed += OnDialogClosed;

        InitializeNavigationItems();
        _navigationService.PageChanged += OnPageChanged;

        _ = LoadDeviceModelAsync();
    }

    /// <summary>
    /// Reads the machine type from the same source WPF uses, rather than assuming
    /// a fixed model.
    /// </summary>
    private async Task LoadDeviceModelAsync()
    {
        try
        {
            var mi = await LoqNova.Lib.Utils.Compatibility.GetMachineInformationAsync();
            DeviceModel = mi.MachineType;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Machine information unavailable: {ex.Message}");
        }
    }
    
    private void InitializeNavigationItems()
    {
        Add(NavigationPage.Dashboard, "Home", "Dashboard");
        Add(NavigationPage.KeyboardBacklight, "Keyboard", "Keyboard");
        Add(NavigationPage.Battery, "Battery", "Battery");
        Add(NavigationPage.Automation, "Automation", "Automation");
        Add(NavigationPage.Macro, "Receipt", "Macros");
        Add(NavigationPage.Packages, "Box", "Packages");
        Add(NavigationPage.Settings, "Settings", "Settings", isFooter: true);
        Add(NavigationPage.About, "Info", "About", isFooter: true);
    }

    private void Add(NavigationPage page, string icon, string label, bool isFooter = false)
    {
        var item = new NavigationItemViewModel
        {
            Page = page,
            Icon = icon,
            Label = label,
            IsVisible = true,
            IsFooter = isFooter
        };

        NavigationItems.Add(item);

        if (isFooter)
        {
            FooterNavigationItems.Add(item);
        }
        else
        {
            PrimaryNavigationItems.Add(item);
        }
    }
    
    private void OnPageChanged(NavigationPage page)
    {
        foreach (var item in NavigationItems)
        {
            item.IsSelected = item.Page == page;
        }
    }
    
/// <summary>
    /// Navigates to a page.
    /// <para>
    /// This used to discard the navigation task with <c>_ =</c>. Navigation resolves the
    /// page view model and constructs the view, and either step can throw; discarding
    /// the task turned every such failure into a silent no-op, leaving the content host
    /// on an empty near-black surface with no exception anywhere. It is awaited and
    /// reported now.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task NavigateAsync(NavigationPage page)
    {
        try
        {
            await _navigationService.NavigateToAsync(page);
        }
        catch (Exception ex)
        {
            Services.NavDiag.LogException($"NAVIGATE-COMMAND-FAILED target={page}", ex);
            System.Diagnostics.Debug.WriteLine($"Navigation to {page} failed: {ex}");
            System.Diagnostics.Debug.WriteLine(ex.ToString());

            OnNavigationFailed?.Invoke(page, ex);
        }
    }

    /// <summary>Raised when a navigation could not be completed.</summary>
    public event Action<NavigationPage, Exception>? OnNavigationFailed;

    private static Window? HostWindow =>
        (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    [RelayCommand]
    private void Minimize()
    {
        if (HostWindow is not null)
        {
            HostWindow.WindowState = WindowState.Minimized;
        }
    }

    [RelayCommand]
    private void ToggleMaximize()
    {
        if (HostWindow is not null)
        {
            HostWindow.WindowState = HostWindow.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }

    [RelayCommand]
    private void Close()
    {
        HostWindow?.Close();
    }

    /// <summary>
    /// Closes the presented dialog. Wired to the dialog service's Closed event so a
    /// dialog can dismiss itself from its own Close command without knowing how it is
    /// hosted.
    /// </summary>
    public void CloseDialog() => _dialogService.Close();

    /// <summary>Clears the scrim once the dialog service reports the dialog is gone.</summary>
    internal void OnDialogClosed() => IsDialogOpen = false;

    /// <summary>
    /// Presents a dialog and loads its state.
    /// <para>
    /// The dialog is shown first so the user sees it immediately, and its state is then
    /// loaded on a background thread. Resolution can construct library controllers, and
    /// <c>IoCContainer.Resolve</c> holds a global lock; those controllers query WMI and
    /// wait on the dispatcher. Doing that on the UI thread while the dispatcher is the
    /// thing being waited on deadlocks the application.
    /// </para>
    /// </summary>
    public async Task ShowDialogAsync(ViewModelBase dialog)
    {
        await _dialogService.ShowAsync(dialog);
        IsDialogOpen = true;

        await Task.Run(async () =>
        {
            try
            {
                await InitializeDialogAsync(dialog);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dialog initialisation failed: {ex}");
            }
        });
    }

    private static Task InitializeDialogAsync(ViewModelBase dialog) => dialog switch
    {
        Dialogs.BalanceModeSettingsViewModel balance => balance.InitializeAsync(),
        Dialogs.CustomModeSettingsViewModel custom => custom.InitializeAsync(),
        Dialogs.OverclockGpuSettingsViewModel overclock => overclock.InitializeAsync(),
        _ => Task.CompletedTask
    };
}

public partial class NavigationItemViewModel : ViewModelBase
{
    public NavigationPage Page { get; init; }
    public string Icon { get; init; } = "";
    public string Label { get; init; } = "";
    public bool IsVisible { get; init; } = true;
    public bool IsFooter { get; init; } = false;
    
    [ObservableProperty]
    private bool _isSelected = false;
}