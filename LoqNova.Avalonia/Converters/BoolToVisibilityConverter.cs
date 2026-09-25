using System;
using Avalonia.Data.Converters;

namespace LoqNova.Avalonia.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            var inverted = parameter is string str && str.Equals("invert", StringComparison.OrdinalIgnoreCase);
            return boolValue ^ inverted;
        }
        return false;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}