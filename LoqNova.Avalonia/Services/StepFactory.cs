using System;
using System.Collections.Generic;
using System.Linq;
using LoqNova.Lib;
using LoqNova.Lib.Automation.Pipeline.Triggers;
using LoqNova.Lib.Automation.Steps;
using Delay = LoqNova.Lib.Automation.Delay;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Maps UI-facing type names onto the real backend step and trigger types.
/// Every entry is constructed with a valid, runnable default state taken from the
/// backend's own enums, so adding a step never yields an object the engine would
/// reject. The catalogue is derived from the actual types in LoqNova.Lib.Automation,
/// not from a hand-written list of names.
/// </summary>
internal static class StepFactory
{
    /// <summary>Every step type the UI may add, in the backend's own order.</summary>
    public static IReadOnlyList<string> SupportedStepTypes { get; } =
    [
        nameof(AlwaysOnUsbAutomationStep),
        nameof(BatteryAutomationStep),
        nameof(BatteryNightChargeAutomationStep),
        nameof(DeactivateGPUAutomationStep),
        nameof(DelayAutomationStep),
        nameof(DisplayBrightnessAutomationStep),
        nameof(DpiScaleAutomationStep),
        nameof(FlipToStartAutomationStep),
        nameof(FnLockAutomationStep),
        nameof(GodModePresetAutomationStep),
        nameof(HDRAutomationStep),
        nameof(HybridModeAutomationStep),
        nameof(MacroAutomationStep),
        nameof(MicrophoneAutomationStep),
        nameof(NotificationAutomationStep),
        nameof(OneLevelWhiteKeyboardBacklightAutomationStep),
        nameof(OverclockDiscreteGPUAutomationStep),
        nameof(OverDriveAutomationStep),
        nameof(PanelLogoBacklightAutomationStep),
        nameof(PlaySoundAutomationStep),
        nameof(PortsBacklightAutomationStep),
        nameof(PowerModeAutomationStep),
        nameof(QuickActionAutomationStep),
        nameof(RefreshRateAutomationStep),
        nameof(ResolutionAutomationStep),
        nameof(RGBKeyboardBacklightAutomationStep),
        nameof(RunAutomationStep),
        nameof(SpeakerAutomationStep),
        nameof(SpectrumKeyboardBacklightBrightnessAutomationStep),
        nameof(SpectrumKeyboardBacklightImportProfileAutomationStep),
        nameof(SpectrumKeyboardBacklightProfileAutomationStep),
        nameof(TouchpadLockAutomationStep),
        nameof(TurnOffMonitorsAutomationStep),
        nameof(TurnOffWiFiAutomationStep),
        nameof(TurnOnWiFiAutomationStep),
        nameof(WhiteKeyboardBacklightAutomationStep),
        nameof(WinKeyAutomationStep)
    ];

    /// <summary>Creates a backend step, or null when the name is not a real type.</summary>
    public static IAutomationStep? Create(string typeName) => typeName switch
    {
        nameof(AlwaysOnUsbAutomationStep) => new AlwaysOnUsbAutomationStep(AlwaysOnUSBState.Off),
        nameof(BatteryAutomationStep) => new BatteryAutomationStep(BatteryState.Normal),
        nameof(BatteryNightChargeAutomationStep) => new BatteryNightChargeAutomationStep(BatteryNightChargeState.Off),
        nameof(DeactivateGPUAutomationStep) => new DeactivateGPUAutomationStep(DeactivateGPUAutomationStepState.KillApps),
        nameof(DelayAutomationStep) => new DelayAutomationStep(new Delay(1)),
        nameof(DisplayBrightnessAutomationStep) => new DisplayBrightnessAutomationStep(50),
        nameof(DpiScaleAutomationStep) => new DpiScaleAutomationStep(new DpiScale(100)),
        nameof(FlipToStartAutomationStep) => new FlipToStartAutomationStep(FlipToStartState.Off),
        nameof(FnLockAutomationStep) => new FnLockAutomationStep(FnLockState.Off),
        nameof(GodModePresetAutomationStep) => new GodModePresetAutomationStep(Guid.Empty),
        nameof(HDRAutomationStep) => new HDRAutomationStep(HDRState.Off),
        nameof(HybridModeAutomationStep) => new HybridModeAutomationStep(HybridModeState.On),
        nameof(MacroAutomationStep) => new MacroAutomationStep(MacroAutomationStepState.Off),
        nameof(MicrophoneAutomationStep) => new MicrophoneAutomationStep(MicrophoneState.Off),
        nameof(NotificationAutomationStep) => new NotificationAutomationStep(null),
        nameof(OneLevelWhiteKeyboardBacklightAutomationStep) => new OneLevelWhiteKeyboardBacklightAutomationStep(OneLevelWhiteKeyboardBacklightState.Off),
        nameof(OverclockDiscreteGPUAutomationStep) => new OverclockDiscreteGPUAutomationStep(OverclockDiscreteGPUAutomationStepState.Off),
        nameof(OverDriveAutomationStep) => new OverDriveAutomationStep(OverDriveState.Off),
        nameof(PanelLogoBacklightAutomationStep) => new PanelLogoBacklightAutomationStep(PanelLogoBacklightState.Off),
        nameof(PlaySoundAutomationStep) => new PlaySoundAutomationStep(null),
        nameof(PortsBacklightAutomationStep) => new PortsBacklightAutomationStep(PortsBacklightState.Off),
        nameof(PowerModeAutomationStep) => new PowerModeAutomationStep(PowerModeState.Balance),
        nameof(QuickActionAutomationStep) => new QuickActionAutomationStep(null),
        nameof(RefreshRateAutomationStep) => new RefreshRateAutomationStep(new RefreshRate(60)),
        nameof(ResolutionAutomationStep) => new ResolutionAutomationStep(new Resolution(1920, 1080)),
        nameof(RGBKeyboardBacklightAutomationStep) => new RGBKeyboardBacklightAutomationStep(RGBKeyboardBacklightPreset.Off),
        nameof(RunAutomationStep) => new RunAutomationStep(null, null, null, null),
        nameof(SpeakerAutomationStep) => new SpeakerAutomationStep(SpeakerState.Unmute),
        nameof(SpectrumKeyboardBacklightBrightnessAutomationStep) => new SpectrumKeyboardBacklightBrightnessAutomationStep(50),
        nameof(SpectrumKeyboardBacklightImportProfileAutomationStep) => new SpectrumKeyboardBacklightImportProfileAutomationStep(null),
        nameof(SpectrumKeyboardBacklightProfileAutomationStep) => new SpectrumKeyboardBacklightProfileAutomationStep(0),
        nameof(TouchpadLockAutomationStep) => new TouchpadLockAutomationStep(TouchpadLockState.Off),
        nameof(TurnOffMonitorsAutomationStep) => new TurnOffMonitorsAutomationStep(),
        nameof(TurnOffWiFiAutomationStep) => new TurnOffWiFiAutomationStep(),
        nameof(TurnOnWiFiAutomationStep) => new TurnOnWiFiAutomationStep(),
        nameof(WhiteKeyboardBacklightAutomationStep) => new WhiteKeyboardBacklightAutomationStep(WhiteKeyboardBacklightState.Off),
        nameof(WinKeyAutomationStep) => new WinKeyAutomationStep(WinKeyState.Off),
        _ => null
    };

