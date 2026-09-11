using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LoqNova.Avalonia.Converters;
using LoqNova.Avalonia.Services;
using LoqNova.Avalonia.ViewModels;
using LoqNova.Avalonia.Views;
using Autofac;

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
        // Build DI container
        var builder = new ContainerBuilder();
        
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
        
        // Register mock services (replace with real services in backend integration phase)
        builder.RegisterType<MockPerformanceService>().As<IPerformanceService>().SingleInstance();
        builder.RegisterType<MockRgbService>().As<IRgbService>().SingleInstance();
        builder.RegisterType<MockThermalService>().As<IThermalService>().SingleInstance();
        builder.RegisterType<MockBatteryService>().As<IBatteryService>().SingleInstance();
        builder.RegisterType<MockSensorsService>().As<ISensorsService>().SingleInstance();
        builder.RegisterType<MockSettingsService>().As<ISettingsService>().SingleInstance();
        builder.RegisterType<MockAutomationService>().As<IAutomationService>().SingleInstance();
        builder.RegisterType<MockMacroService>().As<IMacroService>().SingleInstance();
        builder.RegisterType<MockPackageService>().As<IPackageService>().SingleInstance();
        
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
    
    public override async void OnExit()
    {
        // Cleanup services
        if (Container != null)
        {
            var trayService = Container.ResolveOptional<ITrayService>();
            if (trayService != null)
                await trayService.ShutdownAsync();
            
            var notificationService = Container.ResolveOptional<INotificationService>();
            if (notificationService != null)
                await notificationService.ShutdownAsync();
            
            Container.Dispose();
        }
        
        base.OnExit();
    }
}