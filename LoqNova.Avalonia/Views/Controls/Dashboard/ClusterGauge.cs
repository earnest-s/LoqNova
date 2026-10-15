using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// The utilisation instrument of one device (CPU or GPU), drawn as an
/// automotive-style dial: a bevel of readable numerals, a calibrated tick band,
/// a graduated arc, an analog needle and a large digital readout in the hub.
/// <para>
/// The arc, the needle and the hub all describe the same reported channel. The
/// first frames before the machine has reported anything hold the needle at the
/// bottom of the scale; once a reading arrives the needle, the arc and the
/// numerals sweep up to it together as a one-shot opening animation, and from
/// then on the needle and the arc ease toward each new reading with a time-based
/// exponential approach. A channel the machine does not report is drawn muted
/// with no needle and no arc, never as a zero.
/// </para>
/// <para>
/// The top fifth of the scale is a redline. It is drawn on the scale - a band
/// flush under the tick run plus the ticks of that range redrawn over the
/// identical angles - so it stays aligned with the tick marks and leaves the
/// channel-coloured arc and needle untouched and still readable.
/// </para>
/// </summary>
public class ClusterGauge : AnimatedGauge
{
    /// <summary>
    /// The arc sweeps 240 degrees and leaves a gap at the bottom, which is where
    /// an instrument cluster leaves room for the standstill readouts.
    /// </summary>
    private const double StartAngle = 210.0;
    private const double SweepAngle = 240.0;

    /// <summary>20 steps of 5% with a major mark every five: 0/25/50/75/100.</summary>
    private const int Divisions = 20;
    private const int MajorTickEvery = 5;

    /// <summary>
    /// Where the redline starts, as a fraction of the scale. The top fifth - 80%
    /// utilisation and above - is where a machine is working hard enough for it
    /// to matter at a glance. It lands on tick sixteen of twenty, so the red zone
    /// sits exactly on the tick run instead of near it.
    /// </summary>
    private const double RedlineStartFraction = 0.80;

    /// <summary>Time constant for the needle's approach to a new reading.</summary>
    private const double EaseSeconds = 0.18;

    /// <summary>Major scale values, drawn on the bezel outside the tick run.</summary>
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
    private double _hubRadius;
    private double _redlineRadius;
    private double _redlineThickness;
    private Geometry _ticksMinor = new StreamGeometry();
    private Geometry _ticksMajor = new StreamGeometry();
    private Geometry _redlineTicksMinor = new StreamGeometry();
    private Geometry _redlineTicksMajor = new StreamGeometry();
    private Geometry _redlineArc = new StreamGeometry();
    private Geometry _trackArc = new StreamGeometry();
    private Geometry _needle = new StreamGeometry();

    private double _displayRatio;
    private double _targetRatio;

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
        else if (change.Property == ValueTextProperty
            || change.Property == UnitProperty
            || change.Property == AccentProperty)
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
        var faceBrush = DashboardVisuals.Brush(this, "CardElevatedBrush", Brushes.Transparent);
        var lineBrush = DashboardVisuals.Brush(this, "BorderBrush", Brushes.Gray);
        var strongBrush = DashboardVisuals.Brush(this, "BorderStrongBrush", Brushes.Gray);
        var trackBrush = DashboardVisuals.Brush(this, "SurfaceRaisedBrush", Brushes.Gray);
        var mutedBrush = DashboardVisuals.Brush(this, "TextMutedBrush", Brushes.Gray);
        var dangerBrush = DashboardVisuals.Brush(this, "DangerBrush", Brushes.Red);
        var valueBrush = reported
            ? DashboardVisuals.Brush(this, "TextPrimaryBrush", Brushes.White)
            : mutedBrush;
        var accentBrush = reported
            ? Accent ?? DashboardVisuals.Brush(this, "PrimaryBrush", Brushes.DodgerBlue)
            : mutedBrush;

        // Dial face: a slightly raised panel inside the card.
        context.DrawEllipse(faceBrush, new Pen(lineBrush, 1), center, _outerRadius, _outerRadius);

        var typeface = DashboardVisuals.Typeface(this);

        // Bezel numerals, outside the tick run so the needle never crosses them.
        var labelSize = Math.Max(6.0, _outerRadius * 0.105);
        foreach (var fraction in LabelFractions)
        {
            var label = DashboardVisuals.FittedText(
                (fraction * 100.0).ToString("F0", CultureInfo.CurrentCulture),
                typeface, labelSize, 5.0, _outerRadius * 0.32, mutedBrush);
            DashboardVisuals.DrawCentred(
                context,
                label,
                DashboardVisuals.Polar(center, _labelRadius, StartAngle + SweepAngle * fraction));
        }

        // Precise tick marks: fine minors, weighted majors.
        using (context.PushOpacity(0.5))
        {
            context.DrawGeometry(null, new Pen(lineBrush, 1), _ticksMinor);
        }

        context.DrawGeometry(null, new Pen(strongBrush, 1.4), _ticksMajor);

        // Redline: the top of the scale, drawn on the scale itself rather than
        // over the accent arc, so the needle and the reported-reading arc keep
        // the channel colour while the red zone stays unmistakable. The band sits
        // flush under the tick run and the red ticks are the same geometry as the
        // grey ones, so the zone stays aligned to the scale and its tick marks.
        context.DrawGeometry(null, new Pen(dangerBrush, _redlineThickness), _redlineArc);
        context.DrawGeometry(null, new Pen(dangerBrush, 1), _redlineTicksMinor);
        context.DrawGeometry(null, new Pen(dangerBrush, 1.4), _redlineTicksMajor);

