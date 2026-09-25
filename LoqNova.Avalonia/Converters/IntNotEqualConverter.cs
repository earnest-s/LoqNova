using System;
using Avalonia.Data.Converters;

namespace LoqNova.Avalonia.Converters;

public class IntNotEqualConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        int intValue = 0;
        bool hasValue = false;
        
        if (value is int iv)
        {
            intValue = iv;
            hasValue = true;
        }
        else
        {
            var nullableType = typeof(Nullable<int>);
            if (value?.GetType() == nullableType)
            {
                var niv = (System.Nullable<int>)value;
                if (niv.HasValue)
                {
                    intValue = niv.Value;
                    hasValue = true;
                }
            }
        }
        
        if (!hasValue)
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