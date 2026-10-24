using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// The temperature instrument for one device, drawn as a compact semicircular
/// dial from the same family as <see cref="ClusterGauge"/>: the same 9-to-3
/// scale, the same tick rhythm, the same pivot and clipped pointer, and the same
/// digital readout below the pivot.
/// <para>
/// The two dials stay distinguishable rather than merely duplicated. The
/// utilisation dial's arc and needle carry the active power mode's accent; this
/// dial's thin inner band marks the thermal zones and its active arc carries a
/// severity derived from the reported reading, so a thermal warning is
/// meaningful and stays independent of the performance-mode colour. The needle
/// and pivot still carry the mode accent, so a mode change is visible here too.
/// </para>
/// <para>
/// An unreported channel draws an empty scale and no severity at all. The needle
/// never leaves the annulus between the pivot and the tick run, so it can never
/// cross the readout or the scale whatever the paint order or the resolved
/// brushes do.
/// </para>
/// </summary>
public class TemperatureGauge : AnimatedGauge
{
    private const double StartAngle = 270.0;
    private const double SweepAngle = 180.0;

    /// <summary>Twelve steps with a major every three: quarter-scale marks.</summary>
    private const int Divisions = 12;
    private const int MajorTickEvery = 3;

    /// <summary>Time constant of the needle's and arc's approach to a new reading.</summary>
    private const double EaseSeconds = 0.18;

    /// <summary>
    /// Absolute thermal thresholds, in the unit the machine reports. The band
    /// between them is the warm zone; above the second is the hot zone. They are
    /// converted to scale fractions against the reported maximum, so the band
    /// always lands on the calibrated tick run.
    /// </summary>
    private const double WarmCelsius = 70.0;
    private const double HotCelsius = 85.0;
    private const double WarmFahrenheit = 158.0;
    private const double HotFahrenheit = 185.0;

    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<TemperatureGauge, int>(nameof(Value), -1);

    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<TemperatureGauge, int>(nameof(Maximum), -1);

    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<TemperatureGauge, string?>(nameof(ValueText));

    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<TemperatureGauge, string?>(nameof(Unit));

    /// <summary>
    /// The active power mode's colour, handed in by the page. It drives the
    /// pointer and the pivot, so a mode change is visible on this dial too.
    /// </summary>
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<TemperatureGauge, IBrush?>(nameof(Accent));

