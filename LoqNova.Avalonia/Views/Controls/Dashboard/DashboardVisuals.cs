using System;
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
    private static readonly Point Origin = new(0, 0);

    /// <summary>Resolves a design-token brush by key, falling back to a caller-supplied brush.</summary>
    public static IBrush Brush(IResourceHost host, string key, IBrush fallback)
        => host.TryFindResource(key, out var value) && value is IBrush brush ? brush : fallback;

    /// <summary>Resolves a token brush by key, or <c>null</c> when the key is missing.</summary>
    public static IBrush? BrushOrNull(IResourceHost host, string key)
        => host.TryFindResource(key, out var value) && value is IBrush brush ? brush : null;

    /// <summary>
    /// The application typeface. The application declares it as a resource so
    /// every surface shares one family; the system default is only a fallback
    /// for the designer.
    /// </summary>
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
        using var context = geometry.Open();

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
        return geometry;
    }

    /// <summary>
    /// Formats a number of the given size to fit <paramref name="maxWidth"/>,
    /// shrinking the font until it does. Used for the readouts printed inside a
    /// gauge so a long reading never spills out of the dial.
    /// </summary>
    public static FormattedText FitText(
        string text,
        Typeface typeface,
        double preferredSize,
        double minimumSize,
        double maxWidth,
        IBrush brush)
    {
        var size = Math.Max(minimumSize, preferredSize);
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, size, brush);

        if (formatted.Width > maxWidth && formatted.Width > 0)
        {
            size = Math.Max(minimumSize, size * (maxWidth / formatted.Width));
            formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, size, brush);
        }

        return formatted;
    }

    /// <summary>Draws text centred on a point.</summary>
    public static void DrawCentred(DrawingContext context, FormattedText text, Point center, double dy = 0)
        => context.DrawText(text, new Point(center.X - text.Width / 2.0, center.Y - text.Height / 2.0 + dy));
}
