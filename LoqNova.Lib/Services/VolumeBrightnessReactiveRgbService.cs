using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LoqNova.Lib.Controllers;
using LoqNova.Lib.Controllers.CustomRGBEffects;
using LoqNova.Lib.Extensions;
using LoqNova.Lib.Listeners;
using LoqNova.Lib.Settings;
using LoqNova.Lib.SoftwareDisabler;
using LoqNova.Lib.System.Management;
using LoqNova.Lib.Utils;
using NAudio.CoreAudioApi;

namespace LoqNova.Lib.Services;

/// <summary>
/// Background 4-zone "reactive meter" layer driven by Windows master volume and
/// screen brightness. NOT an effect: never registered in CustomRGBEffectFactory,
/// never shown in effect lists. It modulates intensity only, using the user's
/// configured zone colors, through the SINGLE existing RGB output pipeline
/// (<see cref="RgbFrameDispatcher"/>):
///
///  - custom software effects: per-frame modulation vector applied inside
///    RgbFrameDispatcher.RenderAsync (effects keep animating untouched);
///  - firmware effects (Static/Breath/Wave/Smooth): temporary on-change overlay
///    of the configured zone colors scaled by the reactive intensities, restored
///    back to the user's preset shortly after values stop changing.
///
/// Priority is inherited from the existing architecture: the performance-mode
/// strobe sets IsOverrideActive and owns the keyboard; this service skips all
/// writes while an override is active.
/// </summary>
public class VolumeBrightnessReactiveRgbService(
    RGBKeyboardSettings settings,
    RgbFrameDispatcher dispatcher,
    CustomRGBEffectController customEffectController,
    DisplayBrightnessListener displayBrightnessListener,
    VantageDisabler vantageDisabler,
    RGBKeyboardBacklightController rgbKeyboardBacklightController)
{
    private const int TickMs = 33;               // ~30 fps smoothing ceiling
    private const float AttackPerTick = 0.18f;   // ~180 ms full rise
    private const float DecayPerTick = 0.10f;    // slightly softer fall
    private const int FirmwareHoldMs = 900;      // restore delay after last change (firmware-effect branch)

    private readonly object _gate = new();

    private bool _started;
    private bool _stopped;

    private double _volumePct = -1;   // negative = unknown
    private double? _brightnessPct;   // null = not supported / unknown

    private readonly float[] _target = new float[4];
    private readonly float[] _current = new float[4];

    private Timer? _smoothingTimer;
    private Timer? _holdTimer;
    private volatile bool _overlayActive;
    private int _tickBusy;

    private MMDeviceEnumerator? _mmDeviceEnumerator;
    private MMDevice? _device;
    private Action<AudioVolumeNotificationData>? _volumeHandler;
    private DeviceChangeNotifier? _deviceNotifier;
    private EventHandler<DisplayBrightnessListener.ChangedEventArgs>? _brightnessHandler;

    public Task StartStopIfNeededAsync()
    {
        return _started ? Task.CompletedTask : StartAsync();
    }

    public async Task StartAsync()
    {
        if (_started)
            return;

        _stopped = false;

        // Screen brightness: reuse the existing WMI event listener (push-based).
        _brightnessHandler = (_, e) => SetBrightness(e.Brightness.Value);
        displayBrightnessListener.Changed += _brightnessHandler;
        _brightnessPct = await ReadBrightnessPercentAsync().ConfigureAwait(false);

        // Master volume: push-based CoreAudio endpoint notification.
        BindAudioDevice();
        _volumePct = ReadVolumePercent();

        RecomputeTargets();

        _started = true;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[DIAG] Reactive RGB service started. [volume={_volumePct:F0}%, brightness={(_brightnessPct?.ToString("F0") ?? "n/a")}%]");

        KickSmoothing();
        ScheduleFirmwareRestore();
    }

    public async Task StopAsync()
    {
        if (!_started)
            return;

        _stopped = true;
        _started = false;

        if (_brightnessHandler is not null)
            displayBrightnessListener.Changed -= _brightnessHandler;
        _brightnessHandler = null;

        UnbindAudioDevice();

        lock (_gate)
        {
            _smoothingTimer?.Dispose();
            _smoothingTimer = null;
            _holdTimer?.Dispose();
            _holdTimer = null;
            _overlayActive = false;
        }

        dispatcher.SetReactiveIntensity(1f, 1f, 1f, 1f);

        // Best-effort restore of the user's RGB state on shutdown.
        try
        {
            if (!dispatcher.IsOverrideActive &&
                await vantageDisabler.GetStatusAsync().ConfigureAwait(false) != SoftwareStatus.Enabled &&
                settings.Store.State.SelectedPreset != RGBKeyboardBacklightPreset.Off)
            {
                await rgbKeyboardBacklightController.RefreshCurrentPresetAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] Reactive RGB shutdown restore skipped.", ex);
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[DIAG] Reactive RGB service stopped.");
    }

    // ────────────────────────── inputs ──────────────────────────

    private void BindAudioDevice()
    {
        try
        {
            _mmDeviceEnumerator = new MMDeviceEnumerator();
            _deviceNotifier = new DeviceChangeNotifier(BindAudioDevice);
            _mmDeviceEnumerator.RegisterEndpointNotificationCallback(_deviceNotifier);

            _device = _mmDeviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            _volumeHandler = data => OnVolumeChangedPercent(data.MasterVolume * 100.0);
            _device.AudioEndpointVolume.OnVolumeNotification += _volumeHandler;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] Reactive RGB: audio endpoint unavailable.", ex);
        }
    }

    private void UnbindAudioDevice()
    {
        try
        {
            if (_device is not null && _volumeHandler is not null)
                _device.AudioEndpointVolume.OnVolumeNotification -= _volumeHandler;
            if (_mmDeviceEnumerator is not null && _deviceNotifier is not null)
                _mmDeviceEnumerator.UnregisterEndpointNotificationCallback(_deviceNotifier);
            _device?.Dispose();
        }
        catch { }
        finally
        {
            _device = null;
            _volumeHandler = null;
            _deviceNotifier = null;
            _mmDeviceEnumerator = null;
        }
    }

    private double ReadVolumePercent()
    {
        try
        {
            if (_device is null)
                BindAudioDevice();
            if (_device is not null)
                return _device.AudioEndpointVolume.MasterVolumeLevelScalar * 100.0;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] Reactive RGB: failed to read volume.", ex);
        }
        return -1;
    }

    private void OnVolumeChangedPercent(double pct)
    {
        _volumePct = Math.Clamp(pct, 0, 100);
        RecomputeTargets();
        KickSmoothing();
    }

    private static async Task<double?> ReadBrightnessPercentAsync()
    {
        try
        {
            var values = await WMI.ReadAsync("root\\WMI",
                $"SELECT * FROM WmiMonitorBrightness",
                pdc => Convert.ToDouble(pdc["CurrentBrightness"].Value)).ConfigureAwait(false);

            return values.FirstOrDefault();
        }
        catch
        {
            return null; // desktops / external-only displays may not expose brightness
        }
    }

    // ─────────────────────── computation ────────────────────────

    private void RecomputeTargets()
    {
        lock (_gate)
        {
            var vol = Math.Clamp(_volumePct, 0, 100);
            var bri = _brightnessPct.HasValue ? Math.Clamp(_brightnessPct.Value, 0, 100) : -1.0;

            for (var i = 0; i < 4; i++)
            {
                var fromVolume = Math.Clamp((vol - i * 25) / 25.0, 0, 1);
                var fromBrightness = bri < 0 ? 0 : Math.Clamp((bri - i * 25) / 25.0, 0, 1);
                _target[i] = (float)Math.Max(fromVolume, fromBrightness);
            }
        }
    }

    // ──────────────────────── rendering ─────────────────────────

    private void KickSmoothing()
    {
        lock (_gate)
        {
            if (_smoothingTimer is null)
                _smoothingTimer = new Timer(_ => _ = SmoothingTick(), null, 0, TickMs);
            else
                _smoothingTimer.Change(0, TickMs);
        }
    }

    private async Task SmoothingTick()
    {
        if (Interlocked.Exchange(ref _tickBusy, 1) != 0)
            return;

        try
        {
            float maxDelta;
            lock (_gate)
            {
                maxDelta = 0f;
                for (var i = 0; i < 4; i++)
                {
                    var t = _target[i];
                    var c = _current[i];
                    var n = c < t ? Math.Min(t, c + AttackPerTick) : Math.Max(t, c - DecayPerTick);
                    maxDelta = Math.Max(maxDelta, Math.Abs(t - n));
                    _current[i] = n;
                }
            }

            await RenderCurrentAsync().ConfigureAwait(false);

            if (maxDelta <= 0.001f)
            {
                // Converged — stop ticking until the next input event.
                lock (_gate)
                {
                    _smoothingTimer?.Dispose();
                    _smoothingTimer = null;
                }
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] Reactive RGB tick failed.", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _tickBusy, 0);
        }
    }

    private async Task RenderCurrentAsync()
    {
        if (_stopped)
            return;

        // Performance-mode strobe (or any override) owns the keyboard right now.
        if (dispatcher.IsOverrideActive)
        {
            RescheduleFirmwareRestore();
            return;
        }

        try
        {
            if (await vantageDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
                return;
        }
        catch
        {
            return;
        }

        var state = settings.Store.State;
        var preset = state.SelectedPreset;

        // User turned the keyboard off — respect that, stay idle.
        if (preset == RGBKeyboardBacklightPreset.Off)
        {
            CancelOverlay();
            dispatcher.SetReactiveIntensity(1f, 1f, 1f, 1f);
            return;
        }

        var presetDescription = state.Presets.GetValueOrDefault(
            preset, RGBKeyboardBacklightBacklightPresetDescription.Default);

        if (presetDescription.Effect.IsCustomEffect())
        {
            // Custom software effect: keep its animation alive and modulate every
            // generated frame through the dispatcher hook. One immediate refresh
            // pushes the new intensities through the same single pipeline.
            _overlayActive = false;
            CancelFirmwareRestore();
            dispatcher.SetReactiveIntensity(_current[0], _current[1], _current[2], _current[3]);

            if (customEffectController.IsEffectRunning)
                await customEffectController.SetColorsAsync(customEffectController.CurrentColors).ConfigureAwait(false);
        }
        else
        {
            // Firmware-driven effect: temporarily show the configured zone colors
            // scaled by the reactive intensities, then restore the preset after a
            // short hold. No competing writer — everything goes through the
            // dispatcher's normal render path.
            dispatcher.SetReactiveIntensity(1f, 1f, 1f, 1f);

            var zones = new ZoneColors
            {
                Zone1 = ScaleColor(presetDescription.Zone1, _current[0]),
                Zone2 = ScaleColor(presetDescription.Zone2, _current[1]),
                Zone3 = ScaleColor(presetDescription.Zone3, _current[2]),
                Zone4 = ScaleColor(presetDescription.Zone4, _current[3])
            };

            _overlayActive = true;
            await dispatcher.RenderAsync(zones).ConfigureAwait(false);
            ScheduleFirmwareRestore();
        }
    }

    private void ScheduleFirmwareRestore()
    {
        lock (_gate)
        {
            if (_overlayActive)
            {
                _holdTimer ??= new Timer(_ => _ = RestoreAfterHoldAsync(), null, Timeout.Infinite, Timeout.Infinite);
                _holdTimer.Change(FirmwareHoldMs, Timeout.Infinite);
            }
        }
    }

    private void RescheduleFirmwareRestore() => ScheduleFirmwareRestore();

    private void CancelFirmwareRestore()
    {
        lock (_gate)
        {
            _holdTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void CancelOverlay()
    {
        lock (_gate)
        {
            _overlayActive = false;
            _holdTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private async Task RestoreAfterHoldAsync()
    {
        if (_stopped)
            return;

        // Strobe/override took ownership — let its own recovery restore state.
        if (dispatcher.IsOverrideActive)
        {
            _overlayActive = false;
            return;
        }

        try
        {
            if (await vantageDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
                return;

            var state = settings.Store.State;
            var preset = state.SelectedPreset;
            if (preset == RGBKeyboardBacklightPreset.Off)
                return;

            var presetDescription = state.Presets.GetValueOrDefault(
                preset, RGBKeyboardBacklightBacklightPresetDescription.Default);

            if (presetDescription.Effect.IsCustomEffect())
                return; // custom branch manages itself

            _overlayActive = false;
            await rgbKeyboardBacklightController.RefreshCurrentPresetAsync().ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] Reactive RGB overlay restored preset. [preset={preset}]");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] Reactive RGB restore failed.", ex);
        }
    }

    private static RGBColor ScaleColor(RGBColor c, float f) => new(
        (byte)Math.Round(c.R * f),
        (byte)Math.Round(c.G * f),
        (byte)Math.Round(c.B * f));

    // ─────────────────────── NAudio adapters ────────────────────

    private sealed class DeviceChangeNotifier(Action onDefaultChanged) : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string deviceId) { }
        public void OnDeviceRemoved(string deviceId) { }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Console)
                onDefaultChanged();
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey propertyKey) { }
    }
}
