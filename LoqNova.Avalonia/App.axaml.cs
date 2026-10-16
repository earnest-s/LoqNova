using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LoqNova.Avalonia.Converters;
using LoqNova.Avalonia.Localization;
using LoqNova.Avalonia.Services;
using LoqNova.Avalonia.ViewModels;
using LoqNova.Avalonia.ViewModels.Pages;
using LoqNova.Lib.Macro;
using LoqNova.Avalonia.ViewModels.Dialogs;
using LoqNova.Avalonia.Views;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia;

public partial class App : Application
{
    public static IContainer? Container { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        // A faulted background task would otherwise be swallowed and, in the case of an
        // async void command, take the whole process down with no explanation. The
        // Automation page's editor operations catch their own failures and report them;
        // this is the backstop for anything that does not.
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        // LoqNova.Lib keeps its own Autofac container and is the single source of
        // truth for controllers, features and settings, so the same backend
        // singletons WPF uses are reused here. It is started on a background
        // thread because the library auto-activates Windows message listeners that
        // need Avalonia's dispatcher to be pumping; see LibContainer.Initialize.
        // LoqNova.Lib.Automation's module is registered with the container below and
        // its processor is initialised once the readiness gate has passed.
        LibContainer.Initialize();

        // Build DI container
        var builder = new ContainerBuilder();
        
        // The real automation engine's own registrations: AutomationProcessor and
        // AutomationSettings. Registered as a module rather than by hand so the backend
        // stays the single source of truth for its container setup. These are only
        // resolved after LibContainer.Initialization, never during construction.

        
        // Register IServiceProvider adapter
        builder.Register<IServiceProvider>(c => new AutofacServiceProvider(c.Resolve<ILifetimeScope>())).SingleInstance();
        
        // Register converters
        builder.RegisterType<BoolToVisibilityConverter>().SingleInstance();
        builder.RegisterType<EnumToDisplayConverter>().SingleInstance();
        builder.RegisterType<RgbZoneColorToBrushConverter>().SingleInstance();
        
        // Register core services
        builder.RegisterType<ThemeService>().As<IThemeService>().SingleInstance();
        builder.RegisterType<NavigationService>().As<INavigationService>().SingleInstance();
        builder.RegisterType<NotificationService>().As<INotificationService>().SingleInstance();
        builder.RegisterType<TrayService>().As<ITrayService>().SingleInstance();
        builder.RegisterType<FileDialogService>().As<IFileDialogService>().SingleInstance();
        builder.RegisterType<MainThreadDispatcher>().As<IMainThreadDispatcher>().SingleInstance();
        // Watches the pointer over the notification-area icon and shows the status
        // popup, replacing the shell tooltip WPF does without (see TrayHoverService).
        builder.RegisterType<TrayHoverService>().AsSelf().SingleInstance();
        
        // Register logging. The factory is owned by the container: the previous
        // registration created it inside a `using` and returned a logger for a
        // disposed factory, and no ILoggerFactory was registered, so injecting
        // ILogger<T> could not be satisfied.
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(logging =>
        {
            logging.AddDebug();
            logging.AddConsole();
            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });

        builder.RegisterInstance(loggerFactory).SingleInstance();

        builder.RegisterGeneric(typeof(Microsoft.Extensions.Logging.Logger<>))
               .As(typeof(Microsoft.Extensions.Logging.ILogger<>))
               .SingleInstance();

        // Real adapters over the existing library controllers. Nothing is
        // synthesised: power mode, battery modes, sensors and settings are read
        // from and written to the machine / persisted configuration.
        builder.RegisterType<PerformanceService>().As<IPerformanceService>().SingleInstance();
        // Shared dashboard.json, same store shape as the WPF front end.
        builder.RegisterType<LoqNova.Avalonia.Settings.DashboardSettings>().SingleInstance();
        builder.RegisterType<SensorsService>().As<ISensorsService>().SingleInstance();
        builder.RegisterType<ThermalService>().As<IThermalService>().SingleInstance();
        builder.RegisterType<BatteryService>().As<IBatteryService>().SingleInstance();

        // These two resolve their library singletons inside InitializeAsync, after
        // LibContainer.Initialization. They are deliberately never constructed
        // through IoCContainer.Resolve on the UI thread: IoCContainer.Initialize
        // holds a global lock across Build(), and Build() waits for this thread to
        // pump queued listener callbacks, so resolving from here would deadlock.
        builder.RegisterType<RgbService>().As<IRgbService>().SingleInstance();
        builder.RegisterType<SettingsService>().As<ISettingsService>().SingleInstance();

        // Automation and Macro still have Avalonia-invented contracts that do not
        // match the library models; they are replaced when those pages are ported.

        builder.RegisterType<RealMacroService>().As<IMacroService>().SingleInstance();

        // The notification relay and the Smart Key listener take their library singletons
        // from the shared container, so they are built by a factory instead of being
        // resolved through IoCContainer directly. Both are started only after the
        // readiness gate, which is why constructing them lazily is safe.
        builder.Register(context => new NotificationRelay(
                context.Resolve<INotificationService>(),
                LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.Settings.ApplicationSettings>(),
                context.Resolve<IMainThreadDispatcher>(),
                context.Resolve<ILogger<NotificationRelay>>()))
            .As<INotificationRelay>()
            .SingleInstance();

        builder.Register(context => new SmartKeyService(
                LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.Settings.ApplicationSettings>(),
                LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.SoftwareDisabler.FnKeysDisabler>(),
                LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.Listeners.SpecialKeyListener>(),
                LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.Automation.AutomationProcessor>(),
                context.Resolve<ILogger<SmartKeyService>>()))
            .As<ISmartKeyService>()
            .SingleInstance();

        // Real package downloader (Packages page) straight from LoqNova.Lib.
        // No mock and no local HTTP/API layer: PackagesViewModel resolves the
        // existing PackageDownloaderFactory and calls IPackageDownloader. The
        // factory takes the concrete downloader types, so they are registered as
        // themselves rather than behind the interface. These construct directly
        // rather than through IoCContainer, for the reason described above.
        builder.RegisterType<LoqNova.Lib.HttpClientFactory>().SingleInstance();
        builder.RegisterType<LoqNova.Lib.Settings.PackageDownloaderSettings>().SingleInstance();
        builder.RegisterType<LoqNova.Lib.PackageDownloader.PCSupportPackageDownloader>().SingleInstance();
        builder.RegisterType<LoqNova.Lib.PackageDownloader.VantagePackageDownloader>().SingleInstance();
        builder.RegisterType<LoqNova.Lib.PackageDownloader.PackageDownloaderFactory>().SingleInstance();
        
        // Register ViewModels
        builder.RegisterType<MainWindowViewModel>().SingleInstance();
        // SingleInstance, not InstancePerDependency: DashboardViewModel subscribes to
        // PerformanceService.ModeChanged and SensorsService.Updated/SupportChanged in
        // its constructor for the life of the page VM, and the navigation service
        // builds a fresh page on every visit. A per-dependency instance was created on
        // every navigation and never unsubscribed, so every sensor publish was
        // delivered once per past visit - the telemetry loop grew by one handler per
        // trip back to the Dashboard. Making it shared also means the sweep must come
        // from the view lifecycle rather than from the constructor, so returning to the
        // Dashboard still replays it. See the same decision above for MacroViewModel.
        builder.RegisterType<DashboardViewModel>().SingleInstance();
        builder.RegisterType<KeyboardBacklightViewModel>().InstancePerDependency();
        builder.RegisterType<BatteryViewModel>().InstancePerDependency();
        builder.RegisterType<AutomationViewModel>().InstancePerDependency();
        builder.RegisterType<RealAutomationService>().As<IAutomationService>().SingleInstance();
        // SingleInstance, not InstancePerDependency: MacroViewModel subscribes to the
        // controller's RecorderReceived/RecorderStopped for the life of the page VM. A
        // per-dependency instance was created on every navigation and never unsubscribed,
        // so each recorded key press was delivered once per past visit and written back
        // into the sequence repeatedly.
        builder.RegisterType<MacroViewModel>().SingleInstance();
        builder.RegisterType<PackagesViewModel>().InstancePerDependency();
        builder.RegisterType<SettingsViewModel>().InstancePerDependency();
        builder.RegisterType<AboutViewModel>().InstancePerDependency();
        
        // Register Dialog ViewModels
        // Dialog host. Presented as an overlay inside the main window rather than as
        // separate Windows, so the application keeps a single top-level window and
        // does not prompt for elevation twice.
        builder.RegisterType<DialogService>().As<IDialogService>().SingleInstance();

        builder.RegisterType<CustomModeSettingsViewModel>().InstancePerDependency();
        builder.RegisterType<EditDashboardViewModel>().InstancePerDependency();
        builder.RegisterType<BalanceModeSettingsViewModel>().InstancePerDependency();
        builder.RegisterType<OverclockGpuSettingsViewModel>().InstancePerDependency();
        builder.RegisterType<AddDashboardItemViewModel>().InstancePerDependency();
        builder.RegisterType<ExtendedHybridModeInfoViewModel>().InstancePerDependency();
        builder.RegisterType<CreateAutomationPipelineViewModel>().InstancePerDependency();
        builder.RegisterType<AutomationPipelineTriggerConfigViewModel>().InstancePerDependency();
        builder.RegisterType<AddAutomationStepViewModel>().InstancePerDependency();
        builder.RegisterType<BootLogoViewModel>().InstancePerDependency();
        builder.RegisterType<ExcludeRefreshRatesViewModel>().InstancePerDependency();
        builder.RegisterType<NotificationsSettingsViewModel>().InstancePerDependency();
        builder.RegisterType<SelectSmartKeyPipelinesViewModel>().InstancePerDependency();
        builder.RegisterType<WindowsPowerModesViewModel>().InstancePerDependency();
        builder.RegisterType<WindowsPowerPlansViewModel>().InstancePerDependency();
        builder.RegisterType<DeviceInformationViewModel>().InstancePerDependency();
        builder.RegisterType<LanguageSelectorViewModel>().InstancePerDependency();
        builder.RegisterType<StatusViewModel>().InstancePerDependency();
        builder.RegisterType<UnsupportedViewModel>().InstancePerDependency();
        builder.RegisterType<UpdateViewModel>().InstancePerDependency();
        builder.RegisterType<NotificationViewModel>().InstancePerDependency();
        builder.RegisterType<SpectrumEditEffectViewModel>().InstancePerDependency();
        
        Container = builder.Build();
        AppHost.Initialize(new AutofacServiceProvider(Container));
        
        // Initialize localization
        LocalizationHelper.Initialize();

        // Apply the persisted language before any dialog or resource lookup happens.
        // WPF does the same: the culture is loaded from the shared "lang" file at
        // startup, falling back to English when the file is missing or unknown.
        //
        // Synchronous on purpose. Avalonia's classic desktop lifetime only shows a
        // MainWindow that was assigned before its main loop started, so awaiting here
        // would return from OnFrameworkInitializationCompleted before the window
        // existed and the process would come up with no visible UI at all. See
        // LocalizationHelper.LoadLanguage for why the read is not asynchronous.
        LocalizationHelper.LoadLanguage();
        
        // The theme is applied after the shared library container is ready, because
        // reading the persisted theme goes through ISettingsService, which resolves
        // its backing store from that container.
        Window? shellWindow = null;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Release the library's RGB ownership before the process goes away.
            desktop.ShutdownRequested += OnShutdownRequested;

            // Create main window
            var mainWindow = new MainWindow
            {
                DataContext = Container.Resolve<MainWindowViewModel>()
            };

            // The dialog host is this window's overlay ContentControl. The service is
            // registered as a singleton with no host reference, so the accessor is
            // supplied here once the window exists. Resolving the singleton here is
            // safe: the library container is already up, and this is not a
            // construction path that can run before the readiness gate.
            if (Container.Resolve<IDialogService>() is DialogService dialogs)
            {
                // The service resolves dialogs through this container, and the window
                // supplies the overlay they are presented in.
                dialogs.UseContainer(new AutofacServiceProvider(Container));
                // The window, not the overlay: the service resolves the overlay by name
                // when a dialog is first shown, because the generated field is not
                // populated yet at this point in startup.
                dialogs.Attach(mainWindow);
            }

            desktop.MainWindow = mainWindow;
                shellWindow = mainWindow;
            
            // Initialize tray service
            var trayService = Container.Resolve<ITrayService>();
            await trayService.InitializeAsync(mainWindow);

            // Navigation is deliberately NOT initialized here. Page view models read
            // settings from their constructors (BatteryViewModel reads
            // TemperatureUnitFahrenheit, for example), and SettingsService refuses those
            // reads until InitializeAsync has run. Initializing navigation before the
            // readiness gate made every such page fail to construct, and because the
            // navigate command discarded the task, the failure was silent: the content
            // host was left holding no page at all, which is the near-black screen.
            // Navigation starts after the gate and hydration instead.
            var navigationService = Container.Resolve<INavigationService>();

            // TEMPORARY: navigation diagnostics. Inert unless a marker file exists.
            var diagMarker = Path.Combine(Path.GetTempPath(), "loqnova-navdiag.flag");
            var diagLog = Path.Combine(Path.GetTempPath(), "loqnova-navdiag.log");

            if (File.Exists(diagMarker))
            {
                Services.NavDiag.Reset();
                Services.NavDiag.Enabled = true;
                Services.NavDiag.Log("APP-READY", $"diagnostics enabled marker={diagMarker} log={diagLog}");
            }
        }
        
