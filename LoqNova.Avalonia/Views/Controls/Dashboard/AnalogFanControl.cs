using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// An analog fan dial.
/// <para>
/// The blades rotate at a speed strictly proportional to the RPM the machine
/// reported and stop when it reports zero. The hub always prints that same
/// reported value, so the motion never implies a speed the hardware is not
/// actually running at. A tick scale and an accent arc add a second,
/// at-a-glance reading of reported speed against the reported maximum.
/// </para>
/// <para>
/// The control paints itself in <see cref="Render"/> and only calls
/// <c>InvalidateVisual</c> from its animation tick, so the visual tree stays a
/// single element and no layout runs while the fan spins. That tick is owned by
/// a <see cref="DispatcherTimer"/> which runs only while the control is
/// attached to the tree and visible, and resumes when the dashboard comes back.
/// </para>
/// </summary>
public class AnalogFanControl : Control
{
    /// <summary>
    /// Blade rotation in degrees per second, per reported RPM. A real fan turns
    /// far faster than a screen can usefully show and its blades are seven-fold
    /// symmetric, so a single fixed factor slows the pattern down while keeping
    /// the rotation strictly proportional: a higher reported RPM always rotates
    /// faster than a lower one, and zero stays still.
    /// </summary>
    private const double DegreesPerRpmSecond = 0.04;

    private const int BladeCount = 7;
    private const int TickCount = 56;
    private const int MajorTickEvery = 8;

    /// <summary>Reported speed in RPM; -1 means the machine did not report the channel.</summary>
    public static readonly StyledProperty<int> RpmProperty =
        AvaloniaProperty.Register<AnalogFanControl, int>(nameof(Rpm), -1);

    /// <summary>Highest speed the machine reports for this fan, used for the scale.</summary>
    public static readonly StyledProperty<int> MaxRpmProperty =
        AvaloniaProperty.Register<AnalogFanControl, int>(nameof(MaxRpm), -1);

    /// <summary>Formatted value drawn in the hub; a dash when the channel is not reported.</summary>
    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<AnalogFanControl, string?>(nameof(ValueText), "-");

    /// <summary>Channel accent. A design-token fallback is used when unset.</summary>
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<AnalogFanControl, IBrush?>(nameof(Accent));

    public int Rpm
    {
        get => GetValue(RpmProperty);
        set => SetValue(RpmProperty, value);
    }

    public int MaxRpm
    {
        get => GetValue(MaxRpmProperty);
        set => SetValue(MaxRpmProperty, value);
    }

    public string? ValueText
    {
        get => GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool _attached;
    private bool _running;
    private long _lastTimestamp;
    private double _angle;
    private double _visualRpm;

    // Geometry is rebuilt when the dial is resized, never on the animation tick.
    private Point _center;
    private double _outerRadius;
    private double _ringRadius;
    private double _ringThickness;
    private double _hubRadius;
    private Geometry _bladesEven = new StreamGeometry();
    private Geometry _bladesOdd = new StreamGeometry();
    private Geometry _ticksMinor = new StreamGeometry();
    private Geometry _ticksMajor = new StreamGeometry();
    private Geometry _trackArc = new StreamGeometry();
    private Geometry? _progressArc;
    private double _cachedDialSize = double.NaN;
    private double _cachedRatio = double.NaN;

    public AnalogFanControl()
    {
        _timer.Tick += OnTick;
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            UpdateAnimationState();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            Stop();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RpmProperty)
        {
            UpdateAnimationState();
            InvalidateVisual();
        }
        else if (change.Property == MaxRpmProperty
            || change.Property == ValueTextProperty
            || change.Property == AccentProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == IsVisibleProperty)
        {
            UpdateAnimationState();
        }
    }

