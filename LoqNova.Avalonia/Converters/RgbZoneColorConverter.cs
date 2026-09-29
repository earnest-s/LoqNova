using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Converters;

/// <summary>
/// Two-way bridge between Avalonia's <see cref="Color"/> (used by the built-in colour
/// picker) and the zone colour struct the RGB state carries. The struct stays the
/// value the backend receives; this only lets the native control edit it.
/// </summary>
public sealed class RgbZoneColorConverter : IValueConverter
{
    public static RgbZoneColorConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RgbZoneColor zone)
            return Color.FromRgb(zone.R, zone.G, zone.B);

        return value is Color colour ? colour : Colors.Black;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // ColorView.Color is nullable. A null means "no colour chosen yet", not black,
        // so it must not be written back as black and overwrite the real zone colour.
        if (value is Color colour)
            return new RgbZoneColor(colour.R, colour.G, colour.B);

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
