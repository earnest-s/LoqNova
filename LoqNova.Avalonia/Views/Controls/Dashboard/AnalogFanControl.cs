using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// A fan instrument: a seven-bladed rotor inside a calibrated circular scale,
/// driven by the speed the machine actually reports for one fan channel.
/// <para>
/// The rotor turns at the physical angular velocity of the reported speed,
/// ω = 2π·r/60 rad/s, integrated against real elapsed time, so a higher reported
/// RPM always turns faster and 1 RPM still produces a mathematically nonzero
/// rotation. A real fan turns far faster than a screen can usefully show - at
/// 3000 RPM it is 50 revolutions per second, which a 60 Hz display can only
/// render as an aliased blur - so one fixed display time scale slows the whole
/// pattern down. Because that scale is a constant, the rotation stays exactly
/// proportional to the reported RPM; it is never bucketed into slow/medium/fast.
/// </para>
/// <para>
/// The numeric reading itself lives beside the dial and is bound straight to the
/// reported value, so the displayed RPM is never the eased or scaled one. At
/// zero RPM the rotor settles smoothly to a stop and the loop shuts down
/// completely, so a resting fan costs nothing.
/// </para>
/// </summary>
public class AnalogFanControl : AnimatedGauge
{
    /// <summary>Blades in the rotor. Seven is the common laptop-fan leaf count.</summary>
    private const int BladeCount = 7;

    /// <summary>Scale divisions around the rim and how often a major mark falls.</summary>
    private const int Divisions = 60;
    private const int MajorTickEvery = 5;

    /// <summary>
    /// Fixed display time scale applied to the physical angular velocity. One
    /// revolution takes roughly 2.7 seconds at 3000 RPM: slow enough that the
    /// blade pattern does not alias into a strobe, fast enough to read as a fan.
    /// It is a constant, so angular velocity remains strictly proportional to RPM.
    /// </summary>
    private const double DisplayTimeScale = 0.0075;

    /// <summary>Time constant for the rotor's speed following a new reading.</summary>
    private const double ResponseSeconds = 0.25;

    /// <summary>Reported speed in RPM; -1 means the machine did not report the channel.</summary>
    public static readonly StyledProperty<int> RpmProperty =
        AvaloniaProperty.Register<AnalogFanControl, int>(nameof(Rpm), -1);

    /// <summary>Highest speed the machine reports for this fan, used for the scale.</summary>
    public static readonly StyledProperty<int> MaxRpmProperty =
        AvaloniaProperty.Register<AnalogFanControl, int>(nameof(MaxRpm), -1);

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

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private double _visualRpm;
    private double _angle;

