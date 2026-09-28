using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LoqNova.Avalonia.ViewModels.Controls;
using LoqNova.Lib;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Which dashboard features exist, mirroring WPF's <c>DashboardItem</c> enum and
/// <c>DashboardItemExtensions.GetControlAsync</c> mapping. Only features that map
/// onto a library <c>IFeature&lt;T&gt;</c> are declared here; the remaining WPF
/// items (discrete GPU, GPU overclock, turn off monitors) have bespoke controls in
/// WPF and are handled separately.
/// </summary>
public enum DashboardFeature
{
    PowerMode,
    BatteryMode,
    BatteryNightChargeMode,
    AlwaysOnUsb,
    InstantBoot,
    HybridMode,
    Hdr,
    OverDrive,
    PanelLogoBacklight,
    PortsBacklight,
    FnLock,
    WinKeyLock,
    TouchpadLock,
    Microphone,
    FlipToStart,
    DpiScale,
    WhiteKeyboardBacklight,
    Resolution,
    RefreshRate,
    DiscreteGpu,
    OverclockDiscreteGpu,
    TurnOffMonitors
}

public static class DashboardFeatureRegistry
{
    /// <summary>
    /// Creates the widget for a feature. Toggle widgets need the "on" and "off"
    /// states; choice widgets read their own options from the machine.
    /// </summary>
    public static FeatureWidgetViewModel Create(DashboardFeature feature, IMainThreadDispatcher dispatcher)
    {
        return feature switch
        {
            DashboardFeature.PowerMode => new FeatureChoiceWidgetViewModel<PowerModeState>(
                dispatcher, "Power Mode", "Bolt64"),

            DashboardFeature.BatteryMode => new FeatureChoiceWidgetViewModel<BatteryState>(
                dispatcher, "Battery Charge Mode", "Battery64"),

            DashboardFeature.BatteryNightChargeMode => new FeatureToggleWidgetViewModel<BatteryNightChargeState>(
                dispatcher, "Night Charge", "Moon64",
                BatteryNightChargeState.On, BatteryNightChargeState.Off),

            DashboardFeature.AlwaysOnUsb => new FeatureChoiceWidgetViewModel<AlwaysOnUSBState>(
                dispatcher, "Always On USB", "Usb64"),

            DashboardFeature.InstantBoot => new FeatureChoiceWidgetViewModel<InstantBootState>(
                dispatcher, "Instant Boot", "Plug64"),

            DashboardFeature.HybridMode => new FeatureChoiceWidgetViewModel<HybridModeState>(
                dispatcher, "Hybrid Mode", "Leaf64"),

            DashboardFeature.Hdr => new FeatureToggleWidgetViewModel<HDRState>(
                dispatcher, "HDR", "Hdr64",
                HDRState.On, HDRState.Off),

            DashboardFeature.OverDrive => new FeatureToggleWidgetViewModel<OverDriveState>(
                dispatcher, "OverDrive", "SpeedHigh64",
                OverDriveState.On, OverDriveState.Off),

            DashboardFeature.PanelLogoBacklight => new FeatureToggleWidgetViewModel<PanelLogoBacklightState>(
                dispatcher, "Panel Logo", "Lightbulb64",
                PanelLogoBacklightState.On, PanelLogoBacklightState.Off),

            DashboardFeature.PortsBacklight => new FeatureToggleWidgetViewModel<PortsBacklightState>(
                dispatcher, "Ports Backlight", "UsbPlug64",
                PortsBacklightState.On, PortsBacklightState.Off),

            DashboardFeature.FnLock => new FeatureToggleWidgetViewModel<FnLockState>(
                dispatcher, "Fn Lock", "Keyboard64",
                FnLockState.On, FnLockState.Off),

            DashboardFeature.WinKeyLock => new FeatureToggleWidgetViewModel<WinKeyState>(
                dispatcher, "Win Key Lock", "Window64",
                WinKeyState.On, WinKeyState.Off),

            DashboardFeature.TouchpadLock => new FeatureToggleWidgetViewModel<TouchpadLockState>(
                dispatcher, "Touchpad Lock", "Tablet64",
                TouchpadLockState.On, TouchpadLockState.Off),

            DashboardFeature.Microphone => new FeatureToggleWidgetViewModel<MicrophoneState>(
                dispatcher, "Microphone", "Mic64",
                MicrophoneState.On, MicrophoneState.Off),

            DashboardFeature.FlipToStart => new FeatureToggleWidgetViewModel<FlipToStartState>(
                dispatcher, "Flip To Start", "Power64",
                FlipToStartState.On, FlipToStartState.Off),

            DashboardFeature.DpiScale => new FeatureChoiceWidgetViewModel<DpiScale>(
                dispatcher, "DPI Scale", "TextFontSize64"),

            DashboardFeature.WhiteKeyboardBacklight => new FeatureChoiceWidgetViewModel<WhiteKeyboardBacklightState>(
                dispatcher, "White Keyboard Backlight", "Keyboard64"),

            DashboardFeature.Resolution => new ResolutionWidgetViewModel(dispatcher),

            DashboardFeature.RefreshRate => new RefreshRateWidgetViewModel(dispatcher),

            DashboardFeature.DiscreteGpu => new DiscreteGpuWidgetViewModel(dispatcher),

            DashboardFeature.OverclockDiscreteGpu => new OverclockGpuWidgetViewModel(dispatcher),

            DashboardFeature.TurnOffMonitors => new TurnOffMonitorsWidgetViewModel(dispatcher),

            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null)
        };
    }

    /// <summary>WPF's default dashboard composition, in the same group order.</summary>
    public static IReadOnlyList<DashboardFeature> DefaultFeatures { get; } =
    [
        // Power group
        DashboardFeature.PowerMode,
        DashboardFeature.BatteryMode,
        DashboardFeature.BatteryNightChargeMode,
        DashboardFeature.AlwaysOnUsb,
        DashboardFeature.InstantBoot,
        DashboardFeature.FlipToStart,
        // Graphics group
        DashboardFeature.HybridMode,
        DashboardFeature.DiscreteGpu,
        DashboardFeature.OverclockDiscreteGpu,
        // Display group
        DashboardFeature.Resolution,
        DashboardFeature.RefreshRate,
        DashboardFeature.DpiScale,
        DashboardFeature.Hdr,
        DashboardFeature.OverDrive,
        DashboardFeature.TurnOffMonitors,
        // Other group
        DashboardFeature.Microphone,
        DashboardFeature.WhiteKeyboardBacklight,
        DashboardFeature.PanelLogoBacklight,
        DashboardFeature.PortsBacklight,
        DashboardFeature.TouchpadLock,
        DashboardFeature.FnLock,
        DashboardFeature.WinKeyLock
    ];

    /// <summary>Creates and initializes every widget, hiding the unsupported ones.</summary>
    public static async Task<List<FeatureWidgetViewModel>> CreateAllAsync(
        IReadOnlyList<DashboardFeature> features,
        IMainThreadDispatcher dispatcher)
    {
        var widgets = new List<FeatureWidgetViewModel>(features.Count);

        foreach (var feature in features)
        {
            var widget = Create(feature, dispatcher);

            try
            {
                await widget.InitializeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                widget.ErrorMessage = ex.Message;
            }

            widgets.Add(widget);
        }

        return widgets;
    }
}
