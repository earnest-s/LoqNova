using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
    /// The opening sweep travels from the bottom of the scale out to full scale,
    /// holds there, then returns to the reported reading. Three segments rather
    /// than one so the motion reads as an instrument-cluster self test instead of
    /// a needle that merely slides from zero to the current value.
    /// </summary>
    private const double StartupForwardSeconds = 0.85;
    private const double StartupHoldSeconds = 0.16;
    private const double StartupReturnSeconds = 0.55;
    private const double StartupSweepSeconds =
        StartupForwardSeconds + StartupHoldSeconds + StartupReturnSeconds;

    /// <summary>Time constant of the eased accent-colour transition.</summary>
    private const double AccentSeconds = 0.14;

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
    private double _startupElapsed;
    private bool _startupSweepComplete;

    private Color _accentCurrent;
    private Color _accentTarget;
    private bool _accentKnown;

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
    /// Progress of the opening sweep as a monotonic 0..1 ramp, eased in and out,
    /// used for the digital count-up so the digits travel with the needle and
    /// land exactly on the reported reading when the sweep completes.
    /// </summary>
    protected double StartupSweepPhase
        => _startupSweepComplete ? 1.0 : SweepEase(PhaseOf(_startupElapsed));

    /// <summary>
    /// The scale position the needle and value arc should show during the
    /// opening sweep, as a fraction of the scale toward <paramref name="target"/>
    /// (the reported reading as a fraction of its maximum).
    /// <para>
    /// Out to full scale, hold, then back down to the reported reading. Rides the
    /// live target, so a reading that arrives while the sweep is running is
    /// settled on rather than overshot, and the return segment lands exactly on
    /// it. When the sweep is skipped - or already finished - the position is just
    /// the target.
    /// </para>
    /// </summary>
    protected double StartupSweepPosition(double target)
    {
        if (_startupSweepComplete)
        {
            return target;
        }

        var elapsed = _startupElapsed;
        if (elapsed < StartupForwardSeconds)
        {
            return SweepEase(elapsed / StartupForwardSeconds);
        }

        if (elapsed < StartupForwardSeconds + StartupHoldSeconds)
        {
            return 1.0;
        }

        var returned = PhaseOf(elapsed - StartupForwardSeconds - StartupHoldSeconds);
        return 1.0 - SweepEase(returned) * (1.0 - Math.Clamp(target, 0.0, 1.0));
    }

    /// <summary>
    /// Advances the one-shot opening sweep and returns true while it still needs
    /// frames. Callers only invoke this once the machine has reported a reading,
    /// so a slow or absent channel never sweeps against an invented value.
    /// </summary>
    protected bool AdvanceStartupSweep(double seconds)
    {
        if (_startupSweepComplete)
        {
            return false;
        }

        if (HasSwept(ChannelKey))
        {
            // This channel was already swept earlier in the session: land
            // straight on the reading and hand over to live telemetry.
            FinishStartupSweep();
            return false;
        }

        _startupElapsed += seconds;
        if (_startupElapsed < StartupSweepSeconds)
        {
            return true;
        }

        _startupElapsed = StartupSweepSeconds;
        FinishStartupSweep();
        return false;
    }

    /// <summary>
    /// Sets the accent the instrument is drawing toward. The first call adopts
    /// the colour outright; later calls start a brief interpolated transition, so
    /// a change of power mode eases instead of flashing.
    /// </summary>
    protected void SetAccentTarget(Color target)
    {
        if (!_accentKnown)
        {
            _accentKnown = true;
            _accentCurrent = target;
            _accentTarget = target;
            return;
        }

        _accentTarget = target;
    }

    /// <summary>The accent colour actually drawn; eased between values.</summary>
    protected Color CurrentAccent => _accentCurrent;

    /// <summary>
    /// Eases the drawn accent toward its target and returns true while it is
    /// still moving, so the instrument keeps requesting frames for the transition
    /// without spinning once the colour has settled.
    /// </summary>
    protected bool AdvanceAccent(double seconds)
    {
        if (_accentCurrent == _accentTarget)
        {
            return false;
        }

        _accentCurrent = Blend(_accentCurrent, _accentTarget, 1.0 - Math.Exp(-seconds / AccentSeconds));
        if (IsNear(_accentCurrent, _accentTarget))
        {
            _accentCurrent = _accentTarget;
        }

        return _accentCurrent != _accentTarget;
    }

    /// <summary>
    /// Ease-in-out, so the sweep leaves the minimum and settles gently.
    /// </summary>
    private static double SweepEase(double phase) => phase * phase * (3.0 - 2.0 * phase);

    /// <summary>Clamps a fraction of the sweep to 0..1.</summary>
    private static double PhaseOf(double seconds)
        => Math.Clamp(seconds / StartupSweepSeconds, 0.0, 1.0);

    /// <summary>Linear blend of two colours, channel by channel.</summary>
    private static Color Blend(Color from, Color to, double amount)
    {
        var t = Math.Clamp(amount, 0.0, 1.0);
        static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
        return Color.FromArgb(
            Mix(from.A, to.A, t),
            Mix(from.R, to.R, t),
            Mix(from.G, to.G, t),
            Mix(from.B, to.B, t));
    }

    private static bool IsNear(Color a, Color b)
        => Math.Abs(a.A - b.A) <= 2
            && Math.Abs(a.R - b.R) <= 2
            && Math.Abs(a.G - b.G) <= 2
            && Math.Abs(a.B - b.B) <= 2;

    private static bool HasSwept(string? channelKey)
        => channelKey is { Length: > 0 } && SweptChannels.Contains(channelKey);

    private void FinishStartupSweep()
    {
        _startupElapsed = StartupSweepSeconds;
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
