using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// The utilisation instrument of one device (CPU or GPU), drawn as an
/// automotive-style semicircular dial: a calibrated tick band with the scale
/// numerals outside it, a thin needle on a defined centre pivot, and the live
/// percentage below the pivot.
/// <para>
/// The instrument is one painted control rather than a tree of shapes, so a
/// frame is a repaint and never a layout pass. The arc, needle and readout all
/// describe the same reported channel. The first frames before the machine has
/// reported anything hold the needle at the bottom of the scale; from the first
/// frame the needle and arc run the instrument's opening self-test - down to the
/// bottom of the scale, out across the full arc, then settled on the reading -
/// and from then on they ease toward each new reading with a time-based
/// exponential approach. The self-test replays whenever the instrument becomes
/// active or the power mode changes. A channel the machine does not report is
/// drawn muted with no needle and no arc, never as a zero.
/// </para>
/// <para>
/// The active arc and the needle carry the active power mode's colour, handed in
/// by the page, and are eased between values so a mode change blends rather than
/// flashes. The top fifth of the scale is a redline drawn on the tick run only,
/// so the red zone stays aligned to the scale without tinting the whole dial.
/// </para>
/// </summary>
public class ClusterGauge : AnimatedGauge
{
    /// <summary>
    /// A true semicircle: 9 o'clock (270°) around the top to 3 o'clock (90°).
    /// The open lower half carries the digital readout, the way a modern cluster
    /// keeps the needle sweep clear of the numbers.
    /// </summary>
    private const double StartAngle = 270.0;
    private const double SweepAngle = 180.0;

    /// <summary>20 steps of 5% with a major mark every five: 0/25/50/75/100.</summary>
    private const int Divisions = 20;
    private const int MajorTickEvery = 5;

    /// <summary>
    /// Where the redline starts, as a fraction of the scale. It lands on tick
    /// sixteen of twenty, so the red zone sits exactly on the tick run.
    /// </summary>
    private const double RedlineStartFraction = 0.80;

    /// <summary>Time constant for the needle's approach to a new reading.</summary>
    private const double EaseSeconds = 0.18;

    /// <summary>Major scale values, drawn outside the tick run.</summary>
    private static readonly double[] LabelFractions = [0.0, 0.25, 0.5, 0.75, 1.0];

    /// <summary>Reported reading; -1 means the machine did not report the channel.</summary>
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<ClusterGauge, int>(nameof(Value), -1);

    /// <summary>Highest reading the machine reports for the channel.</summary>
    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<ClusterGauge, int>(nameof(Maximum), -1);

    /// <summary>Formatted reading drawn in the hub; a dash when unreported.</summary>
    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<ClusterGauge, string?>(nameof(ValueText), "-");

    /// <summary>Unit suffix drawn beneath the reading, e.g. "%".</summary>
    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<ClusterGauge, string?>(nameof(Unit));

    /// <summary>Channel accent. A design-token fallback is used when unset.</summary>
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<ClusterGauge, IBrush?>(nameof(Accent));

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

    // Geometry cache, rebuilt only when the control is resized.
    private double _cachedWidth = double.NaN;
    private double _cachedHeight = double.NaN;
    private double _outerRadius;
    private double _labelRadius;
    private double _arcRadius;
    private double _arcThickness;
    private double _pivotRadius;
    private double _tickInner;
    private double _tickOuter;

    /// <summary>
    /// Annulus the needle is allowed to occupy: outside the pivot cap, inside the
    /// tick run, so the tip can mark the arc but can never cross the scale.
    /// </summary>
    private Geometry _needleClip = new StreamGeometry();

    private Geometry _ticksMinor = new StreamGeometry();
    private Geometry _ticksMajor = new StreamGeometry();
    private Geometry _redlineTicksMinor = new StreamGeometry();
    private Geometry _redlineTicksMajor = new StreamGeometry();
    private Geometry _trackArc = new StreamGeometry();
    private Geometry _needle = new StreamGeometry();

    private double _displayRatio;
    private double _targetRatio;

