using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Controllers.GodMode;
using LoqNova.Lib.Features;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// One Custom Mode setting, reproducing WPF's <c>GodModeValueControl</c>.
/// <para>
/// WPF's control is dual-mode: a slider when the backend reports a continuous
/// <c>StepperValue</c>, and a combo box when it reports discrete <c>Steps</c>. A null
/// setting means the hardware does not support it and WPF collapses the row entirely,
/// which is reproduced here by leaving <see cref="IsSupported"/> false.
/// </para>
/// </summary>
public partial class CustomModeSettingViewModel : ViewModelBase
{
    [ObservableProperty]
    private double _value;

    public string Title { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    /// <summary>False when the backend reports the setting as unsupported.</summary>
    [ObservableProperty]
    private bool _isSupported;

    /// <summary>
    /// Range reported by the backend. These are assigned in <see cref="Adopt"/> from
    /// the stepper rather than set at construction: they are <c>init</c> only, so a
    /// control created before the state was read kept a 0..0 range and every value was
    /// clamped to zero.
    /// </summary>
    [ObservableProperty]
    private double _minimum;

    [ObservableProperty]
    private double _maximum;

    [ObservableProperty]
    private double _step;

    public bool IsDiscrete { get; private set; }

    /// <summary>The discrete choices, when the backend reports a stepped setting.</summary>
    public IReadOnlyList<int> Steps { get; private set; } = Array.Empty<int>();

    public int? DefaultValue { get; private set; }

    /// <summary>
    /// The chosen discrete value, used when the backend reports a stepped setting.
    /// Mirrors the combo box WPF shows in that case.
    /// </summary>
    public int? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (_selectedStep == value)
                return;

            _selectedStep = value;
            OnPropertyChanged();

            if (value is { } step)
                Value = step;
        }
    }

    private int? _selectedStep;

    /// <summary>Current value with its unit, matching WPF's label format.</summary>
    public string ValueText => IsDiscrete
        ? $"{Value:0} {Unit}"
        : $"{Value:+0;-0;0} {Unit}";

    /// <summary>Adopts a value straight from the backend, including its range.</summary>
    public void Adopt(StepperValue? stepper)
    {
        if (!stepper.HasValue)
        {
            // Unsupported by this machine: WPF collapses the control, so nothing is
            // shown and nothing is written.
            IsSupported = false;
            OnPropertyChanged(nameof(IsSupported));
            return;
        }

        var s = stepper.Value;
        IsSupported = true;
        IsDiscrete = s.Steps is { Length: > 0 };
        Steps = IsDiscrete ? s.Steps : Array.Empty<int>();
        DefaultValue = s.DefaultValue;

        // The range must be published before the value, otherwise Avalonia clamps the
        // value against the previous range while binding.
        Minimum = s.Min;
        Maximum = s.Max;
        Step = s.Step;

        // A discrete setting picks from the reported choices; only a continuous one is
        // clamped to a range.
        if (IsDiscrete)
        {
            SelectedStep = s.Value;
            Value = s.Value;
        }
        else
        {
            Value = Clamp(s.Value);
        }

        OnPropertyChanged(nameof(IsSupported));
        OnPropertyChanged(nameof(IsDiscrete));
        OnPropertyChanged(nameof(Steps));
    }

    /// <summary>Returns the edited value as a backend stepper, or null if unsupported.</summary>
    public StepperValue? ToStepper()
    {
        if (!IsSupported)
            return null;

        var rounded = (int)Math.Round(Value);
        return new StepperValue(
            rounded,
            (int)Minimum,
            (int)Maximum,
            (int)Step,
            IsDiscrete ? [.. Steps] : [],
            DefaultValue);
    }

    /// <summary>WPF snaps to the tick and then clamps to the reported range.</summary>
    private double Clamp(double value)
    {
        if (Step > 0)
            value = Math.Round(value / Step, MidpointRounding.AwayFromZero) * Step;

        return Math.Clamp(value, Minimum, Maximum);
    }

    partial void OnValueChanged(double value)
    {
        if (!IsSupported)
            return;

        var clamped = Clamp(value);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            Value = clamped;
            return;
        }

        OnPropertyChanged(nameof(ValueText));
    }
}/// <summary>
/// Custom Mode settings, reproducing WPF's <c>GodModeSettingsWindow</c> against the
/// real <see cref="IGodModeController"/>.
/// <para>
/// The user-facing name is Custom Mode; the backend keeps <c>PowerModeState.GodMode</c>,
/// which is deliberately not renamed.
/// </para>
/// <para>
/// Unlike the previous version of this ViewModel, nothing here is invented: every row
/// is populated from the <c>GodModePreset</c> the controller reports, and rows the
/// hardware does not support stay hidden rather than showing a fabricated number.
/// </para>
/// </summary>
public partial class CustomModeSettingsViewModel : DialogViewModelBase
{
    private readonly IMainThreadDispatcher _dispatcher;
    private IGodModeController? _controller;
    private GodModeState _state;
    private Guid _activePresetId;

