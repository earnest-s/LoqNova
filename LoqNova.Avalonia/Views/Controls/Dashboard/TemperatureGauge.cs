using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// The temperature instrument for one device, drawn as a compact circular dial
/// from the same instrument family as <see cref="ClusterGauge"/>: the same
/// 210°-450° scale, the same tick rhythm, the same hub readout and the same
/// clipped pointer. Temperature is a first-class reading here, so it gets a real
/// analog gauge rather than a bar tucked beside the utilisation dial.
/// <para>
/// The two dials stay distinguishable rather than merely duplicated. The
/// utilisation dial's arc and needle carry the active power mode's accent; the
/// temperature dial's arc and pointer carry a display severity derived from the
/// reported reading against the maximum the machine reports for that channel
/// (comfortable / warm / hot), while the mode accent still drives the dial's
/// needle, hub and ticks. That keeps a thermal warning meaningful without letting
/// the warning colour take over the whole cluster, and keeps the mode colour
/// present on every instrument.
/// </para>
/// <para>
/// An unreported channel draws an empty scale and no severity at all, and a
/// channel with no reported maximum is drawn in the temperature channel colour
/// rather than being given a fabricated severity. The needle never leaves the
/// annulus between the hub and the tick run, so it can never cross the readout or
/// the scale whatever the paint order or the resolved brushes do.
/// </para>
/// </summary>
public class TemperatureGauge : AnimatedGauge
{
    private const double StartAngle = 210.0;
    private const double SweepAngle = 240.0;

    private const int Divisions = 12;
    private const int MajorTickEvery = 4;

    /// <summary>Time constant of the needle's and arc's approach to a new reading.</summary>
    private const double EaseSeconds = 0.18;

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
    /// pointer and the hub, so a mode change is visible on this dial too.
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
    private double _hubRadius;

    private Geometry _ticksMinor = new StreamGeometry();
    private Geometry _ticksMajor = new StreamGeometry();
    private Geometry _trackArc = new StreamGeometry();
    private Geometry _needle = new StreamGeometry();
    private Geometry _needleClip = new StreamGeometry();

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

        var reported = HasReading;
        var faceBrush = DashboardVisuals.Brush(this, "CardElevatedBrush", Brushes.Transparent);
        var lineBrush = DashboardVisuals.Brush(this, "BorderBrush", Brushes.Gray);
        var strongBrush = DashboardVisuals.Brush(this, "BorderStrongBrush", Brushes.Gray);
        var trackBrush = DashboardVisuals.Brush(this, "SurfaceRaisedBrush", Brushes.Gray);
        var mutedBrush = DashboardVisuals.Brush(this, "TextMutedBrush", Brushes.Gray);
        var valueBrush = reported
            ? DashboardVisuals.Brush(this, "TextPrimaryBrush", Brushes.White)
            : mutedBrush;

        // The mode accent drives the pointer and the hub; the severity band
        // drives the arc. An unreported channel is muted and gets neither.
        SetAccentTarget(ResolveAccentColor());
        var accentBrush = reported
            ? (IBrush)new SolidColorBrush(CurrentAccent)
            : mutedBrush;
        var severityBrush = Severity(reported, _displayRatio, mutedBrush);

        // Dial face, so the temperature dial reads as a sibling of the
        // utilisation dial rather than a stray ring.
        context.DrawEllipse(faceBrush, new Pen(lineBrush, 1), center, _outerRadius, _outerRadius);

        // Precise tick marks: fine minors, weighted majors.
        using (context.PushOpacity(0.5))
        {
            context.DrawGeometry(null, new Pen(lineBrush, 1), _ticksMinor);
        }

        context.DrawGeometry(null, new Pen(accentBrush, 1.4), _ticksMajor);

        // Scale track, then the reported portion in the severity colour.
        context.DrawGeometry(null, new Pen(trackBrush, _arcThickness, null, PenLineCap.Round), _trackArc);

        var ratio = Math.Clamp(_displayRatio, 0.0, 1.0);
        if (reported && ratio > 0.004)
        {
            var progress = DashboardVisuals.Arc(center, _arcRadius, StartAngle, SweepAngle * ratio);
            context.DrawGeometry(null, new Pen(severityBrush, _arcThickness, null, PenLineCap.Round), progress);
        }

