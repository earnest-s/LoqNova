using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using LoqNova.Avalonia.Localization;
using LoqNova.Avalonia.ViewModels;
using LoqNova.Lib.Automation.Pipeline;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// The notification-area icon and the window lifecycle that belongs to it.
///
/// This reproduces two behaviours that the Avalonia shell otherwise lacks: WPF's
/// <c>TrayHelper</c> (the icon, its menu and the click-to-restore gesture) and WPF
/// <c>MainWindow</c>'s close/minimise handling, where "Minimize to tray" hides a
/// minimised window and "Minimize on close" turns the window's close button into a
/// hide. The tray menu's Close item is what guarantees a way out, exactly as in WPF.
/// </summary>
public class TrayService : ITrayService
{
    private const string IconUri = "avares://LOQ Nova/Assets/icon.ico";

    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private readonly IAutomationService _automationService;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly ILogger<TrayService> _logger;

    private Window? _mainWindow;
    private TrayIcon? _trayIcon;
    private NativeMenu? _menu;

    /// <summary>The automation quick actions currently in the menu, so they can be replaced.</summary>
    private readonly List<NativeMenuItemBase> _automationItems = new();

    /// <summary>
    /// Set while the tray's Close item is shutting the application down. The window's
    /// Closing handler must not cancel that close, or "Minimize on close" would make the
    /// application impossible to exit from the tray.
    /// </summary>
    private bool _isExiting;

    /// <summary>True while the window is hidden in the notification area.</summary>
    private bool _isHiddenToTray;

