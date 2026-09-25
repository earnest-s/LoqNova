using System;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LoqNova.Avalonia.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string paramStr)
        {
            var parts = paramStr.Split('|');
            if (parts.Length == 2)
            {
                var truePart = parts[0].Trim();
                var falsePart = parts[1].Trim();
                
                var trueBrush = ParseColor(truePart);
                var falseBrush = ParseColor(falsePart);
                
                return boolValue ? trueBrush : falseBrush;
            }
        }
        return Brushes.Transparent;
    }
    
    private static IBrush? ParseColor(string name)
    {
        name = name.Trim();
        if (name.StartsWith("#") || byte.TryParse(name, out _))
        {
            return Color.TryParse(name, out var color) ? new SolidColorBrush(color) : null;
        }
        return Brushes.Transparent;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}