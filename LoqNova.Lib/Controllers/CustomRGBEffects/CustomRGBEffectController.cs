// ============================================================================
// CustomRGBEffectController.cs
// 
// Controller for running custom RGB effects on the 4-zone keyboard.
// Provides methods for setting colors and smooth transitions.
// All HID writes are delegated to RgbFrameDispatcher.
// 
// Original Rust source: https://github.com/4JX/L5P-Keyboard-RGB
// Maps to Rust legion_rgb_driver::Keyboard struct methods.
// ============================================================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LoqNova.Lib.Controllers;
using LoqNova.Lib.SoftwareDisabler;
using LoqNova.Lib.System.Management;
using LoqNova.Lib.Utils;

namespace LoqNova.Lib.Controllers.CustomRGBEffects;

/// <summary>
/// Controller for running custom RGB effects on the 4-zone keyboard.
/// All HID output is routed through <see cref="RgbFrameDispatcher"/>.
/// </summary>
public class CustomRGBEffectController(RgbFrameDispatcher dispatcher, VantageDisabler vantageDisabler)
{
    private CancellationTokenSource? _effectCts;
    private Task? _effectTask;
    private ZoneColors _currentColors = ZoneColors.Black;

    /// <summary>
    /// Gets the current zone colors.
    /// </summary>
    public ZoneColors CurrentColors => _currentColors;

    /// <summary>
    /// Whether a custom effect is currently running.
    /// </summary>
    public bool IsEffectRunning => _effectTask is not null && !_effectTask.IsCompleted;

    /// <summary>
    /// Resumes from override by lifting the HID-write gate and immediately
    /// pushing the last computed frame via the dispatcher.
    /// </summary>
    public async Task ResumeFromOverrideAsync()
    {
        // [DIAGNOSTIC-ONLY]
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[DIAG] Effect ResumeFromOverride ENTER. [running={IsEffectRunning}]");
        dispatcher.IsOverrideActive = false;
        await dispatcher.ForceRenderAsync(_currentColors).ConfigureAwait(false);
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[DIAG] Effect ResumeFromOverride DONE.");
    }

    /// <summary>
    /// Starts a custom RGB effect.
    /// </summary>
    public async Task StartEffectAsync(ICustomRGBEffect effect)
    {
        await StopEffectAsync().ConfigureAwait(false);

        if (!dispatcher.IsSupported)
            throw new InvalidOperationException("RGB Keyboard unsupported");

        await ThrowIfVantageEnabled().ConfigureAwait(false);

        // Take light control ownership
        await WMI.LenovoGameZoneData.SetLightControlOwnerAsync(1).ConfigureAwait(false);

        // Set to static mode for manual zone control
        await dispatcher.SetStaticModeAsync().ConfigureAwait(false);

        _effectCts = new CancellationTokenSource();
        _effectTask = RunEffectInternalAsync(effect, _effectCts.Token);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Started custom effect: {effect.Type}");
    }

    /// <summary>
    /// Stops the currently running effect.
    /// </summary>
    public async Task StopEffectAsync()
    {
        if (_effectCts is not null)
        {
            await _effectCts.CancelAsync().ConfigureAwait(false);

            if (_effectTask is not null)
            {
                try
                {
                    await _effectTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }

            _effectCts.Dispose();
            _effectCts = null;
            _effectTask = null;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Stopped custom effect");
        }
    }

    private async Task RunEffectInternalAsync(ICustomRGBEffect effect, CancellationToken cancellationToken)
    {
        try
        {
            await effect.RunAsync(this, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Effect {effect.Type} threw an exception", ex);
        }
    }

    /// <summary>
    /// Sets all zones to the specified colors immediately.
    /// Maps to Rust Keyboard::set_colors_to().
    /// </summary>
    public async Task SetColorsAsync(ZoneColors colors, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _currentColors = colors;
        await dispatcher.RenderAsync(colors, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets all zones to a single solid color.
    /// Maps to Rust Keyboard::solid_set_colors_to().
    /// </summary>
    public Task SetSolidColorAsync(RGBColor color, CancellationToken cancellationToken = default)
    {
        return SetColorsAsync(new ZoneColors
        {
            Zone1 = color,
            Zone2 = color,
            Zone3 = color,
            Zone4 = color
        }, cancellationToken);
    }

    /// <summary>
    /// Sets a specific zone (0-3) to the specified color.
    /// Maps to Rust Keyboard::set_zone_by_index().
    /// </summary>
    public Task SetZoneAsync(int zoneIndex, RGBColor color, CancellationToken cancellationToken = default)
    {
        if (zoneIndex < 0 || zoneIndex > 3)
            throw new ArgumentOutOfRangeException(nameof(zoneIndex), "Zone index must be 0-3");

        var newColors = _currentColors.WithZone(zoneIndex, color);
        return SetColorsAsync(newColors, cancellationToken);
    }

    /// <summary>
    /// Smoothly transitions to target colors over multiple steps.
    /// Maps to Rust Keyboard::transition_colors_to().
    /// Uses Stopwatch-based timing to ensure consistent speed regardless of app focus.
    /// </summary>
    public async Task TransitionColorsAsync(
        ZoneColors targetColors,
        int steps,
        int delayBetweenStepsMs,
        CancellationToken cancellationToken = default)
    {
        if (steps <= 0)
        {
            await SetColorsAsync(targetColors, cancellationToken).ConfigureAwait(false);
            return;
        }

        var startArray = _currentColors.ToArray();
        var targetArray = targetColors.ToArray();

        var stopwatch = Stopwatch.StartNew();
        var totalDurationMs = steps * delayBetweenStepsMs;

        while (!cancellationToken.IsCancellationRequested)
        {
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            var progress = totalDurationMs > 0
                ? Math.Min(1.0f, elapsedMs / (float)totalDurationMs)
                : 1.0f;

            var stepArray = new byte[12];
            for (var i = 0; i < 12; i++)
            {
                stepArray[i] = (byte)Math.Clamp(
                    startArray[i] + (targetArray[i] - startArray[i]) * progress,
                    0, 255);
            }

            await SetColorsAsync(ZoneColors.FromArray(stepArray), cancellationToken).ConfigureAwait(false);

            if (progress >= 1.0f)
                break;

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }

        await SetColorsAsync(targetColors, cancellationToken).ConfigureAwait(false);
    }

    private async Task ThrowIfVantageEnabled()
    {
        var vantageStatus = await vantageDisabler.GetStatusAsync().ConfigureAwait(false);
        if (vantageStatus == SoftwareStatus.Enabled)
            throw new InvalidOperationException("Can't manage RGB keyboard with Vantage enabled");
    }
}
