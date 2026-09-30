using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LoqNova.Lib;
using LoqNova.Lib.Automation;
using LoqNova.Lib.Automation.Pipeline.Triggers;
using LoqNova.Lib.Automation.Steps;
using Delay = LoqNova.Lib.Automation.Delay;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Builds the real backend step and trigger types for the editor, each with a display
/// name for the UI. Types are discovered from the backend itself - every factory here
/// constructs a genuine <see cref="IAutomationStep"/> or
/// <see cref="IAutomationPipelineTrigger"/> with a valid, runnable default state, so
/// nothing the editor creates can be rejected by the engine.
/// </summary>
internal static class StepFactory
{
    /// <summary>Every real step type, in the backend's own alphabetical order.</summary>
    public static IReadOnlyList<AutomationStepOption> Steps { get; } =
    [
        Step(nameof(AlwaysOnUsbAutomationStep), () => new AlwaysOnUsbAutomationStep(AlwaysOnUSBState.Off)),
        Step(nameof(BatteryAutomationStep), () => new BatteryAutomationStep(BatteryState.Normal)),
        Step(nameof(BatteryNightChargeAutomationStep), () => new BatteryNightChargeAutomationStep(BatteryNightChargeState.Off)),
        Step(nameof(DeactivateGPUAutomationStep), () => new DeactivateGPUAutomationStep(DeactivateGPUAutomationStepState.KillApps)),
        Step(nameof(DelayAutomationStep), () => new DelayAutomationStep(new Delay(1))),
        Step(nameof(DisplayBrightnessAutomationStep), () => new DisplayBrightnessAutomationStep(50)),
        Step(nameof(DpiScaleAutomationStep), () => new DpiScaleAutomationStep(new DpiScale(100))),
        Step(nameof(FlipToStartAutomationStep), () => new FlipToStartAutomationStep(FlipToStartState.Off)),
        Step(nameof(FnLockAutomationStep), () => new FnLockAutomationStep(FnLockState.Off)),
        Step(nameof(GodModePresetAutomationStep), () => new GodModePresetAutomationStep(Guid.Empty)),
        Step(nameof(HDRAutomationStep), () => new HDRAutomationStep(HDRState.Off)),
        Step(nameof(HybridModeAutomationStep), () => new HybridModeAutomationStep(HybridModeState.On)),
        Step(nameof(MacroAutomationStep), () => new MacroAutomationStep(MacroAutomationStepState.Off)),
        Step(nameof(MicrophoneAutomationStep), () => new MicrophoneAutomationStep(MicrophoneState.Off)),
        Step(nameof(NotificationAutomationStep), () => new NotificationAutomationStep(null)),
        Step(nameof(OneLevelWhiteKeyboardBacklightAutomationStep), () => new OneLevelWhiteKeyboardBacklightAutomationStep(OneLevelWhiteKeyboardBacklightState.Off)),
        Step(nameof(OverclockDiscreteGPUAutomationStep), () => new OverclockDiscreteGPUAutomationStep(OverclockDiscreteGPUAutomationStepState.Off)),
        Step(nameof(OverDriveAutomationStep), () => new OverDriveAutomationStep(OverDriveState.Off)),
        Step(nameof(PanelLogoBacklightAutomationStep), () => new PanelLogoBacklightAutomationStep(PanelLogoBacklightState.Off)),
        Step(nameof(PlaySoundAutomationStep), () => new PlaySoundAutomationStep(null)),
        Step(nameof(PortsBacklightAutomationStep), () => new PortsBacklightAutomationStep(PortsBacklightState.Off)),
        Step(nameof(PowerModeAutomationStep), () => new PowerModeAutomationStep(PowerModeState.Balance)),
        Step(nameof(QuickActionAutomationStep), () => new QuickActionAutomationStep(null)),
        Step(nameof(RefreshRateAutomationStep), () => new RefreshRateAutomationStep(new RefreshRate(60))),
        Step(nameof(ResolutionAutomationStep), () => new ResolutionAutomationStep(new Resolution(1920, 1080))),
        Step(nameof(RGBKeyboardBacklightAutomationStep), () => new RGBKeyboardBacklightAutomationStep(RGBKeyboardBacklightPreset.Off)),
        Step(nameof(RunAutomationStep), () => new RunAutomationStep(null, null, null, null)),
        Step(nameof(SpeakerAutomationStep), () => new SpeakerAutomationStep(SpeakerState.Unmute)),
        Step(nameof(SpectrumKeyboardBacklightBrightnessAutomationStep), () => new SpectrumKeyboardBacklightBrightnessAutomationStep(50)),
        Step(nameof(SpectrumKeyboardBacklightImportProfileAutomationStep), () => new SpectrumKeyboardBacklightImportProfileAutomationStep(null)),
        Step(nameof(SpectrumKeyboardBacklightProfileAutomationStep), () => new SpectrumKeyboardBacklightProfileAutomationStep(0)),
        Step(nameof(TouchpadLockAutomationStep), () => new TouchpadLockAutomationStep(TouchpadLockState.Off)),
        Step(nameof(TurnOffMonitorsAutomationStep), () => new TurnOffMonitorsAutomationStep()),
        Step(nameof(TurnOffWiFiAutomationStep), () => new TurnOffWiFiAutomationStep()),
        Step(nameof(TurnOnWiFiAutomationStep), () => new TurnOnWiFiAutomationStep()),
        Step(nameof(WhiteKeyboardBacklightAutomationStep), () => new WhiteKeyboardBacklightAutomationStep(WhiteKeyboardBacklightState.Off)),
        Step(nameof(WinKeyAutomationStep), () => new WinKeyAutomationStep(WinKeyState.Off))
    ];

