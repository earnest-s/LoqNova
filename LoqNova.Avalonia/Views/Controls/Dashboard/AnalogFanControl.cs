using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// A fan instrument: a shaded seven-bladed impeller running inside a housing,
/// driven by the speed the machine actually reports for one fan channel.
/// <para>
/// The rotor turns at a speed derived from the reported RPM by a single
/// continuous, strictly increasing function - see <see cref="DegreesPerSecond"/>.
/// There is no bucketing, no quantisation and no clamp: every RPM in the
/// supported range has its own distinct angular velocity, so a change of one RPM
/// is a change of speed. The angle is integrated against real elapsed time rather
/// than a frame count, so the motion is frame-rate independent, and the
/// accumulated angle is never reset, so a telemetry refresh can neither restart
/// nor stall the rotation.
/// </para>
/// <para>
/// A rotor is a periodic pattern, and a periodic pattern sampled at a fixed rate
/// cannot show an unbounded speed faithfully: a seven-bladed rotor repeats every
/// 51.4 degrees, so anything beyond half that per frame is ambiguous. The
/// mapping above therefore compresses rather than clips - it stays strictly
/// increasing across the whole range, but it keeps the per-frame travel below the
/// point where a faster fan would appear to slow down, stall or reverse. A rotor
/// that visually reverses as it speeds up is the artifact this replaces; it made
/// a fan at 3600 RPM look completely stationary.
/// </para>
/// <para>
/// Each frame the rotor is drawn as a motion-blur average across the angle
/// actually travelled, with the opacity falling off behind the leading edge, so a
/// fast fan reads as a long-exposure smear and a parked or barely-turning fan
/// still reads as distinct blades. The smear is capped below half the free angle
/// between blades, so it can never walk into a neighbour's gap and close the
/// rotor up into a flat disc.
/// </para>
/// <para>
/// The numeric reading itself lives beside the dial and is bound straight to the
/// reported value, so the displayed RPM is never the ghosted one. At zero RPM the
/// rotor settles smoothly to a stop and the loop shuts down completely, so a
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
    /// motion blur instead of a single sharp position. Chosen low enough that the
    /// smear builds up through the middle of the range rather than only in the
    /// last few percent, so a working fan reads as moving and a parked or
    /// barely-turning fan still reads as distinct blades.
    /// </summary>
    private const double BlurStartDegrees = 6.0;

    /// <summary>Degrees of travel each blur sample spans.</summary>
    private const double BlurSampleDegrees = 3.0;

    /// <summary>Most blur samples drawn in one frame, bounding UI-thread work.</summary>
    private const int MaxBlurSamples = 6;

    /// <summary>
    /// How quickly the trailing blur samples fade. The samples behind the leading
    /// edge carry an exponentially falling weight, which is what gives the smear
    /// its bright head and soft tail instead of an evenly-lit band that could be
    /// mistaken for a stationary part.
    /// </summary>
    private const double BlurFalloff = 0.55;

    /// <summary>The angle between adjacent blades (7 blades = 51.4 degrees).</summary>
    private const double BladePitchDegrees = 360.0 / BladeCount;

    /// <summary>The blade's widest angular extent; its control points reach +/-16 degrees.</summary>
    private const double BladeWidthDegrees = 32.0;

    /// <summary>The free angle between two neighbouring blades: pitch minus the blade.</summary>
    private const double BladeGapDegrees = BladePitchDegrees - BladeWidthDegrees;

    /// <summary>
    /// The most the smear may spread a single blade, capped at half the free angle
    /// between blades. Smearing further walks the copies into their neighbours'
    /// gaps and the rotor closes up into a flat disc.
    /// </summary>
    private const double MaxBlurSpanDegrees = BladeGapDegrees / 2.0;

    /// <summary>
    /// The frame the speed mapping is normalised against. The mapping is a
    /// continuous function of RPM, so it is correct at any real frame rate; this
    /// only fixes the scale so the fastest setting lands where it should.
    /// </summary>
    private const double NominalFrameSeconds = 1.0 / 60.0;

    /// <summary>
    /// Highest per-frame travel the mapping will ever produce. Held below half the
    /// blade pitch (25.7 degrees) so the rotor's rotation is always unambiguous:
    /// it never appears to stall, slow down or run backwards as speed climbs.
    /// </summary>
    private const double MaxFrameAdvanceDegrees = 22.0;

    /// <summary>The ceiling of the mapping, in degrees of rotation per second.</summary>
    private const double MaxAngularDegreesPerSecond =
        MaxFrameAdvanceDegrees / NominalFrameSeconds;

    /// <summary>
    /// The speed at which the mapping reaches half its ceiling. The low end stays
    /// close to linear - a fan barely turning is barely turning - while the top
    /// end compresses smoothly instead of hitting a wall.
    /// </summary>
    private const double CharacteristicRpm = 2000.0;

    /// <summary>
    /// Per-sample opacity weights for each blur sample count, normalised so the
    /// smear carries the same total ink as a single sharp rotor. Built once, so
    /// animating the rotor never allocates.
    /// </summary>
    private static readonly double[][] BlurWeights = BuildBlurWeights();

    /// <summary>
    /// The angular velocity, in degrees per second, that a reported
    /// <paramref name="rpm"/> turns the rotor at.
    /// <para>
    /// One continuous, strictly increasing function over the whole range: zero at
    /// rest, near-linear at the bottom, smoothly compressing toward
    /// <see cref="MaxAngularDegreesPerSecond"/> at the top. Its derivative is
    /// never zero, so no two speeds share a displayed rotation and the smallest
    /// possible change in RPM is a change in rotation - while the per-frame travel
    /// stays under half the blade pitch, where a seven-bladed rotor sampled at
    /// 60 Hz is still unambiguous.
    /// </para>
    /// </summary>
    public static double DegreesPerSecond(double rpm)
    {
        if (rpm <= 0.0)
        {
            return 0.0;
        }

        return MaxAngularDegreesPerSecond * rpm / (rpm + CharacteristicRpm);
    }

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
    private double _shroudRadius;
    private double _arcThickness;
    private double _shroudThickness;
    private double _hubRadius;
    private double _hubDotRadius;
    private Geometry _bladesEven = new StreamGeometry();
    private Geometry _bladesOdd = new StreamGeometry();
    private Geometry _ridgeEven = new StreamGeometry();
    private Geometry _ridgeOdd = new StreamGeometry();
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

    // Pens and brushes, rebuilt only when the token they were built from changes.
    // A steady fan on a steady theme repaints with no allocations at all.
    private IBrush? _facePenSource;
    private IBrush? _minorPenSource;
    private IBrush? _strongPenSource;
    private IBrush? _trackPenSource;
    private Pen? _facePen;
    private Pen? _minorPen;
    private Pen? _majorPen;
    private Pen? _trackPen;
    private Pen? _hubPen;

    /// <summary>The ink the rotor and its housing are drawn in - the eased power-mode accent.</summary>
    private Color _inkColor = Colors.Transparent;
    private bool _inkBuilt;
    private IBrush? _accentSolid;
    private IBrush? _bladeBodyBrush;
    private IBrush? _bladeAltBrush;
    private IBrush? _ridgeBrush;
    private IBrush? _backdropBrush;
    private Pen? _shroudPen;
    private Pen? _bladeEdgePen;
    private Pen? _hubRingPen;
    private Pen? _progressPen;

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

        EnsurePens(lineBrush, strongBrush, trackBrush);

        SetAccentTarget(ResolveAccentColor());

        // A channel the machine did not report draws in the muted token rather
        // than in the mode accent, so "no data" never looks like a working fan
        // that happens to be quiet.
        var ink = reported
            ? CurrentAccent
            : SolidColor(mutedBrush, Colors.Gray);
        EnsureInk(ink);

        // Dial face: a slightly raised panel inside the card.
        context.DrawEllipse(faceBrush, _facePen, center, _outerRadius, _outerRadius);

        // The well the impeller sits in. It is deliberately darker than the face
        // and carries no bright pixel, so it seats the rotor without competing
        // with it - and without reading as a second, filled-in scale.
        context.DrawEllipse(_backdropBrush, null, center, _shroudRadius, _shroudRadius);

        using (context.PushOpacity(0.5))
        {
            context.DrawGeometry(null, _minorPen, _ticksMinor);
        }

        context.DrawGeometry(null, _majorPen, _ticksMajor);

        // The housing the rotor turns inside: a fine ring that frames the blades
        // and separates them from the calibration scale around them.
        context.DrawEllipse(null, _shroudPen, center, _shroudRadius, _shroudRadius);

        // Speed scale track, with the reported-speed arc drawn on top of it. The
        // track is the dial's own scale; the arc exists only where the machine
        // reports a speed and a maximum to place it on.
        context.DrawGeometry(null, _trackPen, _trackArc);

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

            context.DrawGeometry(null, _progressPen, _progressArc);
        }

        DrawRotor(context);

        // Hub cap with the accent at its centre. It is drawn after the rotor and
        // is never rotated, so the blades turn under a stationary hub.
        context.DrawEllipse(
            DashboardVisuals.Brush(this, "CardBackgroundBrush", Brushes.Transparent),
            _hubPen,
            center, _hubRadius, _hubRadius);
        context.DrawEllipse(null, _hubRingPen, center, _hubRadius * 0.66, _hubRadius * 0.66);
        context.DrawEllipse(_accentSolid, null, center, _hubDotRadius, _hubDotRadius);
    }

    /// <summary>
    /// Draws the rotor at its true angle, averaged across the angle travelled in
    /// the frame so a fast fan reads as motion rather than a strobe. One pass is
    /// used while the frame's travel is small.
    /// </summary>
    private void DrawRotor(DrawingContext context)
    {
        var travelled = _frameAdvanceDegrees;
        if (travelled <= BlurStartDegrees)
        {
            DrawBlades(context, 1.0, 0.0);
            return;
        }

        // The smear is capped at half the free angle between blades. Spreading it
        // further walks each copy into its neighbour's gap and the rotor closes up
        // into a flat disc; capped here, a fast fan smears its blades over a few
        // degrees and stays legible as a rotor.
        var span = Math.Min(travelled, MaxBlurSpanDegrees);
        var samples = Math.Clamp((int)Math.Ceiling(span / BlurSampleDegrees) + 1, 2, MaxBlurSamples);
        var step = span / (samples - 1);
        var weights = BlurWeights[samples];

        for (var i = 0; i < samples; i++)
        {
            DrawBlades(context, weights[i], i * step);
        }
    }

    /// <summary>
    /// One pass of the impeller, turned to the current angle and trailed back by
    /// <paramref name="offset"/> degrees at <paramref name="opacity"/>.
    /// </summary>
    private void DrawBlades(DrawingContext context, double opacity, double offset)
    {
        if (opacity <= 0.0015)
        {
            return;
        }

        using (context.PushOpacity(opacity))
        using (context.PushTransform(
            Matrix.CreateRotation((_angle - offset) * Math.PI / 180.0)
            * Matrix.CreateTranslation(Bounds.Width / 2.0, Bounds.Height / 2.0)))
        {
            // Even blades carry the accent outright; the alternating set is drawn
            // from its own, slightly shaded gradient, which is what gives the
            // impeller its two-tone depth instead of the flat cut-out it replaced.
            context.DrawGeometry(_bladeBodyBrush, _bladeEdgePen, _bladesEven);
            context.DrawGeometry(_ridgeBrush, null, _ridgeEven);
            context.DrawGeometry(_bladeAltBrush, _bladeEdgePen, _bladesOdd);
            using (context.PushOpacity(0.7))
            {
                context.DrawGeometry(_ridgeBrush, null, _ridgeOdd);
            }
        }
    }

    protected override bool Advance(double seconds)
    {
        var accentMoving = AdvanceAccent(seconds);

        // A negative reading means "not reported": the rotor is still and the
        // numeric readout beside it prints the dash. Zero is a real speed and
        // likewise parks the rotor. The rotor is eased rather than snapped to
        // either, so a dropped reading or a fan switching off coasts down the way
        // the hardware does instead of freezing mid-air.
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

        // ω comes straight from the reported speed through the mapping above -
        // one continuous function, no cap and no bucket - and is integrated
        // against real elapsed time, so the rotor's speed tracks the machine
        // across the whole range and stays independent of the frame rate. The
        // frame's travel is handed to the render pass so it can smear the rotor
        // across exactly what was integrated.
        var advance = DegreesPerSecond(_visualRpm) * seconds;
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
        return SolidColor(fallback, Colors.DodgerBlue);
    }

    /// <summary>The colour of a solid brush, or <paramref name="fallback"/>.</summary>
    private static Color SolidColor(IBrush? brush, Color fallback)
        => brush is ISolidColorBrush solid ? solid.Color : fallback;

    /// <summary>Keeps an accumulated angle inside [0, 360) without drift.</summary>
    private static double Normalize(double degrees)
    {
        var normalized = degrees % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static double[][] BuildBlurWeights()
    {
        var table = new double[MaxBlurSamples + 1][];
        for (var samples = 2; samples <= MaxBlurSamples; samples++)
        {
            var weights = new double[samples];
            var total = 0.0;
            for (var i = 0; i < samples; i++)
            {
                weights[i] = Math.Exp(-i * BlurFalloff);
                total += weights[i];
            }

            for (var i = 0; i < samples; i++)
            {
                weights[i] /= total;
            }

            table[samples] = weights;
        }

        return table;
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
        _shroudRadius = _outerRadius * 0.745;
        _shroudThickness = Math.Max(1.0, _outerRadius * 0.016);
        _hubRadius = _outerRadius * 0.21;
        _hubDotRadius = Math.Max(1.5, _hubRadius * 0.20);
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
        _ridgeEven = BuildRidges(_hubRadius, bladeOuter, even: true);
        _ridgeOdd = BuildRidges(_hubRadius, bladeOuter, even: false);
    }

    /// <summary>
    /// Rebuilds the theme-derived pens when the brush behind one of them is
    /// swapped - a theme change replaces the resource rather than mutating it, so
    /// comparing the sources is what keeps a pen from holding a stale colour.
    /// </summary>
    private void EnsurePens(IBrush line, IBrush strong, IBrush track)
    {
        if (!ReferenceEquals(_facePenSource, line))
        {
            _facePenSource = line;
            _facePen = new Pen(line, 1);
        }

        if (!ReferenceEquals(_minorPenSource, line))
        {
            _minorPenSource = line;
            _minorPen = new Pen(line, 1);
        }

        if (!ReferenceEquals(_strongPenSource, strong))
        {
            _strongPenSource = strong;
            _majorPen = new Pen(strong, 1.4);
            _hubPen = new Pen(strong, 1);
        }

        if (!ReferenceEquals(_trackPenSource, track))
        {
            _trackPenSource = track;
            _trackPen = new Pen(track, _arcThickness, null, PenLineCap.Round);
        }
    }

    /// <summary>
    /// Rebuilds the accent-derived brushes and pens when the eased accent lands on
    /// a new colour. The impeller is shaded from the accent rather than filled with
    /// it, so it reads as a moulded part lit from the hub instead of a flat
    /// cut-out - the "static disc with decorative lines" this replaces.
    /// </summary>
    private void EnsureInk(Color ink)
    {
        if (_inkBuilt && ink == _inkColor)
        {
            return;
        }

        _inkBuilt = true;
        _inkColor = ink;

        _accentSolid = new SolidColorBrush(ink);

        // The blade axis runs from the hub out to the tip along -Y in the rotor's
        // own space, so the gradient is anchored there and turns with the blade.
        var start = new RelativePoint(0.0, -_hubRadius, RelativeUnit.Absolute);
        var end = new RelativePoint(0.0, -_outerRadius * 0.72, RelativeUnit.Absolute);

        _bladeBodyBrush = new LinearGradientBrush
        {
            StartPoint = start,
            EndPoint = end,
            GradientStops =
            {
                new GradientStop(Tint(ink, 0.20, 1.0), 0.0),
                new GradientStop(ink, 0.52),
                new GradientStop(Tint(ink, -0.46, 0.96), 1.0),
            },
        };

        var shaded = Tint(ink, -0.14, 1.0);
        _bladeAltBrush = new LinearGradientBrush
        {
            StartPoint = start,
            EndPoint = end,
            GradientStops =
            {
                new GradientStop(Tint(shaded, 0.20, 1.0), 0.0),
                new GradientStop(shaded, 0.52),
                new GradientStop(Tint(shaded, -0.46, 0.96), 1.0),
            },
        };

        // The catch-light along each blade's leading edge. It is the single
        // strongest cue that the rotor is a curved, three-dimensional part.
        _ridgeBrush = new SolidColorBrush(Tint(ink, 0.30, 0.92));

        _backdropBrush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops =
            {
                new GradientStop(Tint(ink, 0.0, 0.13), 0.0),
                new GradientStop(Tint(ink, 0.0, 0.0), 1.0),
            },
        };

        _shroudPen = new Pen(new SolidColorBrush(Tint(ink, 0.0, 0.22)), _shroudThickness);
        _bladeEdgePen = new Pen(new SolidColorBrush(Tint(ink, -0.55, 0.85)), 0.9);
        _hubRingPen = new Pen(new SolidColorBrush(Tint(ink, 0.0, 0.34)), Math.Max(1.0, _outerRadius * 0.012));
        _progressPen = new Pen(_accentSolid, _arcThickness, null, PenLineCap.Round);
    }

    /// <summary>
    /// Lightens (<paramref name="amount"/> positive) or darkens (negative) a
    /// colour, and scales its opacity.
    /// </summary>
    private static Color Tint(Color source, double amount, double opacity)
    {
        static byte Mix(byte channel, double amount)
        {
            var value = channel + (amount >= 0
                ? (255 - channel) * amount
                : channel * amount);
            return (byte)Math.Clamp(Math.Round(value), 0.0, 255.0);
        }

        return Color.FromArgb(
            (byte)Math.Clamp(Math.Round(255.0 * opacity), 0.0, 255.0),
            Mix(source.R, amount),
            Mix(source.G, amount),
            Mix(source.B, amount));
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

    private static Geometry BuildRidges(double innerRadius, double outerRadius, bool even)
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

                AppendRidge(context, innerRadius, outerRadius, i * 360.0 / BladeCount);
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

    /// <summary>
    /// The catch-light that runs along a blade's leading edge. It sits inside the
    /// blade silhouette at every point - hugging the leading edge, starting clear
    /// of the hub and stopping short of the tip - so it can never spill past the
    /// blade into the gap between two of them.
    /// </summary>
    private static void AppendRidge(StreamGeometryContext context, double innerRadius, double outerRadius, double angle)
    {
        var origin = default(Point);
        var tipRadius = outerRadius * 0.94;

        // The ribbon's two long edges, in degrees off the blade axis. Both run
        // parallel to the blade's own leading edge, which is what keeps the band
        // an even width from root to tip.
        const double innerEdge = 0.4;
        const double outerEdge = 3.4;

        var startRadius = innerRadius * 1.16;
        var nearTipRadius = tipRadius * 0.98;
        var farTipRadius = tipRadius * 0.84;

        context.BeginFigure(DashboardVisuals.Polar(origin, startRadius, angle - 7 + innerEdge), true);
        context.CubicBezierTo(
            DashboardVisuals.Polar(origin, outerRadius * 0.55, angle - 16 + innerEdge),
            DashboardVisuals.Polar(origin, outerRadius * 0.92, angle - 16 + innerEdge),
            DashboardVisuals.Polar(origin, nearTipRadius, angle - 15 + innerEdge));
        context.CubicBezierTo(
            DashboardVisuals.Polar(origin, farTipRadius, angle - 13 + outerEdge),
            DashboardVisuals.Polar(origin, farTipRadius, angle - 15 + outerEdge),
            DashboardVisuals.Polar(origin, farTipRadius, angle - 15 + outerEdge + 2.6));
        context.CubicBezierTo(
            DashboardVisuals.Polar(origin, outerRadius * 0.78, angle - 13 + outerEdge),
            DashboardVisuals.Polar(origin, outerRadius * 0.42, angle - 10 + outerEdge),
            DashboardVisuals.Polar(origin, startRadius, angle - 7 + outerEdge));
        context.EndFigure(true);
    }
}
