// ============================================================================
// AudioVisualizerEffect.cs
//
// 4-zone audio visualizer using the SAME zone/intensity concept as
// VolumeBrightnessReactiveRgbService: smooth level meter with per-zone
// brightness response driven by overall audio intensity.
//
// PIPELINE:
// 1. Audio Capture (WASAPI loopback -> mono ring buffer)
// 2. RMS amplitude calculation (smoothed with attack/release envelope)
// 3. Normalized intensity (0-1) mapped to 4-zone level meter (same as VBR service)
// 4. Per-zone color scaling (uses shared zone color scheme: green/yellow/orange/red)
// 5. Frame output via CustomRGBEffectController -> RgbFrameDispatcher -> HID
// ============================================================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LoqNova.Lib.Utils;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LoqNova.Lib.Controllers.CustomRGBEffects.Effects;

/// <summary>
/// Audio visualizer using the SAME 4-zone level meter concept as VolumeBrightnessReactiveRgbService.
/// Input: normalized audio intensity (0..1) -> 4 zones fill progressively (25% each).
/// Smoothing: attack/release envelope on the intensity signal.
/// Colors: shared zone color scheme (Zone1=Green, Zone2=Yellow, Zone3=Orange, Zone4=Red).
/// </summary>
public class AudioVisualizerEffect : ICustomRGBEffect, IDisposable
{
    // ========================================================================
    // CONSTANTS
    // ========================================================================
    private const int FftSize = 1024; // smaller FFT, lower latency
    private const int SampleRate = 48000;

    // Smoothing parameters (tunable)
    private const float AttackTimeMs = 10f;   // fast attack for responsiveness
    private const float ReleaseTimeMs = 200f; // slower release for smooth decay
    private const float NoiseFloor = 0.0001f; // ignore very low signals
    private const float MaxNormalizedInput = 2.0f; // clamp multiplier for AGC

    // ========================================================================
    // CONFIGURATION
    // ========================================================================
    private readonly int _speed; // 1-4, controls overall responsiveness
    private bool _disposed;

    // ========================================================================
    // AUDIO CAPTURE (WASAPI loopback)
    // ========================================================================
    private WasapiLoopbackCapture? _capture;
    private readonly object _audioLock = new();

    // Ring buffer for audio samples
    private readonly float[] _ringBuffer = new float[FftSize];
    private int _ringWritePos;
    private bool _ringReady;

    // ========================================================================
    // ENVELOPE STATE (attack/release smoothing)
    // ========================================================================
    private float _envelopeLevel = 0f;        // current smoothed intensity (0..1)
    private float _agcEstimate = 0.1f;        // running estimate of typical peak for normalization

    // ========================================================================
    // ZONE COLORS (shared with VolumeBrightnessReactiveRgbService)
    // ========================================================================
    // Zone1 = Green, Zone2 = Yellow, Zone3 = Orange, Zone4 = Red
    private static readonly RGBColor ZoneColor1 = new(0, 255, 0);       // Green
    private static readonly RGBColor ZoneColor2 = new(255, 255, 0);     // Yellow
    private static readonly RGBColor ZoneColor3 = new(255, 128, 0);     // Orange
    private static readonly RGBColor ZoneColor4 = new(255, 0, 0);       // Red

    private readonly RGBColor[] _zoneColors = [ZoneColor1, ZoneColor2, ZoneColor3, ZoneColor4];

    // ========================================================================
    // CONSTRUCTOR
    // ========================================================================
    public AudioVisualizerEffect(ZoneColors? zoneColors = null, int speed = 2)
    {
        _speed = Math.Clamp(speed, 1, 4);
        // zoneColors parameter kept for factory compatibility; 
        // we use shared VBR zone colors internally
    }

    // ========================================================================
    // INTERFACE
    // ========================================================================
    public CustomRGBEffectType Type => CustomRGBEffectType.AudioVisualizer;
    public string Description => "Audio-driven 4-zone level meter (shared with volume/brightness reactivity)";
    public bool RequiresInputMonitoring => false;
    public bool RequiresSystemAccess => true; // needs audio endpoint access

