// ============================================================================
// AudioVisualizerEffect.cs
//
// Cumulative 4-Stage Frequency Progression Visualizer
//
// PIPELINE:
// 1. Audio Capture (WASAPI loopback -> mono ring buffer)
// 2. Overlapped FFT analysis (1024-point, Hann window, 256-sample hop)
// 3. SINGLE cumulative progression signal from spectral centroid/energy distribution
// 4. Cumulative zone mapping: Z1 = clamp(p, 0, 1), Z2 = clamp(p-1, 0, 1), ...
// 4. Per-zone attack/release on cumulative brightness
// 5. Preset color x brightness
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
/// Cumulative 4-stage frequency progression visualizer.
/// Frequency determines the progression stage (0..4).
/// Zones activate cumulatively: Z1 -> Z1+Z2 -> Z1+Z2+Z3 -> Z1+Z2+Z3+Z4.
/// Zone colors come from the currently selected RGB preset.
/// </summary>
public class AudioVisualizerEffect : ICustomRGBEffect, IDisposable
{
    // ========================================================================
    // AUDIO / FFT CONSTANTS
    // ========================================================================
    private const int FftSize = 1024;
    private const int DefaultHopSize = 256;       // ~5.3ms @ 48kHz
    private const int MinHopSize = 64;

    // ========================================================================
    // PROGRESSION MAPPING CONSTANTS
    // ========================================================================
    // Frequency range mapped to progression [0..4]
    private const float MinFreq = 40f;     // Hz - below this = no response
    private const float MaxFreq = 12000f;  // Hz - above this = full progression

    // ========================================================================
    // TEMPORAL SMOOTHING
    // ========================================================================
    private const float AttackTimeMs = 10f;    // fast for transients
    private const float ReleaseTimeMs = 80f;   // slower release for smooth decay
    private const float NoiseFloor = 0.00001f; // per-bin noise floor
    private const float MinResponseThreshold = 0.05f; // minimum progression to activate

    // ========================================================================
    // RUNTIME STATE
    // ========================================================================
    private int _sampleRate = 48000;            // set from actual capture format
    private float _freqResolution;              // sampleRate / FftSize
    private int _hopSize;                       // actual hop size in samples

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
    private readonly float[] _ringBuffer = new float[FftSize * 2];
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
    // PROGRESSION STATE
    // ========================================================================
    private float _progression = 0f;            // current progression [0..4]
    private float _progressionTarget = 0f;      // target from spectral analysis
    private float _smoothedProgression = 0f;    // after attack/release

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
    public string Description => "Cumulative 4-stage frequency progression visualizer using preset colors";
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

            // CRITICAL: Get ACTUAL sample rate from capture format
            _sampleRate = _capture.WaveFormat.SampleRate;
            _freqResolution = (float)_sampleRate / FftSize;
            _hopSize = Math.Max(MinHopSize, _sampleRate / 200); // ~5ms hop, min 64

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[AudioVisualizer] Started: sampleRate={_sampleRate}Hz, fftSize={FftSize}, hop={_hopSize}, freqRes={_freqResolution:F2}Hz/bin");

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
        long lastTicks = stopwatch.ElapsedTicks;
        double ticksPerSecond = Stopwatch.Frequency;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // --- Perform overlapped FFT analysis when enough new samples accumulated ---
                bool haveNewAnalysis = false;
                float progressionTarget = 0f;