        base.OnFrameworkInitializationCompleted();

        // The shared library container is built on a background thread because the
        // library's Windows message listeners need Avalonia's dispatcher to be
        // pumping. Now that it is, wait for it to finish before any page resolves a
        // service that needs a library singleton, so no page can observe a
        // half-built container.
        try
        {
            await LibContainer.Initialization;

            // The shared container is up. Hydrate the adapters that read settings or
            // hardware so the shell renders real state on its first paint.
            await Container.Resolve<ISettingsService>().InitializeAsync();
            await Container.Resolve<IThemeService>().InitializeAsync();
            await Container.Resolve<IRgbService>().InitializeAsync();
            await Container.Resolve<IPerformanceService>().InitializeAsync();
            await Container.Resolve<IBatteryService>().InitializeAsync();

            // The tray icon exists from the shell's construction, but its popup reads
            // the power mode and battery adapters, which are only hydrated above; the
            // hover watcher starts here so the first hover shows real state.
            Container.Resolve<TrayHoverService>().Start();

            // The real automation engine, once and only once. It is resolved from the
            // shared container and initialized after the readiness gate, so the
            // IoCContainer lock is never held while awaiting UI-thread-dependent work.
            // InitializeAsync subscribes the native listeners the engine relies on.
            await Container.Resolve<IAutomationService>().InitializeAsync();

            // Matches WPF: pipelines whose trigger matched at launch run once here.
            LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.Automation.AutomationProcessor>()
                .RunOnStartup();

            // Matches WPF's App startup (InitHWiNFOIntegrationAsync / IpcServer): bring
            // up the HWiNFO shared-memory integration and the command-line IPC server
            // when their persisted switches are on. The automation engine is already
            // initialised above, which the IPC server depends on.
            //
            // Each is guarded on its own. Unlike WPF, the shell window already exists at
            // this point, so a native/pipe failure here must not abort the rest of the
            // startup (macro hook, first navigation) and leave an empty window.
            try
            {
                await LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.Integrations.HWiNFOIntegration>()
                    .StartStopIfNeededAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HWiNFO integration start failed: {ex}");
            }

            try
            {
                await LoqNova.Lib.IoCContainer.Resolve<CLI.IpcServer>()
                    .StartStopIfNeededAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IPC server start failed: {ex}");
            }

            // The macro keyboard hook, once for the application lifetime. WPF does this
            // in App.xaml.cs InitMacroController, which resolves the controller and
            // calls Start(); without it WH_KEYBOARD_LL is never installed and physical
            // number-pad keys cannot play macros. Done here rather than in the page so
            // opening or revisiting the Macro page never re-installs it.
            var macroService = Container.Resolve<IMacroService>();
            await macroService.InitializeAsync();
            macroService.StartHook();

            // Notifications. The toast manager is attached to the window here, and the
            // relay is what applies the Notifications settings to the library's messages.
            // Without these two calls nothing in the shell could ever display a toast:
            // the manager was constructed on demand and always left null.
            await Container.Resolve<INotificationService>().InitializeAsync();
            Container.Resolve<INotificationRelay>().Start();

            // Smart Key (Fn+F9). The shared listener deliberately leaves Fn+F9 alone and
            // WPF handles it in its own SmartKeyHelper, so the shell has to own it here;
            // an unconfigured press raises the window, as in WPF.
            var smartKeyService = Container.Resolve<ISmartKeyService>();
            smartKeyService.BringToForeground = () => Container.Resolve<ITrayService>().ShowMainWindow();
            smartKeyService.Start();

            // First navigation happens only now: the shared container is up and every
            // adapter a page view model reads in its constructor has been hydrated.
            var navigation = Container.Resolve<INavigationService>();
            await navigation.InitializeAsync(shellWindow!);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Library container failed to initialize: {ex}");
        }
    }


    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Unobserved task exception: {e.Exception}");

