using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace LoqNova.Avalonia.Converters;

/// <summary>
/// True when the bound string is non-null and non-empty. Used to reveal optional
/// status text (warnings, backend errors) without a style trigger.
/// </summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public static StringNotEmptyConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("StringNotEmptyConverter is one-way.");
}