    /// <summary>Temperature the machine reported, in the unit it reports, or -1.</summary>
    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The maximum the machine reports for this channel, or -1 when none.</summary>
    public int Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>The formatted reading, exactly as the view model produced it.</summary>
    public string? ValueText
    {
        get => GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    /// <summary>Unit suffix, e.g. °C or °F.</summary>
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

    private double _displayRatio;
    private double _targetRatio;

    private double _cachedWidth = double.NaN;
    private double _cachedHeight = double.NaN;
    private double _outerRadius;
    private double _arcRadius;
    private double _arcThickness;
    private double _pivotRadius;
    private double _tickInner;
    private double _tickOuter;

    private Geometry _ticksMinor = new StreamGeometry();
    private Geometry _ticksMajor = new StreamGeometry();
    private Geometry _trackArc = new StreamGeometry();
    private Geometry _needle = new StreamGeometry();
    private Geometry _needleClip = new StreamGeometry();

    /// <summary>
    /// Thermal zone bands, rebuilt only when the reported scale or its unit
    /// changes. They are drawn inside the reading arc rather than over it.
    /// </summary>
    private Geometry _warmZone = new StreamGeometry();
    private Geometry _hotZone = new StreamGeometry();
    private (int Maximum, string Unit) _zoneKey = (int.MinValue, string.Empty);

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
            // is still parked at the bottom of the scale, so the one-shot opening
            // sweep carries it to this reading.
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
        if (double.IsNaN(dial) || dial <= 32)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
        EnsureGeometry(dial, center);
        EnsureZones(center);

        var reported = HasReading;
        var lineBrush = DashboardVisuals.Brush(this, "BorderBrush", Brushes.Gray);
        var strongBrush = DashboardVisuals.Brush(this, "BorderStrongBrush", Brushes.Gray);
        var trackBrush = DashboardVisuals.Brush(this, "SurfaceRaisedBrush", Brushes.Gray);
        var mutedBrush = DashboardVisuals.Brush(this, "TextMutedBrush", Brushes.Gray);
        var warningBrush = DashboardVisuals.Brush(this, "WarningBrush", Brushes.Orange);
        var dangerBrush = DashboardVisuals.Brush(this, "DangerBrush", Brushes.Red);
        var cardBrush = DashboardVisuals.Brush(this, "CardBackgroundBrush", Brushes.Transparent);
        var valueBrush = reported
            ? DashboardVisuals.Brush(this, "TextPrimaryBrush", Brushes.White)
            : mutedBrush;

        // The mode accent drives the pointer and the pivot; the severity band
        // drives the reading arc. An unreported channel is muted and gets neither.
        SetAccentTarget(ResolveAccentColor());
        var accentBrush = reported
            ? (IBrush)new SolidColorBrush(CurrentAccent)
            : mutedBrush;
        var accentColor = reported
            ? CurrentAccent
            : mutedBrush is ISolidColorBrush mutedSolid ? mutedSolid.Color : Colors.Gray;
        var severityBrush = Severity(reported, Math.Clamp(_displayRatio, 0.0, 1.0), mutedBrush);

        // Precise tick marks: fine minors, weighted majors.
        using (context.PushOpacity(0.55))
        {
            context.DrawGeometry(null, new Pen(lineBrush, 1), _ticksMinor);
        }

        context.DrawGeometry(null, new Pen(strongBrush, 1.4), _ticksMajor);

        // Scale track, then the thermal zones inside it, then the reported
        // portion on the track in the severity colour.
        context.DrawGeometry(null, new Pen(trackBrush, _arcThickness, null, PenLineCap.Round), _trackArc);

        if (reported)
        {
            context.DrawGeometry(
                null,
                new Pen(warningBrush, Math.Max(1.5, _arcThickness * 0.38), null, PenLineCap.Flat),
                _warmZone);
            context.DrawGeometry(
                null,
                new Pen(dangerBrush, Math.Max(1.5, _arcThickness * 0.38), null, PenLineCap.Flat),
                _hotZone);
        }

        var ratio = Math.Clamp(_displayRatio, 0.0, 1.0);
        if (reported && ratio > 0.002)
        {
            if (Math.Abs(ratio - _arcRatio) > 0.0005 || _arcGeometry is null)
            {
                _arcGeometry = DashboardVisuals.Arc(center, _arcRadius, StartAngle, SweepAngle * ratio);
                _arcRatio = ratio;
            }

            context.DrawGeometry(null, new Pen(severityBrush, _arcThickness, null, PenLineCap.Round), _arcGeometry);
        }

        // The pointer only appears for a channel the machine reported, confined
        // to the annulus between the pivot cap and the tick run and rotated about
        // the exact dial centre.
        if (reported)
        {
            using (context.PushGeometryClip(_needleClip))
            using (context.PushTransform(
                Matrix.CreateRotation((StartAngle + SweepAngle * ratio) * Math.PI / 180.0)
                * Matrix.CreateTranslation(center.X, center.Y)))
            {
                context.DrawGeometry(accentBrush, null, _needle);
            }
        }

        // Mechanical pivot.
        context.DrawEllipse(cardBrush, new Pen(strongBrush, 1.2), center, _pivotRadius, _pivotRadius);
        context.DrawEllipse(new SolidColorBrush(accentColor), null, center, _pivotRadius * 0.36, _pivotRadius * 0.36);

        // Digital readout below the pivot, matching the utilisation dial.
        var typeface = DashboardVisuals.Typeface(this);
        var labelTypeface = DashboardVisuals.Weighted(typeface, FontWeight.Medium);
        var valueTypeface = DashboardVisuals.Weighted(typeface, FontWeight.SemiBold);
        var reading = ValueText ?? "-";
        var value = DashboardVisuals.FittedText(
            IsStartupSweeping ? DashboardVisuals.CountUp(reading, StartupSweepPhase) : reading,
            valueTypeface,
            _outerRadius * 0.34,
            9.0,
            _outerRadius * 1.30,
            valueBrush);
        DashboardVisuals.DrawCentred(context, value, center, _outerRadius * 0.34);

        // The unit only means something next to a reading the machine reported.
        if (reported && !string.IsNullOrEmpty(Unit))
        {
            var unit = DashboardVisuals.FittedText(
                Unit!, labelTypeface, _outerRadius * 0.13, 7.0, _outerRadius * 0.6, mutedBrush);
            DashboardVisuals.DrawCentred(context, unit, center, _outerRadius * 0.63);
        }
    }

    protected override bool Advance(double seconds)
    {
        var accentMoving = AdvanceAccent(seconds);

        if (IsStartupSweeping)
        {
            // The same opening self-test as the utilisation dial: collapse to the
            // bottom of the scale, out to full scale, then settle on the reported
            // reading, with the readout counting up on the same phase. Run before
            // the HasReading guard so the clock starts with the control, keeping
            // this dial in step with the utilisation dial rather than with its own
            // first sample.
            var sweeping = AdvanceStartupSweep(seconds);
            _displayRatio = StartupSweepPosition(_targetRatio);
            return sweeping || accentMoving;
        }

        if (!HasReading)
        {
            // No reported reading: hold the needle at the bottom of the scale
            // until the machine reports this channel, so no movement is invented.
            return accentMoving;
        }

        var delta = _targetRatio - _displayRatio;
        if (Math.Abs(delta) < 0.0015)
        {
            _displayRatio = _targetRatio;
            return accentMoving;
        }

        _displayRatio += delta * (1.0 - Math.Exp(-seconds / EaseSeconds));
        return true;
    }