        // The pointer only appears for a channel the machine reported: an
        // unreported channel must never be shown resting on a scale value.
        if (reported)
        {
            // Confined to the annulus between the hub cap and the tick run, and
            // rotated about the exact dial centre, so the pivot stays on the hub
            // and the tip can never reach the readout or the scale.
            using (context.PushGeometryClip(_needleClip))
            using (context.PushTransform(
                Matrix.CreateRotation((StartAngle + SweepAngle * ratio) * Math.PI / 180.0)
                * Matrix.CreateTranslation(center.X, center.Y)))
            {
                context.DrawGeometry(accentBrush, null, _needle);
            }
        }

        // Digital hub: the reported reading, or the eased count-up while the
        // opening sweep is still running, drawn at the size the finished reading
        // resolves to so the digits do not change size as the sweep lands.
        context.DrawEllipse(
            DashboardVisuals.Brush(this, "CardBackgroundBrush", Brushes.Transparent),
            new Pen(strongBrush, 1),
            center, _hubRadius, _hubRadius);

        var typeface = DashboardVisuals.Typeface(this);
        var reading = ValueText ?? "-";
        var value = DashboardVisuals.Text(
            IsStartupSweeping ? DashboardVisuals.CountUp(reading, StartupSweepPhase) : reading,
            typeface,
            DashboardVisuals.FittedSize(reading, typeface, _hubRadius * 0.92, 9.0, _hubRadius * 1.5),
            valueBrush);
        DashboardVisuals.DrawCentred(context, value, center, -_hubRadius * 0.22);

        // The unit only means something next to a reading the machine reported.
        if (reported && !string.IsNullOrEmpty(Unit))
        {
            var unit = DashboardVisuals.FittedText(
                Unit!, typeface, _hubRadius * 0.34, 7.0, _hubRadius * 1.4, mutedBrush);
            DashboardVisuals.DrawCentred(context, unit, center, _hubRadius * 0.46);
        }
    }

    protected override bool Advance(double seconds)
    {
        var accentMoving = AdvanceAccent(seconds);

        if (IsStartupSweeping)
        {
            // The same opening self-test as the utilisation dial: collapse to the
            // bottom of the scale, out to full scale, then settle on the reported
            // reading, with the readout counting up on the same phase so the two
            // never disagree. Run before the HasReading guard so the clock starts
            // with the control, keeping this dial in step with the CPU dial
            // rather than with its own first sample.
            var sweeping = AdvanceStartupSweep(seconds);
            _displayRatio = StartupSweepPosition(_targetRatio);
            return sweeping || accentMoving;
        }

        if (!HasReading)
        {
            // No reported reading: hold the needle at the bottom of the scale
            // until the machine reports this channel, so no movement is invented
            // for it.
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
    /// Display severity for the arc. It is derived only from the reported value
    /// and the maximum the machine reports for the channel, and is not applied at
    /// all when the channel is unreported.
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

        return ratio switch
        {
            < 0.60 => DashboardVisuals.Brush(this, "SuccessBrush", Brushes.Green),
            < 0.85 => DashboardVisuals.Brush(this, "WarningBrush", Brushes.Orange),
            _ => DashboardVisuals.Brush(this, "DangerBrush", Brushes.Red)
        };
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
        _arcRadius = _outerRadius * 0.62;
        _arcThickness = Math.Clamp(_outerRadius * 0.075, 2.0, 7.0);
        _hubRadius = _outerRadius * 0.34;

        var tickOuter = _outerRadius * 0.855;
        var tickInner = _outerRadius * 0.80;
        var ticks = DashboardVisuals.Ticks(
            center, tickInner, tickOuter, Divisions, MajorTickEvery, StartAngle, SweepAngle);
        _ticksMinor = ticks.Minor;
        _ticksMajor = ticks.Major;

        _trackArc = DashboardVisuals.Arc(center, _arcRadius, StartAngle, SweepAngle);

        var needleLength = _outerRadius * 0.79;
        var needleHalfWidth = Math.Max(1.4, _outerRadius * 0.040);
        var needleTail = _outerRadius * 0.14;
        _needle = DashboardVisuals.Needle(needleLength, needleHalfWidth, needleTail);

        var hubClip = new EllipseGeometry(new Rect(
            center.X - _hubRadius, center.Y - _hubRadius,
            _hubRadius * 2.0, _hubRadius * 2.0));
        var tickClip = new EllipseGeometry(new Rect(
            center.X - tickInner, center.Y - tickInner,
            tickInner * 2.0, tickInner * 2.0));
        _needleClip = new CombinedGeometry(GeometryCombineMode.Exclude, tickClip, hubClip);
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
