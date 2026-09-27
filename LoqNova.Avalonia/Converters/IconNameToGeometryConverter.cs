using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LoqNova.Avalonia.Styles;

namespace LoqNova.Avalonia.Converters;

/// <summary>
/// Resolves an icon *name* (e.g. "Home", "Cpu64") to a drawable
/// <see cref="Geometry"/> from <see cref="IconGeometries"/>, so views never
/// hardcode path data.
/// </summary>
public class IconNameToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return IconGeometries.ByName.TryGetValue(Normalize(name), out var geometry) ? geometry : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();

    private static string Normalize(string name)
    {
        // Existing view models carry Segoe MDL2 style names such as "Cpu64".
        // Strip the trailing font-size suffix so both forms resolve.
        return name.EndsWith("64", StringComparison.Ordinal) && name.Length > 2
            ? name[..^2]
            : name;
    }
}
