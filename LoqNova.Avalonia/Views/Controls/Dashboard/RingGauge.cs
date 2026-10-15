using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// A circular gauge for one utilisation channel.
/// <para>
/// The arc length is the reading the machine reported as a fraction of the
/// maximum it reports for that channel; the value printed in the middle is the
/// same reported reading, so the two can never disagree. An unreported channel
/// (<c>Value &lt; 0</c>) is drawn muted with an empty arc rather than as a zero.
/// </para>
/// <para>
/// Like <see cref="AnalogFanControl"/> the control paints itself in
/// <see cref="Render"/> and animates through a short, self-cancelling
/// <see cref="DispatcherTimer"/>: after the initial layout the tick only runs
/// while the needle is still travelling, and stops the moment it settles.
/// </para>
/// </summary>
public class RingGauge : Control
{
    /// <summary>The gauge sweeps 270 degrees, leaving a gap at the bottom.</summary>
    private const double StartAngle = 225.0;
    private const double SweepAngle = 270.0;

    /// <summary>Time constant for the needle's approach to a new reading.</summary>
    private const double EaseSeconds = 0.16;

    [Flags]
    private enum Reserved
    {
        None = 0
    }

    /// <summary>Reported reading; -1 means the machine did not report the channel.</summary>
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<RingGauge, int>(nameof(Value), -1);

    /// <summary>Highest reading the machine reports for the channel.</summary>
    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<RingGauge, int>(nameof(Maximum), -1);

    /// <summary>Formatted value drawn in the middle; a dash when unreported.</summary>
    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<RingGauge, string?>(nameof(ValueText), "-");

    /// <summary>Unit suffix drawn beneath the value, e.g. "%".</summary>
    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<RingGauge, string?>(nameof(Unit));

    /// <summary>Channel accent. A design-token fallback is used when unset.</summary>
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<RingGauge, IBrush?>(nameof(Accent));

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public string? ValueText
    {
        get => GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public string? Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool _attached;
    private bool _running;
    private bool _initialised;
    private long _lastTimestamp;
    private double _displayRatio;
    private double _targetRatio;

    // Geometry cache, rebuilt only when the control is resized.
    private Point _center;
    private double _ringRadius;
    private double _thickness;
    private double _contentRadius;
    private Geometry? _ticksMinor;
    private Geometry? _ticksMajor;
    private Geometry? _trackArc;
    private double _cachedDialSize = double.NaN;

    public RingGauge()
    {
        _timer.Tick += OnTick;
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            if (Math.Abs(_displayRatio - _targetRatio) > 0.001)
            {
                StartAnimation();
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            StopAnimation();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty || change.Property == MaximumProperty)
        {
            _targetRatio = ComputeRatio();
            if (!_initialised)
            {
                _initialised = true;
                _displayRatio = _targetRatio;
            }
            else if (_attached && IsVisible)
            {
                StartAnimation();
            }

            InvalidateVisual();
        }
        else if (change.Property == ValueTextProperty
            || change.Property == UnitProperty
            || change.Property == AccentProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == IsVisibleProperty)
        {
            if (change.GetNewValue<bool>() && Math.Abs(_displayRatio - _targetRatio) > 0.001)
            {
                StartAnimation();
            }
            else if (!change.GetNewValue<bool>())
            {
                StopAnimation();
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        var dial = Math.Min(Bounds.Width, Bounds.Height);
        if (double.IsNaN(dial) || dial <= 32)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
        EnsureGeometry(dial, center);

        var reported = Value >= 0 && Maximum >= 0;
        var accent = reported
            ? Accent ?? DashboardVisuals.Brush(this, "PrimaryBrush", Brushes.DodgerBlue)
            : DashboardVisuals.Brush(this, "TextMutedBrush", Brushes.Gray);
        var track = DashboardVisuals.Brush(this, "SurfaceRaisedBrush", Brushes.Gray);
        var line = DashboardVisuals.Brush(this, "BorderBrush", Brushes.Gray);

        // Scale.
        using (context.PushOpacity(0.5))
        {
            context.DrawGeometry(null, new Pen(line, 1), _ticksMinor);
        }

        context.DrawGeometry(null, new Pen(DashboardVisuals.Brush(this, "BorderStrongBrush", line), 1.4), _ticksMajor);

        // Track and the reported reading.
        context.DrawGeometry(null, new Pen(track, _thickness, null, PenLineCap.Round), _trackArc);

        var sweep = SweepAngle * Math.Clamp(_displayRatio, 0.0, 1.0);
        if (sweep > 0.5)
        {
            var progress = DashboardVisuals.Arc(center, _ringRadius, StartAngle, sweep);
            context.DrawGeometry(null, new Pen(accent, _thickness, null, PenLineCap.Round), progress);
        }

        // Readout.
        var typeface = DashboardVisuals.Typeface(this);
        var valueBrush = reported
            ? DashboardVisuals.Brush(this, "TextPrimaryBrush", Brushes.White)
            : DashboardVisuals.Brush(this, "TextMutedBrush", Brushes.Gray);
        var mutedBrush = DashboardVisuals.Brush(this, "TextMutedBrush", Brushes.Gray);

        var value = DashboardVisuals.FittedText(ValueText ?? "-", typeface,
            _contentRadius * 0.86, 10, _contentRadius * 1.9, valueBrush);
        DashboardVisuals.DrawCentred(context, value, center, -_contentRadius * 0.34);

        if (!string.IsNullOrEmpty(Unit))
        {
            var unit = DashboardVisuals.FittedText(Unit, typeface, _contentRadius * 0.44, 8, _contentRadius * 1.6, mutedBrush);
            DashboardVisuals.DrawCentred(context, unit, center, _contentRadius * 0.34);
        }
    }

    private void EnsureGeometry(double dial, Point center)
    {
        if (Math.Abs(dial - _cachedDialSize) < 0.5 && _trackArc is not null)
        {
            return;
        }

        _cachedDialSize = dial;
        _center = center;

        var outer = dial / 2.0 - 1.0;
        _thickness = Math.Clamp(outer * 0.20, 4.0, 9.0);
        _ringRadius = outer - _thickness / 2.0;

        var tickOuter = _ringRadius - _thickness / 2.0 - 1.0;
        var tickInner = tickOuter - outer * 0.09;
        _contentRadius = tickInner - 2.0;

        var ticks = DashboardVisuals.Ticks(center, tickInner, tickOuter, 20, 5, StartAngle, SweepAngle);
        _ticksMinor = ticks.Minor;
        _ticksMajor = ticks.Major;
        _trackArc = DashboardVisuals.Arc(center, _ringRadius, StartAngle, SweepAngle);
    }

    private double ComputeRatio()
        => Value >= 0 && Maximum > 0 ? Math.Clamp((double)Value / Maximum, 0.0, 1.0) : 0.0;

    private void StartAnimation()
    {
        if (_running || !_attached || !IsVisible)
        {
            return;
        }

        _running = true;
        _lastTimestamp = Environment.TickCount64;
        _timer.Start();
    }

    private void StopAnimation()
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

        // A minimised window stops the needle advancing without tearing the timer
        // down; the remaining travel is picked up on the next visible tick.
        if (!IsEffectivelyVisible)
        {
            return;
        }

        var delta = _targetRatio - _displayRatio;
        if (Math.Abs(delta) < 0.0015)
        {
            _displayRatio = _targetRatio;
            StopAnimation();
            InvalidateVisual();
            return;
        }

        var seconds = Math.Min(elapsed / 1000.0, 0.1);
        _displayRatio += delta * Math.Min(1.0, seconds / EaseSeconds);
        InvalidateVisual();
    }
}
