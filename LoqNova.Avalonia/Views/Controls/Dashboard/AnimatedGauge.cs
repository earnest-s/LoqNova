using System;
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

    private readonly DispatcherTimer _timer;
    private bool _attached;
    private bool _running;
    private long _lastTimestamp;

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
    /// Advances the instrument by <paramref name="seconds"/> of real time and
    /// returns true while it still needs frames. Returning false stops the loop
    /// until something calls <see cref="RequestFrames"/> again.
    /// </summary>
    protected abstract bool Advance(double seconds);

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
