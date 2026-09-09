// ============================================================================
// AudioVisualizerEffect.cs
//
// Audio visualizer using PRESET-DRIVEN zone colors + per-zone brightness
// driven by FREQUENCY-BAND spectral analysis.
// 
// PIPELINE:
// 1. Audio Capture (WASAPI loopback -> mono ring buffer)
// 2. Overlapped FFT analysis (1024-point, Hann window, 256-sample hop)
// 3. 4-band spectral energy (RMS of magnitude) with per-band AGC
// 4. Per-band attack/release envelopes
// 5. Per-zone brightness applied to PRESET zone colors
// 6. Frame output via CustomRGBEffectController -> RgbFrameDispatcher -> HID
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
/// Audio visualizer using PRESET zone colors + 4-band frequency analysis.
/// Zone colors come from the currently selected RGB preset (same source as main RGB system).
/// Audio signal is analyzed into 4 frequency bands, each driving independent zone brightness.
/// </summary>
public class AudioVisualizerEffect : ICustomRGBEffect, IDisposable
{
    // ========================================================================
    // AUDIO / FFT CONSTANTS
    // ========================================================================
    private const int FftSize = 1024;
    private const int HopSize = 256;              // 4x overlap -> ~5.3ms @ 48kHz
    private const int SampleRate = 48000;
    private const float FreqResolution = (float)SampleRate / FftSize; // ~46.875 Hz/bin

    // ========================================================================
    // FREQUENCY BAND DEFINITIONS (musically meaningful, log-spaced)
    // ========================================================================
    // Zone 1: Sub-bass + Bass      -> 20 Hz  - 250 Hz  (bins 1-5)
    // Zone 2: Low-Mid              -> 250 Hz - 500 Hz  (bins 6-10)
    // Zone 3: Mid / Upper-Mid      -> 500 Hz - 2000 Hz (bins 11-42)
    // Zone 4: High / Presence      -> 2000 Hz - 16000 Hz (bins 43-341)
    private static readonly (int binStart, int binEnd)[] BandBins = new[]
    {
        (1, 5),       // Zone 1: 20-250 Hz   (bass / kick)
        (6, 10),      // Zone 2: 250-500 Hz  (low-mid / bass guitar, low vocals)
        (11, 42),     // Zone 3: 500-2000 Hz (mid / vocals, snare body)
        (43, 341)     // Zone 4: 2000-16000 Hz (high / cymbals, hi-hats, transients)
    };

    // ========================================================================
    // TEMPORAL SMOOTHING (per-band attack/release)
    // ========================================================================
    private const float AttackTimeMs = 15f;    // very fast for transients
    private const float ReleaseTimeMs = 50f;   // fast fall, no linger
    private const float NoiseFloor = 0.00001f; // per-bin noise floor

    // ========================================================================
    // CONFIGURATION
    // ========================================================================
    private readonly int _speed;
    private readonly ZoneColors _presetZoneColors;
    private bool _disposed;

    // ========================================================================
    // AUDIO CAPTURE (WASAPI loopback)
    // ========================================================================
    private WasapiLoopbackCapture? _capture;
    private readonly object _audioLock = new();

    // Ring buffer for audio samples (must hold at least FftSize samples)
    private readonly float[] _ringBuffer = new float[FftSize * 2]; // double for safety
    private int _ringWritePos;
    private int _samplesSinceLastAnalysis;
    private bool _ringReady;

    // ========================================================================
    // FFT STATE (pre-allocated, reused)
    // ========================================================================
    private readonly float[] _hannWindow = new float[FftSize];
    private readonly float[] _fftInput = new float[FftSize];
    private readonly double[] _fftReal = new double[FftSize];
    private readonly double[] _fftImag = new double[FftSize];
    private readonly float[] _magnitudes = new float[FftSize / 2];

    // ========================================================================
    // PER-BAND STATE
    // ========================================================================
    private readonly float[] _bandEnergy = new float[4];       // current spectral energy
    private readonly float[] _bandAgc = new float[4] { 0.1f, 0.1f, 0.1f, 0.1f }; // per-band AGC
    private readonly float[] _bandEnvelope = new float[4];     // per-band attack/release