    // ========================================================================
    // MAIN LOOP
    // ========================================================================
    public async Task RunAsync(CustomRGBEffectController controller, CancellationToken cancellationToken)
    {
        // --- Start audio capture ---
        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[AudioVisualizer] Capture failed, using idle fallback: {ex.Message}");
            await RunIdleFallbackAsync(controller, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Pre-calculated attack/release coefficients per frame (~16ms @ 60fps)
        // Using 1 - exp(-dt / tau) where tau = time_constant / 1000
        // Speed 1=slow, 4=fast -> scale attack/release inversely
        float speedFactor = _speed switch
        {
            1 => 0.5f,
            2 => 1.0f,
            3 => 1.5f,
            4 => 2.0f,
            _ => 1.0f
        };

        float attackCoeff = 1f - MathF.Exp(-16f / (AttackTimeMs / speedFactor));
        float releaseCoeff = 1f - MathF.Exp(-16f / (ReleaseTimeMs / speedFactor));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // --- Get current audio RMS from ring buffer ---
                float rms = 0f;
                lock (_audioLock)
                {
                    if (_ringReady)
                    {
                        float sumSq = 0f;
                        int count = 0;
                        for (int i = 0; i < FftSize; i++)
                        {
                            float sample = _ringBuffer[i];
                            sumSq += sample * sample;
                            count++;
                        }
                        if (count > 0)
                            rms = MathF.Sqrt(sumSq / count);
                    }
                }

                // --- Apply noise floor ---
                if (rms < NoiseFloor)
                    rms = 0f;

                // --- AGC: update running estimate of typical peak (very slow) ---
                _agcEstimate = _agcEstimate * 0.999f + rms * 0.001f;

                // Normalize RMS against AGC estimate (with minimum floor)
                float normDenom = Math.Max(_agcEstimate * MaxNormalizedInput, 0.01f);
                float normalizedRms = Math.Clamp(rms / normDenom, 0f, 1f);

                if (normalizedRms > _envelopeLevel)
                    _envelopeLevel += (normalizedRms - _envelopeLevel) * attackCoeff; // attack
                else
                    _envelopeLevel += (normalizedRms - _envelopeLevel) * releaseCoeff; // release

                _envelopeLevel = Math.Clamp(_envelopeLevel, 0f, 1f);

                // --- Map envelope (0..1) to 4-zone level meter (same as VBR service) ---
                // Each zone = 25% range: Zone1=0-25%, Zone2=25-50%, Zone3=50-75%, Zone4=75-100%
                float intensity = _envelopeLevel;

                // Per-zone fill: clamp((intensity - zone_start) / 0.25, 0, 1)
                float z1 = Math.Clamp(intensity / 0.25f, 0f, 1f);
                float z2 = Math.Clamp((intensity - 0.25f) / 0.25f, 0f, 1f);
                float z3 = Math.Clamp((intensity - 0.50f) / 0.25f, 0f, 1f);
                float z4 = Math.Clamp((intensity - 0.75f) / 0.25f, 0f, 1f);

                // Scale zone colors by fill amount
                var colors = new ZoneColors(
                    ScaleColor(ZoneColor1, z1),
                    ScaleColor(ZoneColor2, z2),
                    ScaleColor(ZoneColor3, z3),
                    ScaleColor(ZoneColor4, z4)
                );

                // --- Output frame ---
                await controller.SetColorsAsync(colors, cancellationToken).ConfigureAwait(false);

                // ~60 fps frame rate
                await Task.Delay(16, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            StopCapture();
        }
    }

    // ========================================================================
    // AUDIO CALLBACK - fills mono ring buffer
    // ========================================================================
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture == null || e.BytesRecorded == 0) return;

        // WASAPI loopback gives 32-bit float stereo typically
        const int bytesPerSample = 4; // float32
        const int channels = 2;
        int frameBytes = bytesPerSample * channels;
        int totalFrames = e.BytesRecorded / frameBytes;

        lock (_audioLock)
        {
            for (int f = 0; f < totalFrames; f++)
            {
                int offset = f * frameBytes;
                if (offset + bytesPerSample > e.BytesRecorded) break;

                // Average left+right channels for mono
                float left = BitConverter.ToSingle(e.Buffer, offset);
                float right = BitConverter.ToSingle(e.Buffer, offset + bytesPerSample);
                float mono = (left + right) * 0.5f;

                _ringBuffer[_ringWritePos] = Math.Clamp(mono, -1f, 1f);
                _ringWritePos = (_ringWritePos + 1) % FftSize;
            }

            _ringReady = true;
        }
    }

    // ========================================================================
    // IDLE FALLBACK (when audio capture fails)
    // ========================================================================
    private async Task RunIdleFallbackAsync(CustomRGBEffectController controller, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!cancellationToken.IsCancellationRequested)
        {
            float t = (float)stopwatch.Elapsed.TotalSeconds;
            // Gentle breathing animation across zones
            float phase = t * 0.5f;
            var colors = new ZoneColors(
                ScaleColor(ZoneColor1, 0.15f + 0.1f * MathF.Sin(phase + 0.0f)),
                ScaleColor(ZoneColor2, 0.15f + 0.1f * MathF.Sin(phase + 1.0f)),
                ScaleColor(ZoneColor3, 0.15f + 0.1f * MathF.Sin(phase + 2.0f)),
                ScaleColor(ZoneColor4, 0.15f + 0.1f * MathF.Sin(phase + 3.0f))
            );
            await controller.SetColorsAsync(colors, cancellationToken).ConfigureAwait(false);
            await Task.Delay(33, cancellationToken).ConfigureAwait(false);
        }
    }

    // ========================================================================
    // HELPERS
    // ========================================================================
    private static RGBColor ScaleColor(RGBColor color, float brightness)
    {
        brightness = Math.Clamp(brightness, 0f, 1f);
        return new RGBColor(
            (byte)(color.R * brightness),
            (byte)(color.G * brightness),
            (byte)(color.B * brightness)
        );
    }

    private void StopCapture()
    {
        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            try { _capture.StopRecording(); } catch { }
            _capture.Dispose();
            _capture = null;
        }
    }

    // ========================================================================
    // DISPOSE
    // ========================================================================
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCapture();
        GC.SuppressFinalize(this);
    }
}