// ============================================================================
// PerformanceModeTransitionEffect.cs
//
// Premium performance-mode keyboard transition (ROG / Predator style).
// Pattern per pulse: instant flash → sine breath-out → dark gap.
// 3 pulses total, then 0.5 s black hold before resuming.
//
// All output goes through RgbFrameDispatcher.ForceRenderAsync so the
// preview stays in sync automatically — no callback needed.
// ============================================================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LoqNova.Lib.Controllers.CustomRGBEffects;

namespace LoqNova.Lib.Controllers;

/// <summary>
/// Self-contained premium transition animation for performance mode changes.
/// All output goes through <see cref="RgbFrameDispatcher"/> — caller is
/// responsible for pausing and resuming any running RGB effect.
/// </summary>
public sealed class PerformanceModeTransitionEffect
{
    private const int PulseCount = 3;
    private const float FlashDurationMs = 50f;
    private const float BreathOutDurationMs = 500f;
    private const float DarkGapMs = 120f;
    private const float PulseCycleMs = FlashDurationMs + BreathOutDurationMs + DarkGapMs;
    private const float BlackHoldMs = 500f;
    private const float TotalDurationMs = PulseCount * PulseCycleMs + BlackHoldMs;
    private const int FrameDelayMs = 16;
    private const float Speed = 1.5f;

    /// <summary>
    /// Plays the full transition animation via the dispatcher.
    /// Blocks asynchronously for ~2.5 s. Fully cancellable.
    /// Every frame fires <see cref="RgbFrameDispatcher.FrameRendered"/>
    /// automatically through <see cref="RgbFrameDispatcher.ForceRenderAsync"/>.
    /// </summary>
    public static async Task PlayAsync(
        RgbFrameDispatcher dispatcher,
        RGBColor modeColor,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        // [DIAGNOSTIC-ONLY] frame telemetry (no behavior change)
        var diagFrameIndex = 0;
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[DIAG] Strobe PlayAsync START. [color=#{modeColor.R:X2}{modeColor.G:X2}{modeColor.B:X2}, totalMs={(int)TotalDurationMs}, realMs={(int)(TotalDurationMs / Speed)}]");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var elapsedMs = (float)sw.Elapsed.TotalMilliseconds * Speed;
                if (elapsedMs >= TotalDurationMs)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"[DIAG] Strobe loop BREAK: duration reached. [frames={diagFrameIndex}, realMs={sw.Elapsed.TotalMilliseconds:F0}]");
                    break;
                }

                var brightness = ComputeBrightness(elapsedMs);
                var r = (byte)(modeColor.R * brightness);
                var g = (byte)(modeColor.G * brightness);
                var b = (byte)(modeColor.B * brightness);
                var color = new RGBColor(r, g, b);

                await dispatcher.ForceRenderAsync(new ZoneColors(color), cancellationToken)
                    .ConfigureAwait(false);

                // [DIAGNOSTIC-ONLY]
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[DIAG] Strobe frame {diagFrameIndex}. [realMs={sw.Elapsed.TotalMilliseconds:F1}, animMs={elapsedMs:F0}, brightness={brightness:F2}]");

                diagFrameIndex++;
                await Task.Delay(FrameDelayMs, cancellationToken).ConfigureAwait(false);
            }

            // Ensure we end on pure black
            if (!cancellationToken.IsCancellationRequested)
            {
                await dispatcher.ForceRenderAsync(ZoneColors.Black, cancellationToken).ConfigureAwait(false);
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[DIAG] Strobe final BLACK sent. [frames={diagFrameIndex}, realMs={sw.Elapsed.TotalMilliseconds:F0}]");
            }
            else if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"[DIAG] Strobe ended on cancellation BEFORE final black. [frames={diagFrameIndex}]");
            }
        }
        catch (OperationCanceledException)
        {
            // Interrupted — send black and exit cleanly
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] Strobe OCE at frame {diagFrameIndex}. [realMs={sw.Elapsed.TotalMilliseconds:F0}, tokenCancelled={cancellationToken.IsCancellationRequested}]");
            try { await dispatcher.ForceRenderAsync(ZoneColors.Black).ConfigureAwait(false); }
            catch { /* best effort */ }
            throw;
        }
    }

    private static float ComputeBrightness(float elapsedMs)
    {
        var pulseIndex = (int)(elapsedMs / PulseCycleMs);
        if (pulseIndex >= PulseCount)
            return 0f;

        var localMs = elapsedMs - pulseIndex * PulseCycleMs;

        if (localMs < FlashDurationMs)
            return 1f;

        var breathLocal = localMs - FlashDurationMs;
        if (breathLocal < BreathOutDurationMs)
        {
            var progress = breathLocal / BreathOutDurationMs;
            return MathF.Sin((1f - progress) * MathF.PI * 0.5f);
        }

        return 0f;
    }
}
