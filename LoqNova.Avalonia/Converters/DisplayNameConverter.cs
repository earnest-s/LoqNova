using System;
using System.Globalization;
using Avalonia.Data.Converters;
using LoqNova.Lib;

namespace LoqNova.Avalonia.Converters;

/// <summary>
/// Renders a feature option for display. Library state types that implement
/// <see cref="IDisplayName"/> supply their own text (for example
/// <c>Resolution</c> renders "1920 x 1080"); everything else falls back to the
/// enum member name.
/// </summary>
public sealed class DisplayNameConverter : IValueConverter
{
    public static DisplayNameConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            IDisplayName displayName => displayName.DisplayName,
            null => string.Empty,
            _ => value.ToString() ?? string.Empty
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("DisplayNameConverter is one-way.");
}