        // Graduated arc: a muted scale track carrying the reported reading.
        context.DrawGeometry(null, new Pen(trackBrush, _arcThickness, null, PenLineCap.Round), _trackArc);

        var ratio = Math.Clamp(_displayRatio, 0.0, 1.0);
        if (reported && ratio > 0.004)
        {
            var progress = DashboardVisuals.Arc(center, _arcRadius, StartAngle, SweepAngle * ratio);
            context.DrawGeometry(null, new Pen(accentBrush, _arcThickness, null, PenLineCap.Round), progress);
        }

        // The needle is only drawn for a channel the machine reported: an
        // unreported channel must never be shown resting on a scale value.
        if (reported)
        {
            var angle = StartAngle + SweepAngle * ratio;
            using (context.PushTransform(
                Matrix.CreateRotation(angle * Math.PI / 180.0) * Matrix.CreateTranslation(center.X, center.Y)))
            {
                context.DrawGeometry(accentBrush, null, _needle);
            }
        }

        // Digital hub. The text is the reported reading, never the eased one.
        context.DrawEllipse(
            DashboardVisuals.Brush(this, "CardBackgroundBrush", Brushes.Transparent),
            new Pen(strongBrush, 1),
            center, _hubRadius, _hubRadius);

        var value = DashboardVisuals.FittedText(
            IsStartupSweeping
                ? DashboardVisuals.CountUp(ValueText, StartupSweepPhase)
                : ValueText ?? "-",
            typeface, _hubRadius * 0.95, 10.0, _hubRadius * 1.5, valueBrush);
        DashboardVisuals.DrawCentred(context, value, center, -_hubRadius * 0.24);

        // The unit only means something next to a reading the machine reported.
        if (reported && !string.IsNullOrEmpty(Unit))
        {
            var unit = DashboardVisuals.FittedText(
                Unit!, typeface, _hubRadius * 0.38, 7.0, _hubRadius * 1.35, mutedBrush);
            DashboardVisuals.DrawCentred(context, unit, center, _hubRadius * 0.5);
        }
    }

    protected override bool Advance(double seconds)
    {
        if (!HasReading)
        {
            // This channel has no reported reading: hold the needle at the bottom
            // of the scale until the machine reports one, so no sweep and no
            // movement are invented for a channel that carries no value.
            return false;
        }

        if (IsStartupSweeping)
        {
            // The opening sweep rides the reported target, so a reading that
            // lands while it is still running is absorbed rather than overshot.
            if (AdvanceStartupSweep(seconds, skip: _targetRatio <= 0.0))
            {
                _displayRatio = _targetRatio * StartupSweepPhase;
                return true;
            }

            _displayRatio = _targetRatio;
            return false;
        }

        var delta = _targetRatio - _displayRatio;
        if (Math.Abs(delta) < 0.0015)
        {
            _displayRatio = _targetRatio;
            return false;
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
        _labelRadius = _outerRadius * 0.925;
        _arcRadius = _outerRadius * 0.62;
        _arcThickness = Math.Clamp(_outerRadius * 0.075, 3.0, 9.0);
        _hubRadius = _outerRadius * 0.36;

        var tickOuter = _outerRadius * 0.855;
        var tickInner = _outerRadius * 0.80;
        var needleLength = _outerRadius * 0.82;
        var needleHalfWidth = Math.Max(1.8, _outerRadius * 0.05);

        var ticks = DashboardVisuals.Ticks(
            center, tickInner, tickOuter, Divisions, MajorTickEvery, StartAngle, SweepAngle);
        _ticksMinor = ticks.Minor;
        _ticksMajor = ticks.Major;

        // Redline geometry, built from the same scale: a band flush under the
        // tick run, plus the ticks of the top fifth of the scale redrawn over
        // the identical angles.
        var redlineFirst = (int)Math.Round(Divisions * RedlineStartFraction);
        _redlineThickness = Math.Clamp(_outerRadius * 0.045, 2.5, 6.0);
        _redlineRadius = tickInner - _redlineThickness / 2.0;
        _redlineArc = DashboardVisuals.Arc(
            center,
            _redlineRadius,
            StartAngle + SweepAngle * RedlineStartFraction,
            SweepAngle * (1.0 - RedlineStartFraction));
        var redlineTicks = DashboardVisuals.Ticks(
            center, tickInner, tickOuter, Divisions, MajorTickEvery, StartAngle, SweepAngle,
            redlineFirst, Divisions);
        _redlineTicksMinor = redlineTicks.Minor;
        _redlineTicksMajor = redlineTicks.Major;

        _trackArc = DashboardVisuals.Arc(center, _arcRadius, StartAngle, SweepAngle);
        _needle = DashboardVisuals.Needle(needleLength, needleHalfWidth, _outerRadius * 0.14);
    }

    /// <summary>True when the machine reported both a reading and a maximum.</summary>
    private bool HasReading => Value >= 0 && Maximum >= 0;

    private double ComputeRatio()
        => Value >= 0 && Maximum > 0 ? Math.Clamp((double)Value / Maximum, 0.0, 1.0) : 0.0;
}
