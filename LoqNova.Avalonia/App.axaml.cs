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

            // The real automation engine, once and only once. It is resolved from the
            // shared container and initialized after the readiness gate, so the
            // IoCContainer lock is never held while awaiting UI-thread-dependent work.
            // InitializeAsync subscribes the native listeners the engine relies on.
            await Container.Resolve<IAutomationService>().InitializeAsync();

            // Matches WPF: pipelines whose trigger matched at launch run once here.
            LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.Automation.AutomationProcessor>()
                .RunOnStartup();

            // First navigation happens only now: the shared container is up and every
            // adapter a page view model reads in its constructor has been hydrated.
            var navigation = Container.Resolve<INavigationService>();
            await navigation.InitializeAsync(shellWindow!);

            if (Services.NavDiag.Enabled)
            {
                _ = RunNavDiagnosticsAsync(navigation, shellWindow!, string.Empty);
                _ = VerifyMacroParityAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Library container failed to initialize: {ex}");
        }
    }

    /// <summary>
    /// TEMPORARY: runs the navigation acceptance sequence unattended and records the
    /// [NAV-DIAG] trace. Uses the same INavigationService entry point as the sidebar.
    /// </summary>
    private static async Task RunNavDiagnosticsAsync(
        INavigationService navigation, Window window, string logPath)
    {
        try
        {
            var cycles = 5;

            try
            {
                var marker = Path.Combine(Path.GetTempPath(), "loqnova-navdiag.flag");
                var first = File.ReadAllLines(marker).FirstOrDefault()?.Trim();
                if (int.TryParse(first, out var parsed))
                {
                    cycles = parsed;
                }
            }
            catch (Exception)
            {
                // Default cycle count is fine.
            }

            Services.NavDiag.Log("DRIVER-START", $"cycles={cycles} log={logPath}");

            await Services.NavDiag.RunNavigationCycleAsync(navigation, () => window, cycles);
        }
        catch (Exception ex)
        {
            Services.NavDiag.LogException("NAV-DIAG-DRIVER", ex);
        }
    }

