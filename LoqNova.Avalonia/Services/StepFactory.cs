using System;
using System.Collections.Generic;
using LoqNova.Lib.Automation.Steps;
using LoqNova.Lib.Automation.Pipeline.Triggers;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Maps the step type names used by the UI onto the real backend step types. The
/// catalogue is derived from the actual types in LoqNova.Lib.Automation.Steps rather
/// than invented, and each entry is constructed with a valid default state so that
/// adding a step always yields something runnable.
/// </summary>
internal static class StepFactory
{
    /// <summary>Every step type the UI may add, in WPF's discovery order.</summary>
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
        nameof(InstantBootAutomationStep),
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
        nameof(WinKeyAutomationStep),
    ];

    /// <summary>Creates a backend step, or null when the name is not a real type.</summary>
    public static IAutomationStep? Create(string typeName) => typeName switch
    {
        nameof(AlwaysOnUsbAutomationStep) => new AlwaysOnUsbAutomationStep(),
        nameof(BatteryAutomationStep) => new BatteryAutomationStep(),
        nameof(BatteryNightChargeAutomationStep) => new BatteryNightChargeAutomationStep(),
        nameof(DeactivateGPUAutomationStep) => new DeactivateGPUAutomationStep(DeactivateGPUAutomationStepState.KillApps),
        nameof(DelayAutomationStep) => new DelayAutomationStep(new Structs.Delay(1)),
        nameof(DisplayBrightnessAutomationStep) => new DisplayBrightnessAutomationStep(),
        nameof(DpiScaleAutomationStep) => new DpiScaleAutomationStep(),
        nameof(FlipToStartAutomationStep) => new FlipToStartAutomationStep(),
        nameof(FnLockAutomationStep) => new FnLockAutomationStep(),
        nameof(GodModePresetAutomationStep) => new GodModePresetAutomationStep(Guid.Empty),
        nameof(HDRAutomationStep) => new HDRAutomationStep(),
        nameof(HybridModeAutomationStep) => new HybridModeAutomationStep(),
        nameof(InstantBootAutomationStep) => new InstantBootAutomationStep(),
        nameof(MacroAutomationStep) => new MacroAutomationStep(MacroAutomationStepState.Disable),
        nameof(MicrophoneAutomationStep) => new MicrophoneAutomationStep(),
        nameof(NotificationAutomationStep) => new NotificationAutomationStep(),
        nameof(OneLevelWhiteKeyboardBacklightAutomationStep) => new OneLevelWhiteKeyboardBacklightAutomationStep(),
        nameof(OverclockDiscreteGPUAutomationStep) => new OverclockDiscreteGPUAutomationStep(OverclockDiscreteGPUAutomationStepState.Overclock),
        nameof(OverDriveAutomationStep) => new OverDriveAutomationStep(),
        nameof(PanelLogoBacklightAutomationStep) => new PanelLogoBacklightAutomationStep(),
        nameof(PlaySoundAutomationStep) => new PlaySoundAutomationStep(),
        nameof(PortsBacklightAutomationStep) => new PortsBacklightAutomationStep(),
        nameof(PowerModeAutomationStep) => new PowerModeAutomationStep(SystemPowerMode.Balanced),
        nameof(QuickActionAutomationStep) => new QuickActionAutomationStep(Guid.Empty),
        nameof(RefreshRateAutomationStep) => new RefreshRateAutomationStep(),
        nameof(ResolutionAutomationStep) => new ResolutionAutomationStep(),
        nameof(RGBKeyboardBacklightAutomationStep) => new RGBKeyboardBacklightAutomationStep(),
        nameof(RunAutomationStep) => new RunAutomationStep(string.Empty),
        nameof(SpeakerAutomationStep) => new SpeakerAutomationStep(),
        nameof(SpectrumKeyboardBacklightBrightnessAutomationStep) => new SpectrumKeyboardBacklightBrightnessAutomationStep(50),
        nameof(SpectrumKeyboardBacklightImportProfileAutomationStep) => new SpectrumKeyboardBacklightImportProfileAutomationStep(),
        nameof(SpectrumKeyboardBacklightProfileAutomationStep) => new SpectrumKeyboardBacklightProfileAutomationStep(),
        nameof(TouchpadLockAutomationStep) => new TouchpadLockAutomationStep(),
        nameof(TurnOffMonitorsAutomationStep) => new TurnOffMonitorsAutomationStep(),
        nameof(TurnOffWiFiAutomationStep) => new TurnOffWiFiAutomationStep(),
        nameof(TurnOnWiFiAutomationStep) => new TurnOnWiFiAutomationStep(),
        nameof(WhiteKeyboardBacklightAutomationStep) => new WhiteKeyboardBacklightAutomationStep(),
        nameof(WinKeyAutomationStep) => new WinKeyAutomationStep(),
        _ => null
    };

    /// <summary>Every trigger the UI may offer, from the real backend types.</summary>
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
        new PowerModeAutomationPipelineTrigger(SystemPowerMode.Balanced),
        new ProcessesAreRunningAutomationPipelineTrigger([]),
        new ProcessesStopRunningAutomationPipelineTrigger(),
        new SessionLockAutomationPipelineTrigger(),
        new SessionUnlockAutomationPipelineTrigger(),
        new TimeAutomationPipelineTrigger(),
        new UserInactivityAutomationPipelineTrigger(TimeSpan.FromSeconds(30)),
        new WiFiConnectedAutomationPipelineTrigger([]),
        new WiFiDisconnectedAutomationPipelineTrigger(),
    ];
}
