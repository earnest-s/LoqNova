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
/// <c>ω = rpm·360/60</c> degrees per second - the same mapping a real fan obeys.
/// There is no arbitrary slowness factor and no slow/medium/fast bucketing: the
/// angle is integrated against real elapsed time and reduced modulo one turn, so
/// it stays correct at any frame rate and the accumulated angle cannot lose
/// precision.
/// </para>
/// <para>
/// A display cannot show every revolution: 4800 RPM is 80 revolutions per
/// second. Rather than substitute an unrelated speed, each frame integrates the
/// true angle and the rotor is drawn as a motion-blur average across the angle
/// actually travelled in that frame - saturating at one full turn, which is the
/// uniform blur a genuinely 80-rev/s fan produces. At low speeds the travelled
/// angle is small and the rotor is drawn sharply, still strictly proportional to
/// RPM, so 1 RPM and a 1 RPM difference remain real, non-zero changes.
/// </para>
/// <para>
/// The numeric reading itself lives beside the dial and is bound straight to the
/// reported value, so the displayed RPM is never the ghosted one. At zero RPM
/// the rotor settles smoothly to a stop and the loop shuts down completely, so a
/// resting fan costs nothing.
/// </para>
/// </summary>
public class AnalogFanControl : AnimatedGauge
{
    /// <summary>Blades in the rotor. Seven is the common laptop-fan leaf count.</summary>
    private const int BladeCount = 7;

    /// <summary>Scale divisions around the rim and how often a major mark falls.</summary>
    private const int Divisions = 60;
    private const int MajorTickEvery = 5;

    /// <summary>Time constant for the rotor's speed following a new reading.</summary>
    private const double ResponseSeconds = 0.18;

    /// <summary>
    /// Angle travelled within one frame above which the rotor is drawn as a
    /// motion blur instead of a single sharp position. Below it, a frame is
    /// sharp.
    /// </summary>
    private const double BlurStartDegrees = 20.0;

    /// <summary>Degrees of travel each blur sample spans. Smaller than this is sharp.</summary>
    private const double BlurSampleDegrees = 25.0;

    /// <summary>Most blur samples drawn in one frame, bounding UI-thread work.</summary>
    private const int MaxBlurSamples = 14;

    /// <summary>Reported speed in RPM; -1 means the machine did not report the channel.</summary>
    public static readonly StyledProperty<int> RpmProperty =
        AvaloniaProperty.Register<AnalogFanControl, int>(nameof(Rpm), -1);

    /// <summary>Highest speed the machine reports for this fan, used for the scale.</summary>
    public static readonly StyledProperty<int> MaxRpmProperty =
        AvaloniaProperty.Register<AnalogFanControl, int>(nameof(MaxRpm), -1);

    /// <summary>Channel accent, the active power mode's colour.</summary>
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
    private double _frameAdvanceDegrees;

    // Geometry cache, rebuilt only when the dial is resized.
    private double _outerRadius;
    private double _arcRadius;
    private double _arcThickness;
    private double _hubRadius;
    private double _hubDotRadius;
    private Geometry _bladesEven = new StreamGeometry();
    private Geometry _bladesOdd = new StreamGeometry();
    private Geometry _ticksMinor = new StreamGeometry();
    private Geometry _ticksMajor = new StreamGeometry();
    private Geometry _trackArc = new StreamGeometry();
    private double _cachedWidth = double.NaN;
    private double _cachedHeight = double.NaN;