    [ObservableProperty]
    private bool _isSupported;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _vantageWarning;

    [ObservableProperty]
    private string? _legionZoneWarning;

    /// <summary>Name of the preset the controller reports as active.</summary>
    [ObservableProperty]
    private string _activePresetName = string.Empty;

    public CustomModeSettingViewModel CpuLongTermPowerLimit { get; } = New("CPU Long Term Power Limit", "W");
    public CustomModeSettingViewModel CpuShortTermPowerLimit { get; } = New("CPU Short Term Power Limit", "W");
    public CustomModeSettingViewModel CpuPeakPowerLimit { get; } = New("CPU Peak Power Limit", "W");
    public CustomModeSettingViewModel CpuCrossLoading { get; } = New("CPU Cross Loading Power Limit", "W");
    public CustomModeSettingViewModel CpuPl1Tau { get; } = New("CPU PL1 Tau", "s");
    public CustomModeSettingViewModel ApuSpptPowerLimit { get; } = New("APUsSPT Power Limit", "W");
    public CustomModeSettingViewModel CpuTemperatureLimit { get; } = New("CPU Temperature Limit", "°C");
    public CustomModeSettingViewModel GpuPowerBoost { get; } = New("GPU Dynamic Boost", "W");
    public CustomModeSettingViewModel GpuConfigurableTgp { get; } = New("GPU Configurable TGP", "W");
    public CustomModeSettingViewModel GpuTemperatureLimit { get; } = New("GPU Temperature Limit", "°C");
    public CustomModeSettingViewModel GpuTotalProcessingPowerOffset { get; } = New("GPU Total Processing Power Target On AC Offset", "W");
    public CustomModeSettingViewModel GpuToCpuDynamicBoost { get; } = New("GPU To CPU Dynamic Boost", "W");
    public CustomModeSettingViewModel MaxValueOffset { get; } = New("Max Value Offset", "%");
    public CustomModeSettingViewModel MinValueOffset { get; } = New("Min Value Offset", "%");

    /// <summary>Fan full speed, which WPF exposes as a toggle rather than a stepper.</summary>
    [ObservableProperty]
    private bool _fanFullSpeed;

    /// <summary>The fan curve editor, hidden while full speed is on as WPF does.</summary>
    public FanCurveViewModel FanCurve { get; } = new();

    /// <summary>True when full speed is active, so the curve editor can be hidden.</summary>
    public bool IsFanFullSpeedEnabled => IsFanFullSpeedSupported && FanFullSpeed;

    [ObservableProperty]
    private bool _isFanFullSpeedSupported;

    /// <summary>Every row, in the order WPF presents them: CPU, then GPU, then advanced.</summary>
    public IReadOnlyList<CustomModeSettingViewModel> AllSettings { get; }

    public IReadOnlyList<CustomModeSettingViewModel> CpuSettings { get; }

    public IReadOnlyList<CustomModeSettingViewModel> GpuSettings { get; }

    public IReadOnlyList<CustomModeSettingViewModel> AdvancedSettings { get; }

