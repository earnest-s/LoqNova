#if !DEBUG
using LoqNova.Lib.System;
#endif
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LoqNova.Lib;
using LoqNova.Lib.Automation;
using LoqNova.Lib.Controllers;
using LoqNova.Lib.Extensions;
using LoqNova.Lib.Features;
using LoqNova.Lib.Features.Hybrid;
using LoqNova.Lib.Features.Hybrid.Notify;
using LoqNova.Lib.Features.PanelLogo;
using LoqNova.Lib.Features.WhiteKeyboardBacklight;
using LoqNova.Lib.Integrations;
using LoqNova.Lib.Listeners;
using LoqNova.Lib.Macro;
using LoqNova.Lib.Services;
using LoqNova.Lib.SoftwareDisabler;
using LoqNova.Lib.Utils;
using LoqNova.WPF.CLI;
using LoqNova.WPF.Extensions;
using LoqNova.WPF.Pages;
using LoqNova.WPF.Resources;
using LoqNova.WPF.Utils;
using LoqNova.WPF.Windows;
using LoqNova.WPF.Windows.Utils;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using WinFormsApp = System.Windows.Forms.Application;
using WinFormsHighDpiMode = System.Windows.Forms.HighDpiMode;

namespace LoqNova.WPF;

public partial class App
{
    private const string MUTEX_NAME = "LOQNova_Mutex_6efcc882-924c-4cbc-8fec-f45c25696f98";
    private const string EVENT_NAME = "LOQNova_Event_6efcc882-924c-4cbc-8fec-f45c25696f98";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _singleInstanceWaitHandle;
    private readonly string _sessionId = Guid.NewGuid().ToString("N")[..8];
    private bool _shutdownInitiated;
    private readonly List<Exception> _shutdownErrors = new();

    public new static App Current => (App)Application.Current;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        var startupStage = "initializing";
        try
        {
#if DEBUG
            if (Debugger.IsAttached)
            {
                Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)
                    .Where(p => p.Id != Environment.ProcessId)
                    .ForEach(p =>
                    {
                        p.Kill();
                        p.WaitForExit();
                    });
            }
#endif

        var flags = new Flags(e.Args);

        Log.Instance.IsTraceEnabled = flags.IsTraceEnabled;

        AppDomain.CurrentDomain.UnhandledException += AppDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[{_sessionId}] ===== STARTUP BEGIN ===== Flags: {flags}");

        startupStage = "power_optimization";
        PowerOptimization.Apply();

        startupStage = "localization";
        await LocalizationHelper.SetLanguageAsync(true);

        startupStage = "compatibility_check";
        if (!flags.SkipCompatibilityCheck)
        {
            try
            {
                if (!await CheckBasicCompatibilityAsync())
                    return;
                if (!await CheckCompatibilityAsync())
                    return;
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[{_sessionId}] Compatibility check failed", ex);

                MessageBox.Show(Resource.CompatibilityCheckError_Message, Resource.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(200);
                return;
            }
        }

        startupStage = "single_instance";
        EnsureSingleInstance();

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[{_sessionId}] Starting... [version={Assembly.GetEntryAssembly()?.GetName().Version}, build={Assembly.GetEntryAssembly()?.GetBuildDateTimeString()}, os={Environment.OSVersion}, dotnet={Environment.Version}]");

        startupStage = "high_dpi";
        WinFormsApp.SetHighDpiMode(WinFormsHighDpiMode.PerMonitorV2);

        startupStage = "di_container";
        IoCContainer.Initialize(
            new Lib.IoCModule(),
            new Lib.Automation.IoCModule(),
            new Lib.Macro.IoCModule(),
            new IoCModule()
        );

        startupStage = "proxy_config";
        IoCContainer.Resolve<HttpClientFactory>().SetProxy(flags.ProxyUrl, flags.ProxyUsername, flags.ProxyPassword, flags.ProxyAllowAllCerts);

        startupStage = "feature_flags";
        IoCContainer.Resolve<PowerModeFeature>().AllowAllPowerModesOnBattery = flags.AllowAllPowerModesOnBattery;
        IoCContainer.Resolve<RgbFrameDispatcher>().ForceDisable = flags.ForceDisableRgbKeyboardSupport;
        IoCContainer.Resolve<SpectrumKeyboardBacklightController>().ForceDisable = flags.ForceDisableSpectrumKeyboardSupport;
        IoCContainer.Resolve<WhiteKeyboardLenovoLightingBacklightFeature>().ForceDisable = flags.ForceDisableLenovoLighting;
        IoCContainer.Resolve<PanelLogoLenovoLightingBacklightFeature>().ForceDisable = flags.ForceDisableLenovoLighting;
        IoCContainer.Resolve<PortsBacklightFeature>().ForceDisable = flags.ForceDisableLenovoLighting;
        IoCContainer.Resolve<IGPUModeFeature>().ExperimentalGPUWorkingMode = flags.ExperimentalGPUWorkingMode;
        IoCContainer.Resolve<DGPUNotify>().ExperimentalGPUWorkingMode = flags.ExperimentalGPUWorkingMode;
        IoCContainer.Resolve<UpdateChecker>().Disable = flags.DisableUpdateChecker;

        AutomationPage.EnableHybridModeAutomation = flags.EnableHybridModeAutomation;

        startupStage = "software_status";
        await LogSoftwareStatusAsync();
        startupStage = "power_mode_feature";
        await InitPowerModeFeatureAsync();
        startupStage = "battery_feature";
        await InitBatteryFeatureAsync();
        startupStage = "rgb_keyboard_controller";
        await InitRgbKeyboardControllerAsync();
        startupStage = "spectrum_keyboard_controller";
        await InitSpectrumKeyboardControllerAsync();
        startupStage = "gpu_overclock_controller";
        await InitGpuOverclockControllerAsync();
        startupStage = "hybrid_mode";
        await InitHybridModeAsync();
        startupStage = "automation_processor";
        await InitAutomationProcessorAsync();
        startupStage = "macro_controller";
        InitMacroController();

        startupStage = "services_start";
        await IoCContainer.Resolve<AIController>().StartIfNeededAsync();
        await IoCContainer.Resolve<HWiNFOIntegration>().StartStopIfNeededAsync();
        await IoCContainer.Resolve<IpcServer>().StartStopIfNeededAsync();
        await IoCContainer.Resolve<BatteryDischargeRateMonitorService>().StartStopIfNeededAsync();
        await IoCContainer.Resolve<VolumeBrightnessReactiveRgbService>().StartStopIfNeededAsync();

#if !DEBUG
        Autorun.Validate();
#endif

        startupStage = "main_window";
        var mainWindow = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            TrayTooltipEnabled = !flags.DisableTrayTooltip,
            DisableConflictingSoftwareWarning = flags.DisableConflictingSoftwareWarning
        };
        MainWindow = mainWindow;