    public override void Render(DrawingContext context)
    {
        var dial = Math.Min(Bounds.Width, Bounds.Height);
        if (double.IsNaN(dial) || dial <= 24)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
        EnsureGeometry(dial, center);

        var reported = Rpm >= 0;
        var ratio = reported && MaxRpm > 0 ? Math.Clamp((double)Rpm / MaxRpm, 0.0, 1.0) : 0.0;

        var lineBrush = DashboardVisuals.Brush(this, "BorderBrush", Brushes.Gray);
        var strongBrush = DashboardVisuals.Brush(this, "BorderStrongBrush", Brushes.Gray);
        var faceBrush = DashboardVisuals.Brush(this, "CardElevatedBrush", Brushes.Transparent);
        var trackBrush = DashboardVisuals.Brush(this, "SurfaceRaisedBrush", Brushes.Gray);
        var mutedBrush = DashboardVisuals.Brush(this, "TextMutedBrush", Brushes.Gray);
        var accentBrush = reported
            ? Accent ?? DashboardVisuals.Brush(this, "PrimaryBrush", Brushes.DodgerBlue)
            : mutedBrush;

        // Dial face.
        context.DrawEllipse(faceBrush, new Pen(lineBrush, 1), center, _outerRadius, _outerRadius);

        // Analog scale.
        using (context.PushOpacity(0.5))
        {
            context.DrawGeometry(null, new Pen(lineBrush, 1), _ticksMinor);
        }

        context.DrawGeometry(null, new Pen(strongBrush, 1.4), _ticksMajor);

        // Speed ring: a muted track plus the accent arc for reported / maximum.
        context.DrawGeometry(null, new Pen(trackBrush, _ringThickness, null, PenLineCap.Round), _trackArc);
        if (ratio > 0.001)
        {
            if (double.IsNaN(_cachedRatio) || Math.Abs(ratio - _cachedRatio) > 0.001)
            {
                _progressArc = DashboardVisuals.Arc(_center, _ringRadius, 0, 360.0 * ratio);
                _cachedRatio = ratio;
            }

            if (_progressArc is not null)
            {
                context.DrawGeometry(null, new Pen(accentBrush, _ringThickness, null, PenLineCap.Round), _progressArc);
            }
        }

        // Blades, alternating in weight so the rotation reads clearly.
        var rotation = Matrix.CreateRotation(_angle * Math.PI / 180.0)
            * Matrix.CreateTranslation(center.X, center.Y);
        var bladePen = new Pen(lineBrush, 1);
        using (context.PushTransform(rotation))
        {
            context.DrawGeometry(accentBrush, bladePen, _bladesEven);
            using (context.PushOpacity(0.64))
            {
                context.DrawGeometry(accentBrush, bladePen, _bladesOdd);
            }
        }

        // Hub, then the reported value and its unit.
        context.DrawEllipse(faceBrush, new Pen(lineBrush, 1), center, _hubRadius, _hubRadius);

        var typeface = DashboardVisuals.Typeface(this);
        var valueBrush = reported
            ? DashboardVisuals.Brush(this, "TextPrimaryBrush", Brushes.White)
            : mutedBrush;
        var value = DashboardVisuals.FittedText(ValueText ?? "-", typeface,
            _hubRadius * 0.86, 9, _hubRadius * 1.68, valueBrush);
        DashboardVisuals.DrawCentred(context, value, center, -_hubRadius * 0.3);

        if (reported && Rpm > 0)
        {
            var unit = DashboardVisuals.FittedText("RPM", typeface,
                _hubRadius * 0.34, 7, _hubRadius * 1.5, mutedBrush);
            DashboardVisuals.DrawCentred(context, unit, center, _hubRadius * 0.5);
        }
    }