    // ========================================================================
    // THREAD SYNC (stale-frame handling)
    // ========================================================================
    private readonly object _analysisLock = new();
    private int _analysisVersion;
    private int _lastRenderedVersion;

    // ========================================================================
    // CONSTRUCTOR
    // ========================================================================
    public AudioVisualizerEffect(ZoneColors? presetZoneColors = null, int speed = 2)
    {
        _speed = Math.Clamp(speed, 1, 4);
        _presetZoneColors = presetZoneColors ?? ZoneColors.White;

        // Pre-compute Hann window
        for (int i = 0; i < FftSize; i++)
            _hannWindow[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
    }

    // ========================================================================
    // INTERFACE
    // ========================================================================
    public CustomRGBEffectType Type => CustomRGBEffectType.AudioVisualizer;
    public string Description => "Audio-driven 4-zone frequency visualizer using preset colors";
    public bool RequiresInputMonitoring => false;
    public bool RequiresSystemAccess => true;

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
                // --- Perform overlapped FFT analysis when enough new samples accumulated ---
                bool haveNewAnalysis = false;
                lock (_audioLock)
                {
                    if (_ringReady && _samplesSinceLastAnalysis >= HopSize)
                    {
                        // Copy latest FftSize samples (newest at end of ring buffer)
                        int startPos = (_ringWritePos - FftSize) % (FftSize * 2);
                        if (startPos < 0) startPos += FftSize * 2;

                        for (int i = 0; i < FftSize; i++)
                        {
                            int srcIdx = (startPos + i) % (FftSize * 2);
                            _fftInput[i] = _ringBuffer[srcIdx] * _hannWindow[i];
                        }

                        _samplesSinceLastAnalysis = 0;
                        haveNewAnalysis = true;
                    }
                }

                // --- FFT and band analysis (outside lock to minimize audio thread blocking) ---
                float[] bandEnergies = new float[4];
                if (haveNewAnalysis)
                {
                    ComputeFftAndBands(bandEnergies);
                }

                // --- Per-band AGC + attack/release + zone mapping ---
                float z1 = 0f, z2 = 0f, z3 = 0f, z4 = 0f;

                if (haveNewAnalysis)
                {
                    for (int b = 0; b < 4; b++)
                    {
                        // --- Per-band AGC (fast: ~200ms time constant) ---
                        _bandAgc[b] = _bandAgc[b] * 0.992f + bandEnergies[b] * 0.008f;
                        float normDenom = Math.Max(_bandAgc[b] * 3.0f, 0.001f);
                        float normalized = Math.Clamp(bandEnergies[b] / normDenom, 0f, 1f);

                        // --- Per-band attack/release ---
                        if (normalized > _bandEnvelope[b])
                            _bandEnvelope[b] += (normalized - _bandEnvelope[b]) * attackCoeff;
                        else
                            _bandEnvelope[b] += (normalized - _bandEnvelope[b]) * releaseCoeff;

                        _bandEnvelope[b] = Math.Clamp(_bandEnvelope[b], 0f, 1f);
                    }

                    // --- Map each band envelope to its zone ---
                    z1 = _bandEnvelope[0];
                    z2 = _bandEnvelope[1];
                    z3 = _bandEnvelope[2];
                    z4 = _bandEnvelope[3];

                    // Mark analysis as ready for rendering
                    lock (_analysisLock)
                        _analysisVersion++;
                }
                else
                {
                    // No new analysis this frame: apply release only
                    for (int b = 0; b < 4; b++)
                    {
                        _bandEnvelope[b] += (0f - _bandEnvelope[b]) * releaseCoeff;
                        _bandEnvelope[b] = Math.Clamp(_bandEnvelope[b], 0f, 1f);
                    }
                    z1 = _bandEnvelope[0];
                    z2 = _bandEnvelope[1];
                    z3 = _bandEnvelope[2];
                    z4 = _bandEnvelope[3];
                }

                // --- Apply brightness to PRESET zone colors ---
                var colors = new ZoneColors(
                    ScaleColor(_presetZoneColors.Zone1, z1),
                    ScaleColor(_presetZoneColors.Zone2, z2),
                    ScaleColor(_presetZoneColors.Zone3, z3),
                    ScaleColor(_presetZoneColors.Zone4, z4)
                );

                // --- Stale-frame handling: only render if we have newer analysis ---
                // This prevents processing stale frames when render loop is faster than analysis
                bool shouldRender;
                lock (_analysisLock)
                {
                    shouldRender = _analysisVersion > _lastRenderedVersion;
                    if (shouldRender) _lastRenderedVersion = _analysisVersion;
                }

                if (shouldRender)
                {
                    await controller.SetColorsAsync(colors, cancellationToken).ConfigureAwait(false);
                }

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
    // FFT + BAND ENERGY COMPUTATION
    // ========================================================================
    private void ComputeFftAndBands(float[] outBandEnergies)
    {
        // 1. Apply window and copy to real/imag arrays
        for (int i = 0; i < FftSize; i++)
        {
            _fftReal[i] = _fftInput[i];
            _fftImag[i] = 0.0;
        }

        // 2. Cooley-Tukey radix-2 DIT FFT (in-place)
        Fft(_fftReal, _fftImag, FftSize);

        // 3. Compute magnitude spectrum (single-sided)
        for (int i = 0; i < FftSize / 2; i++)
        {
            double re = _fftReal[i];
            double im = _fftImag[i];
            _magnitudes[i] = (float)Math.Sqrt(re * re + im * im) / FftSize;
        }

        // 4. Compute per-band energy (RMS of magnitude = spectral energy)
        for (int b = 0; b < 4; b++)
        {
            var (start, end) = BandBins[b];
            int count = end - start + 1;
            float sumSq = 0f;
            for (int i = start; i <= end; i++)
            {
                float m = _magnitudes[i];
                if (m > NoiseFloor)
                    sumSq += m * m;
            }
            outBandEnergies[b] = count > 0 ? MathF.Sqrt(sumSq / count) : 0f;
        }
    }

    // ========================================================================
    // COOLEY-TUKEY RADIX-2 DIT FFT (in-place)
    // ========================================================================
    private static void Fft(double[] real, double[] imag, int n)
    {
        // Bit-reversal permutation
        int j = 0;
        for (int i = 1; i < n - 1; i++)
        {
            int bit = n >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }
            j ^= bit;

            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        // Danielson-Lanczos
        for (int len = 2; len <= n; len <<= 1)
        {
            int halfLen = len >> 1;
            double angle = -2.0 * Math.PI / len;
            double wReal = Math.Cos(angle);
            double wImag = Math.Sin(angle);

            for (int i = 0; i < n; i += len)
            {
                double curReal = 1.0;
                double curImag = 0.0;
                for (int m = 0; m < halfLen; m++)
                {
                    int idxJ = i + m + halfLen;
                    double tReal = curReal * real[idxJ] - curImag * imag[idxJ];
                    double tImag = curReal * imag[idxJ] + curImag * real[idxJ];
                    real[idxJ] = real[i + m] - tReal;
                    imag[idxJ] = imag[i + m] - tImag;
                    real[i + m] += tReal;
                    imag[i + m] += tImag;

                    double nextReal = curReal * wReal - curImag * wImag;
                    curImag = curReal * wImag + curImag * wReal;
                    curReal = nextReal;
                }
            }
        }
    }

    // ========================================================================
    // AUDIO CALLBACK - fills mono ring buffer
    // ========================================================================
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture == null || e.BytesRecorded == 0) return;

        const int bytesPerSample = 4;
        const int channels = 2;
        int frameBytes = bytesPerSample * channels;
        int totalFrames = e.BytesRecorded / frameBytes;

        lock (_audioLock)
        {
            for (int f = 0; f < totalFrames; f++)
            {
                int offset = f * frameBytes;
                if (offset + bytesPerSample > e.BytesRecorded) break;

                float left = BitConverter.ToSingle(e.Buffer, offset);
                float right = BitConverter.ToSingle(e.Buffer, offset + bytesPerSample);
                float mono = (left + right) * 0.5f;

                _ringBuffer[_ringWritePos] = Math.Clamp(mono, -1f, 1f);
                _ringWritePos = (_ringWritePos + 1) % (FftSize * 2);
            }

            _samplesSinceLastAnalysis += totalFrames;
            if (!_ringReady && _ringWritePos >= FftSize)
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