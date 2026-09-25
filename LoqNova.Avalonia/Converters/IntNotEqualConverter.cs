using System;
using Avalonia.Data.Converters;

namespace LoqNova.Avalonia.Converters;

public class IntNotEqualConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        int intValue;
        if (value is int iv)
        {
            intValue = iv;
        }
        else if (value is Nullable<int> niv && niv.HasValue)
        {
            intValue = niv.Value;
        }
        else
        {
            return true;
        }
        
        if (parameter is string paramStr && int.TryParse(paramStr, out var paramValue))
        {
            return intValue != paramValue;
        }
        return true;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}