    /// <summary>
    /// The progress arc for the currently displayed ratio. Rebuilt only when the
    /// displayed value moves, so an unchanged frame allocates no geometry.
    /// </summary>
    private Geometry? _arcGeometry;
    private double _arcRatio = double.NaN;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty || change.Property == MaximumProperty)
        {
            // The needle is deliberately left where it is: on the first frames it
            // is still at the bottom of the scale, so the one-shot opening sweep
            // carries it up to this reading.
            _targetRatio = ComputeRatio();
            RequestFrames();
            InvalidateVisual();
        }
        else if (change.Property == AccentProperty)
        {
            // The accent brush is the active power-mode colour. Retargeting it
            // starts an eased transition, so the arc and needle change colour
            // with a short blend instead of a flash.
            SetAccentTarget(ResolveAccentColor());
            RequestFrames();
            InvalidateVisual();
        }
        else if (change.Property == ValueTextProperty || change.Property == UnitProperty)
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

        var reported = Value >= 0 && Maximum >= 0;
        var lineBrush = DashboardVisuals.Brush(this, "BorderBrush", Brushes.Gray);
        var strongBrush = DashboardVisuals.Brush(this, "BorderStrongBrush", Brushes.Gray);
        var trackBrush = DashboardVisuals.Brush(this, "SurfaceRaisedBrush", Brushes.Gray);
        var mutedBrush = DashboardVisuals.Brush(this, "TextMutedBrush", Brushes.Gray);
        var dangerBrush = DashboardVisuals.Brush(this, "DangerBrush", Brushes.Red);
        var cardBrush = DashboardVisuals.Brush(this, "CardBackgroundBrush", Brushes.Transparent);
        var valueBrush = reported
            ? DashboardVisuals.Brush(this, "TextPrimaryBrush", Brushes.White)
            : mutedBrush;

        // The accent is the active power-mode colour, handed in by the page from
        // the design system. It is eased between values, so a mode change is a
        // short transition rather than a flash; an unreported channel is drawn
        // muted and never given a status colour.
        SetAccentTarget(ResolveAccentColor());
        var accentBrush = reported
            ? (IBrush)new SolidColorBrush(CurrentAccent)
            : mutedBrush;
        var accentColor = reported
            ? CurrentAccent
            : mutedBrush is ISolidColorBrush mutedSolid ? mutedSolid.Color : Colors.Gray;

        var typeface = DashboardVisuals.Typeface(this);
        var labelTypeface = DashboardVisuals.Weighted(typeface, FontWeight.Medium);
        var valueTypeface = DashboardVisuals.Weighted(typeface, FontWeight.SemiBold);

        // Scale numerals sit outside the tick band, so the needle never crosses
        // them and the tick run stays readable at a glance.
        var labelSize = Math.Max(7.0, _outerRadius * 0.115);
        foreach (var fraction in LabelFractions)
        {
            var label = DashboardVisuals.FittedText(
                (fraction * 100.0).ToString("F0", CultureInfo.CurrentCulture),
                labelTypeface, labelSize, 6.0, _outerRadius * 0.34, mutedBrush);
            DashboardVisuals.DrawCentred(
                context,
                label,
                DashboardVisuals.Polar(center, _labelRadius, StartAngle + SweepAngle * fraction));
        }

        // Precise tick marks: fine minors, weighted majors. The redline is the
        // same geometry recoloured, so the red zone lands on exactly the same
        // angles as the scale underneath it.
        using (context.PushOpacity(0.55))
        {
            context.DrawGeometry(null, new Pen(lineBrush, 1), _ticksMinor);
        }

        context.DrawGeometry(null, new Pen(strongBrush, 1.5), _ticksMajor);
        context.DrawGeometry(null, new Pen(dangerBrush, 1), _redlineTicksMinor);
        context.DrawGeometry(null, new Pen(dangerBrush, 1.5), _redlineTicksMajor);

        // Graduated arc: a restrained neutral track carrying the reported
        // reading, drawn with a consistent stroke and rounded ends.
        context.DrawGeometry(null, new Pen(trackBrush, _arcThickness, null, PenLineCap.Round), _trackArc);

        var ratio = Math.Clamp(_displayRatio, 0.0, 1.0);
        if (reported && ratio > 0.002)
        {
            // The progress geometry is only rebuilt when the displayed value
            // actually moves, so a frame with an unchanged reading allocates
            // nothing beyond the cached pen.
            if (Math.Abs(ratio - _arcRatio) > 0.0005 || _arcGeometry is null)
            {
                _arcGeometry = DashboardVisuals.Arc(center, _arcRadius, StartAngle, SweepAngle * ratio);
                _arcRatio = ratio;
            }

            context.DrawGeometry(null, new Pen(accentBrush, _arcThickness, null, PenLineCap.Round), _arcGeometry);
        }

        // Needle: a thin pointer from the cap to the arc, confined to the
        // annulus and rotated about the exact dial centre.
        if (reported)
        {
            var angle = StartAngle + SweepAngle * ratio;
            using (context.PushGeometryClip(_needleClip))
            using (context.PushTransform(
                Matrix.CreateRotation(angle * Math.PI / 180.0) * Matrix.CreateTranslation(center.X, center.Y)))
            {
                context.DrawGeometry(accentBrush, null, _needle);
            }
        }

        // Mechanical pivot: a small cap with the mode-coloured centre, drawn over
        // the needle tail so the pointer reads as rotating on it.
        context.DrawEllipse(cardBrush, new Pen(strongBrush, 1.2), center, _pivotRadius, _pivotRadius);
        context.DrawEllipse(new SolidColorBrush(accentColor), null, center, _pivotRadius * 0.34, _pivotRadius * 0.34);

        // Digital readout in the open lower half: the reported reading, or the
        // eased count-up while the opening sweep is still running, drawn at the
        // size the finished reading resolves to so the digits do not resize as
        // the sweep lands.
        var reading = ValueText ?? "-";
        var value = DashboardVisuals.FittedText(
            IsStartupSweeping ? DashboardVisuals.CountUp(reading, StartupSweepPhase) : reading,
            valueTypeface,
            _outerRadius * 0.30,
            10.0,
            _outerRadius * 1.30,
            valueBrush);
        DashboardVisuals.DrawCentred(context, value, center, _outerRadius * 0.34);

        // The unit only means something next to a reading the machine reported.
        if (reported && !string.IsNullOrEmpty(Unit))
        {
            var unit = DashboardVisuals.FittedText(
                Unit!, labelTypeface, _outerRadius * 0.105, 7.0, _outerRadius * 0.5, mutedBrush);
            DashboardVisuals.DrawCentred(context, unit, center, _outerRadius * 0.62);
        }
    }

    protected override bool Advance(double seconds)
    {
        var accentMoving = AdvanceAccent(seconds);

        if (IsStartupSweeping)
        {
            // The opening sweep rides the reported target, so a reading that
            // lands while it is still running is settled on rather than overshot:
            // the needle collapses to the bottom of the scale, travels out to full
            // scale, then settles on whatever the machine is reporting. It runs
            // before the HasReading guard so the clock starts with the control
            // rather than with the first sample, keeping CPU and GPU in step.
            var sweeping = AdvanceStartupSweep(seconds);
            _displayRatio = StartupSweepPosition(_targetRatio);
            return sweeping || accentMoving;
        }

        if (!HasReading)
        {
            // This channel has no reported reading: hold the needle at the bottom
            // of the scale until the machine reports one, so no sweep and no
            // movement are invented for a channel that carries no value.
            return accentMoving;
        }

        var delta = _targetRatio - _displayRatio;
        if (Math.Abs(delta) < 0.0015)
        {
            _displayRatio = _targetRatio;
            return accentMoving;
        }

        // Exponential approach driven by the real elapsed time, so the needle
        // travels at the same speed whatever frame rate the UI manages.
        _displayRatio += delta * (1.0 - Math.Exp(-seconds / EaseSeconds));
        return true;
    }

    /// <summary>Rebuilds static geometry only when the dial is resized.</summary>
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
        _labelRadius = _outerRadius * 0.905;
        _arcRadius = _outerRadius * 0.665;
        _arcThickness = Math.Clamp(_outerRadius * 0.055, 3.0, 7.0);
        _pivotRadius = Math.Max(3.0, _outerRadius * 0.075);

        _tickInner = _outerRadius * 0.735;
        _tickOuter = _outerRadius * 0.815;

        var ticks = DashboardVisuals.Ticks(
            center, _tickInner, _tickOuter, Divisions, MajorTickEvery, StartAngle, SweepAngle);
        _ticksMinor = ticks.Minor;
        _ticksMajor = ticks.Major;

        // The redline is the same scale restricted to its top fifth, so its
        // marks land on exactly the same angles as the grey run.
        var redlineFirst = (int)Math.Round(Divisions * RedlineStartFraction);
        var redlineTicks = DashboardVisuals.Ticks(
            center, _tickInner, _tickOuter, Divisions, MajorTickEvery, StartAngle, SweepAngle,
            redlineFirst, Divisions);
        _redlineTicksMinor = redlineTicks.Minor;
        _redlineTicksMajor = redlineTicks.Major;

        _trackArc = DashboardVisuals.Arc(center, _arcRadius, StartAngle, SweepAngle);

        // Thin, tapered pointer. It reaches the arc so the tip marks the value;
        // the tail sits under the pivot cap.
        var needleLength = _arcRadius - _arcThickness * 0.25;
        var needleHalfWidth = Math.Max(1.1, _outerRadius * 0.022);
        var needleTail = _outerRadius * 0.10;
        _needle = DashboardVisuals.Needle(needleLength, needleHalfWidth, needleTail);

        var pivotClip = new EllipseGeometry(new Rect(
            center.X - _pivotRadius * 0.55, center.Y - _pivotRadius * 0.55,
            _pivotRadius * 1.1, _pivotRadius * 1.1));
        var tickClip = new EllipseGeometry(new Rect(
            center.X - _tickInner, center.Y - _tickInner,
            _tickInner * 2.0, _tickInner * 2.0));
        _needleClip = new CombinedGeometry(GeometryCombineMode.Exclude, tickClip, pivotClip);

        _arcGeometry = null;
        _arcRatio = double.NaN;
    }

    /// <summary>
    /// The accent to draw, resolved from the brush the page binds (the active
    /// power-mode colour). A brush that is absent or fully transparent falls back
    /// to the application primary, so the gauge is never invisible while the
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

    /// <summary>True when the machine reported both a reading and a maximum.</summary>
    private bool HasReading => Value >= 0 && Maximum >= 0;

    /// <summary>
    /// The needle's current position, so a replayed self-test collapses from where
    /// the needle actually is rather than snapping to the bottom of the scale.
    /// </summary>
    protected override double DisplayedSweepPosition => Math.Clamp(_displayRatio, 0.0, 1.0);

    private double ComputeRatio()
        => Value >= 0 && Maximum > 0 ? Math.Clamp((double)Value / Maximum, 0.0, 1.0) : 0.0;
}
