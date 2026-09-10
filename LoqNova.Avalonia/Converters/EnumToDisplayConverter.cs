using System;
using Avalonia.Data.Converters;

namespace LoqNova.Avalonia.Converters;

public class EnumToDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            return enumValue.ToString();
        }
        return value?.ToString() ?? "";
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}