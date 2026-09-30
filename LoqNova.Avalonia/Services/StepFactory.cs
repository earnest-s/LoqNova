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
///
/// The catalogues are lazy and their entries hold factories, never instances. Building a
/// step resolves its feature from the global IoC container, and resolving 38 of them
/// eagerly - 28 more for triggers - happens under that container's lock and froze the
/// window when the page was opened.
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
    /// Every real trigger type. Display names are the backend's own resource strings,
    /// read here so no trigger instance has to be constructed just to name it.
    /// </summary>
    public static IReadOnlyList<TriggerOption> Triggers { get; } =
    [
        Trigger("ACAdapterConnectedAutomationPipelineTrigger", "When AC power adapter is connected", () => new ACAdapterConnectedAutomationPipelineTrigger()),
        Trigger("ACAdapterDisconnectedAutomationPipelineTrigger", "When AC power adapter is disconnected", () => new ACAdapterDisconnectedAutomationPipelineTrigger()),
        Trigger("DeviceConnectedAutomationPipelineTrigger", "When device is connected", () => new DeviceConnectedAutomationPipelineTrigger([])),
        Trigger("DeviceDisconnectedAutomationPipelineTrigger", "When device is disconnected", () => new DeviceDisconnectedAutomationPipelineTrigger([])),
        Trigger("DisplayOffAutomationPipelineTrigger", "When displays turn off", () => new DisplayOffAutomationPipelineTrigger()),
        Trigger("DisplayOnAutomationPipelineTrigger", "When displays turn on", () => new DisplayOnAutomationPipelineTrigger()),
        Trigger("ExternalDisplayConnectedAutomationPipelineTrigger", "When external display is connected", () => new ExternalDisplayConnectedAutomationPipelineTrigger()),
        Trigger("ExternalDisplayDisconnectedAutomationPipelineTrigger", "When external display is disconnected", () => new ExternalDisplayDisconnectedAutomationPipelineTrigger()),
        Trigger("GamesAreRunningAutomationPipelineTrigger", "When game is running", () => new GamesAreRunningAutomationPipelineTrigger()),
        Trigger("GamesStopAutomationPipelineTrigger", "When game closes", () => new GamesStopAutomationPipelineTrigger()),
        Trigger("GodModePresetChangedAutomationPipelineTrigger", "When Custom Mode preset changes", () => new GodModePresetChangedAutomationPipelineTrigger(Guid.Empty)),
        Trigger("HDROffAutomationPipelineTrigger", "When HDR turns off", () => new HDROffAutomationPipelineTrigger()),
        Trigger("HDROnAutomationPipelineTrigger", "When HDR turns on", () => new HDROnAutomationPipelineTrigger()),
        Trigger("LidClosedAutomationPipelineTrigger", "Lid closed", () => new LidClosedAutomationPipelineTrigger()),
        Trigger("LidOpenedAutomationPipelineTrigger", "Lid opened", () => new LidOpenedAutomationPipelineTrigger()),
        Trigger("LowWattageACAdapterConnectedAutomationPipelineTrigger", "When low wattage AC power adapter is connected", () => new LowWattageACAdapterConnectedAutomationPipelineTrigger()),
        Trigger("OnResumeAutomationPipelineTrigger", "On resume", () => new OnResumeAutomationPipelineTrigger()),
        Trigger("OnStartupAutomationPipelineTrigger", "On startup", () => new OnStartupAutomationPipelineTrigger()),
        Trigger("PeriodicAutomationPipelineTrigger", "Periodic action", () => new PeriodicAutomationPipelineTrigger(TimeSpan.FromMinutes(1))),
        Trigger("PowerModeAutomationPipelineTrigger", "When Power Mode is changed", () => new PowerModeAutomationPipelineTrigger(PowerModeState.Balance)),
        Trigger("ProcessesAreRunningAutomationPipelineTrigger", "When app starts", () => new ProcessesAreRunningAutomationPipelineTrigger([])),
        Trigger("ProcessesStopRunningAutomationPipelineTrigger", "When app closes", () => new ProcessesStopRunningAutomationPipelineTrigger([])),
        Trigger("SessionLockAutomationPipelineTrigger", "Session locked", () => new SessionLockAutomationPipelineTrigger()),
        Trigger("SessionUnlockAutomationPipelineTrigger", "Session unlocked", () => new SessionUnlockAutomationPipelineTrigger()),
        Trigger("TimeAutomationPipelineTrigger", "At specified time", () => new TimeAutomationPipelineTrigger(false, false, null, null)),
        Trigger("UserInactivityAutomationPipelineTrigger", "When user becomes inactive", () => new UserInactivityAutomationPipelineTrigger(TimeSpan.FromSeconds(30))),
        Trigger("WiFiConnectedAutomationPipelineTrigger", "When Wi-Fi is connected", () => new WiFiConnectedAutomationPipelineTrigger([])),
        Trigger("WiFiDisconnectedAutomationPipelineTrigger", "When Wi-Fi is disconnected", () => new WiFiDisconnectedAutomationPipelineTrigger())
    ];

    private static AutomationStepOption Step(
        string typeName, Func<IAutomationStep> create)
    {
        // Steps carry no DisplayName of their own, so the CLR name is humanised once
        // here. No instance is created: that would resolve the step's feature.
        return new AutomationStepOption(typeName, Humanize(typeName), create);
    }

    private static TriggerOption Trigger(
        string typeName, string displayName, Func<IAutomationPipelineTrigger> create)
    {
        return new TriggerOption(typeName, displayName, create);
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
            // Insert a space before an uppercase letter that starts a new word. The
            // index is bounds-checked: a trailing uppercase run has no next character.
            if (char.IsUpper(c) && previous != '\0' && !char.IsUpper(previous)
                && builder.Length < name.Length)
            {
                builder.Append(' ');
                builder.Append(c);
            }
            else if (char.IsUpper(c) && previous != '\0' && char.IsUpper(previous)
                     && builder.Length > 0 && builder.Length < name.Length
                     && char.IsLower(name[builder.Length]))
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