                lock (_audioLock)
                {
                    if (_ringReady && _samplesSinceLastAnalysis >= _hopSize)
                    {
                        // Copy newest FftSize samples (ending at write position - 1)
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

                // --- FFT and progression analysis (outside lock) ---
                if (haveNewAnalysis)
                {
                    progressionTarget = ComputeProgressionFromSpectrum();
                }

                // --- Smooth progression with attack/release ---
                // Use actual elapsed time for frame-independent smoothing
                long nowTicks = stopwatch.ElapsedTicks;
                double dtMs = (nowTicks - lastTicks) * 1000.0 / ticksPerSecond;
                lastTicks = nowTicks;
                dtMs = Math.Clamp(dtMs, 1.0, 50.0); // clamp for stability

                float frameAttackCoeff = 1f - MathF.Exp(-(float)dtMs / (AttackTimeMs / speedFactor));
                float frameReleaseCoeff = 1f - MathF.Exp(-(float)dtMs / (ReleaseTimeMs / speedFactor));

                if (progressionTarget > _smoothedProgression)
                    _smoothedProgression += (progressionTarget - _smoothedProgression) * frameAttackCoeff;
                else
                    _smoothedProgression += (progressionTarget - _smoothedProgression) * frameReleaseCoeff;

                _smoothedProgression = Math.Clamp(_smoothedProgression, 0f, 4f);

                // --- Cumulative zone mapping ---
                float p = _smoothedProgression;

                // Gate: no response below threshold
                if (p < MinResponseThreshold) p = 0f;

                // Cumulative mapping: Z1 = clamp(p, 0, 1), Z2 = clamp(p-1, 0, 1), etc.
                float z1 = Math.Clamp(p, 0f, 1f);
                float z2 = Math.Clamp(p - 1f, 0f, 1f);
                float z3 = Math.Clamp(p - 2f, 0f, 1f);
                float z4 = Math.Clamp(p - 3f, 0f, 1f);

                // --- Apply brightness to PRESET zone colors ---
                var colors = new ZoneColors(
                    ScaleColor(_presetZoneColors.Zone1, z1),
                    ScaleColor(_presetZoneColors.Zone2, z2),
                    ScaleColor(_presetZoneColors.Zone3, z3),
                    ScaleColor(_presetZoneColors.Zone4, z4)
                );

                // --- Stale-frame handling: only render if we have newer analysis ---
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
    // SPECTRAL ANALYSIS -> PROGRESSION
    // ========================================================================
    private float ComputeProgressionFromSpectrum()
    {
        // 1. Apply window and copy to real/imag arrays
        for (int i = 0; i < FftSize; i++)
        {
            _fftReal[i] = _fftInput[i];
            _fftImag[i] = 0.0;
        }

        // 2. Cooley-Tukey radix-2 DIT FFT (in-place)
        Fft(_fftReal, _fftImag, FftSize);

        // 2. Compute power spectrum (single-sided, properly scaled)
        float scale = 2.0f / FftSize;
        for (int i = 0; i < FftSize / 2; i++)
        {
            double re = _fftReal[i];
            double im = _fftImag[i];
            float magSq = (float)(re * re + im * im);
            // Power spectral density: 2 * |X[k]|^2 / N^2
            _magnitudes[i] = magSq * scale * scale;
        }

        // 2. Compute SPECTRAL CENTROID (weighted mean frequency)
        // centroid = sum(freq * power) / sum(power)
        double sumPower = 0.0;
        double weightedSum = 0.0;

        int maxBin = FftSize / 2;
        for (int i = 1; i < maxBin; i++) // skip DC bin 0
        {
            float power = _magnitudes[i];
            if (power > NoiseFloor)
            {
                float freq = i * _freqResolution;
                sumPower += power;
                weightedSum += freq * power;
            }
        }

        if (sumPower <= 0) return 0f;

        float centroid = (float)(weightedSum / sumPower);

        // Map centroid frequency to progression [0..4]
        // Linear mapping from MinFreq..MaxFreq to 0..4
        float progression = (centroid - MinFreq) / (MaxFreq - MinFreq) * 4f;

        return Math.Clamp(progression, 0f, 4f);
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

        var waveFormat = _capture.WaveFormat;
        int bytesPerSample = waveFormat.BitsPerSample / 8;
        int channels = waveFormat.Channels;
        int frameBytes = bytesPerSample * channels;
        int totalFrames = e.BytesRecorded / frameBytes;

        lock (_audioLock)
        {
            for (int f = 0; f < totalFrames; f++)
            {
                int offset = f * frameBytes;
                if (offset + bytesPerSample > e.BytesRecorded) break;

                float mono;
                if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
                {
                    float left = BitConverter.ToSingle(e.Buffer, offset);
                    float right = channels > 1 ? BitConverter.ToSingle(e.Buffer, offset + bytesPerSample) : left;
                    mono = (left + right) * 0.5f;
                }
                else if (waveFormat.Encoding == WaveFormatEncoding.Pcm && bytesPerSample == 2)
                {
                    short left = BitConverter.ToInt16(e.Buffer, offset);
                    short right = channels > 1 ? BitConverter.ToInt16(e.Buffer, offset + bytesPerSample) : left;
                    mono = (left + right) * 0.5f / 32768f;
                }
                else
                {
                    mono = 0f;
                }

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