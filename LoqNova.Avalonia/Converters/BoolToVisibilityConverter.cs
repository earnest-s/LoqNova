using System;
using Avalonia.Data.Converters;
using Avalonia.Controls;

namespace LoqNova.Avalonia.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            var inverted = parameter is string str && str.Equals("invert", StringComparison.OrdinalIgnoreCase);
            return (boolValue ^ inverted) ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}