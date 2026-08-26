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

    // Dedicated palette — visually distinct from performance-mode colors
    // (quiet=blue, balance=white, performance=red, godmode=purple).
    private static readonly RGBColor VolumeBaseColor = new(0, 200, 255);     // cyan
    private static readonly RGBColor BrightnessBaseColor = new(255, 210, 74); // warm amber

    private readonly object _gate = new();

    private bool _started;

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

            var baseColor = isBrightness ? BrightnessBaseColor : VolumeBaseColor;

            if (Log.Instance.IsTraceEnabled)
            {
                var src = isBrightness ? "brightness" : "volume";
                Log.Instance.Trace($"[DIAG] Reactive event START. [source={src}, value={_eventValue:F0}%]");
            }

            // Take temporary ownership exactly like the strobe does.
            dispatcher.IsOverrideActive = true;
            _restorePending = true;

            var sw = System.Diagnostics.Stopwatch.StartNew();

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
                var zones = BuildVisualization(baseColor, valueNow);
                await dispatcher.RenderAsync(zones, cancellationToken).ConfigureAwait(false);

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
                await dispatcher.RenderAsync(ZoneColors.Black, cancellationToken).ConfigureAwait(false);
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
    /// Dedicated 4-zone progressive visualization:
    /// Zone i fills linearly across its own 25 % stage of the source value.
    /// Colors are the event's dedicated base color scaled per zone — completely
    /// independent from the user's configured preset/effect colors.
    /// </summary>
    private static ZoneColors BuildVisualization(RGBColor baseColor, double pct)
    {
        Span<float> v = stackalloc float[4];
        for (var i = 0; i < 4; i++)
            v[i] = (float)Math.Clamp((pct - i * 25.0) / 25.0, 0.0, 1.0);

        return new ZoneColors
        {
            Zone1 = ScaleColor(baseColor, v[0]),
            Zone2 = ScaleColor(baseColor, v[1]),
            Zone3 = ScaleColor(baseColor, v[2]),
            Zone4 = ScaleColor(baseColor, v[3])
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
