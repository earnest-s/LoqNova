// ============================================================================
// AudioVisualizerEffect.cs
//
// Cumulative 4-Stage Frequency Progression Visualizer
//
// PIPELINE:
// 1. Audio Capture (WASAPI loopback -> mono ring buffer)
// 2. Overlapped FFT analysis (1024-point, Hann window, ~5ms hop)
// 3. 4-band spectral energy analysis with energy-density normalization
// 4. Single cumulative progression from band energies
// 5. Cumulative zone mapping: Z1 = clamp(p, 0, 1), Z2 = clamp(p-1, 0, 1), ...
// 6. Attack/release smoothing on progression
// 7. Preset color x brightness
// 8. Frame output via CustomRGBEffectController -> RgbFrameDispatcher -> HID
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
    // FREQUENCY BANDS (musically meaningful ranges)
    // ========================================================================
    // Zone 1: 20-250 Hz     (bass / low)
    // Zone 2: 250-2000 Hz   (mids)
    // Zone 3: 2000-4000 Hz  (upper mids / presence)
    // Zone 4: 4000-20000 Hz (treble / highs)
    private const float Band1MinFreq = 20f;
    private const float Band1MaxFreq = 250f;
    private const float Band2MinFreq = 250f;
    private const float Band2MaxFreq = 2000f;
    private const float Band3MinFreq = 2000f;
    private const float Band3MaxFreq = 4000f;
    private const float Band4MinFreq = 4000f;
    private const float Band4MaxFreq = 20000f;

    // ========================================================================
    // TEMPORAL SMOOTHING
    // ========================================================================
    private const float AttackTimeMs = 10f;     // fast for transients
    private const float ReleaseTimeMs = 80f;    // slower release for smooth decay
    private const float NoiseFloor = 0.00001f;  // per-bin noise floor
    private const float MinResponseThreshold = 0.05f; // minimum progression to activate

    // Progression smoothing (separate from per-zone, applied to progression value)
    private const float ProgressionAttackMs = 15f;
    private const float ProgressionReleaseMs = 100f;

    // ========================================================================
    // ADAPTIVE BASELINE (slow AGC for band normalization)
    // ========================================================================
    private const float BaselineAttackMs = 500f;   // slow rise
    private const float BaselineReleaseMs = 2000f; // slow decay
    private const float MinBaseline = 0.0001f;     // prevents division by zero / noise amplification

    // ========================================================================
    // RUNTIME STATE
    // ========================================================================
    private int _sampleRate = 48000;            // set from actual capture format
    private float _freqResolution;              // sampleRate / FftSize
    private int _hopSize;                       // actual hop size in samples

    // Pre-computed bin ranges for each band (computed once sample rate is known)
    private int _band1StartBin, _band1EndBin;
    private int _band2StartBin, _band2EndBin;
    private int _band3StartBin, _band3EndBin;
    private int _band4StartBin, _band4EndBin;
    private int _band1BinCount, _band2BinCount, _band3BinCount, _band4BinCount;

    // Adaptive baselines for each band (energy density)
    private float _band1Baseline = MinBaseline;
    private float _band2Baseline = MinBaseline;
    private float _band3Baseline = MinBaseline;
    private float _band4Baseline = MinBaseline;

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
    private float _smoothedProgression = 0f;    // after attack/release

    // ========================================================================
    // DIAGNOSTICS
    // ========================================================================
    private int _frameCounter;
    private readonly Stopwatch _diagStopwatch = Stopwatch.StartNew();

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

            // Compute bin ranges for each frequency band
            ComputeBandBinRanges();

            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"[AudioVisualizer] Started: sampleRate={_sampleRate}Hz, fftSize={FftSize}, hop={_hopSize}, freqRes={_freqResolution:F2}Hz/bin");
                Log.Instance.Trace($"[AudioVisualizer] Band bins: B1[{_band1StartBin}-{_band1EndBin}]({_band1BinCount}) B2[{_band2StartBin}-{_band2EndBin}]({_band2BinCount}) B3[{_band3StartBin}-{_band3EndBin}]({_band3BinCount}) B4[{_band4StartBin}-{_band4EndBin}]({_band4BinCount})");
            }

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

                float progAttackCoeff = 1f - MathF.Exp(-(float)dtMs / ProgressionAttackMs);
                float progReleaseCoeff = 1f - MathF.Exp(-(float)dtMs / ProgressionReleaseMs);

                if (progressionTarget > _smoothedProgression)
                    _smoothedProgression += (progressionTarget - _smoothedProgression) * progAttackCoeff;
                else
                    _smoothedProgression += (progressionTarget - _smoothedProgression) * progReleaseCoeff;

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

                await controller.SetColorsAsync(colors, cancellationToken).ConfigureAwait(false);

                // --- Throttled diagnostic logging ---
                _frameCounter++;
                if (_frameCounter % 120 == 0) // ~2Hz at 60fps
                {
                    LogDiagnostics(z1, z2, z3, z4);
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
    // COMPUTE BAND BIN RANGES FROM ACTUAL SAMPLE RATE
    // ========================================================================
    private void ComputeBandBinRanges()
    {
        // Nyquist frequency
        float nyquist = _sampleRate * 0.5f;
        float maxFreq = Math.Min(Band4MaxFreq, nyquist);

        _band1StartBin = Math.Max(1, (int)MathF.Ceiling(Band1MinFreq / _freqResolution));
        _band1EndBin = Math.Min(FftSize / 2 - 1, (int)MathF.Floor(Math.Min(Band1MaxFreq, maxFreq) / _freqResolution));
        _band2StartBin = Math.Max(_band1EndBin + 1, (int)MathF.Ceiling(Band2MinFreq / _freqResolution));
        _band2EndBin = Math.Min(FftSize / 2 - 1, (int)MathF.Floor(Math.Min(Band2MaxFreq, maxFreq) / _freqResolution));
        _band3StartBin = Math.Max(_band2EndBin + 1, (int)MathF.Ceiling(Band3MinFreq / _freqResolution));
        _band3EndBin = Math.Min(FftSize / 2 - 1, (int)MathF.Floor(Math.Min(Band3MaxFreq, maxFreq) / _freqResolution));
        _band4StartBin = Math.Max(_band3EndBin + 1, (int)MathF.Ceiling(Band4MinFreq / _freqResolution));
        _band4EndBin = Math.Min(FftSize / 2 - 1, (int)MathF.Floor(maxFreq / _freqResolution));

        _band1BinCount = Math.Max(1, _band1EndBin - _band1StartBin + 1);
        _band2BinCount = Math.Max(1, _band2EndBin - _band2StartBin + 1);
        _band3BinCount = Math.Max(1, _band3EndBin - _band3StartBin + 1);
        _band4BinCount = Math.Max(1, _band4EndBin - _band4StartBin + 1);
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

        // 3. Compute power spectrum (single-sided, properly scaled)
        // Power = 2 * |X[k]|^2 / N^2 for Hann window (coherent gain = 0.5)
        float scale = 2.0f / FftSize;
        for (int i = 0; i < FftSize / 2; i++)
        {
            double re = _fftReal[i];
            double im = _fftImag[i];
            float magSq = (float)(re * re + im * im);
            _magnitudes[i] = magSq * scale * scale;
        }

        // 4. Compute energy density (average power per bin) for each band
        float band1Energy = ComputeBandEnergyDensity(_band1StartBin, _band1EndBin, _band1BinCount);
        float band2Energy = ComputeBandEnergyDensity(_band2StartBin, _band2EndBin, _band2BinCount);
        float band3Energy = ComputeBandEnergyDensity(_band3StartBin, _band3EndBin, _band3BinCount);
        float band4Energy = ComputeBandEnergyDensity(_band4StartBin, _band4EndBin, _band4BinCount);

        // 5. Update adaptive baselines (slow AGC)
        UpdateBaselines(band1Energy, band2Energy, band3Energy, band4Energy);

        // 6. Normalize each band by its baseline
        // This makes bands comparable regardless of bin count or spectral tilt
        float norm1 = band1Energy / _band1Baseline;
        float norm2 = band2Energy / _band2Baseline;
        float norm3 = band3Energy / _band3Baseline;
        float norm4 = band4Energy / _band4Baseline;

        // 7. Apply soft threshold to suppress noise
        const float bandThreshold = 1.5f; // must exceed baseline by this factor
        if (norm1 < bandThreshold) norm1 = 0f; else norm1 = (norm1 - bandThreshold) / (10f - bandThreshold); // map [1.5, 10] -> [0, 1]
        if (norm2 < bandThreshold) norm2 = 0f; else norm2 = (norm2 - bandThreshold) / (10f - bandThreshold);
        if (norm3 < bandThreshold) norm3 = 0f; else norm3 = (norm3 - bandThreshold) / (10f - bandThreshold);
        if (norm4 < bandThreshold) norm4 = 0f; else norm4 = (norm4 - bandThreshold) / (10f - bandThreshold);

        norm1 = Math.Clamp(norm1, 0f, 1f);
        norm2 = Math.Clamp(norm2, 0f, 1f);
        norm3 = Math.Clamp(norm3, 0f, 1f);
        norm4 = Math.Clamp(norm4, 0f, 1f);

        // 8. Compute single progression from normalized band energies
        // Progression logic:
        // - Band 1 active -> progression toward 1.0
        // - Band 1 + Band 2 active -> progression toward 2.0
        // - Band 1 + Band 2 + Band 3 active -> progression toward 3.0
        // - All bands active -> progression toward 4.0
        //
        // Weight bands by their normalized energy.
        // Lower bands must be present for higher bands to contribute fully.

        float progression = 0f;

        // Zone 1: Bass presence
        if (norm1 > 0f)
        {
            progression = 1f * norm1; // 0..1
        }

        // Zone 2: Mids presence (requires bass)
        if (norm2 > 0f && norm1 > 0f)
        {
            // Bass anchors at 1.0, mids push toward 2.0
            progression = 1f + 1f * norm2 * norm1; // 1..2, gated by bass
        }

        // Zone 3: Upper mids presence (requires bass + mids)
        if (norm3 > 0f && norm1 > 0f && norm2 > 0f)
        {
            // Mids anchor at 2.0, upper mids push toward 3.0
            float midAnchor = Math.Min(1f, norm1 + norm2 * 0.5f); // how solid is the mid foundation
            progression = 2f + 1f * norm3 * midAnchor; // 2..3
        }

        // Zone 4: Treble presence (requires bass + mids + upper mids)
        if (norm4 > 0f && norm1 > 0f && norm2 > 0f && norm3 > 0f)
        {
            // Upper mids anchor at 3.0, treble pushes toward 4.0
            float highAnchor = Math.Min(1f, norm1 * 0.33f + norm2 * 0.33f + norm3 * 0.33f);
            progression = 3f + 1f * norm4 * highAnchor; // 3..4
        }

        // Alternative simpler progression that's more robust:
        // Weighted sum with cumulative gating
        //float progressionSimple = 0f;
        //if (norm1 > 0) progressionSimple += 1f * norm1;
        //if (norm1 > 0 && norm2 > 0) progressionSimple += 1f * norm2;
        //if (norm1 > 0 && norm2 > 0 && norm3 > 0) progressionSimple += 1f * norm3;
        //if (norm1 > 0 && norm2 > 0 && norm3 > 0 && norm4 > 0) progressionSimple += 1f * norm4;

        return Math.Clamp(progression, 0f, 4f);
    }

    private float ComputeBandEnergyDensity(int startBin, int endBin, int binCount)
    {
        double sumPower = 0.0;
        int validBins = 0;

        for (int i = startBin; i <= endBin; i++)
        {
            float power = _magnitudes[i];
            if (power > NoiseFloor)
            {
                sumPower += power;
                validBins++;
            }
        }

        if (validBins == 0) return 0f;
        return (float)(sumPower / validBins); // energy density = average power per bin
    }

    private void UpdateBaselines(float b1, float b2, float b3, float b4)
    {
        // Use actual elapsed time for frame-independent baseline adaptation
        float dtMs = (float)_diagStopwatch.Elapsed.TotalMilliseconds;
        _diagStopwatch.Restart();
        dtMs = Math.Clamp(dtMs, 1f, 50f);

        float baselineAttackCoeff = 1f - MathF.Exp(-dtMs / BaselineAttackMs);
        float baselineReleaseCoeff = 1f - MathF.Exp(-dtMs / BaselineReleaseMs);

        // Attack: baseline rises quickly to track signal
        // Release: baseline decays slowly to hold the reference level
        if (b1 > _band1Baseline) _band1Baseline += (b1 - _band1Baseline) * baselineAttackCoeff;
        else _band1Baseline += (b1 - _band1Baseline) * baselineReleaseCoeff;

        if (b2 > _band2Baseline) _band2Baseline += (b2 - _band2Baseline) * baselineAttackCoeff;
        else _band2Baseline += (b2 - _band2Baseline) * baselineReleaseCoeff;

        if (b3 > _band3Baseline) _band3Baseline += (b3 - _band3Baseline) * baselineAttackCoeff;
        else _band3Baseline += (b3 - _band3Baseline) * baselineReleaseCoeff;

        if (b4 > _band4Baseline) _band4Baseline += (b4 - _band4Baseline) * baselineAttackCoeff;
        else _band4Baseline += (b4 - _band4Baseline) * baselineReleaseCoeff;

        // Clamp baselines to minimum
        _band1Baseline = Math.Max(_band1Baseline, MinBaseline);
        _band2Baseline = Math.Max(_band2Baseline, MinBaseline);
        _band3Baseline = Math.Max(_band3Baseline, MinBaseline);
        _band4Baseline = Math.Max(_band4Baseline, MinBaseline);
    }

    // ========================================================================
    // DIAGNOSTICS
    // ========================================================================
    private void LogDiagnostics(float z1, float z2, float z3, float z4)
    {
        if (!Log.Instance.IsTraceEnabled) return;

        // Recompute band energies for logging (or store them)
        float band1Energy = ComputeBandEnergyDensity(_band1StartBin, _band1EndBin, _band1BinCount);
        float band2Energy = ComputeBandEnergyDensity(_band2StartBin, _band2EndBin, _band2BinCount);
        float band3Energy = ComputeBandEnergyDensity(_band3StartBin, _band3EndBin, _band3BinCount);
        float band4Energy = ComputeBandEnergyDensity(_band4StartBin, _band4EndBin, _band4BinCount);

        float norm1 = band1Energy / _band1Baseline;
        float norm2 = band2Energy / _band2Baseline;
        float norm3 = band3Energy / _band3Baseline;
        float norm4 = band4Energy / _band4Baseline;

        Log.Instance.Trace($"[AudioVisualizer] Bands: B1={band1Energy:E3}(norm={norm1:F2},base={_band1Baseline:E3}) B2={band2Energy:E3}(norm={norm2:F2},base={_band2Baseline:E3}) B3={band3Energy:E3}(norm={norm3:F2},base={_band3Baseline:E3}) B4={band4Energy:E3}(norm={norm4:F2},base={_band4Baseline:E3}) | Prog={_smoothedProgression:F2} | Z={z1:F2},{z2:F2},{z3:F2},{z4:F2}");
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