    public TrayService(
        ISettingsService settingsService,
        INavigationService navigationService,
        IAutomationService automationService,
        IMainThreadDispatcher dispatcher,
        ILogger<TrayService> logger)
    {
        _settingsService = settingsService;
        _navigationService = navigationService;
        _automationService = automationService;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public Task InitializeAsync(Window mainWindow)
    {
        _mainWindow = mainWindow;
        _menu = new NativeMenu();

        AddNavigationItems(_menu);
        _menu.Items.Add(new NativeMenuItemSeparator());

        var open = new NativeMenuItem(LocalizationHelper.T("Open"));
        open.Click += (_, _) => ShowMainWindow();
        _menu.Items.Add(open);

        var close = new NativeMenuItem(LocalizationHelper.T("Close"));
        close.Click += (_, _) => ExitApplication();
        _menu.Items.Add(close);

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri(IconUri))),
            ToolTipText = LocalizationHelper.T("AppName"),
            IsVisible = true,
            Menu = _menu
        };

        // WPF's NotifyIcon.OnClick brings the window forward.
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        if (Application.Current is { } application)
            TrayIcon.SetIcons(application, new TrayIcons { _trayIcon });

        // WPF wires these two in MainWindow's constructor.
        mainWindow.Closing += OnMainWindowClosing;
        mainWindow.PropertyChanged += OnMainWindowPropertyChanged;

        // WPF's TrayHelper rebuilds its automation block from the processor's events.
        _automationService.PipelinesReloaded += RebuildAutomationItems;
        _automationService.PipelineEdited += OnPipelineEdited;

        RebuildAutomationItems();

        return Task.CompletedTask;
    }

    /// <summary>
    /// WPF's tray menu is built from its navigation store, so the tray offers the same
    /// destinations as the sidebar. The labels come from the shell view model for the
    /// same reason: there is one list of destinations, not two that can drift apart.
    /// </summary>
    private void AddNavigationItems(NativeMenu menu)
    {
        foreach (var destination in NavigationItems())
        {
            var page = destination.Page;
            var menuItem = new NativeMenuItem(destination.Label);
            menuItem.Click += (_, _) => NavigateFromTray(page);

            menu.Items.Add(menuItem);
        }
    }

    private IEnumerable<NavigationItemViewModel> NavigationItems()
    {
        if (_mainWindow?.DataContext is MainWindowViewModel mainWindowViewModel)
            return mainWindowViewModel.NavigationItems.Where(static item => item.IsVisible);

        return Array.Empty<NavigationItemViewModel>();
    }

    private async void NavigateFromTray(NavigationPage page)
    {
        try
        {
            // WPF brings the window forward before navigating; it needs a delay there
            // because its navigation requires a realised window. Navigation here needs
            // only the window to be visible.
            ShowMainWindow();
            await _navigationService.NavigateToAsync(page);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray navigation to {Page} failed", page);
        }
    }

    /// <summary>
    /// Rebuilds the quick actions (the pipelines with no trigger) from the backend.
    /// WPF's TrayHelper does the same on every pipeline change.
    /// </summary>
    private void RebuildAutomationItems()
    {
        var menu = _menu;
        if (menu is null)
            return;

        foreach (var item in _automationItems)
            menu.Items.Remove(item);

        _automationItems.Clear();

        List<AutomationPipeline> pipelines;

        try
        {
            pipelines = [.. _automationService.ManualPipelines];
        }
        catch (Exception ex)
        {
            // The service is not initialized yet during startup; the reload event will
            // ask again once it is.
            _logger.LogDebug(ex, "Tray: automation quick actions are not available yet");
            return;
        }

        if (pipelines.Count == 0)
            return;

        foreach (var pipeline in pipelines)
        {
            var target = pipeline;
            var item = new NativeMenuItem(target.Name ?? LocalizationHelper.T("Unnamed"));

            item.Click += async (_, _) =>
            {
                try
                {
                    await _automationService.RunNowAsync(target);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Tray: running automation '{Name}' failed", target.Name);
                }
            };

            _automationItems.Add(item);
        }

        // WPF puts the automation block ahead of the navigation items, followed by a
        // separator, then the navigation items.
        _automationItems.Add(new NativeMenuItemSeparator());

        for (var index = 0; index < _automationItems.Count; index++)
            menu.Items.Insert(index, _automationItems[index]);
    }

    private void OnPipelineEdited(AutomationPipeline pipeline) => RebuildAutomationItems();

    /// <summary>
    /// Brings the window back from the notification area. This is WPF's
    /// <c>BringToForeground</c>: show it if it was hidden, restore it if it was
    /// minimised, then activate it.
    /// </summary>
    public void ShowMainWindow()
    {
        // Callers include the shared library's key listener and the tray menu, so this
        // has to be safe off the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(ShowMainWindow);
            return;
        }

        var window = _mainWindow;
        if (window is null)
            return;

        if (!window.IsVisible)
            window.Show();

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        _isHiddenToTray = false;
        window.Activate();
    }

    /// <summary>
    /// WPF's <c>MainWindow.SendToTray</c>. Hides the window only when the user asked for
    /// it, so minimising with the option off keeps the normal taskbar behaviour.
    /// </summary>
    private void HideToTray()
    {
        var window = _mainWindow;
        if (window is null || !ReadMinimizeToTray())
            return;

        window.Hide();
        _isHiddenToTray = true;
    }

    private void OnMainWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty)
            return;

        var window = _mainWindow;
        if (window is null)
            return;

        switch (window.WindowState)
        {
            case WindowState.Minimized:
                // WPF's MainWindow_StateChanged -> SendToTray, which does nothing unless
                // "Minimize to tray" is on.
                HideToTray();
                break;

            case WindowState.Normal:
                // WPF's BringToForeground on restore. Activating is enough here; the
                // window is already shown by whichever gesture restored it.
                if (_isHiddenToTray)
                {
                    _isHiddenToTray = false;
                    window.Activate();
                }

                break;
        }
    }

    /// <summary>
    /// WPF's <c>MainWindow_Closing</c>. With "Minimize on close" on, the close is
    /// cancelled and the window is minimised; the minimise then goes to the tray when
    /// "Minimize to tray" is on too. With the option off the close proceeds and the
    /// application shuts down normally.
    /// </summary>
    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isExiting)
            return;

        if (!ReadMinimizeOnClose())
            return;

        e.Cancel = true;

        if (_mainWindow is { } window)
            window.WindowState = WindowState.Minimized;
    }

    private bool ReadMinimizeToTray() =>
        ReadSetting(static service => service.MinimizeToTray, nameof(ISettingsService.MinimizeToTray));

    private bool ReadMinimizeOnClose() =>
        ReadSetting(static service => service.MinimizeOnClose, nameof(ISettingsService.MinimizeOnClose));

    /// <summary>
    /// Reads a setting without letting a failure escape into the window's event handling.
    /// A failed read is treated as "off", which is the behaviour that cannot trap the
    /// user in a window that refuses to close.
    /// </summary>
    private bool ReadSetting(Func<ISettingsService, bool> read, string name)
    {
        try
        {
            return read(_settingsService);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray: could not read {Setting}; treating it as off", name);
            return false;
        }
    }

    /// <summary>
    /// The tray menu's Close item. WPF calls <c>App.ShutdownAsync()</c> here; the
    /// Avalonia equivalent is the desktop lifetime's Shutdown, which raises the
    /// application's own ShutdownRequested hook so the RGB, macro, IPC and HWiNFO
    /// teardown still runs.
    /// </summary>
    private void ExitApplication()
    {
        _isExiting = true;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            _mainWindow?.Close();
    }

    public Task UpdateTooltipAsync(string tooltip)
    {
        if (_trayIcon is not null)
            _trayIcon.ToolTipText = tooltip;

        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        if (_mainWindow is not null)
        {
            _mainWindow.Closing -= OnMainWindowClosing;
            _mainWindow.PropertyChanged -= OnMainWindowPropertyChanged;
        }

        _automationService.PipelinesReloaded -= RebuildAutomationItems;
        _automationService.PipelineEdited -= OnPipelineEdited;

        _trayIcon?.Dispose();
        _trayIcon = null;
        _menu = null;

        return Task.CompletedTask;
    }
}
