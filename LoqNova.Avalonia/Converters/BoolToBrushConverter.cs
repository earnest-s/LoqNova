using System;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace LoqNova.Avalonia.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string paramStr)
        {
            var parts = paramStr.Split(',');
            if (parts.Length == 2)
            {
                var truePart = parts[0].Trim();
                var falsePart = parts[1].Trim();
                
                var trueBrush = ParseBrushPart(truePart);
                var falseBrush = ParseBrushPart(falsePart);
                
                return boolValue ? trueBrush : falseBrush;
            }
        }
        return Brushes.Transparent;
    }
    
    private static IBrush? ParseBrushPart(string part)
    {
        if (part.StartsWith("selected:"))
        {
            var resourceKey = part["selected:".Length..].Trim();
            return ResolveBrush(resourceKey);
        }
        else if (part.StartsWith("default:"))
        {
            var resourceKey = part["default:".Length..].Trim();
            return ResolveBrush(resourceKey);
        }
        return ResolveBrush(part);
    }
    
    private static IBrush? ResolveBrush(string key)
    {
        key = key.Trim();
        
        // Handle {StaticResource Key} syntax
        if (key.StartsWith("{StaticResource") && key.EndsWith("}"))
        {
            var resourceKey = key["{StaticResource".Length..].TrimEnd('}').Trim();
            if (Application.Current?.TryFindResource(resourceKey, out var resource) == true && resource is IBrush brush)
            {
                return brush;
            }
            return Brushes.Transparent;
        }
        
        // Handle direct color values
        if (key.StartsWith("#") || byte.TryParse(key, out _))
        {
            return Color.TryParse(key, out var color) ? new SolidColorBrush(color) : null;
        }
        
        // Handle resource key directly
        if (Application.Current?.TryFindResource(key, out var resource) == true && resource is IBrush brush)
        {
            return brush;
        }
        
        return Brushes.Transparent;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}