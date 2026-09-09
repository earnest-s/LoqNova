// ============================================================================
// AudioVisualizerEffect.cs
//
// Audio visualizer using PRESET-DRIVEN zone colors + per-zone brightness
// driven by FREQUENCY-BAND spectral analysis.
//
// PIPELINE:
// 1. Audio Capture (WASAPI loopback -> mono ring buffer)
// 2. Overlapped FFT analysis (1024-point, Hann window, 256-sample hop)
// 3. 4-band spectral energy (RMS of power) with per-band peak-tracking AGC
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

    // ========================================================================
    // FREQUENCY BAND DEFINITIONS (log-spaced, calculated from ACTUAL sample rate)
    // ========================================================================
    // Zone 1: Sub-bass + Bass      -> 20 Hz  - 250 Hz
    // Zone 2: Low-Mid              -> 250 Hz - 600 Hz
    // Zone 3: Mid / Upper-Mid      -> 600 Hz - 2500 Hz
    // Zone 4: High / Presence      -> 2500 Hz - 12000 Hz
    private (int binStart, int binEnd)[] _bandBins;

    // ========================================================================
    // TEMPORAL SMOOTHING (per-band attack/release)
    // ========================================================================
    private const float AttackTimeMs = 15f;     // fast for transients (kicks, snares)
    private const float ReleaseTimeMs = 60f;    // fast fall, no linger
    private const float NoiseFloor = 0.00001f;  // per-bin noise floor
    private const float GateThreshold = 0.08f;  // normalized energy must exceed this to drive envelope

    // ========================================================================
    // RUNTIME STATE
    // ========================================================================
    private int _sampleRate = 48000;            // will be set from actual capture format
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
    // PER-BAND STATE
    // ========================================================================
    private readonly float[] _bandEnergy = new float[4];
    private readonly float[] _bandAgc = new float[4] { 0.0001f, 0.0001f, 0.0001f, 0.0001f }; // peak AGC
    private readonly float[] _bandEnvelope = new float[4];

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
            
            // CRITICAL: Get ACTUAL sample rate from capture format
            _sampleRate = _capture.WaveFormat.SampleRate;
            _freqResolution = (float)_sampleRate / FftSize;
            _hopSize = Math.Max(64, _sampleRate / 200); // ~5ms hop, min 64
            
            // Recompute band bins for actual sample rate
            ComputeBandBins();
            
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

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // --- Perform overlapped FFT analysis when enough new samples accumulated ---
                bool haveNewAnalysis = false;
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
                        // --- Per-band PEAK-TRACKING AGC (fast attack, slow release) ---
                        // Track peaks, not mean - so quiet passages don't raise the noise floor
                        if (bandEnergies[b] > _bandAgc[b])
                            _bandAgc[b] = bandEnergies[b]; // instant attack on new peak
                        else
                            _bandAgc[b] *= 0.997f; // slow release (~333ms time constant)

                        // Normalize against PEAK level (with minimum floor)
                        float normDenom = Math.Max(_bandAgc[b] * 2.5f, 0.001f);
                        float normalized = Math.Clamp(bandEnergies[b] / normDenom, 0f, 1f);

                        // --- HARD GATE: only energy significantly above noise floor drives envelope ---
                        // This prevents broadband noise / quiet passages from activating zones
                        float gated = normalized > GateThreshold ? normalized : 0f;

                        // --- Per-band attack/release on GATED signal ---
                        if (gated > _bandEnvelope[b])
                            _bandEnvelope[b] += (gated - _bandEnvelope[b]) * attackCoeff;
                        else
                            _bandEnvelope[b] += (gated - _bandEnvelope[b]) * releaseCoeff;

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

                // ~60 fps frame rate - use actual elapsed time for smoother timing
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
    // BAND BIN COMPUTATION (from actual sample rate)
    // ========================================================================
    private void ComputeBandBins()
    {
        // Log-spaced frequency bands appropriate for keyboard visualizer
        // Zone 1: Bass        40 - 250 Hz
        // Zone 2: Low-Mid     250 - 600 Hz
        // Zone 3: Mid         600 - 2500 Hz
        // Zone 4: High        2500 - min(12000, sampleRate/2)
        
        float nyquist = _sampleRate / 2f;
        float fMax = Math.Min(12000f, nyquist);

        int Bin(float freq) => (int)Math.Clamp(Math.Round(freq / _freqResolution), 1, FftSize / 2 - 1);

        _bandBins = new (int, int)[]
        {
            (1, Bin(250f)),           // Zone 1: 20-250 Hz (bass/kick)
            (Bin(250f) + 1, Bin(600f)),      // Zone 2: 250-600 Hz (low-mid)
            (Bin(600f) + 1, Bin(2500f)),     // Zone 3: 600-2500 Hz (mid/upper-mid)
            (Bin(2500f) + 1, Bin(fMax))      // Zone 4: 2500-12000 Hz (high/treble)
        };

        if (Log.Instance.IsTraceEnabled)
        {
            for (int i = 0; i < 4; i++)
            {
                var (s, e) = _bandBins[i];
                float fStart = s * _freqResolution;
                float fEnd = e * _freqResolution;
                Log.Instance.Trace($"[AudioVisualizer] Zone {i+1}: {fStart:F0}-{fEnd:F0} Hz (bins {s}-{e})");
            }
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

        // 2. Compute power spectrum (single-sided, properly scaled)
        // For real input: power = 2 * (re^2 + im^2) / N^2 for bins 1..N/2-1
        // DC (bin 0) and Nyquist (bin N/2) are not doubled
        float scale = 2.0f / FftSize;
        for (int i = 0; i < FftSize / 2; i++)
        {
            double re = _fftReal[i];
            double im = _fftImag[i];
            float magSq = (float)(re * re + im * im);
            // Power spectral density: 2 * |X[k]|^2 / N^2 for k=1..N/2-1
            // For simplicity and correct energy, we use magnitude^2 * scale
            _magnitudes[i] = magSq * scale * scale;
        }

        // 3. Compute per-band energy (mean power in band = spectral energy density)
        for (int b = 0; b < 4; b++)
        {
            var (start, end) = _bandBins[b];
            int count = end - start + 1;
            if (count <= 0)
            {
                outBandEnergies[b] = 0f;
                continue;
            }

            double sumPower = 0.0;
            for (int i = start; i <= end; i++)
            {
                float power = _magnitudes[i];
                if (power > NoiseFloor)
                    sumPower += power;
            }

            // Mean power in band (proper spectral energy density)
            outBandEnergies[b] = (float)(sumPower / count);
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