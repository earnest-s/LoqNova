using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LoqNova.Avalonia.Converters;
using LoqNova.Avalonia.Localization;
using LoqNova.Avalonia.Services;
using LoqNova.Avalonia.ViewModels;
using LoqNova.Avalonia.ViewModels.Pages;
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
        // An unhandled exception on the dispatcher, or on a background task, would end
        // the process with no explanation. This reports it instead of silently killing
        // the app, which is what an async void command did when a backend call threw.
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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
        builder.RegisterModule(new LoqNova.Lib.Automation.IoCModule());
        
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

        builder.RegisterType<MockMacroService>().As<IMacroService>().SingleInstance();

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
        builder.RegisterType<DashboardViewModel>().InstancePerDependency();
        builder.RegisterType<KeyboardBacklightViewModel>().InstancePerDependency();
        builder.RegisterType<BatteryViewModel>().InstancePerDependency();
        builder.RegisterType<AutomationViewModel>().InstancePerDependency();
        builder.RegisterType<RealAutomationService>().As<IAutomationService>().SingleInstance();
        builder.RegisterType<MacroViewModel>().InstancePerDependency();
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
        builder.RegisterType<MacroRecordingViewModel>().InstancePerDependency();
        builder.RegisterType<SpectrumEditEffectViewModel>().InstancePerDependency();
        
        Container = builder.Build();
        AppHost.Initialize(new AutofacServiceProvider(Container));
        
        // Initialize localization
        LocalizationHelper.Initialize();
        
        // The theme is applied after the shared library container is ready, because
        // reading the persisted theme goes through ISettingsService, which resolves
        // its backing store from that container.
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
            
            // Initialize tray service
            var trayService = Container.Resolve<ITrayService>();
            await trayService.InitializeAsync(mainWindow);
            
            // Initialize navigation
            var navigationService = Container.Resolve<INavigationService>();
            await navigationService.InitializeAsync(mainWindow);
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

            // The real automation engine, once and only once. It is resolved from the
            // shared container and initialized after the readiness gate, so the
            // IoCContainer lock is never held while awaiting UI-thread-dependent work.
            // InitializeAsync subscribes the native listeners the engine relies on.
            await Container.Resolve<IAutomationService>().InitializeAsync();

            // Matches WPF: pipelines whose trigger matched at launch run once here.
            LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.Automation.AutomationProcessor>()
                .RunOnStartup();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Library container failed to initialize: {ex}");
        }
    }

    /// <summary>
    /// Releases the library's RGB resources on shutdown, mirroring what WPF does when
    /// it closes: stop the global volume/brightness reactive RGB service and hand the
    /// keyboard's light-control ownership back so firmware and other front ends can
    /// drive it again. Without this the reactive service keeps holding the keyboard.
    /// <para>
    /// Avalonia has no overridable OnExit, so this hangs off the lifetime's
    /// ShutdownRequested. The handler cannot be awaited, so it is driven to
    /// completion and only then allowed to continue the shutdown.
    /// </para>
    /// </summary>
    /// <summary>
    /// Reports an otherwise fatal UI exception and keeps the window alive, so a backend
    /// call that throws surfaces as a logged fault instead of a silent process exit.
    /// </summary>
    private static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Unhandled UI exception: {e.Exception}");

        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Unobserved task exception: {e.Exception}");

        e.SetObserved();
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        try
        {
            // The container is null only if Build() threw, in which case nothing was
            // ever resolved and there is nothing to release.
            if (Container?.IsRegistered<IRgbService>() == true)
                await Container.Resolve<IRgbService>().ShutdownAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RGB shutdown failed: {ex}");
        }
    }
}