    /// <summary>
    /// Display severity for the reading arc. Derived only from the reported value
    /// and the maximum the machine reports for the channel.
    /// </summary>
    private IBrush Severity(bool reported, double ratio, IBrush mutedBrush)
    {
        if (!reported)
        {
            return mutedBrush;
        }

        if (Maximum <= 0)
        {
            // No reported maximum means no honest band to place the reading in.
            return DashboardVisuals.Brush(this, "SensorTempBrush", Brushes.Orange);
        }

        var (warm, hot) = ZoneFractions();
        if (ratio >= hot)
        {
            return DashboardVisuals.Brush(this, "DangerBrush", Brushes.Red);
        }

        if (ratio >= warm)
        {
            return DashboardVisuals.Brush(this, "WarningBrush", Brushes.Orange);
        }

        // Below the thermal zones the dial stays in the neutral reading colour
        // rather than inventing a "safe" green indicator.
        return DashboardVisuals.Brush(this, "TextSecondaryBrush", Brushes.LightGray);
    }

    /// <summary>
    /// The thermal thresholds as fractions of the reported scale, in the unit the
    /// machine reports. A channel with no reported maximum has no honest scale,
    /// so the zones are not drawn.
    /// </summary>
    private (double Warm, double Hot) ZoneFractions()
    {
        if (Maximum <= 0)
        {
            return (1.0, 1.0);
        }

        var fahrenheit = Unit is not null && Unit.Contains('F');
        var warmTemperature = fahrenheit ? WarmFahrenheit : WarmCelsius;
        var hotTemperature = fahrenheit ? HotFahrenheit : HotCelsius;
        var warm = Math.Clamp(warmTemperature / Maximum, 0.05, 0.98);
        var hot = Math.Clamp(hotTemperature / Maximum, warm, 1.0);
        return (warm, hot);
    }

    /// <summary>Rebuilds the thermal zone bands when the scale or unit changes.</summary>
    private void EnsureZones(Point center)
    {
        var key = (Maximum, Unit ?? string.Empty);
        if (_zoneKey == key)
        {
            return;
        }

        _zoneKey = key;

        if (Maximum <= 0)
        {
            _warmZone = new StreamGeometry();
            _hotZone = new StreamGeometry();
            return;
        }

        var (warm, hot) = ZoneFractions();
        var radius = _arcRadius - _arcThickness * 0.75;
        _warmZone = DashboardVisuals.Arc(
            center, radius, StartAngle + SweepAngle * warm, SweepAngle * (hot - warm));
        _hotZone = DashboardVisuals.Arc(
            center, radius, StartAngle + SweepAngle * hot, SweepAngle * (1.0 - hot));
    }

    /// <summary>
    /// Rebuilds the static geometry when the dial size changes, using the same
    /// proportions as <see cref="ClusterGauge"/> so the two instruments read as
    /// one family at any size.
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
        _arcRadius = _outerRadius * 0.665;
        _arcThickness = Math.Clamp(_outerRadius * 0.088, 2.5, 6.0);
        _pivotRadius = Math.Max(2.5, _outerRadius * 0.085);

        _tickInner = _outerRadius * 0.735;
        _tickOuter = _outerRadius * 0.815;

        var ticks = DashboardVisuals.Ticks(
            center, _tickInner, _tickOuter, Divisions, MajorTickEvery, StartAngle, SweepAngle);
        _ticksMinor = ticks.Minor;
        _ticksMajor = ticks.Major;

        _trackArc = DashboardVisuals.Arc(center, _arcRadius, StartAngle, SweepAngle);

        var needleLength = _arcRadius - _arcThickness * 0.25;
        var needleHalfWidth = Math.Max(1.0, _outerRadius * 0.026);
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
        _zoneKey = (int.MinValue, string.Empty);
    }

    /// <summary>
    /// The accent to draw, resolved from the brush the page binds (the active
    /// power-mode colour). A brush that is absent or fully transparent falls back
    /// to the application primary, so the dial is never invisible while the
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
    /// The pointer's current position, so a replayed self-test collapses from
    /// where the pointer actually is rather than snapping to the bottom of the
    /// scale.
    /// </summary>
    protected override double DisplayedSweepPosition => Math.Clamp(_displayRatio, 0.0, 1.0);

    private double ComputeRatio()
        => Value >= 0 && Maximum > 0 ? Math.Clamp((double)Value / Maximum, 0.0, 1.0) : 0.0;
}