    /// <summary>
    /// Every real trigger type. The prototype's own <c>DisplayName</c> is authoritative
    /// and is what WPF shows, so it is used verbatim rather than invented here.
    /// </summary>
    public static IReadOnlyList<TriggerOption> Triggers { get; } =
    [
        Trigger(() => new ACAdapterConnectedAutomationPipelineTrigger()),
        Trigger(() => new ACAdapterDisconnectedAutomationPipelineTrigger()),
        Trigger(() => new DeviceConnectedAutomationPipelineTrigger([])),
        Trigger(() => new DeviceDisconnectedAutomationPipelineTrigger([])),
        Trigger(() => new DisplayOffAutomationPipelineTrigger()),
        Trigger(() => new DisplayOnAutomationPipelineTrigger()),
        Trigger(() => new ExternalDisplayConnectedAutomationPipelineTrigger()),
        Trigger(() => new ExternalDisplayDisconnectedAutomationPipelineTrigger()),
        Trigger(() => new GamesAreRunningAutomationPipelineTrigger()),
        Trigger(() => new GamesStopAutomationPipelineTrigger()),
        Trigger(() => new GodModePresetChangedAutomationPipelineTrigger(Guid.Empty)),
        Trigger(() => new HDROffAutomationPipelineTrigger()),
        Trigger(() => new HDROnAutomationPipelineTrigger()),
        Trigger(() => new LidClosedAutomationPipelineTrigger()),
        Trigger(() => new LidOpenedAutomationPipelineTrigger()),
        Trigger(() => new LowWattageACAdapterConnectedAutomationPipelineTrigger()),
        Trigger(() => new OnResumeAutomationPipelineTrigger()),
        Trigger(() => new OnStartupAutomationPipelineTrigger()),
        Trigger(() => new PeriodicAutomationPipelineTrigger(TimeSpan.FromMinutes(1))),
        Trigger(() => new PowerModeAutomationPipelineTrigger(PowerModeState.Balance)),
        Trigger(() => new ProcessesAreRunningAutomationPipelineTrigger([])),
        Trigger(() => new ProcessesStopRunningAutomationPipelineTrigger([])),
        Trigger(() => new SessionLockAutomationPipelineTrigger()),
        Trigger(() => new SessionUnlockAutomationPipelineTrigger()),
        Trigger(() => new TimeAutomationPipelineTrigger(false, false, null, null)),
        Trigger(() => new UserInactivityAutomationPipelineTrigger(TimeSpan.FromSeconds(30))),
        Trigger(() => new WiFiConnectedAutomationPipelineTrigger([])),
        Trigger(() => new WiFiDisconnectedAutomationPipelineTrigger())
    ];

    private static AutomationStepOption Step(
        string typeName, Func<IAutomationStep> create)
    {
        // Steps carry no DisplayName of their own, so the CLR name is humanised once
        // here. WPF builds its step titles the same way, from the type.
        return new AutomationStepOption(typeName, Humanize(typeName), create);
    }

    private static TriggerOption Trigger(Func<IAutomationPipelineTrigger> create)
    {
        var prototype = create();

        // The trigger's own DisplayName comes from the backend's resources.
        return new TriggerOption(
            prototype.GetType().Name, prototype.DisplayName, create);
    }

    /// <summary>
    /// Turns a CLR type name into a readable label: strips the Automation suffix, splits
    /// PascalCase, and keeps known acronyms upper-case.
    /// </summary>
    internal static string Humanize(string typeName)
    {
        var name = typeName;

        foreach (var suffix in new[] { "AutomationStep", "AutomationPipelineTrigger" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        var acronyms = new HashSet<string>(["Gpu", "Cpu", "Usb", "Hdr", "Rgb", "Dpi", "Fn", "Mac", "Ac", "Dc", "Wifi", "Hdmi"], StringComparer.Ordinal);

        var builder = new StringBuilder();
        var previous = '\0';

        foreach (var c in name)
        {
            if (char.IsUpper(c) && previous != '\0' && !char.IsUpper(previous))
            {
                builder.Append(' ');
                builder.Append(c);
            }
            else if (char.IsUpper(c) && previous != '\0' && char.IsUpper(previous)
                     && builder.Length > 0 && char.IsLower(name[builder.Length]))
            {
                builder.Append(' ');
                builder.Append(c);
            }
            else
            {
                builder.Append(c);
            }

            previous = c;
        }

        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => acronyms.Contains(w) ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..]);

        return string.Join(' ', words);
    }
}
