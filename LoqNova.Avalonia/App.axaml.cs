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
        // LoqNova.Lib keeps its own Autofac container and is the single source of
        // truth for controllers, features and settings. WPF seeds it with the three
        // library modules (its own module only supplies WPF view models), so the
        // same set is used here and every page resolves the exact same backend
        // singletons WPF does. It must be initialized before anything is resolved.
        LoqNova.Lib.IoCContainer.Initialize(
            new LoqNova.Lib.IoCModule(),
            new LoqNova.Lib.Automation.IoCModule(),
            new LoqNova.Lib.Macro.IoCModule());

        // Build DI container
        var builder = new ContainerBuilder();
        
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
        
        // Register logging
        builder.Register(c => 
        {
            using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => 
            {
                builder.AddDebug();
                builder.AddConsole();
                builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
            });
            return loggerFactory.CreateLogger<Program>();
        }).SingleInstance();
        
        builder.RegisterGeneric(typeof(Microsoft.Extensions.Logging.Logger<>))
               .As(typeof(Microsoft.Extensions.Logging.ILogger<>))
               .SingleInstance();

        // LoqNova.Lib controllers and features are singletons owned by
        // LoqNova.Lib.IoCContainer. They are surfaced here as instances so the
        // Avalonia adapters below talk to the same objects WPF uses.
        builder.Register(_ => LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.Controllers.Sensors.ISensorsController>()).SingleInstance();
        builder.Register(_ => LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.Features.PowerModeFeature>()).SingleInstance();

        // Real power mode / sensor / thermal adapters over the existing library
        // controllers. Power mode state is read from and written to the machine;
        // nothing is synthesised.
        builder.RegisterType<PerformanceService>().As<IPerformanceService>().SingleInstance();
        builder.RegisterType<SensorsService>().As<ISensorsService>().SingleInstance();
        builder.RegisterType<ThermalService>().As<IThermalService>().SingleInstance();

        // Remaining pages still use mocks until their adapters are ported.
        builder.RegisterType<MockRgbService>().As<IRgbService>().SingleInstance();
        builder.RegisterType<MockBatteryService>().As<IBatteryService>().SingleInstance();
        builder.RegisterType<MockSettingsService>().As<ISettingsService>().SingleInstance();
        builder.RegisterType<MockAutomationService>().As<IAutomationService>().SingleInstance();
        builder.RegisterType<MockMacroService>().As<IMacroService>().SingleInstance();

        // Real package downloader (Packages page) straight from LoqNova.Lib.
        // No mock and no local HTTP/API layer: PackagesViewModel resolves the
        // existing PackageDownloaderFactory and calls IPackageDownloader. The
        // factory takes the concrete downloader types, so the same singletons
        // WPF uses are surfaced rather than constructing second copies.
        builder.Register(_ => LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.HttpClientFactory>()).SingleInstance();
        builder.Register(_ => LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.Settings.PackageDownloaderSettings>()).SingleInstance();
        builder.Register(_ => LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.PackageDownloader.PCSupportPackageDownloader>()).SingleInstance();
        builder.Register(_ => LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.PackageDownloader.VantagePackageDownloader>()).SingleInstance();
        builder.Register(_ => LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.PackageDownloader.PackageDownloaderFactory>()).SingleInstance();
        
        // Register ViewModels
        builder.RegisterType<MainWindowViewModel>().SingleInstance();
        builder.RegisterType<DashboardViewModel>().InstancePerDependency();
        builder.RegisterType<KeyboardBacklightViewModel>().InstancePerDependency();
        builder.RegisterType<BatteryViewModel>().InstancePerDependency();
        builder.RegisterType<AutomationViewModel>().InstancePerDependency();
        builder.RegisterType<MacroViewModel>().InstancePerDependency();
        builder.RegisterType<PackagesViewModel>().InstancePerDependency();
        builder.RegisterType<SettingsViewModel>().InstancePerDependency();
        builder.RegisterType<AboutViewModel>().InstancePerDependency();
        
        // Register Dialog ViewModels
        builder.RegisterType<GodModeSettingsViewModel>().InstancePerDependency();
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
        
        // Apply theme
        var themeService = Container.Resolve<IThemeService>();
        await themeService.InitializeAsync();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create main window
            var mainWindow = new MainWindow
            {
                DataContext = Container.Resolve<MainWindowViewModel>()
            };
            
            desktop.MainWindow = mainWindow;
            
            // Initialize tray service
            var trayService = Container.Resolve<ITrayService>();
            await trayService.InitializeAsync(mainWindow);
            
            // Initialize navigation
            var navigationService = Container.Resolve<INavigationService>();
            await navigationService.InitializeAsync(mainWindow);
        }
        
        base.OnFrameworkInitializationCompleted();
    }
}