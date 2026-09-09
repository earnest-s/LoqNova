// ============================================================================
// AudioVisualizerEffect.cs
//
// Audio visualizer using PRESET-DRIVEN zone colors + per-zone brightness
// driven by audio intensity. Uses the SAME zone color source as the main
// RGB system (RGBKeyboardSettings presets).
//
// PIPELINE:
// 1. Audio Capture (WASAPI loopback -> mono ring buffer)
// 2. RMS amplitude calculation (smoothed with attack/release envelope)
// 3. Normalized intensity (0-1) mapped to 4-zone level meter (same as VBR service)
// 4. Per-zone brightness applied to PRESET zone colors (not hard-coded palette)
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
/// Audio visualizer using PRESET zone colors + per-zone brightness from audio.
/// Zone colors come from the currently selected RGB preset (same source as main RGB system).
/// Audio signal controls ONLY per-zone brightness/intensity.
/// </summary>
public class AudioVisualizerEffect : ICustomRGBEffect, IDisposable
{
    // ========================================================================
    // CONSTANTS
    // ========================================================================
    private const int FftSize = 1024;
    private const int SampleRate = 48000;

    // Smoothing parameters (tunable) - TARGET: energetic response
    // Attack: ~20ms for punchy transients (kicks, snares)
    // Release: ~60ms for fast fall without flicker
    private const float AttackTimeMs = 20f;
    private const float ReleaseTimeMs = 60f;
    private const float NoiseFloor = 0.0001f;

    // ========================================================================
    // CONFIGURATION
    // ========================================================================
    private readonly int _speed; // 1-4, scales overall responsiveness
    private readonly ZoneColors _presetZoneColors; // from current RGB preset
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
    // ENVELOPE STATE (attack/release smoothing on GLOBAL intensity)
    // ========================================================================
    private float _envelopeLevel = 0f; // smoothed global intensity (0..1)

    // ========================================================================
    // CONSTRUCTOR
    // ========================================================================
    /// <summary>
    /// Creates an audio visualizer effect.
    /// </summary>
    /// <param name="presetZoneColors">Zone colors from the currently selected RGB preset (Zone1..Zone4).</param>
    /// <param name="speed">Speed 1-4, scales attack/release inversely (1=slow, 4=fast).</param>
    public AudioVisualizerEffect(ZoneColors? presetZoneColors = null, int speed = 2)
    {
        _speed = Math.Clamp(speed, 1, 4);
        // Use preset colors if provided, otherwise default to white (will be dimmed by audio)
        _presetZoneColors = presetZoneColors ?? ZoneColors.White;
    }

    // ========================================================================
    // INTERFACE
    // ========================================================================
    public CustomRGBEffectType Type => CustomRGBEffectType.AudioVisualizer;
    public string Description => "Audio-driven 4-zone visualizer using preset colors";
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

                // --- Normalize RMS (simple AGC with running estimate) ---
                // Track a slow-moving estimate of typical peak level
                _agcEstimate = _agcEstimate * 0.999f + rms * 0.001f;
                float normDenom = Math.Max(_agcEstimate * 3.0f, 0.01f); // scale factor
                float normalizedRms = Math.Clamp(rms / normDenom, 0f, 1f);

                // --- Attack/Release envelope on GLOBAL intensity ---
                if (normalizedRms > _envelopeLevel)
                    _envelopeLevel += (normalizedRms - _envelopeLevel) * attackCoeff; // attack
                else
                    _envelopeLevel += (normalizedRms - _envelopeLevel) * releaseCoeff; // release

                _envelopeLevel = Math.Clamp(_envelopeLevel, 0f, 1f);

                // --- Map envelope (0..1) to 4-zone level meter with INDEPENDENT zone brightness ---
                // Zone 1: 0-25%, Zone 2: 25-50%, Zone 3: 50-75%, Zone 4: 75-100%
                // Each zone gets its own brightness from the envelope
                float intensity = _envelopeLevel;

                float z1 = Math.Clamp(intensity / 0.25f, 0f, 1f);
                float z2 = Math.Clamp((intensity - 0.25f) / 0.25f, 0f, 1f);
                float z3 = Math.Clamp((intensity - 0.50f) / 0.25f, 0f, 1f);
                float z4 = Math.Clamp((intensity - 0.75f) / 0.25f, 0f, 1f);

                // Apply brightness to PRESET zone colors (not hard-coded palette)
                var colors = new ZoneColors(
                    ScaleColor(_presetZoneColors.Zone1, z1),
                    ScaleColor(_presetZoneColors.Zone2, z2),
                    ScaleColor(_presetZoneColors.Zone3, z3),
                    ScaleColor(_presetZoneColors.Zone4, z4)
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
    // AGC ESTIMATE STATE
    // ========================================================================
    private float _agcEstimate = 0.1f; // running estimate of typical peak

    // ========================================================================
    // AUDIO CALLBACK - fills mono ring buffer
    // ========================================================================
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture == null || e.BytesRecorded == 0) return;

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
            // Gentle breathing animation using PRESET colors at low brightness
            float phase = t * 0.5f;
            var colors = new ZoneColors(
                ScaleColor(_presetZoneColors.Zone1, 0.15f + 0.1f * MathF.Sin(phase + 0.0f)),
                ScaleColor(_presetZoneColors.Zone2, 0.15f + 0.1f * MathF.Sin(phase + 1.0f)),
                ScaleColor(_presetZoneColors.Zone3, 0.15f + 0.1f * MathF.Sin(phase + 2.0f)),
                ScaleColor(_presetZoneColors.Zone4, 0.15f + 0.1f * MathF.Sin(phase + 3.0f))
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