    // Geometry cache, rebuilt only when the dial is resized.
    private double _outerRadius;
    private double _arcRadius;
    private double _arcThickness;
    private double _hubRadius;
    private Geometry _bladesEven = new StreamGeometry();
    private Geometry _bladesOdd = new StreamGeometry();
    private Geometry _ticksMinor = new StreamGeometry();
    private Geometry _ticksMajor = new StreamGeometry();
    private Geometry _trackArc = new StreamGeometry();
    private double _cachedDialSize = double.NaN;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RpmProperty)
        {
            RequestFrames();
            InvalidateVisual();
        }
        else if (change.Property == MaxRpmProperty || change.Property == AccentProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var dial = Math.Min(Bounds.Width, Bounds.Height);
        if (double.IsNaN(dial) || dial <= 48)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
        EnsureGeometry(dial, center);

        var reported = Rpm >= 0;
        var faceBrush = DashboardVisuals.Brush(this, "CardElevatedBrush", Brushes.Transparent);
        var lineBrush = DashboardVisuals.Brush(this, "BorderBrush", Brushes.Gray);
        var strongBrush = DashboardVisuals.Brush(this, "BorderStrongBrush", Brushes.Gray);
        var trackBrush = DashboardVisuals.Brush(this, "SurfaceRaisedBrush", Brushes.Gray);
        var mutedBrush = DashboardVisuals.Brush(this, "TextMutedBrush", Brushes.Gray);
        var accentBrush = reported
            ? Accent ?? DashboardVisuals.Brush(this, "PrimaryBrush", Brushes.DodgerBlue)
            : mutedBrush;

        // Dial face, so the fan reads as part of the same instrument family.
        context.DrawEllipse(faceBrush, new Pen(lineBrush, 1), center, _outerRadius, _outerRadius);

        using (context.PushOpacity(0.5))
        {
            context.DrawGeometry(null, new Pen(lineBrush, 1), _ticksMinor);
        }

        context.DrawGeometry(null, new Pen(strongBrush, 1.4), _ticksMajor);

        // Speed scale, drawn only from what the machine reports: with no reported
        // maximum there is no scale to place the reading on and no arc is drawn.
        context.DrawGeometry(null, new Pen(trackBrush, _arcThickness, null, PenLineCap.Round), _trackArc);

        var ratio = reported && MaxRpm > 0
            ? Math.Clamp(_visualRpm / MaxRpm, 0.0, 1.0)
            : 0.0;
        if (ratio > 0.004)
        {
            var progress = DashboardVisuals.Arc(center, _arcRadius, 0.0, 360.0 * ratio);
            context.DrawGeometry(null, new Pen(accentBrush, _arcThickness, null, PenLineCap.Round), progress);
        }

        // The rotor. Alternating blade weight keeps the rotation direction and
        // speed readable instead of turning into a symmetric blur.
        var rotation = Matrix.CreateRotation(_angle * Math.PI / 180.0)
            * Matrix.CreateTranslation(center.X, center.Y);
        var bladePen = new Pen(lineBrush, 1);
        using (context.PushTransform(rotation))
        {
            context.DrawGeometry(accentBrush, bladePen, _bladesEven);
            using (context.PushOpacity(0.6))
            {
                context.DrawGeometry(accentBrush, bladePen, _bladesOdd);
            }
        }

        // Hub cap with the channel colour at its centre.
        context.DrawEllipse(
            DashboardVisuals.Brush(this, "CardBackgroundBrush", Brushes.Transparent),
            new Pen(strongBrush, 1),
            center, _hubRadius, _hubRadius);
        context.DrawEllipse(accentBrush, null, center, Math.Max(1.5, _hubRadius * 0.22));
    }

    protected override bool Advance(double seconds)
    {
        var target = Rpm > 0 ? Rpm : 0.0;

        // Exponential, time-based approach: the fraction covered depends on the
        // real elapsed time rather than on how many frames happened to be drawn,
        // so the deceleration is identical at any frame rate.
        _visualRpm += (target - _visualRpm) * (1.0 - Math.Exp(-seconds / ResponseSeconds));
        if (Math.Abs(target - _visualRpm) < 0.5)
        {
            _visualRpm = target;
        }

        if (_visualRpm <= 0.001)
        {
            // Settled at rest: stop the loop entirely rather than repainting a
            // still dial sixty times a second.
            _visualRpm = 0.0;
            return false;
        }

        // ω = 2π·r/60 rad/s, scaled by the one fixed display factor. Integrating
        // against real elapsed time keeps the speed independent of frame rate.
        var omegaDegreesPerSecond = _visualRpm / 60.0 * Math.Tau * DisplayTimeScale * (180.0 / Math.PI);
        _angle = (_angle + omegaDegreesPerSecond * seconds) % 360.0;
        return true;
    }

    /// <summary>
    /// Rebuilds the static geometry when the dial size changes. The blades are
    /// built around the origin so one transform per frame can rotate them.
    /// </summary>
    private void EnsureGeometry(double dial, Point center)
    {
        if (Math.Abs(dial - _cachedDialSize) < 0.5)
        {
            return;
        }

        _cachedDialSize = dial;
        _outerRadius = dial / 2.0 - 1.0;
        _hubRadius = _outerRadius * 0.19;
        _arcRadius = _outerRadius * 0.79;
        _arcThickness = Math.Clamp(_outerRadius * 0.05, 2.0, 5.0);

        var tickOuter = _outerRadius * 0.92;
        var tickInner = _outerRadius * 0.86;
        var ticks = DashboardVisuals.Ticks(center, tickInner, tickOuter, Divisions, MajorTickEvery, 0, 360);
        _ticksMinor = ticks.Minor;
        _ticksMajor = ticks.Major;

        _trackArc = DashboardVisuals.Arc(center, _arcRadius, 0, 360);

        var bladeOuter = _outerRadius * 0.72;
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
}