        IoCContainer.Resolve<ThemeManager>().Apply();

        if (flags.Minimized)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] Sending MainWindow to tray...");

            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.Show();
            mainWindow.SendToTray();
        }
        else
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] Showing MainWindow...");

            mainWindow.Show();
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[{_sessionId}] ===== STARTUP COMPLETE =====");
        }
        catch (Exception ex)
        {
            Log.Instance.ErrorReport($"Application_Startup failed at stage: {startupStage}", ex);
            Log.Instance.Trace($"[{_sessionId}] STARTUP FAILED at stage: {startupStage}", ex);
            MessageBox.Show(string.Format(Resource.UnexpectedException, ex.ToStringDemystified()),
                "Application Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(102);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Instance.ErrorReport("TaskScheduler_UnobservedTaskException", e.Exception);
        Log.Instance.Trace($"[{_sessionId}] Unobserved task exception: {e.Exception}", e.Exception);
        e.SetObserved();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[{_sessionId}] Application_Exit");

        try
        {
            _singleInstanceWaitHandle?.Close();
            _singleInstanceWaitHandle = null;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] Failed to close wait handle", ex);
        }

        try
        {
            _singleInstanceMutex?.Close();
            _singleInstanceMutex = null;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] Failed to close mutex", ex);
        }
    }

    public void RestartMainWindow()
    {
        if (MainWindow is MainWindow mw)
        {
            mw.SuppressClosingEventHandler = true;
            mw.Close();
        }

        var mainWindow = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    public async Task ShutdownAsync()
    {
        if (_shutdownInitiated)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] ShutdownAsync already in progress");
            return;
        }

        _shutdownInitiated = true;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[{_sessionId}] ===== SHUTDOWN BEGIN =====");

        await StopServiceAsync("AIController", () => IoCContainer.TryResolve<AIController>()?.StopAsync());
        await StopServiceAsync("RGBKeyboardBacklightController", async () =>
        {
            if (IoCContainer.TryResolve<RGBKeyboardBacklightController>() is { } ctrl && await ctrl.IsSupportedAsync())
                await ctrl.SetLightControlOwnerAsync(false);
        });
        await StopServiceAsync("SpectrumKeyboardBacklightController", () => IoCContainer.TryResolve<SpectrumKeyboardBacklightController>()?.StopAuroraIfNeededAsync());
        await StopServiceAsync("NativeWindowsMessageListener", () => IoCContainer.TryResolve<NativeWindowsMessageListener>()?.StopAsync());
        await StopServiceAsync("SessionLockUnlockListener", () => IoCContainer.TryResolve<SessionLockUnlockListener>()?.StopAsync());
        await StopServiceAsync("HWiNFOIntegration", () => IoCContainer.TryResolve<HWiNFOIntegration>()?.StopAsync());
        await StopServiceAsync("IpcServer", () => IoCContainer.TryResolve<IpcServer>()?.StopAsync());
        await StopServiceAsync("BatteryDischargeRateMonitorService", () => IoCContainer.TryResolve<BatteryDischargeRateMonitorService>()?.StopAsync());
        await StopServiceAsync("VolumeBrightnessReactiveRgbService", () => IoCContainer.TryResolve<VolumeBrightnessReactiveRgbService>()?.StopAsync());
        await StopServiceAsync("MacroController", () => Task.Run(() => IoCContainer.TryResolve<MacroController>()?.Stop()));
        await StopServiceAsync("AutomationProcessor", () => IoCContainer.TryResolve<AutomationProcessor>()?.SetEnabledAsync(false));

        if (_shutdownErrors.Count > 0)
        {
            var aggregate = new AggregateException("Shutdown errors", _shutdownErrors);
            Log.Instance.ErrorReport("ShutdownAsync aggregate errors", aggregate);
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] Shutdown completed with {_shutdownErrors.Count} errors", aggregate);
        }
        else if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"[{_sessionId}] ===== SHUTDOWN COMPLETE =====");
        }

        Shutdown();
    }

    private async Task StopServiceAsync(string name, Func<Task?> stopAction)
    {
        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] Stopping {name}...");

            var task = stopAction();
            if (task is not null)
                await task.ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] {name} stopped");
        }
        catch (Exception ex)
        {
            _shutdownErrors.Add(new InvalidOperationException($"Failed to stop {name}", ex));
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] ERROR stopping {name}", ex);
        }
    }

    private void AppDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;

        Log.Instance.ErrorReport("AppDomain_UnhandledException", exception ?? new Exception($"Unknown exception caught: {e.ExceptionObject}"));
        Log.Instance.Trace($"[{_sessionId}] Unhandled exception occurred.", exception);

        MessageBox.Show(string.Format(Resource.UnexpectedException, exception?.ToStringDemystified() ?? "Unknown exception."),
            "Application Domain Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(100);
    }

    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Instance.ErrorReport("Application_DispatcherUnhandledException", e.Exception);
        Log.Instance.Trace($"[{_sessionId}] Dispatcher unhandled exception.", e.Exception);

        MessageBox.Show(string.Format(Resource.UnexpectedException, e.Exception.ToStringDemystified()),
            "Application Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(101);
    }

    private async Task<bool> CheckBasicCompatibilityAsync()
    {
        var isCompatible = await Compatibility.CheckBasicCompatibilityAsync();
        if (isCompatible)
            return true;

        MessageBox.Show(Resource.IncompatibleDevice_Message, Resource.AppName, MessageBoxButton.OK, MessageBoxImage.Error);

        Shutdown(201);
        return false;
    }

    private async Task<bool> CheckCompatibilityAsync()
    {
        var (isCompatible, mi) = await Compatibility.IsCompatibleAsync();
        if (isCompatible)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] Compatibility check passed. [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}, BIOS={mi.BiosVersion}]");
            return true;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[{_sessionId}] Incompatible system detected. [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}, BIOS={mi.BiosVersion}]");

        var unsupportedWindow = new UnsupportedWindow(mi);
        unsupportedWindow.Show();

        var result = await unsupportedWindow.ShouldContinue;
        if (result)
        {
            Log.Instance.IsTraceEnabled = true;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] Compatibility check OVERRIDE. [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}, version={Assembly.GetEntryAssembly()?.GetName().Version}, build={Assembly.GetEntryAssembly()?.GetBuildDateTimeString() ?? string.Empty}]");
            return true;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[{_sessionId}] Shutting down... [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}]");

        Shutdown(202);
        return false;
    }

    private void EnsureSingleInstance()
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[{_sessionId}] Checking for other instances...");

        try
        {
            // Clean up any stale EventWaitHandle from previous crashed instance
            try
            {
                EventWaitHandle.OpenExisting(EVENT_NAME)?.Close();
            }
            catch { /* no existing handle */ }

            _singleInstanceMutex = new Mutex(true, MUTEX_NAME, out var isOwned);
            _singleInstanceWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, EVENT_NAME);

            if (!isOwned)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[{_sessionId}] Another instance running, closing...");

                _singleInstanceWaitHandle.Set();
                Shutdown();
                return;
            }
        }
        catch (AbandonedMutexException)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] Abandoned mutex detected, taking ownership...");

            // Clean up stale wait handle
            try { EventWaitHandle.OpenExisting(EVENT_NAME)?.Close(); } catch { }

            _singleInstanceWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, EVENT_NAME);
        }
        catch (Exception ex)
        {
            Log.Instance.ErrorReport("EnsureSingleInstance", ex);
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[{_sessionId}] Single instance check failed", ex);
            Shutdown(103);
            return;
        }

        new Thread(() =>
        {
            try
            {
                while (_singleInstanceWaitHandle?.WaitOne() == true)
                {
                    Current.Dispatcher.BeginInvoke(async () =>
                    {
                        if (Current.MainWindow is { } window)
                        {
                            if (Log.Instance.IsTraceEnabled)
                                Log.Instance.Trace($"[{_sessionId}] Another instance started, bringing this one to front...");

                            window.BringToForeground();
                        }
                        else
                        {
                            if (Log.Instance.IsTraceEnabled)
                                Log.Instance.Trace($"[{_sessionId}] !!! PANIC !!! This instance is missing main window. Shutting down.");

                            await ShutdownAsync();
                        }
                    });
                }
            }
            catch (ObjectDisposedException) { /* mutex/wait handle disposed during shutdown */ }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[{_sessionId}] Single instance watchdog error", ex);
            }
        })
        {
            IsBackground = true,
            Name = "LoqNova-SingleInstanceWatchdog"
        }.Start();
    }

    private static async Task LogSoftwareStatusAsync()
    {
        if (!Log.Instance.IsTraceEnabled)
            return;

        var vantageStatus = await IoCContainer.Resolve<VantageDisabler>().GetStatusAsync();
        Log.Instance.Trace($"Vantage status: {vantageStatus}");

        var legionZoneStatus = await IoCContainer.Resolve<LegionZoneDisabler>().GetStatusAsync();
        Log.Instance.Trace($"LegionZone status: {legionZoneStatus}");

        var fnKeysStatus = await IoCContainer.Resolve<FnKeysDisabler>().GetStatusAsync();
        Log.Instance.Trace($"FnKeys status: {fnKeysStatus}");
    }

    private static async Task InitHybridModeAsync()
    {
        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Initializing hybrid mode...");

            var feature = IoCContainer.Resolve<HybridModeFeature>();
            await feature.EnsureDGPUEjectedIfNeededAsync();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't initialize hybrid mode.", ex);
        }
    }

    private static async Task InitAutomationProcessorAsync()
    {
        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Initializing automation processor...");

            var automationProcessor = IoCContainer.Resolve<AutomationProcessor>();
            await automationProcessor.InitializeAsync();
            automationProcessor.RunOnStartup();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't initialize automation processor.", ex);
        }
    }

    private static async Task InitPowerModeFeatureAsync()
    {
        try
        {
            var feature = IoCContainer.Resolve<PowerModeFeature>();
            if (await feature.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Ensuring god mode state is applied...");

                await feature.EnsureGodModeStateIsAppliedAsync();
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't ensure god mode state.", ex);
        }

        try
        {
            var feature = IoCContainer.Resolve<PowerModeFeature>();
            if (await feature.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Ensuring correct power plan is set...");

                await feature.EnsureCorrectWindowsPowerSettingsAreSetAsync();
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't ensure correct power plan.", ex);
        }
    }

    private static async Task InitBatteryFeatureAsync()
    {
        try
        {
            var feature = IoCContainer.Resolve<BatteryFeature>();
            if (await feature.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Ensuring correct battery mode is set...");

                await feature.EnsureCorrectBatteryModeIsSetAsync();
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't ensure correct battery mode.", ex);
        }
    }

    private static async Task InitRgbKeyboardControllerAsync()
    {
        try
        {
            var controller = IoCContainer.Resolve<RGBKeyboardBacklightController>();
            if (await controller.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Setting light control owner and restoring preset...");

                await controller.SetLightControlOwnerAsync(true, true);
            }
            else
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"RGB keyboard is not supported.");
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't set light control owner or current preset.", ex);
        }
    }

    private static async Task InitSpectrumKeyboardControllerAsync()
    {
        try
        {
            var controller = IoCContainer.Resolve<SpectrumKeyboardBacklightController>();
            if (await controller.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Starting Aurora if needed...");

                var result = await controller.StartAuroraIfNeededAsync();
                if (result)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Aurora started.");
                }
                else
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Aurora not needed.");
                }
            }
            else
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Spectrum keyboard is not supported.");
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't start Aurora if needed.", ex);
        }
    }

    private static async Task InitGpuOverclockControllerAsync()
    {
        try
        {
            var controller = IoCContainer.Resolve<GPUOverclockController>();
            if (await controller.IsSupportedAsync())
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Ensuring GPU overclock is applied...");

                var result = await controller.EnsureOverclockIsAppliedAsync();
                if (result)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"GPU overclock applied.");
                }
                else
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"GPU overclock not needed.");
                }
            }
            else
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"GPU overclock is not supported.");
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't overclock GPU.", ex);
        }
    }

    private static void InitMacroController()
    {
        var controller = IoCContainer.Resolve<MacroController>();
        controller.Start();
    }
}
