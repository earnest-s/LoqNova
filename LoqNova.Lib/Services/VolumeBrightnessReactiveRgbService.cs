using System;
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
/// Independent temporary RGB event for Windows master VOLUME and screen BRIGHTNESS,
/// modelled on the performance-mode transition strobe:
///
///     input change → take temporary ownership (IsOverrideActive)
///                  → dedicated 4-zone visualization frames
///                  → release ownership
///                  → restore/resume the previous preset/effect via the
///                    existing RGB controller lifecycle.
///
/// It is NOT an effect (not in CustomRGBEffectFactory / effects list), does not
/// modulate the current effect's frames, and never persists settings.
/// The LATEST input always wins: repeated changes restart/update the same event.
/// Volume and brightness share this single controller — one writer, ever.
/// Priority: if the performance-mode strobe is running (or starts mid-event),
/// this event aborts immediately and yields; the strobe's own recovery restores RGB.
/// </summary>
public class VolumeBrightnessReactiveRgbService(
    RGBKeyboardSettings settings,
    RgbFrameDispatcher dispatcher,
    DisplayBrightnessListener displayBrightnessListener,
    VantageDisabler vantageDisabler,
    RGBKeyboardBacklightController rgbKeyboardBacklightController)
{
    private const int FrameMs = 33;              // visualization refresh (~30 fps)
    private const int HoldMs = 900;              // visible hold after the LAST change
    private static readonly TimeSpan MaxEventDuration = TimeSpan.FromSeconds(4); // hard safety cap

    // Dedicated LEVEL-METER palette — fixed for this temporary event, independent
    // from any preset/effect/performance-mode colors: low → high = green → red.
    private static readonly RGBColor MeterZone1 = new(0, 255, 0);       // Green
    private static readonly RGBColor MeterZone2 = new(255, 255, 0);     // Yellow
    private static readonly RGBColor MeterZone3 = new(255, 128, 0);     // Orange
    private static readonly RGBColor MeterZone4 = new(255, 0, 0);       // Red

    private readonly object _gate = new();

    private bool _started;
    private volatile bool _stopped;

    private double _volumePct = -1;   // negative = unknown
    private double? _brightnessPct;   // null = unsupported/unknown

    // Current temporary event state (single event, latest wins).
    private CancellationTokenSource? _eventCts;
    private Task? _eventTask;
    private double _eventValue;
    private bool _eventIsBrightness;
    private DateTime _lastChangeUtc;
    private volatile bool _restorePending;

    private MMDeviceEnumerator? _mmDeviceEnumerator;
    private MMDevice? _device;
    private AudioEndpointVolumeNotificationDelegate? _volumeHandler;
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

        // Screen brightness: reuse the existing WMI event listener (push-based).
        _brightnessHandler = (_, e) => OnInput(isBrightness: true, e.Brightness.Value);
        displayBrightnessListener.Changed += _brightnessHandler;
        _brightnessPct = await ReadBrightnessPercentAsync().ConfigureAwait(false);

        // Master volume: push-based CoreAudio endpoint notification.
        BindAudioDevice();
        _volumePct = ReadVolumePercent();

        _started = true;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[DIAG] Reactive RGB event service started. [volume={_volumePct:F0}%, brightness={(_brightnessPct?.ToString("F0") ?? "n/a")}%]");
    }

    public async Task StopAsync()
    {
        if (!_started)
            return;

        _started = false;
        _stopped = true;

        if (_brightnessHandler is not null)
            displayBrightnessListener.Changed -= _brightnessHandler;
        _brightnessHandler = null;

        UnbindAudioDevice();

        await CancelEventAsync().ConfigureAwait(false);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[DIAG] Reactive RGB event service stopped.");
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
            _volumeHandler = data => OnInput(isBrightness: false, data.MasterVolume * 100.0);
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

    private static async Task<double?> ReadBrightnessPercentAsync()
    {
        try
        {
            var values = await WMI.WmiMonitorBrightnessReader.ReadAsync().ConfigureAwait(false);
            return values.FirstOrDefault();
        }
        catch
        {
            return null; // desktops / external-only displays may not expose brightness
        }
    }

    /// <summary>
    /// Single entry point for both sources. Starts a new temporary event or updates
    /// the running one with the latest value (latest always wins, nothing queues).
    /// </summary>
    private void OnInput(bool isBrightness, double pct)
    {
        if (!_started)
            return;

        lock (_gate)
        {
            _eventValue = Math.Clamp(pct, 0, 100);
            _eventIsBrightness = isBrightness;
            _lastChangeUtc = DateTime.UtcNow;

            if (_eventTask is null || _eventTask.IsCompleted)
            {
                _eventCts?.Dispose();
                _eventCts = new CancellationTokenSource();
                var token = _eventCts.Token;
                var isBright = _eventIsBrightness;
                _eventTask = Task.Run(() => RunTemporaryEventAsync(isBright, token), token);
            }
        }
    }

    private async Task CancelEventAsync()
    {
        Task? task;
        lock (_gate)
        {
            _eventCts?.Cancel();
            task = _eventTask;
            _eventTask = null;
        }

        if (task is not null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    // ─────────────────── temporary event runner ─────────────────

    private async Task RunTemporaryEventAsync(bool isBrightness, CancellationToken cancellationToken)
    {
        try
        {
            // Never start on top of the performance-mode strobe.
            if (dispatcher.IsOverrideActive || rgbKeyboardBacklightController.IsTransitionActive)
                return;

            try
            {
                if (await vantageDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
                    return;
            }
            catch
            {
                return;
            }

            if (!dispatcher.IsSupported || settings.Store.State.SelectedPreset == RGBKeyboardBacklightPreset.Off)
                return;

            if (Log.Instance.IsTraceEnabled)
            {
                var src = isBrightness ? "brightness" : "volume";
                Log.Instance.Trace($"[DIAG] Reactive event START. [source={src}, value={_eventValue:F0}%]");
            }

            // Take temporary ownership exactly like the strobe does.
            dispatcher.IsOverrideActive = true;
            _restorePending = true;

            var sw = global::System.Diagnostics.Stopwatch.StartNew();

            while (!cancellationToken.IsCancellationRequested)
            {
                // Performance-mode strobe took over — yield immediately; its own
                // recovery will restore the user's RGB state.
                if (rgbKeyboardBacklightController.IsTransitionActive)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"[DIAG] Reactive event YIELD to performance transition.");
                    return;
                }

                var valueNow = _eventValue;
                var zones = BuildVisualization(valueNow);

                // [VBR] temporary diagnostics — prove calculation → hardware chain.
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[VBR] FRAME x={valueNow:F1}% Z1=({zones.Zone1.R},{zones.Zone1.G},{zones.Zone1.B}) Z2=({zones.Zone2.R},{zones.Zone2.G},{zones.Zone2.B}) Z3=({zones.Zone3.R},{zones.Zone3.G},{zones.Zone3.B}) Z4=({zones.Zone4.R},{zones.Zone4.G},{zones.Zone4.B})");

                // NOTE: must be ForceRenderAsync — RenderAsync intentionally drops
                // frames while IsOverrideActive is true (that guard exists so the
                // strobe can pause custom effects). Our event OWNS the override
                // window, exactly like the working performance-mode strobe.
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[VBR] BEFORE HARDWARE WRITE");

                await dispatcher.ForceRenderAsync(zones, cancellationToken).ConfigureAwait(false);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[VBR] HARDWARE WRITE COMPLETED");

                // Latest-wins loop: keep rendering until the value has been quiet
                // for HoldMs (rapid changes simply update the same event).
                var sinceLast = DateTime.UtcNow - _lastChangeUtc;
                if (sinceLast.TotalMilliseconds >= HoldMs || sw.Elapsed > MaxEventDuration)
                    break;

                await Task.Delay(FrameMs, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // End on black so the hand-off back to the preset is clean.
            if (!rgbKeyboardBacklightController.IsTransitionActive)
            {
                await dispatcher.ForceRenderAsync(ZoneColors.Black, cancellationToken).ConfigureAwait(false);
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[VBR] BLACK END-FRAME SENT");
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer value or service stop — newer path owns output.
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] Reactive event failed.", ex);
        }
        finally
        {
            // Release temporary ownership and restore/resume previous RGB state
            // through the existing controller lifecycle. If we yielded to a
            // performance transition, skip restoration (the strobe owns recovery).
            dispatcher.IsOverrideActive = false;

            if (_restorePending && !rgbKeyboardBacklightController.IsTransitionActive && !_stopped)
            {
                try
                {
                    await rgbKeyboardBacklightController.RefreshCurrentPresetAsync().ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"[DIAG] Reactive event END — previous RGB restored.");
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"[DIAG] Reactive event restore failed.", ex);
                }
            }

            _restorePending = false;

            lock (_gate)
            {
                if (_eventCts is not null && _eventTask is not null && _eventTask.IsCompleted)
                {
                    _eventCts.Dispose();
                    _eventCts = null;
                    _eventTask = null;
                }
            }
        }
    }

    /// <summary>
    /// Dedicated 4-zone LEVEL METER frame:
    ///   Zone1 = Green,  Zone2 = Yellow,  Zone3 = Orange,  Zone4 = Red.
    /// Input x ∈ [0,100] split into four 25 % stages; zone i fills linearly
    /// (clamp((x - 25i)/25)) and its fixed color is scaled per channel by that
    /// intensity. Fully independent from preset/effect colors.
    /// </summary>
    private static ZoneColors BuildVisualization(double pct)
    {
        float Stage(int i) => (float)Math.Clamp((pct - i * 25.0) / 25.0, 0.0, 1.0);

        return new ZoneColors
        {
            Zone1 = ScaleColor(MeterZone1, Stage(0)),
            Zone2 = ScaleColor(MeterZone2, Stage(1)),
            Zone3 = ScaleColor(MeterZone3, Stage(2)),
            Zone4 = ScaleColor(MeterZone4, Stage(3))
        };
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
