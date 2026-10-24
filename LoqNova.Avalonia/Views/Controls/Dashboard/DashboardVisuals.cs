using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// Drawing helpers shared by the Dashboard's custom instruments
/// (<see cref="ClusterGauge"/>, <see cref="TemperatureGauge"/> and
/// <see cref="AnalogFanControl"/>).
/// <para>
/// Every instrument paints itself in a <c>Render</c> override rather than
/// building a visual tree. That keeps each gauge a single element, so the
/// animation loop only ever calls <c>InvalidateVisual</c> on one control: no
/// layout pass, no element churn.
/// </para>
/// <para>
/// Colours are always resolved from the design tokens at draw time, so the
/// instruments follow the active theme and the user's accent colour without
/// holding a copy of the palette.
/// </para>
/// </summary>
internal static class DashboardVisuals
{
    /// <summary>Resolves a design-token brush, falling back when the key is absent.</summary>
    public static IBrush Brush(IResourceHost host, string key, IBrush fallback)
        => host.TryFindResource(key, out var value) && value is IBrush brush ? brush : fallback;

    /// <summary>The application typeface, so drawn text matches the rest of the UI.</summary>
    public static Typeface Typeface(IResourceHost host)
        => host.TryFindResource("DefaultFontFamily", out var value) && value is FontFamily family
            ? new Typeface(family)
            : new Typeface(FontFamily.Default);

    /// <summary>
    /// The same typeface at a different weight, so an instrument can give its
    /// numerals emphasis without introducing a second font family.
    /// </summary>
    public static Typeface Weighted(Typeface typeface, FontWeight weight)
        => new(typeface.FontFamily, typeface.Style, weight, typeface.Stretch);

    /// <summary>
    /// Point on a circle. Zero degrees is 12 o'clock and angles increase
    /// clockwise, which is the natural reading order for a gauge.
    /// </summary>
    public static Point Polar(Point center, double radius, double degrees)
    {
        var radians = (degrees - 90.0) * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    /// <summary>
    /// An arc between two angles, drawn for stroking. A full turn is emitted as
    /// two half turns; an empty sweep produces a degenerate figure so callers
    /// can draw unconditionally.
    /// </summary>
    public static Geometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        var geometry = new StreamGeometry();
        var context = geometry.Open();
        try
        {
            if (sweepDegrees >= 359.5)
            {
                // A single arc cannot express a full turn: its start and end
                // points coincide, which is a degenerate arc that resolves to
                // nothing - or to a line wrapped back on itself - as soon as the
                // sweep reaches a complete circle. A fan reaching its maximum
                // RPM is exactly that case, so the ring is built from two half
                // turns, which always resolves into a complete circle.
                var top = Polar(center, radius, 0);
                var bottom = Polar(center, radius, 180);
                context.BeginFigure(top, false);
                context.ArcTo(bottom, new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                context.ArcTo(top, new Size(radius, radius), 0, false, SweepDirection.Clockwise);

                // Closed, so the ring has one join at 12 o'clock instead of two
                // stroke caps landing on top of each other.
                context.EndFigure(true);
            }
            else if (sweepDegrees <= 0.01)
            {
                var start = Polar(center, radius, startDegrees);
                context.BeginFigure(start, false);
                context.LineTo(start);
                context.EndFigure(false);
            }
            else
            {
                context.BeginFigure(Polar(center, radius, startDegrees), false);
                context.ArcTo(
                    Polar(center, radius, startDegrees + sweepDegrees),
                    new Size(radius, radius),
                    0,
                    sweepDegrees > 180,
                    SweepDirection.Clockwise);
                context.EndFigure(false);
            }
        }
        finally
        {
            context.Dispose();
        }

        return geometry;
    }

    /// <summary>
    /// A calibrated run of radial tick marks between two radii. The sweep is cut
    /// into <paramref name="divisions"/> equal steps and a tick is drawn at each
    /// step boundary <em>including both ends</em> (so <c>divisions + 1</c> ticks
    /// span the full arc). Every <paramref name="majorEvery"/>-th tick is
    /// returned separately and drawn 2.6 times longer, which is what gives the
    /// scale its readable major/minor rhythm.
    /// <para>
    /// <paramref name="firstDivision"/> and <paramref name="lastDivision"/> can
    /// restrict the run to a span of the same scale. The redline is drawn that
    /// way rather than from its own start angle, so its marks land on exactly
    /// the same angles and the same major/minor rhythm as the full run.
    /// </para>
    /// </summary>
    public static (Geometry Minor, Geometry Major) Ticks(
        Point center,
        double innerRadius,
        double outerRadius,
        int divisions,
        int majorEvery,
        double startDegrees,
        double sweepDegrees,
        int firstDivision = 0,
        int lastDivision = -1)
    {
        var minor = new StreamGeometry();
        var major = new StreamGeometry();
        var minorContext = minor.Open();
        var majorContext = major.Open();
        var majorInnerRadius = innerRadius - (outerRadius - innerRadius) * 1.6;
        var last = lastDivision < 0 ? divisions : lastDivision;
        try
        {
            for (var i = firstDivision; i <= last; i++)
            {
                var isMajor = i % majorEvery == 0;
                var angle = startDegrees + sweepDegrees * i / divisions;
                var from = Polar(center, isMajor ? majorInnerRadius : innerRadius, angle);
                var to = Polar(center, outerRadius, angle);
                var context = isMajor ? majorContext : minorContext;
                context.BeginFigure(from, false);
                context.LineTo(to);
                context.EndFigure(false);
            }
        }
        finally
        {
            minorContext.Dispose();
            majorContext.Dispose();
        }

        return (minor, major);
    }

    /// <summary>
    /// A tapered indicator built around the origin and pointing at 12 o'clock,
    /// so a caller can aim it with a single rotation about the dial centre. The
    /// tail runs behind the origin so the hub can be painted over it.
    /// </summary>
    public static Geometry Needle(double length, double halfWidth, double tail)
    {
        var geometry = new StreamGeometry();
        var context = geometry.Open();
        try
        {
            context.BeginFigure(new Point(0, tail), true);
            context.LineTo(new Point(halfWidth, 0));
            context.LineTo(new Point(0, -length));
            context.LineTo(new Point(-halfWidth, 0));
            context.EndFigure(true);
        }
        finally
        {
            context.Dispose();
        }

        return geometry;
    }

    /// <summary>
    /// Text drawn with the application typeface at an exact size, for callers
    /// that already know the text fits - including the eased count-up, which uses
    /// it so its digits carry the very metrics of the reading they are counting
    /// up to.
    /// </summary>
    public static FormattedText Text(string text, Typeface typeface, double size, IBrush brush)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);

