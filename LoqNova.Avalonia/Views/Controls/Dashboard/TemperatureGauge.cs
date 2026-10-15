using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Dashboard;

/// <summary>
/// The temperature instrument for one device: a graduated bar with its own
/// numeric readout, so temperature is never reduced to a text row beside the
/// utilisation dial.
/// <para>
/// The bar length is the reported temperature as a fraction of the maximum the
/// machine reports for that channel, exactly like the utilisation dial's arc.
/// The fill colour is a display severity band derived from that same ratio
/// (comfortable / warm / hot); it never replaces the numeric reading, which
/// always shows what the machine reported, or a dash when it reported nothing.
/// </para>
/// <para>
/// An unreported channel draws an empty track and no severity at all, and a
/// channel with no reported maximum is drawn in the temperature channel colour
/// rather than being given a fabricated severity.
/// </para>
/// </summary>
public class TemperatureGauge : AnimatedGauge
{
    private const int Divisions = 10;
    private const int MajorTickEvery = 5;

    /// <summary>Time constant for the fill's approach to a new reading.</summary>
    private const double EaseSeconds = 0.18;

    /// <summary>Reported temperature; -1 means the machine did not report the channel.</summary>
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<TemperatureGauge, int>(nameof(Value), -1);

    /// <summary>Highest temperature the machine reports for the channel.</summary>
    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<TemperatureGauge, int>(nameof(Maximum), -1);

    /// <summary>Formatted temperature drawn beside the bar; a dash when unreported.</summary>
    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<TemperatureGauge, string?>(nameof(ValueText), "-");

    /// <summary>Selected temperature unit, e.g. "°C".</summary>
    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<TemperatureGauge, string?>(nameof(Unit));

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

    private double _displayRatio;
    private double _targetRatio;
    private bool _initialised;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty || change.Property == MaximumProperty)
        {
            _targetRatio = ComputeRatio();
            if (!_initialised)
            {
                _initialised = true;
                _displayRatio = _targetRatio;
            }
            else
            {
                RequestFrames();
            }

            InvalidateVisual();
        }
        else if (change.Property == ValueTextProperty || change.Property == UnitProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (double.IsNaN(width) || double.IsNaN(height) || width <= 40 || height <= 18)
        {
            return;
        }

        var reported = Value >= 0 && Maximum >= 0;
        var typeface = DashboardVisuals.Typeface(this);
        var mutedBrush = DashboardVisuals.Brush(this, "TextMutedBrush", Brushes.Gray);
        var trackBrush = DashboardVisuals.Brush(this, "SurfaceRaisedBrush", Brushes.Gray);
        var lineBrush = DashboardVisuals.Brush(this, "BorderBrush", Brushes.Gray);
        var fillBrush = Severity(reported, _displayRatio, mutedBrush);

        // Readout on the right; the graduated bar takes whatever is left.
        var value = DashboardVisuals.FittedText(
            ValueText ?? "-", typeface, height * 0.60, 9.0, width * 0.4,
            reported ? fillBrush : mutedBrush);
        var unit = DashboardVisuals.FittedText(
            Unit ?? string.Empty, typeface, height * 0.30, 7.0, width * 0.2, mutedBrush);

        const double UnitGap = 4.0;
        const double BarGap = 14.0;
        var textWidth = value.Width + (unit.Width > 0 ? UnitGap + unit.Width : 0);
        var barWidth = width - textWidth - BarGap;
        var textLeft = width - textWidth;
        var textCenter = height * 0.5;

        if (barWidth > 8)
        {
            var barHeight = Math.Clamp(height * 0.17, 5.0, 8.0);
            var barTop = (height - barHeight) / 2.0 + height * 0.10;
            var majorLength = Math.Min(barHeight + 6.0, barTop - 2.0);
            var minorLength = Math.Min(barHeight + 2.0, barTop - 2.0);

            // Scale: subdivisions above the track, weighted every five.
            var ticks = BuildTicks(barWidth, barTop, majorLength, minorLength);
            using (context.PushOpacity(0.6))
            {
                context.DrawGeometry(null, new Pen(lineBrush, 1), ticks);
            }

            // Track, then the reported portion.
            context.DrawRectangle(trackBrush, null,
                new Rect(0, barTop, barWidth, barHeight), barHeight / 2.0, barHeight / 2.0);

            var fillWidth = barWidth * Math.Clamp(_displayRatio, 0.0, 1.0);
            if (reported && fillWidth > 0.05)
            {
                context.DrawRectangle(fillBrush, null,
                    new Rect(0, barTop, fillWidth, barHeight), barHeight / 2.0, barHeight / 2.0);

                // A leading mark so the current level stays legible at a glance.
                context.DrawRectangle(
                    DashboardVisuals.Brush(this, "TextPrimaryBrush", Brushes.White), null,
                    new Rect(Math.Max(0, fillWidth - 1.5), barTop - barHeight * 0.7, 1.5, barHeight * 2.4));
            }
        }

        context.DrawText(value, new Point(textLeft, textCenter - value.Height / 2.0));
        if (unit.Width > 0)
        {
            context.DrawText(unit, new Point(textLeft + value.Width + UnitGap, textCenter - unit.Height / 2.0));
        }
    }

    protected override bool Advance(double seconds)
    {
        var delta = _targetRatio - _displayRatio;
        if (Math.Abs(delta) < 0.0015)
        {
            _displayRatio = _targetRatio;
            return false;
        }

        _displayRatio += delta * (1.0 - Math.Exp(-seconds / EaseSeconds));
        return true;
    }

    /// <summary>
    /// Display severity for the fill. It is derived only from the reported
    /// value and the maximum the machine reports for the channel, and is not
    /// applied at all when the channel is unreported.
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

    private static Geometry BuildTicks(double barWidth, double barTop, double majorLength, double minorLength)
    {
        var geometry = new StreamGeometry();
        var ticks = geometry.Open();
        try
        {
            for (var i = 0; i <= Divisions; i++)
            {
                var isMajor = i % MajorTickEvery == 0;
                var x = barWidth * i / Divisions;
                ticks.BeginFigure(new Point(x, barTop - (isMajor ? majorLength : minorLength)), false);
                ticks.LineTo(new Point(x, barTop));
                ticks.EndFigure(false);
            }
        }
        finally
        {
            ticks.Dispose();
        }

        return geometry;
    }

    private double ComputeRatio()
        => Value >= 0 && Maximum > 0 ? Math.Clamp((double)Value / Maximum, 0.0, 1.0) : 0.0;
}
