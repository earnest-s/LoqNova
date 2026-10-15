using System;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LoqNova.Avalonia.Localization;
using LoqNova.Avalonia.Services;
using LoqNova.Avalonia.Styles;
using LoqNova.Lib;
using LoqNova.Lib.Controllers;
using LoqNova.Lib.Controllers.GodMode;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.ViewModels.Windows;

/// <summary>
/// The tray hover popup, the Avalonia equivalent of WPF's <c>StatusWindow</c>.
///
/// Every value is read once, when the pointer opens the popup, from the same
/// backends the rest of the shell uses: the power mode adapter, the God Mode
/// controller, the GPU controller and the battery adapter. Nothing here holds a
/// second copy of a domain value, and a value the machine has not reported is
/// shown as "-" rather than guessed.
/// </summary>
public partial class StatusTrayPopupViewModel : ViewModelBase
{
    /// <summary>Shown for a value no backend has reported yet, as in WPF.</summary>
    private const string Unknown = "-";

    private readonly IPerformanceService _performanceService;
    private readonly IBatteryService _batteryService;
    private readonly ILogger<StatusTrayPopupViewModel> _logger;

    public StatusTrayPopupViewModel(
        IPerformanceService performanceService,
        IBatteryService batteryService,
        ILogger<StatusTrayPopupViewModel> logger)
    {
        _performanceService = performanceService;
        _batteryService = batteryService;
        _logger = logger;
    }

    // ---- Labels. The same resources WPF's StatusWindow binds. ----

    public string AppName => LocalizationHelper.T("AppName");

    public string PowerModeLabel => LocalizationHelper.T("StatusTrayPopup_PowerMode");

    public string PresetLabel => LocalizationHelper.T("StatusTrayPopup_Preset");

    public string DiscreteGpuLabel => LocalizationHelper.T("StatusTrayPopup_DiscreteGPU");

    public string PowerStateLabel => LocalizationHelper.T("StatusTrayPopup_PowerState");

    public string BatteryLabel => LocalizationHelper.T("StatusTrayPopup_Battery");

    public string BatteryModeLabel => LocalizationHelper.T("StatusTrayPopup_Mode");

    public string DischargeRateLabel => LocalizationHelper.T("StatusTrayPopup_DischargeRate");

    public string MinDischargeRateLabel => LocalizationHelper.T("StatusTrayPopup_MinDischargeRate");

    public string MaxDischargeRateLabel => LocalizationHelper.T("StatusTrayPopup_MaxDischargeRate");

    // ---- Power mode ----

    /// <summary>Null until the machine has reported one; the view then shows "-".</summary>
    [ObservableProperty]
    private PowerModeState? _powerMode;

    [ObservableProperty]
    private IBrush _powerModeColor = ThemeBrushes.Secondary;

    /// <summary>True only in God Mode, where WPF shows the active preset.</summary>
    [ObservableProperty]
    private bool _showPreset;

    [ObservableProperty]
    private string _presetName = Unknown;

    // ---- Discrete GPU ----

    /// <summary>False when the machine has no NVIDIA GPU to report on.</summary>
    [ObservableProperty]
    private bool _showGpu;

    [ObservableProperty]
    private string _gpuStateName = Unknown;

    [ObservableProperty]
    private IBrush _gpuStateColor = ThemeBrushes.Secondary;

    /// <summary>Hidden while the GPU is powered off, as in WPF.</summary>
    [ObservableProperty]
    private bool _showGpuPowerState;

    [ObservableProperty]
    private string _gpuPowerState = Unknown;

    // ---- Battery ----

    /// <summary>Name of the charge-level geometry to draw.</summary>
    [ObservableProperty]
    private string _batteryIcon = "Battery";

    [ObservableProperty]
    private string _batteryValue = Unknown;

    /// <summary>The value turns amber when the battery reports itself low.</summary>
    [ObservableProperty]
    private IBrush _batteryValueBrush = ThemeBrushes.Secondary;

    [ObservableProperty]
    private BatteryState _batteryMode = BatteryState.Normal;

    [ObservableProperty]
    private string _dischargeRate = Unknown;

    [ObservableProperty]
    private string _minDischargeRate = Unknown;

    [ObservableProperty]
    private string _maxDischargeRate = Unknown;

    /// <summary>
    /// Reads the machine once and applies what it reports.
    ///
    /// The reads run on the thread pool — resolving the shared library container
    /// takes a global lock, and the GPU read talks to NVAPI and WMI — and the
    /// results are applied back on the UI thread, because writing the bound
    /// properties is only safe there.
    /// </summary>
    public async Task RefreshAsync()
    {
        // Adapter values that are already cached: read here, no I/O.
        var powerMode = _performanceService.CurrentMode;

        var presetName = powerMode == PowerModeState.GodMode
            ? await Task.Run(ReadActivePresetNameAsync).ConfigureAwait(true)
            : null;

        var gpu = await Task.Run(ReadGpuAsync).ConfigureAwait(true);

        ApplyPowerMode(powerMode, presetName);
        ApplyGpu(gpu);
        RefreshBattery();
    }