    /// <summary>
    /// The size <see cref="FittedText"/> settles on for a text: the preferred
    /// size, scaled down until the text fits the allowed width. Exposed so a
    /// caller can draw a different string at exactly the same size.
    /// </summary>
    public static double FittedSize(
        string text, Typeface typeface, double preferredSize, double minimumSize, double maxWidth)
    {
        var size = Math.Max(minimumSize, preferredSize);
        var measured = Text(text, typeface, size, Brushes.Transparent);
        if (measured.Width <= maxWidth || measured.Width <= 0)
        {
            return size;
        }

        return Math.Max(minimumSize, preferredSize * (maxWidth / measured.Width));
    }

    /// <summary>
    /// Text scaled down until it fits the allowed width, so a long reading never
    /// overflows the hub or dial it is drawn inside.
    /// </summary>
    public static FormattedText FittedText(
        string text,
        Typeface typeface,
        double preferredSize,
        double minimumSize,
        double maxWidth,
        IBrush brush)
        => Text(text, typeface, FittedSize(text, typeface, preferredSize, minimumSize, maxWidth), brush);

    /// <summary>Draws text centred on a point, with an optional vertical nudge.</summary>
    public static void DrawCentred(DrawingContext context, FormattedText text, Point center, double dy = 0)
        => context.DrawText(text, new Point(center.X - text.Width / 2.0, center.Y - text.Height / 2.0 + dy));

    /// <summary>
    /// The reading shown while an opening sweep is running: the reported number
    /// eased up from zero, so the digits travel with the needle instead of
    /// snapping straight to the answer.
    /// <para>
    /// It counts up from the formatted reading itself rather than from the raw
    /// sensor value, so it lands on exactly the text the machine's reading
    /// formats to - including any unit conversion already applied to it - and a
    /// reading that is not a plain number (an unreported channel's dash) is
    /// returned untouched rather than replaced with an invented figure.
    /// </para>
    /// </summary>
    public static string CountUp(string? valueText, double phase)
    {
        if (valueText is null
            || !int.TryParse(valueText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var reported))
        {
            return valueText ?? "-";
        }

        return Math.Round(reported * Math.Clamp(phase, 0.0, 1.0))
            .ToString("F0", CultureInfo.CurrentCulture);
    }
}
