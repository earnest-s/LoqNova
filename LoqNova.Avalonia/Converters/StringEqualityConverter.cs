using System;
using Avalonia.Data.Converters;

namespace LoqNova.Avalonia.Converters;

public class StringEqualityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string strValue && parameter is string paramStr)
        {
            return string.Equals(strValue, paramStr, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}