    /// <summary>
    /// The reported-speed arc, rebuilt only when the level actually moves. The
    /// rotor repaints every frame, so caching this keeps a steady reading from
    /// allocating a new geometry each frame.
    /// </summary>
    private Geometry _progressArc = new StreamGeometry();
    private double _progressRatio = double.NaN;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RpmProperty)
        {
            RequestFrames();
            InvalidateVisual();
        }
        else if (change.Property == AccentProperty)
        {
            // The accent brush is the active power-mode colour. Retargeting it
            // starts an eased transition, so a mode change blends rather than
            // flashes.
            SetAccentTarget(ResolveAccentColor());
            RequestFrames();
            InvalidateVisual();
        }
        else if (change.Property == MaxRpmProperty)
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

        SetAccentTarget(ResolveAccentColor());
        var accentBrush = reported
            ? (IBrush)new SolidColorBrush(CurrentAccent)
            : mutedBrush;

        // Dial face: a slightly raised panel inside the card.
        context.DrawEllipse(faceBrush, new Pen(lineBrush, 1), center, _outerRadius, _outerRadius);

        using (context.PushOpacity(0.5))
        {
            context.DrawGeometry(null, new Pen(lineBrush, 1), _ticksMinor);
        }

        context.DrawGeometry(null, new Pen(strongBrush, 1.4), _ticksMajor);

        // Speed scale track, with the reported-speed arc drawn on top of it. The
        // track is the dial's own scale; the arc exists only where the machine
        // reports a speed and a maximum to place it on.
        context.DrawGeometry(null, new Pen(trackBrush, _arcThickness, null, PenLineCap.Round), _trackArc);

        var ratio = VisualRatio();
        if (ratio > 0.004)
        {
            if (double.IsNaN(_progressRatio) || Math.Abs(ratio - _progressRatio) > 0.0005)
            {
                // 0.0 and 1.0 are both well defined: a full turn is a complete
                // ring, which DashboardVisuals.Arc draws as two half turns.
                _progressArc = DashboardVisuals.Arc(center, _arcRadius, 0.0, 360.0 * ratio);
                _progressRatio = ratio;
            }

            context.DrawGeometry(null, new Pen(accentBrush, _arcThickness, null, PenLineCap.Round), _progressArc);
        }

        DrawRotor(context, center, accentBrush, new Pen(lineBrush, 1));

        // Hub cap with the accent at its centre. It is drawn after the rotor and
        // is never rotated, so the blades turn under a stationary hub.
        context.DrawEllipse(
            DashboardVisuals.Brush(this, "CardBackgroundBrush", Brushes.Transparent),
            new Pen(strongBrush, 1),
            center, _hubRadius, _hubRadius);
        context.DrawEllipse(accentBrush, null, center, _hubDotRadius, _hubDotRadius);
    }

    /// <summary>
    /// Draws the rotor at its true angle, averaged across the angle travelled in
    /// the frame so a fast fan reads as motion rather than a strobe. One pass is
    /// used while the frame's travel is small.
    /// </summary>
    private void DrawRotor(DrawingContext context, Point center, IBrush accentBrush, Pen bladePen)
    {
        var travelled = _frameAdvanceDegrees;
        if (travelled <= BlurStartDegrees)
        {
            DrawBlades(context, center, accentBrush, bladePen, 1.0, 0.0, 1);
            return;
        }

        // The blur never needs to exceed one whole turn: once the rotor has
        // passed every angle in a frame the result is a uniform ring.
        var span = Math.Min(travelled, 360.0);
        var samples = Math.Clamp((int)Math.Ceiling(span / BlurSampleDegrees) + 1, 2, MaxBlurSamples);
        var step = span / (samples - 1);
        var opacity = 1.0 / samples;

        DrawBlades(context, center, accentBrush, bladePen, opacity, step, samples);
    }

    private void DrawBlades(
        DrawingContext context, Point center, IBrush accentBrush, Pen bladePen,
        double opacity, double step, int count)
    {
        for (var i = 0; i < count; i++)
        {
            // Samples trail the current angle, matching the clockwise rotation,
            // and cover the angle integrated since the previous frame.
            var angle = _angle - i * step;
            using (context.PushOpacity(opacity))
            using (context.PushTransform(
                Matrix.CreateRotation(angle * Math.PI / 180.0)
                * Matrix.CreateTranslation(center.X, center.Y)))
            {
                context.DrawGeometry(accentBrush, bladePen, _bladesEven);
                using (context.PushOpacity(0.6))
                {
                    context.DrawGeometry(accentBrush, bladePen, _bladesOdd);
                }
            }
        }
    }

    protected override bool Advance(double seconds)
    {
        var accentMoving = AdvanceAccent(seconds);

        // A negative reading means "not reported": the rotor is still and the
        // numeric readout beside it prints the dash. Zero is a real speed and
        // likewise parks the rotor.
        var target = Math.Max(0, Rpm);

        var delta = target - _visualRpm;
        if (Math.Abs(delta) > 0.001)
        {
            _visualRpm += delta * (1.0 - Math.Exp(-seconds / ResponseSeconds));
            if (Math.Abs(target - _visualRpm) < 0.5)
            {
                _visualRpm = target;
            }
        }
        else
        {
            _visualRpm = target;
        }

        if (_visualRpm <= 0.001)
        {
            // Settled at rest: stop the loop entirely rather than repainting a
            // still dial sixty times a second.
            _visualRpm = 0.0;
            _frameAdvanceDegrees = 0.0;
            return accentMoving;
        }

        // ω = rpm * 360 / 60 degrees per second - the physical mapping, with no
        // arbitrary cap. Integrating against real elapsed time keeps the speed
        // independent of frame rate; the traveller is remembered so the render
        // pass can blur across exactly what was integrated.
        var omegaDegreesPerSecond = _visualRpm * 360.0 / 60.0;
        var advance = omegaDegreesPerSecond * seconds;
        _frameAdvanceDegrees = advance;
        _angle = Normalize(_angle + advance);
        return true;
    }

    /// <summary>
    /// The reported speed as a fraction of the reported maximum, for the arc and
    /// for nothing else - the numeric readout beside the dial always prints the
    /// machine's own value, unreported or over the maximum included.
    /// <para>
    /// Every boundary is explicit. Without a reported maximum there is no scale
    /// to place the reading on and no arc at all; at rest the arc is empty; a
    /// reading at the maximum pins the arc to a complete ring, and one above the
    /// maximum is clamped to that same ring rather than wrapping past it. The
    /// mapping is linear over the whole range, so 1 RPM is a real, non-zero
    /// fraction and the smallest change in speed is a real change in the arc.
    /// </para>
    /// </summary>
    private double VisualRatio()
    {
        if (MaxRpm <= 0)
        {
            return 0.0;
        }

        return Math.Clamp(_visualRpm / MaxRpm, 0.0, 1.0);
    }

    /// <summary>
    /// The accent to draw, resolved from the brush the page binds (the active
    /// power-mode colour). A brush that is absent or fully transparent falls back
    /// to the application primary, so the fan is never invisible while the
    /// power-mode colour is still unknown.
    /// </summary>
    private Color ResolveAccentColor()
    {
        if (Accent is ISolidColorBrush { Color.A: > 0 } accent)
        {
            return accent.Color;
        }

        var fallback = DashboardVisuals.Brush(this, "PrimaryBrush", Brushes.DodgerBlue);
        return fallback is ISolidColorBrush solid ? solid.Color : Colors.DodgerBlue;
    }

    /// <summary>Keeps an accumulated angle inside [0, 360) without drift.</summary>
    private static double Normalize(double degrees)
    {
        var normalized = degrees % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    /// <summary>
    /// Rebuilds the static geometry when the dial size changes. The blades are
    /// built around the origin so one transform per frame can rotate them.
    /// </summary>
    private void EnsureGeometry(double dial, Point center)
    {
        // Keyed on both dimensions: a dial that only grows sideways still moves
        // its centre, so a size cache on the shorter side alone would go stale.
        if (Math.Abs(Bounds.Width - _cachedWidth) < 0.5 && Math.Abs(Bounds.Height - _cachedHeight) < 0.5)
        {
            return;
        }

        _cachedWidth = Bounds.Width;
        _cachedHeight = Bounds.Height;
        _outerRadius = dial / 2.0 - 1.0;
        _hubRadius = _outerRadius * 0.19;
        _hubDotRadius = Math.Max(1.5, _hubRadius * 0.22);
        _arcRadius = _outerRadius * 0.79;
        _arcThickness = Math.Clamp(_outerRadius * 0.05, 2.0, 5.0);

        var tickOuter = _outerRadius * 0.92;
        var tickInner = _outerRadius * 0.86;
        var ticks = DashboardVisuals.Ticks(center, tickInner, tickOuter, Divisions, MajorTickEvery, 0, 360);
        _ticksMinor = ticks.Minor;
        _ticksMajor = ticks.Major;

        _trackArc = DashboardVisuals.Arc(center, _arcRadius, 0, 360);
        _progressRatio = double.NaN;

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
        // The blade is deliberately narrow against its pitch (7 blades = 51.4 deg).
        // Every control point stays within +/-16 deg of the blade axis, so the
        // silhouette can never swell into its neighbour: the swept shape keeps a
        // gap of roughly 19 deg next to it, and the blade pitch stays legible at
        // rest instead of the rotor closing up into a solid disc.
        var tipRadius = outerRadius * 0.94;
        var rootLeading = DashboardVisuals.Polar(origin, innerRadius, angle - 7);
        var tipLeading = DashboardVisuals.Polar(origin, tipRadius, angle - 15);
        var tipTrailing = DashboardVisuals.Polar(origin, tipRadius, angle + 11);
        var rootTrailing = DashboardVisuals.Polar(origin, innerRadius, angle + 8);

        context.BeginFigure(rootLeading, true);
        context.CubicBezierTo(
            DashboardVisuals.Polar(origin, outerRadius * 0.55, angle - 16),
            DashboardVisuals.Polar(origin, outerRadius * 0.92, angle - 16),
            tipLeading);
        context.CubicBezierTo(
            DashboardVisuals.Polar(origin, outerRadius, angle - 3),
            DashboardVisuals.Polar(origin, outerRadius, angle + 7),
            tipTrailing);
        context.CubicBezierTo(
            DashboardVisuals.Polar(origin, (innerRadius + outerRadius) * 0.55, angle + 16),
            DashboardVisuals.Polar(origin, innerRadius * 1.06, angle + 13),
            rootTrailing);
        context.CubicBezierTo(
            DashboardVisuals.Polar(origin, innerRadius * 0.96, angle + 4),
            DashboardVisuals.Polar(origin, innerRadius * 0.96, angle - 4),
            rootLeading);
        context.EndFigure(true);
    }
}
