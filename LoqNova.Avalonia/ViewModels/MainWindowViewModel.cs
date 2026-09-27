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
    private readonly INavigationService _navigationService;
    private readonly ISettingsService _settingsService;
    private readonly IPerformanceService _performanceService;
    private readonly IRgbService _rgbService;
    private readonly IThermalService _thermalService;
    private readonly IBatteryService _batteryService;

    [ObservableProperty]
    private string _deviceModel = "LOQ 15IRH8";

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
        IBatteryService batteryService)
    {
        _navigationService = navigationService;
        _settingsService = settingsService;
        _performanceService = performanceService;
        _rgbService = rgbService;
        _thermalService = thermalService;
        _batteryService = batteryService;
        
        InitializeNavigationItems();
        _navigationService.PageChanged += OnPageChanged;
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
    
    [RelayCommand]
    private void Navigate(NavigationPage page)
    {
        _ = _navigationService.NavigateToAsync(page);
    }

    private static Window? HostWindow =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

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