    /// <summary>
    /// Rebuilds the static geometry when the dial size changes. Everything is
    /// expressed in control coordinates except the blades, which are built
    /// around the origin so the tick can rotate them with one transform.
    /// </summary>
    private void EnsureGeometry(double dial, Point center)
    {
        if (Math.Abs(dial - _cachedDialSize) < 0.5)
        {
            return;
        }

        _cachedDialSize = dial;
        _center = center;
        _outerRadius = dial / 2.0 - 1.0;
        _ringThickness = Math.Clamp(_outerRadius * 0.09, 2.0, 4.0);
        _ringRadius = _outerRadius - _ringThickness / 2.0;
        _hubRadius = _outerRadius * 0.42;

        var tickOuter = _ringRadius - _ringThickness - 1.0;
        var tickInner = tickOuter - _outerRadius * 0.08;
        var ticks = DashboardVisuals.Ticks(center, tickInner, tickOuter, TickCount, MajorTickEvery, 0, 360);
        _ticksMinor = ticks.Minor;
        _ticksMajor = ticks.Major;

        _trackArc = DashboardVisuals.Arc(center, _ringRadius, 0, 360);
        _progressArc = null;
        _cachedRatio = double.NaN;

        var bladeOuter = tickInner - _outerRadius * 0.08;
        _bladesEven = BuildBlades(_hubRadius, bladeOuter, even: true);
        _bladesOdd = BuildBlades(_hubRadius, bladeOuter, even: false);
    }

    private static Geometry BuildBlades(double innerRadius, double outerRadius, bool even)
    {
        var geometry = new StreamGeometry();
        var context = geometry.Open();
        try
        {
            for (var i = 0; i < BladeCount; i++)
            {
                if ((i % 2 == 0) != even)
                {
                    continue;
                }

                AppendBlade(context, innerRadius, outerRadius, i * 360.0 / BladeCount);
            }
        }
        finally
        {
            context.Dispose();
        }

        return geometry;
    }

    /// <summary>
    /// One swept blade, rooted at <paramref name="innerRadius"/> and turned to
    /// <paramref name="angle"/>, drawn around the origin.
    /// </summary>
    private static void AppendBlade(StreamGeometryContext context, double innerRadius, double outerRadius, double angle)
    {
        var origin = default(Point);
        var rootLeading = DashboardVisuals.Polar(origin, innerRadius, angle - 11);
        var tipLeading = DashboardVisuals.Polar(origin, outerRadius * 0.93, angle - 19);
        var tipTrailing = DashboardVisuals.Polar(origin, outerRadius * 0.93, angle + 15);
        var rootTrailing = DashboardVisuals.Polar(origin, innerRadius, angle + 11);

        context.BeginFigure(rootLeading, true);
        context.CubicBezierTo(
            DashboardVisuals.Polar(origin, outerRadius * 0.62, angle - 30),
            DashboardVisuals.Polar(origin, outerRadius * 0.99, angle - 30),
            tipLeading);
        context.CubicBezierTo(
            DashboardVisuals.Polar(origin, outerRadius, angle - 4),
            DashboardVisuals.Polar(origin, outerRadius, angle + 10),
            tipTrailing);
        context.CubicBezierTo(
            DashboardVisuals.Polar(origin, (innerRadius + outerRadius) * 0.55, angle + 26),
            DashboardVisuals.Polar(origin, innerRadius * 1.08, angle + 22),
            rootTrailing);
        context.CubicBezierTo(
            DashboardVisuals.Polar(origin, innerRadius * 0.94, angle + 5),
            DashboardVisuals.Polar(origin, innerRadius * 0.94, angle - 5),
            rootLeading);
        context.EndFigure(true);
    }

    private void UpdateAnimationState()
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

        // A minimised window or hidden ancestor stops producing motion without
        // tearing the timer down; the next visible tick picks the rotation up.
        if (!IsEffectivelyVisible)
        {
            return;
        }

        var elapsedSeconds = Math.Min(elapsed / 1000.0, 0.1);
        var target = Rpm > 0 ? Rpm : 0.0;

        // Only the motion is eased toward the measured speed; the hub keeps
        // showing the value the machine reported, never an interpolated one.
        _visualRpm += (target - _visualRpm) * Math.Min(1.0, elapsedSeconds / 0.22);
        if (Math.Abs(target - _visualRpm) < 0.5)
        {
            _visualRpm = target;
        }

        if (_visualRpm <= 0.01)
        {
            return;
        }

        _angle = (_angle + _visualRpm * DegreesPerRpmSecond * elapsedSeconds) % 360.0;
        InvalidateVisual();
    }
}