/// <summary>
    /// TEMPORARY: drives the real MacroViewModel against the real MacroController and
    /// logs what the WPF page does at each step, so the behaviour can be compared
    /// without a human clicking. Does not modify WPF or the backend.
    /// </summary>
    private static async Task VerifyMacroParityAsync()
    {
        const string Tag = "PARITY";

        try
        {
            var vm = Container.Resolve<LoqNova.Avalonia.ViewModels.Pages.MacroViewModel>();
            var service = Container.Resolve<IMacroService>();
            var navigation = (INavigationAware)vm;

            await navigation.OnNavigatedToAsync();

            var zero = new MacroIdentifier(MacroSource.Keyboard, 0x60);

            Services.NavDiag.Log($"{Tag} default-selection", $"selected={vm.SelectedPadKey?.Label} " +
                $"vk=0x{(vm.SelectedPadKey?.VirtualKey ?? 0):X2} hasEvents={vm.HasEvents} " +
                $"repeatOptions={vm.RepeatOptions.Count} firstRepeatLabel={vm.RepeatOptions.FirstOrDefault()?.Label}");

            // 1. Select slots 0, 1, 2 and 9 and confirm each loads its own real sequence.
            foreach (var label in new[] { "0", "1", "2", "9" })
            {
                var padKey = vm.PadKeys.First(k => k is not null && k.Label == label);
                vm.SelectPadKeyCommand.Execute(padKey);

                var seq = service.GetSequence(padKey.Identifier);
                Services.NavDiag.Log($"{Tag} select-{label}", $"vk=0x{padKey.VirtualKey:X2} " +
                    $"events={seq.Events?.Length} repeat={seq.RepeatCount} cards={vm.EventCards.Count} " +
                    $"hasEvents={vm.HasEvents}");
            }

            // 2. Put a real sequence on key 0 so the option rules have something to act on.
            service.SetSequence(zero, new MacroSequence
            {
                RepeatCount = 1,
                IgnoreDelays = false,
                InterruptOnOtherKey = false,
                Events =
                [
                    new MacroEvent { Source = MacroSource.Keyboard, Direction = MacroDirection.Down, Key = 0x41, Delay = TimeSpan.FromMilliseconds(10) },
                    new MacroEvent { Source = MacroSource.Keyboard, Direction = MacroDirection.Up, Key = 0x41, Delay = TimeSpan.FromMilliseconds(20) },
                    // Three consecutive movements, which WPF folds into one card whose
                    // displayed delay is their sum.
                    new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Move, Key = 0, Delay = TimeSpan.FromMilliseconds(5) },
                    new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Move, Key = 0, Delay = TimeSpan.FromMilliseconds(15) },
                    new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Move, Key = 0, Delay = TimeSpan.FromMilliseconds(25) },
                    new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Down, Key = 1, Delay = TimeSpan.FromMilliseconds(30) }
                ]
            });

            vm.SelectPadKeyCommand.Execute(vm.PadKeys.First(k => k is not null && k.Label == "0"));

            var merged = vm.EventCards.SingleOrDefault(c => c.IsMovement);
            Services.NavDiag.Log($"{Tag} move-merge", $"cards={vm.EventCards.Count} " +
                $"movementCards={vm.EventCards.Count(c => c.IsMovement)} " +
                $"mergedHolds={merged?.Events.Count} mergedDelayMs={(int?)merged?.TotalDelay.TotalMilliseconds} " +
                $"cardTitles={string.Join(" | ", vm.EventCards.Select(c => c.Title))}");

            // 3. Each option must persist immediately, then survive leaving and returning.
            vm.IgnoreDelays = true;
            vm.InterruptOnOtherKey = true;
            vm.RepeatCount = 7;

            var afterOptions = service.GetSequence(zero);
            Services.NavDiag.Log($"{Tag} option-persist", $"repeat={afterOptions.RepeatCount} " +
                $"ignoreDelays={afterOptions.IgnoreDelays} interrupt={afterOptions.InterruptOnOtherKey}");

            // Leaving the page re-runs the navigation hook, which reloads from the backend.
            vm.SelectPadKeyCommand.Execute(vm.PadKeys.First(k => k is not null && k.Label == "1"));
            await navigation.OnNavigatedToAsync();

            var reloaded = service.GetSequence(zero);
            Services.NavDiag.Log($"{Tag} after-leave-return", $"vmRepeat={vm.RepeatCount} " +
                $"vmIgnoreDelays={vm.IgnoreDelays} vmInterrupt={vm.InterruptOnOtherKey} " +
                $"backendRepeat={reloaded.RepeatCount} backendIgnoreDelays={reloaded.IgnoreDelays} " +
                $"backendInterrupt={reloaded.InterruptOnOtherKey} events={reloaded.Events?.Length}");

            // 4. Clear must empty the sequence and persist that.
            vm.ClearCommand.Execute(null);
            var cleared = service.GetSequence(zero);
            Services.NavDiag.Log($"{Tag} after-clear", $"vmHasEvents={vm.HasEvents} cards={vm.EventCards.Count} " +
                $"backendEvents={cleared.Events?.Length} backendRepeat={cleared.RepeatCount}");

            service.SetEnabled(false);
        }
        catch (Exception ex)
        {
            Services.NavDiag.LogException($"{Tag}-FAILED", ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Unobserved task exception: {e.Exception}");

        // AppDomain exceptions cannot be marked handled, but on .NET the process is
        // not terminated from this handler, so logging is enough to see the cause.
    }

    /// <summary>
    /// Catches what would otherwise end the process silently. Avalonia raises this for an
    /// exception escaping the UI thread - which is how a failure while expanding an
    /// automation page template killed the app with no message.
    /// </summary>
    private static void OnDomainUnhandledException(object? sender, global::System.UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Unhandled UI exception: {e.ExceptionObject}");

        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LOQNova",
                "ui-errors.log");

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {e.ExceptionObject}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }

        // AppDomain exceptions cannot be marked handled, but on .NET the process is
        // not terminated from this handler, so logging is enough to see the cause.
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
