using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LoqNova.Lib;

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
/// <para>
/// The opening self-test is driven entirely from the instrument's own lifecycle
/// and its bound <see cref="PowerMode"/>: it replays whenever the instrument is
/// attached (i.e. whenever the Dashboard is navigated to) and whenever the active
/// power mode changes. Nothing here subscribes to an event, holds a timer per
/// visit or keys off the page's view model, so revisiting the Dashboard can
/// neither duplicate nor miss a sweep.
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
    /// The opening self-test, timed like the WPF dashboard's bar sweep: the
    /// instrument collapses to the bottom of the scale, sweeps out across the full
    /// arc, then settles on the reading the machine is reporting. Three phases
    /// rather than one so a replayed self-test reads as an instrument check rather
    /// than a needle that merely slides from wherever it was to the current value -
    /// and so a replay never snaps.
    /// </summary>
    private const double StartupCollapseSeconds = 0.26;
    private const double StartupExpandSeconds = 0.32;
    private const double StartupSettleSeconds = 0.32;
    private const double StartupSweepSeconds =
        StartupCollapseSeconds + StartupExpandSeconds + StartupSettleSeconds;

    /// <summary>Time constant of the eased accent-colour transition.</summary>
    private const double AccentSeconds = 0.14;

    /// <summary>
    /// True when the user has switched Windows animations off. The opening
    /// self-test sweep is pure decoration, so under that setting the dial skips it
    /// and settles straight onto the reported reading instead of flinging to full
    /// scale first.
    /// </summary>
    private static readonly bool ReducedMotion = ReadReducedMotion();

    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    private static bool ReadReducedMotion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var enabled = 1;
            return SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref enabled, 0) && enabled == 0;
        }
        catch
        {
            // An unavailable system call never disables the sweep.
            return false;
        }
    }

    private readonly DispatcherTimer _timer;
    private bool _attached;
    private bool _running;
    private long _lastTimestamp;
    private double _startupElapsed;
    private bool _startupSweepComplete;

    /// <summary>
    /// The scale position the current self-test's collapse phase starts from,
    /// captured at <see cref="ReplaySweep"/> so a replay picks up exactly where the
    /// instrument is instead of snapping.
    /// </summary>
    private double _sweepStartRatio;

    private Color _accentCurrent;
    private Color _accentTarget;
    private bool _accentKnown;

    protected AnimatedGauge()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = FrameInterval };
        _timer.Tick += OnTick;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _attached = true;

        // The self-test replays every time the instrument becomes active. The
        // navigation service builds a fresh Dashboard page - and therefore fresh
        // gauges - on every visit, so this is what makes navigating away and back
        // sweep again; it also covers a gauge instance that is reused.
        ReplaySweep();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _attached = false;
        Stop();
    }

    /// <summary>
    /// Identity of the active power mode, handed in by the page. A change between
    /// two known modes replays the opening self-test, so a performance-mode change
    /// is signalled by the value the page already binds rather than by a
    /// subscription the instrument would have to manage. The value is the mode
    /// itself, so an unrelated property update cannot trigger a sweep.
    /// </summary>
    public static readonly StyledProperty<PowerModeState?> PowerModeProperty =
        AvaloniaProperty.Register<AnimatedGauge, PowerModeState?>(nameof(PowerMode));

    public PowerModeState? PowerMode
    {
        get => GetValue(PowerModeProperty);
        set => SetValue(PowerModeProperty, value);
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
    /// The scale position the instrument is showing right now, as a fraction of
    /// the scale. A replay captures it so the collapse phase starts where the
    /// needle or bar actually is.
    /// </summary>
    protected virtual double DisplayedSweepPosition => 0.0;

    /// <summary>
    /// Progress of the opening sweep as a monotonic 0..1 ramp, eased in and out,
    /// used for the digital count-up so the digits travel with the needle and
    /// land exactly on the reported reading when the sweep completes. The ramp
    /// starts once the collapse phase has parked the needle at the bottom of the
    /// scale, so the readout shows zero while the needle is there and then climbs
    /// with it.
    /// </summary>
    protected double StartupSweepPhase
    {
        get
        {
            if (_startupSweepComplete)
            {
                return 1.0;
            }

            var climbed = (_startupElapsed - StartupCollapseSeconds) /
                          (StartupExpandSeconds + StartupSettleSeconds);
            return SweepEase(Math.Clamp(climbed, 0.0, 1.0));
        }
    }

    /// <summary>
    /// The scale position the needle and value arc should show during the
    /// opening sweep, as a fraction of the scale toward <paramref name="target"/>
    /// (the reported reading as a fraction of its maximum).
    /// <para>
    /// Collapse from wherever the needle was to the bottom of the scale, out
    /// across the full arc, then settle on the reported reading. Rides the live
    /// target, so a reading that arrives while the sweep is running is settled on
    /// rather than overshot, and the settle segment lands exactly on it. When the
    /// sweep is skipped - or already finished - the position is just the target.
    /// </para>
    /// </summary>
    protected double StartupSweepPosition(double target)
    {
        if (_startupSweepComplete)
        {
            return target;
        }

        var elapsed = _startupElapsed;
        if (elapsed < StartupCollapseSeconds)
        {
            // From the captured position down to the bottom of the scale. On a
            // first show that position is already zero, so the phase is a no-op
            // and the sweep starts from the beginning of the scale.
            return _sweepStartRatio * (1.0 - QuinticInOut(elapsed / StartupCollapseSeconds));
        }

        if (elapsed < StartupCollapseSeconds + StartupExpandSeconds)
        {
            return QuinticOut((elapsed - StartupCollapseSeconds) / StartupExpandSeconds);
        }

        var settled = Math.Clamp(
            (elapsed - StartupCollapseSeconds - StartupExpandSeconds) / StartupSettleSeconds, 0.0, 1.0);
        return 1.0 - QuinticOut(settled) * (1.0 - Math.Clamp(target, 0.0, 1.0));
    }

    /// <summary>
    /// Restarts the opening self-test from the beginning. Called when the
    /// instrument becomes active and when the active power mode changes. A sweep
    /// already in flight is restarted rather than stacked, and the collapse phase
    /// starts from the position the instrument is showing, so a replay continues
    /// smoothly from wherever the needle was instead of snapping.
    /// </summary>
    protected void ReplaySweep()
    {
        _sweepStartRatio = Math.Clamp(DisplayedSweepPosition, 0.0, 1.0);
        _startupElapsed = 0.0;
        _startupSweepComplete = false;
        RequestFrames();
    }

    /// <summary>
    /// Advances the one-shot opening sweep and returns true while it still needs
    /// frames. It is driven from the instrument's first frame rather than from its
    /// first reading, so all four Dashboard gauges sweep together even though the
    /// GPU counters report a tick later; that late reading is simply what the
    /// sweep settles on.
    /// </summary>
    protected bool AdvanceStartupSweep(double seconds)
    {
        if (_startupSweepComplete)
        {
            return false;
        }

        if (ReducedMotion)
        {
            // The sweep is decorative: when the user has asked Windows to reduce
            // motion, skip it and settle straight onto the reading.
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
    /// Ease-in-out, so the digital count-up leaves the minimum and settles gently.
    /// </summary>
    private static double SweepEase(double phase) => phase * phase * (3.0 - 2.0 * phase);

    /// <summary>
    /// Quintic ease-in-out, matching the collapse phase of the WPF dashboard's bar
    /// sweep.
    /// </summary>
    private static double QuinticInOut(double phase)
    {
        var t = Math.Clamp(phase, 0.0, 1.0);
        return t < 0.5
            ? 16.0 * t * t * t * t * t
            : 1.0 - Math.Pow(-2.0 * t + 2.0, 5.0) / 2.0;
    }

    /// <summary>
    /// Quintic ease-out, matching the expand and settle phases of the WPF
    /// dashboard's bar sweep.
    /// </summary>
    private static double QuinticOut(double phase)
    {
        var t = Math.Clamp(phase, 0.0, 1.0);
        var inverse = 1.0 - t;
        return 1.0 - inverse * inverse * inverse * inverse * inverse;
    }

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

    private void FinishStartupSweep()
    {
        _startupElapsed = StartupSweepSeconds;
        _startupSweepComplete = true;
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
        else if (change.Property == PowerModeProperty
            && change.OldValue is PowerModeState previous
            && change.NewValue is PowerModeState current
            && previous != current)
        {
            // The active mode changed while the instrument was on screen: replay
            // the self-test. A null boundary is the backend reporting - or losing -
            // the mode rather than a change, and the first report is already
            // covered by the sweep that runs when the instrument becomes active.
            ReplaySweep();
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
