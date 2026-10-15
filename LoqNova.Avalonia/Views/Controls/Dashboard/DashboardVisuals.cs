using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// Drawing helpers shared by the Dashboard's custom instruments
/// (<see cref="AnalogFanControl"/> and <see cref="RingGauge"/>).
/// <para>
/// Both instruments paint themselves in a <c>Render</c> override rather than
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
    private static readonly Point Origin = default;

    /// <summary>Resolves a design-token brush, falling back when the key is absent.</summary>
    public static IBrush Brush(IResourceHost host, string key, IBrush fallback)
        => host.TryFindResource(key, out var value) && value is IBrush brush ? brush : fallback;

    /// <summary>The application typeface, so drawn text matches the rest of the UI.</summary>
    public static Typeface Typeface(IResourceHost host)
        => host.TryFindResource("DefaultFontFamily", out var value) && value is FontFamily family
            ? new Typeface(family)
            : new Typeface(FontFamily.Default);

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
    /// An open arc between two angles, drawn for stroking. A full turn is
    /// emitted as a closing arc; an empty sweep produces a degenerate figure so
    /// callers can draw unconditionally.
    /// </summary>
    public static Geometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        var geometry = new StreamGeometry();
        var context = geometry.Open();
        try
        {
            if (sweepDegrees >= 359.5)
            {
                var top = Polar(center, radius, 0);
                context.BeginFigure(top, false);
                context.ArcTo(top, new Size(radius, radius), 0, true, SweepDirection.Clockwise);
            }
            else if (sweepDegrees <= 0.01)
            {
                var start = Polar(center, radius, startDegrees);
                context.BeginFigure(start, false);
                context.LineTo(start);
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
            }

            context.EndFigure(false);
        }
        finally
        {
            context.Dispose();
        }

        return geometry;
    }

    /// <summary>
    /// A run of radial tick marks between two radii, used for the analog scales.
    /// Every <paramref name="majorEvery"/>-th tick is returned separately so the
    /// caller can weight the major and minor marks differently.
    /// </summary>
    public static (Geometry Minor, Geometry Major) Ticks(
        Point center,
        double innerRadius,
        double outerRadius,
        int count,
        int majorEvery,
        double startDegrees,
        double sweepDegrees)
    {
        var minor = new StreamGeometry();
        var major = new StreamGeometry();
        var minorContext = minor.Open();
        var majorContext = major.Open();
        try
        {
            for (var i = 0; i <= count; i++)
            {
                var isMajor = i % majorEvery == 0;
                var angle = startDegrees + sweepDegrees * i / count;
                var from = Polar(center, Inner(isMajor, innerRadius, outerRadius), angle);
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

    private static double Inner(bool isMajor, double innerRadius, double outerRadius)
        => isMajor ? innerRadius * 0.55 : innerRadius;

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
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, Math.Max(minimumSize, preferredSize), brush);

        if (formatted.Width > maxWidth && formatted.Width > 0)
        {
            var size = Math.Max(minimumSize, formatted.Width <= 0
                ? preferredSize
                : preferredSize * (maxWidth / formatted.Width));
            formatted = new FormattedText(text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, size, brush);
        }

        return formatted;
    }

    /// <summary>Draws text centred on a point, with an optional vertical nudge.</summary>
    public static void DrawCentred(DrawingContext context, FormattedText text, Point center, double dy = 0)
        => context.DrawText(text, new Point(center.X - text.Width / 2.0, center.Y - text.Height / 2.0 + dy));
}
