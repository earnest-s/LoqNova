using System;
using System.ComponentModel;
using Avalonia.Data.Converters;
using LoqNova.Lib.Extensions;

namespace LoqNova.Avalonia.Converters;

/// <summary>
/// Resolves an enum value to its display name using the same
/// <see cref="DisplayAttribute"/> lookup the WPF page uses, so the OS list
/// shows "Windows 11" rather than "Windows11".
/// </summary>
public class EnumDisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Enum enumValue ? enumValue.GetDisplayName() : value?.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
