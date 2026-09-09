using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using LoqNova.Avalonia.Platform;
using LoqNova.Avalonia.Views;
using LoqNova.Lib;
using LoqNova.Lib.Automation;
using LoqNova.Lib.Controllers;
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

namespace LoqNova.Avalonia.Startup;

/// <summary>
/// Ordered application-startup lifecycle for the Avalonia UI, mirroring the functional
/// WPF startup (LoqNova.WPF App.Application_Startup) against the frozen LoqNova backend.
///
/// WPF-coupled components that have no Avalonia equivalent yet are intentionally deferred
/// and documented: IpcServer (LoqNova.WPF.CLI), ThemeManager, NotificationsManager,
/// SpectrumScreenCapture and DashboardSettings (LoqNova.WPF IoCModule), plus the interactive
/// LanguageSelectorWindow used by LocalizationHelper.
/// </summary>
public class AppStartup
{
    private readonly Flags _flags;
    private bool _shutdownRequested;

    public AppStartup(string[] args)
    {
        _flags = new Flags(args);
    }

    public async Task<bool> InitializeBackendAsync()
    {
        try
        {
            Log.Instance.IsTraceEnabled = _flags.IsTraceEnabled;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Flags: {_flags}");

            // Prevent Windows EcoQoS throttling, improve timer resolution,
            // and set a stable AboveNormal process priority. (Platform adapter.)
            PowerOptimization.Apply();

            await SetLanguageAsync();

            if (!_flags.SkipCompatibilityCheck)
            {
                if (!await CheckBasicCompatibilityAsync())
                    return false;
                if (!await CheckCompatibilityAsync())
                    return false;
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Starting... [version={Assembly.GetEntryAssembly()?.GetName().Version}, os={Environment.OSVersion}, dotnet={Environment.Version}]");

            IoCContainer.Initialize(
                new Lib.IoCModule(),
                new Lib.Automation.IoCModule(),
                new Lib.Macro.IoCModule(),
                new IoCModule()
            );

            IoCContainer.Resolve<HttpClientFactory>().SetProxy(_flags.ProxyUrl, _flags.ProxyUsername, _flags.ProxyPassword, _flags.ProxyAllowAllCerts);

            IoCContainer.Resolve<PowerModeFeature>().AllowAllPowerModesOnBattery = _flags.AllowAllPowerModesOnBattery;
            IoCContainer.Resolve<RgbFrameDispatcher>().ForceDisable = _flags.ForceDisableRgbKeyboardSupport;
            IoCContainer.Resolve<SpectrumKeyboardBacklightController>().ForceDisable = _flags.ForceDisableSpectrumKeyboardSupport;
            IoCContainer.Resolve<WhiteKeyboardLenovoLightingBacklightFeature>().ForceDisable = _flags.ForceDisableLenovoLighting;
            IoCContainer.Resolve<PanelLogoLenovoLightingBacklightFeature>().ForceDisable = _flags.ForceDisableLenovoLighting;
            IoCContainer.Resolve<PortsBacklightFeature>().ForceDisable = _flags.ForceDisableLenovoLighting;
            IoCContainer.Resolve<IGPUModeFeature>().ExperimentalGPUWorkingMode = _flags.ExperimentalGPUWorkingMode;
            IoCContainer.Resolve<DGPUNotify>().ExperimentalGPUWorkingMode = _flags.ExperimentalGPUWorkingMode;
            IoCContainer.Resolve<UpdateChecker>().Disable = _flags.DisableUpdateChecker;

            await LogSoftwareStatusAsync();
            await InitPowerModeFeatureAsync();
            await InitBatteryFeatureAsync();
            await InitRgbKeyboardControllerAsync();
            await InitSpectrumKeyboardControllerAsync();
            await InitGpuOverclockControllerAsync();
            await InitHybridModeAsync();
            await InitAutomationProcessorAsync();
            InitMacroController();

            await IoCContainer.Resolve<AIController>().StartIfNeededAsync();
            await IoCContainer.Resolve<HWiNFOIntegration>().StartStopIfNeededAsync();

            // DEFERRED (WPF-coupled): IpcServer (LoqNova.WPF.CLI) per constraint #8.
            // Documented in the Phase 1 report. Named-pipe CLI is not started.

            await IoCContainer.Resolve<BatteryDischargeRateMonitorService>().StartStopIfNeededAsync();
            await IoCContainer.Resolve<VolumeBrightnessReactiveRgbService>().StartStopIfNeededAsync();

            // DEFERRED (would hijack the WPF scheduler task): Autorun.Validate().
            // The shared LOQNova_Autorun task targets the WPF executable; validating from the
            // Avalonia exe would rewrite it. Intentionally not performed during migration.

            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.ErrorReport("AvaloniaStartup_InitializeBackend", ex);
            Log.Instance.Trace($"Failed to initialize backend.", ex);
            return false;
        }
    }

    public Window CreateMainWindow()
    {
        var window = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        if (_flags.Minimized)
            window.WindowState = WindowState.Minimized;

        return window;
    }

    public void ReportStartupFailure(Exception ex)
    {
        Log.Instance.ErrorReport("AvaloniaStartup_Failure", ex);
        Log.Instance.Trace($"Startup failed.", ex);
    }

    public async Task ShutdownAsync()
    {
        if (_shutdownRequested)
            return;

        _shutdownRequested = true;

        try
        {
            if (IoCContainer.TryResolve<AIController>() is { } aiController)
                await aiController.StopAsync();
        }
        catch { /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<RGBKeyboardBacklightController>() is { } rgbKeyboardBacklightController)
            {
                if (await rgbKeyboardBacklightController.IsSupportedAsync())
                    await rgbKeyboardBacklightController.SetLightControlOwnerAsync(false);
            }
        }
        catch { /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<SpectrumKeyboardBacklightController>() is { } spectrumKeyboardBacklightController)
            {
                if (await spectrumKeyboardBacklightController.IsSupportedAsync())
                    await spectrumKeyboardBacklightController.StopAuroraIfNeededAsync();
            }
        }
        catch { /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<NativeWindowsMessageListener>() is { } nativeMessageWindowListener)
                await nativeMessageWindowListener.StopAsync();
        }
        catch { /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<SessionLockUnlockListener>() is { } sessionLockUnlockListener)
                await sessionLockUnlockListener.StopAsync();
        }
        catch { /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<HWiNFOIntegration>() is { } hwinfoIntegration)
                await hwinfoIntegration.StopAsync();
        }
        catch { /* Ignored. */ }

        // DEFERRED (WPF-coupled): IpcServer.StopAsync()

        try
        {
            if (IoCContainer.TryResolve<BatteryDischargeRateMonitorService>() is { } batteryDischargeMon)
                await batteryDischargeMon.StopAsync();
        }
        catch { /* Ignored. */ }

        try
        {
            if (IoCContainer.TryResolve<VolumeBrightnessReactiveRgbService>() is { } reactiveRgb)
                await reactiveRgb.StopAsync();
        }
        catch { /* Ignored. */ }
    }

    private static async Task SetLanguageAsync()
    {
        try
        {
            var languagePath = Path.Combine(Folders.AppData, "lang");
            CultureInfo cultureInfo;

            if (File.Exists(languagePath))
            {
                try
                {
                    cultureInfo = new CultureInfo(await File.ReadAllTextAsync(languagePath));
                }
                catch
                {
                    cultureInfo = new CultureInfo("en");
                }
            }
            else
            {
                cultureInfo = new CultureInfo("en");
            }

            Thread.CurrentThread.CurrentCulture = new CultureInfo("en");
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en");

            Thread.CurrentThread.CurrentUICulture = cultureInfo;
            CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

            LoqNova.Lib.Resources.Resource.Culture = cultureInfo;
            LoqNova.Lib.Automation.Resources.Resource.Culture = cultureInfo;
            LoqNova.Lib.Macro.Resources.Resource.Culture = cultureInfo;

            // NOTE: The interactive language selector (LoqNova.WPF LanguageSelectorWindow)
            // is not ported yet; it will be a Phase 7 Settings dialog.

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Applied culture: {cultureInfo.Name}");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Couldn't set language.", ex);
        }
    }

    private async Task<bool> CheckBasicCompatibilityAsync()
    {
        var isCompatible = await Compatibility.CheckBasicCompatibilityAsync();
        if (isCompatible)
            return true;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Incompatible device detected (basic check failed).");

        await ShowFatalErrorAsync("Incompatible device: LOQ Nova requires a supported Lenovo Legion device.");
        return false;
    }

    private async Task<bool> CheckCompatibilityAsync()
    {
        var (isCompatible, mi) = await Compatibility.IsCompatibleAsync();
        if (isCompatible)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Compatibility check passed. [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}, BIOS={mi.BiosVersion}]");
            return true;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Incompatible system detected. [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}, BIOS={mi.BiosVersion}]");

        // DEFERRED (WPF-coupled): the interactive UnsupportedWindow (continue-only-once flow)
        // is not ported yet. For Phase 1 an unsupported system simply cannot continue.

        await ShowFatalErrorAsync("Incompatible device detected.");
        return false;
    }

    private static async Task ShowFatalErrorAsync(string message)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } w
            })
        {
            await new MessageBoxWindow(message, "LOQ Nova").ShowDialog(w);
        }
        else
        {
            // No window yet — display on the console in case the exe was run from a terminal.
            Log.Instance.ErrorReport("StartupBlocked", new Exception(message));
        }
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