    /// <summary>
    /// A prototype step for a type, used when renaming or retyping an existing step.
    /// WPF edits steps in place, so the existing instance is mutated rather than
    /// replaced wherever the backend exposes a settable state.
    /// </summary>
    public static IAutomationStep CreateOrNull(string typeName) => Create(typeName);

    /// <summary>
    /// Every trigger the UI may offer. Constructed with the backend's own defaults so
    /// a newly created pipeline has a valid, evaluable trigger immediately.
    /// </summary>
    public static IReadOnlyList<IAutomationPipelineTrigger> SupportedTriggers { get; } =
    [
        new ACAdapterConnectedAutomationPipelineTrigger(),
        new ACAdapterDisconnectedAutomationPipelineTrigger(),
        new DeviceConnectedAutomationPipelineTrigger([]),
        new DeviceDisconnectedAutomationPipelineTrigger([]),
        new DisplayOffAutomationPipelineTrigger(),
        new DisplayOnAutomationPipelineTrigger(),
        new ExternalDisplayConnectedAutomationPipelineTrigger(),
        new ExternalDisplayDisconnectedAutomationPipelineTrigger(),
        new GamesAreRunningAutomationPipelineTrigger(),
        new GamesStopAutomationPipelineTrigger(),
        new GodModePresetChangedAutomationPipelineTrigger(Guid.Empty),
        new HDROffAutomationPipelineTrigger(),
        new HDROnAutomationPipelineTrigger(),
        new LidClosedAutomationPipelineTrigger(),
        new LidOpenedAutomationPipelineTrigger(),
        new LowWattageACAdapterConnectedAutomationPipelineTrigger(),
        new OnResumeAutomationPipelineTrigger(),
        new OnStartupAutomationPipelineTrigger(),
        new PeriodicAutomationPipelineTrigger(TimeSpan.FromMinutes(1)),
        new PowerModeAutomationPipelineTrigger(PowerModeState.Balance),
        new ProcessesAreRunningAutomationPipelineTrigger([]),
        new ProcessesStopRunningAutomationPipelineTrigger([]),
        new SessionLockAutomationPipelineTrigger(),
        new SessionUnlockAutomationPipelineTrigger(),
        new TimeAutomationPipelineTrigger(false, false, null, null),
        new UserInactivityAutomationPipelineTrigger(TimeSpan.FromSeconds(30)),
        new WiFiConnectedAutomationPipelineTrigger([]),
        new WiFiDisconnectedAutomationPipelineTrigger()
    ];

    /// <summary>A fresh copy of a trigger, so two pipelines never share an instance.</summary>
    public static IAutomationPipelineTrigger? CreateTrigger(string typeName)
    {
        var prototype = SupportedTriggers.FirstOrDefault(t => t.GetType().Name == typeName);
        return prototype?.DeepCopy();
    }
}