// AppDomain exceptions cannot be marked handled, but on .NET the process is
        // not terminated from this handler, so logging is enough to see the cause.
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Domain unhandled exception: {e.ExceptionObject}");
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Remove the notification-area icon and detach the window's close/minimise
        // handlers. The tray icon would also be disposed by Avalonia's own UI-thread
        // shutdown hook, but doing it here is explicit and keeps the icon from
        // outliving the window it restores.
        // Detach the shell's own listeners before the container goes away: the Smart Key
        // subscription and the notification subscription both outlive the UI otherwise.
        try
        {
            if (Container?.IsRegistered<ISmartKeyService>() == true)
                Container.Resolve<ISmartKeyService>().Stop();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Smart key shutdown failed: {ex}");
        }

        try
        {
            if (Container?.IsRegistered<INotificationRelay>() == true)
                Container.Resolve<INotificationRelay>().Stop();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification relay shutdown failed: {ex}");
        }

        // The hover popup is a window of its own; it is closed before the tray icon
        // it belongs to is removed, so nothing is left on screen during shutdown.
        try
        {
            if (Container?.IsRegistered<TrayHoverService>() == true)
                Container.Resolve<TrayHoverService>().Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tray hover shutdown failed: {ex}");
        }

        try
        {
            if (Container?.IsRegistered<ITrayService>() == true)
                await Container.Resolve<ITrayService>().ShutdownAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tray shutdown failed: {ex}");
        }

        try
        {
            if (Container?.IsRegistered<IRgbService>() == true)
                await Container.Resolve<IRgbService>().ShutdownAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RGB shutdown failed: {ex}");
        }

        // The macro keyboard hook must not outlive the process. WPF does the equivalent
        // in its shutdown list with MacroController.Stop().
        try
        {
            if (Container?.IsRegistered<IMacroService>() == true)
                Container.Resolve<IMacroService>().StopHook();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Macro hook shutdown failed: {ex}");
        }

        // WPF stops the command-line IPC server and the HWiNFO integration in its
        // shutdown list. The server holds a named pipe and a background loop, so it
        // must not outlive the process.
        try
        {
            if (LoqNova.Lib.IoCContainer.TryResolve<CLI.IpcServer>() is { } ipcServer)
                await ipcServer.StopAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"IPC server shutdown failed: {ex}");
        }

        try
        {
            if (LoqNova.Lib.IoCContainer.TryResolve<LoqNova.Lib.Integrations.HWiNFOIntegration>() is { } hwinfo)
                await hwinfo.StopAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HWiNFO integration shutdown failed: {ex}");
        }
    }
}
