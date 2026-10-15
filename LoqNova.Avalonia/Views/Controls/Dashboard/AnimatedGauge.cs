using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// Shared rendering loop for the Dashboard's custom-drawn instruments.
/// <para>
/// One <see cref="DispatcherTimer"/> per instrument owns exactly one
/// <see cref="Visual.InvalidateVisual"/> call per frame. The instrument paints
/// itself from cached geometry, so a frame is a repaint and never a layout pass.
/// </para>
/// <para>
/// The loop only runs while the control is attached, visible and has work to do:
/// it is torn down when the control leaves the tree or is hidden, and stopped by
/// the derived instrument the moment its movement settles. <see cref="Advance"/>
/// receives the real elapsed time rather than a fixed frame step, so motion is
/// frame-rate independent.
/// </para>
/// </summary>
public abstract class AnimatedGauge : Control
{
    /// <summary>Frame interval. One tick owns one repaint.</summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// Longest step integrated in one frame. A stalled or hidden UI resumes with
    /// a normal step instead of jumping, which keeps the instruments continuous.
    /// </summary>
    private const double MaxStepSeconds = 0.1;

    /// <summary>
    /// Duration of the one-shot opening sweep, measured in rendered time so a
    /// stalled UI resumes the sweep instead of skipping past it.
    /// </summary>
    private const double StartupSweepSeconds = 0.9;

    /// <summary>
    /// Channels whose opening sweep has already run in this session. The
    /// navigation service builds a fresh Dashboard page - and therefore fresh
    /// gauges - every time it is shown, so a per-instance flag alone would replay
    /// the sweep on every visit. The sweep is an opening animation for the
    /// session, not for every reconstruction of the page. Only ever touched from
    /// the UI thread.
    /// </summary>
    private static readonly HashSet<string> SweptChannels = [];

    private readonly DispatcherTimer _timer;
    private bool _attached;
    private bool _running;
    private long _lastTimestamp;
    private double _startupPhase;
    private bool _startupSweepComplete;

    protected AnimatedGauge()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = FrameInterval };
        _timer.Tick += OnTick;

        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            RequestFrames();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            Stop();
        };
    }

    /// <summary>
    /// Identifies the telemetry channel this instrument draws, so its opening
    /// sweep runs once per session. A gauge left without a key sweeps once per
    /// instance instead.
    /// </summary>
    public static readonly StyledProperty<string?> ChannelKeyProperty =
        AvaloniaProperty.Register<AnimatedGauge, string?>(nameof(ChannelKey));

    public string? ChannelKey
    {
        get => GetValue(ChannelKeyProperty);
        set => SetValue(ChannelKeyProperty, value);
    }

    /// <summary>
    /// Advances the instrument by <paramref name="seconds"/> of real time and
    /// returns true while it still needs frames. Returning false stops the loop
    /// until something calls <see cref="RequestFrames"/> again.
    /// </summary>
    protected abstract bool Advance(double seconds);

    /// <summary>True until the one-shot opening sweep has finished.</summary>
    protected bool IsStartupSweeping => !_startupSweepComplete;

    /// <summary>
    /// Progress of the opening sweep, from 0 at the bottom of the scale to 1 at
    /// the reported reading, eased in and out so the needle leaves the minimum
    /// and settles onto the reading instead of stopping abruptly.
    /// </summary>
    protected double StartupSweepPhase => _startupSweepComplete ? 1.0 : SweepEase(_startupPhase);

    /// <summary>
    /// Advances the one-shot opening sweep and returns true while it still needs
    /// frames. Callers only invoke this once the machine has reported a reading,
    /// so a slow or absent channel never sweeps against an invented value; pass
    /// <paramref name="skip"/> when that reading sits at the bottom of the scale
    /// and there is nothing to sweep through.
    /// </summary>
    protected bool AdvanceStartupSweep(double seconds, bool skip = false)
    {
        if (_startupSweepComplete)
        {
            return false;
        }

        if (skip || HasSwept(ChannelKey))
        {
            // Nothing to sweep through, or this channel was already swept
            // earlier in the session: land straight on the reading and hand over
            // to live telemetry.
            FinishStartupSweep();
            return false;
        }

        _startupPhase += seconds / StartupSweepSeconds;
        if (_startupPhase < 1.0)
        {
            return true;
        }

        FinishStartupSweep();
        return false;
    }

    /// <summary>Ease-in-out, so the sweep leaves the minimum and settles gently.</summary>
    private static double SweepEase(double phase) => phase * phase * (3.0 - 2.0 * phase);

    private static bool HasSwept(string? channelKey)
        => channelKey is { Length: > 0 } && SweptChannels.Contains(channelKey);

    private void FinishStartupSweep()
    {
        _startupPhase = 1.0;
        _startupSweepComplete = true;
        if (ChannelKey is { Length: > 0 } channel)
        {
            SweptChannels.Add(channel);
        }
    }

    /// <summary>
    /// Starts the loop when the instrument has work to do and is on screen. Called
    /// by derived instruments whenever a reading changes.
    /// </summary>
    protected void RequestFrames()
    {
        if (_attached && IsVisible)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            RequestFrames();
        }
    }

    private void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _lastTimestamp = Environment.TickCount64;
        _timer.Start();
    }

    private void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = Environment.TickCount64;
        var elapsed = now - _lastTimestamp;
        _lastTimestamp = now;
        if (elapsed <= 0)
        {
            return;
        }

        // A minimised window or a hidden ancestor stops the motion without tearing
        // the timer down; the next visible frame picks it up with a normal step
        // because the timestamp above has already been advanced.
        if (!IsEffectivelyVisible)
        {
            return;
        }

        var seconds = Math.Min(elapsed / 1000.0, MaxStepSeconds);
        var needsMoreFrames = Advance(seconds);
        InvalidateVisual();

        if (!needsMoreFrames)
        {
            Stop();
        }
    }
}