    public CustomModeSettingsViewModel(
        IMainThreadDispatcher dispatcher, IDialogService dialogs, ISensorsService sensors)
        : base(dialogs)
    {
        _dispatcher = dispatcher;

        // Each curve point shows the live speed of the fan it drives, so the sensor
        // feed is attached up front rather than resolved during initialisation.
        FanCurve.Attach(sensors);


        CpuSettings =
        [
            CpuLongTermPowerLimit, CpuShortTermPowerLimit, CpuPeakPowerLimit, CpuCrossLoading,
            CpuPl1Tau, ApuSpptPowerLimit, CpuTemperatureLimit
        ];

        GpuSettings =
        [
            GpuPowerBoost, GpuConfigurableTgp, GpuTemperatureLimit,
            GpuTotalProcessingPowerOffset, GpuToCpuDynamicBoost
        ];

        AdvancedSettings = [MaxValueOffset, MinValueOffset];

        AllSettings = [.. CpuSettings, .. GpuSettings, .. AdvancedSettings];
    }

    private static CustomModeSettingViewModel New(string title, string unit)
        => new() { Title = title, Unit = unit };

    /// <summary>
    /// Reads the authoritative state from the controller, then the Vantage and Legion
    /// Zone warnings, exactly as WPF's <c>RefreshAsync</c> does.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        try
        {
            // Resolved off the UI thread: IoCContainer.Resolve holds a global lock, and
            // the controller chain queries WMI, which needs the dispatcher to be free.
            _controller = await Task.Run(
                () => LoqNova.Lib.IoCContainer.Resolve<IGodModeController>()).ConfigureAwait(false);

            // The fan curve shows each fan's live speed, so it needs the sensor feed.
            var sensors = await Task.Run(
                () => LoqNova.Avalonia.IoCContainer.Resolve<LoqNova.Avalonia.Services.ISensorsService>())
                .ConfigureAwait(false);

            await _dispatcher.InvokeAsync(() => FanCurve.Attach(sensors)).ConfigureAwait(false);

            // WPF only shows a warning when the controller requires the software to be
            // closed *and* that software is actually running. Checking only the first
            // reported "Vantage must be closed" on a machine where Vantage is not even
            // installed.
            var needsVantage = await _controller.NeedsVantageDisabledAsync().ConfigureAwait(false);
            var needsLegionZone = await _controller.NeedsLegionZoneDisabledAsync().ConfigureAwait(false);

            var vantage = await Task.Run(async () =>
            {
                var disabler = LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.SoftwareDisabler.VantageDisabler>();
                return await disabler.GetStatusAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

            var legionZone = await Task.Run(async () =>
            {
                var disabler = LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.SoftwareDisabler.LegionZoneDisabler>();
                return await disabler.GetStatusAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

            var state = await _controller.GetStateAsync().ConfigureAwait(false);
            var activeId = await _controller.GetActivePresetIdAsync().ConfigureAwait(false);
            var activeName = await _controller.GetActivePresetNameAsync().ConfigureAwait(false);

            var vantageRunning = needsVantage && vantage == LoqNova.Lib.SoftwareStatus.Enabled;
            var legionZoneRunning = needsLegionZone && legionZone == LoqNova.Lib.SoftwareStatus.Enabled;

            await _dispatcher.InvokeAsync(() =>
            {
                IsSupported = true;
                ErrorMessage = null;
                VantageWarning = vantageRunning
                    ? "Lenovo Vantage must be closed before Custom Mode settings can be applied."
                    : null;
                LegionZoneWarning = legionZoneRunning
                    ? "Legion Zone must be closed before Custom Mode settings can be applied."
                    : null;
                ActivePresetName = activeName ?? "Preset";
                ApplyState(state, activeId);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                IsSupported = false;
                ErrorMessage = ex.Message;
            }).ConfigureAwait(false);
        }
    }

    private void ApplyState(GodModeState state, Guid activePresetId)
    {
        _state = state;
        _activePresetId = activePresetId;

        if (!state.Presets.TryGetValue(activePresetId, out var preset))
            return;

        CpuLongTermPowerLimit.Adopt(preset.CPULongTermPowerLimit);
        CpuShortTermPowerLimit.Adopt(preset.CPUShortTermPowerLimit);
        CpuPeakPowerLimit.Adopt(preset.CPUPeakPowerLimit);
        CpuCrossLoading.Adopt(preset.CPUCrossLoadingPowerLimit);
        CpuPl1Tau.Adopt(preset.CPUPL1Tau);
        ApuSpptPowerLimit.Adopt(preset.APUsPPTPowerLimit);
        CpuTemperatureLimit.Adopt(preset.CPUTemperatureLimit);

        GpuPowerBoost.Adopt(preset.GPUPowerBoost);
        GpuConfigurableTgp.Adopt(preset.GPUConfigurableTGP);
        GpuTemperatureLimit.Adopt(preset.GPUTemperatureLimit);
        GpuTotalProcessingPowerOffset.Adopt(preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline);
        GpuToCpuDynamicBoost.Adopt(preset.GPUToCPUDynamicBoost);

        MaxValueOffset.Adopt(preset.MaxValueOffset is { } mx ? new StepperValue(mx, 0, 100, 1, [], null) : null);
        MinValueOffset.Adopt(preset.MinValueOffset is { } mn ? new StepperValue(mn, -100, 0, 1, [], null) : null);

        // WPF hides the whole toggle when the preset reports it as unsupported.
        IsFanFullSpeedSupported = preset.FanFullSpeed.HasValue;
        FanFullSpeed = preset.FanFullSpeed ?? false;

        FanCurve.Adopt(preset.FanTableInfo);
        OnPropertyChanged(nameof(IsFanFullSpeedEnabled));
    }

    /// <summary>
    /// WPF's apply path: rebuild the active preset from the edited controls, write the
    /// state, ensure Custom Mode is selected, apply, then re-read so the UI shows what
    /// the machine accepted rather than what was asked for.
    /// </summary>
    partial void OnFanFullSpeedChanged(bool value)
        => OnPropertyChanged(nameof(IsFanFullSpeedEnabled));

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsBusy || _controller is null)
            return;

        try
        {
            IsBusy = true;

            var presets = new Dictionary<Guid, GodModePreset>(_state.Presets);
            if (!presets.TryGetValue(_activePresetId, out var preset))
                return;

            var updated = preset with
            {
                CPULongTermPowerLimit = CpuLongTermPowerLimit.ToStepper(),
                CPUShortTermPowerLimit = CpuShortTermPowerLimit.ToStepper(),
                CPUPeakPowerLimit = CpuPeakPowerLimit.ToStepper(),
                CPUCrossLoadingPowerLimit = CpuCrossLoading.ToStepper(),
                CPUPL1Tau = CpuPl1Tau.ToStepper(),
                APUsPPTPowerLimit = ApuSpptPowerLimit.ToStepper(),
                CPUTemperatureLimit = CpuTemperatureLimit.ToStepper(),
                GPUPowerBoost = GpuPowerBoost.ToStepper(),
                GPUConfigurableTGP = GpuConfigurableTgp.ToStepper(),
                GPUTemperatureLimit = GpuTemperatureLimit.ToStepper(),
                GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = GpuTotalProcessingPowerOffset.ToStepper(),
                GPUToCPUDynamicBoost = GpuToCpuDynamicBoost.ToStepper(),
                FanFullSpeed = IsFanFullSpeedSupported ? FanFullSpeed : null,
                FanTableInfo = FanCurve.BuildFanTableInfo(preset.FanTableInfo),
                MaxValueOffset = MaxValueOffset.IsSupported ? (int?)MaxValueOffset.Value : null,
                MinValueOffset = MinValueOffset.IsSupported ? (int?)MinValueOffset.Value : null
            };

            presets[_activePresetId] = updated;

            var newState = new GodModeState
            {
                ActivePresetId = _activePresetId,
                Presets = new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, GodModePreset>(presets)
            };

            await _controller.SetStateAsync(newState).ConfigureAwait(false);

            // Custom Mode has to be selected for the limits to take effect. Resolved
            // off the UI thread for the same reason as the controller above.
            var powerMode = await Task.Run(
                () => LoqNova.Lib.IoCContainer.Resolve<PowerModeFeature>()).ConfigureAwait(false);

            if (await powerMode.GetStateAsync().ConfigureAwait(false) != PowerModeState.GodMode)
                await powerMode.SetStateAsync(PowerModeState.GodMode).ConfigureAwait(false);

            await _controller.ApplyStateAsync().ConfigureAwait(false);

            await InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => InitializeAsync();
}