    /// <summary>One reading of the discrete GPU.</summary>
    private readonly record struct GpuReading(bool Supported, GPUState State, string? PerformanceState);

    private async Task<string?> ReadActivePresetNameAsync()
    {
        try
        {
            var controller = LoqNova.Lib.IoCContainer.Resolve<IGodModeController>();
            return await controller.GetActivePresetNameAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tray popup: could not read the active Custom Mode preset");
            return null;
        }
    }

    private async Task<GpuReading> ReadGpuAsync()
    {
        try
        {
            var controller = LoqNova.Lib.IoCContainer.Resolve<GPUController>();

            if (!controller.IsSupported())
                return new GpuReading(false, GPUState.Unknown, null);

            var status = await controller.RefreshNowAsync().ConfigureAwait(false);
            return new GpuReading(true, status.State, status.PerformanceState);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tray popup: could not read the discrete GPU state");
            return new GpuReading(false, GPUState.Unknown, null);
        }
    }

    private void ApplyPowerMode(PowerModeState? powerMode, string? presetName)
    {
        PowerMode = powerMode;
        PowerModeColor = ThemeBrushes.PowerMode(powerMode);

        // The active preset is only meaningful in Custom Mode, which is where WPF
        // shows the row.
        if (powerMode != PowerModeState.GodMode)
        {
            ShowPreset = false;
            PresetName = Unknown;
            return;
        }

        ShowPreset = true;
        PresetName = presetName ?? Unknown;
    }

    private void ApplyGpu(GpuReading gpu)
    {
        ShowGpu = gpu.Supported;

        if (!gpu.Supported)
            return;

        ShowGpuPowerState = gpu.State != GPUState.PoweredOff;
        GpuPowerState = gpu.PerformanceState ?? Unknown;

        // The states and their colours match WPF's StatusWindow: active (and
        // monitor-connected, which WPF groups with it), inactive, powered off.
        switch (gpu.State)
        {
            case GPUState.Active:
            case GPUState.MonitorConnected:
                GpuStateName = LocalizationHelper.T("Active");
                GpuStateColor = ThemeBrushes.Success;
                break;

            case GPUState.PoweredOff:
                GpuStateName = LocalizationHelper.T("PoweredOff");
                GpuStateColor = ThemeBrushes.Danger;
                break;

            default:
                GpuStateName = LocalizationHelper.T("Inactive");
                GpuStateColor = ThemeBrushes.Warning;
                break;
        }
    }

    private void RefreshBattery()
    {
        if (!_batteryService.IsSupported)
        {
            // WPF always shows the block; a machine without a battery gets "-".
            BatteryIcon = "Battery";
            BatteryValue = Unknown;
            BatteryValueBrush = ThemeBrushes.Secondary;
            BatteryMode = BatteryState.Normal;
            DischargeRate = Unknown;
            MinDischargeRate = Unknown;
            MaxDischargeRate = Unknown;
            return;
        }

        var percentage = _batteryService.Percentage;

        BatteryValue = percentage >= 0 ? $"{percentage}%" : Unknown;
        BatteryValueBrush = _batteryService.IsLowBattery ? ThemeBrushes.Warning : ThemeBrushes.Secondary;
        BatteryIcon = _batteryService.IsCharging ? "BatteryCharging" : BatteryIconFor(percentage);
        BatteryMode = _batteryService.CurrentMode;

        // The adapter already converts the machine's milliwatts to watts, which is
        // the unit WPF divides down to before formatting.
        DischargeRate = Watts(_batteryService.DischargeRate);
        MinDischargeRate = Watts(_batteryService.MinDischargeRate);
        MaxDischargeRate = Watts(_batteryService.MaxDischargeRate);
    }

    /// <summary>WPF's <c>$"{value:+0.00;-0.00;0.00} W"</c>.</summary>
    private static string Watts(double value) => $"{value:+0.00;-0.00;0.00} W";

    /// <summary>
    /// WPF steps through ten Fluent battery glyphs as the charge falls. The icon set
    /// here has four charge levels plus a charging glyph, so the same rounded
    /// ten-percent level drives the nearest of those.
    /// </summary>
    private static string BatteryIconFor(int percentage)
    {
        if (percentage < 0)
            return "Battery";

        return (int)Math.Round(percentage / 10.0) switch
        {
            >= 8 => "BatteryFull",
            >= 5 => "BatteryHigh",
            >= 3 => "BatteryMedium",
            _ => "BatteryLow"
        };
    }
}
