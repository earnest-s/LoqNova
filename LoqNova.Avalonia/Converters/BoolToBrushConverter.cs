using System;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
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
            var colorName = part["selected:".Length..].Trim();
            return ParseColorOrResource(colorName);
        }
        else if (part.StartsWith("default:"))
        {
            var colorName = part["default:".Length..].Trim();
            return ParseColorOrResource(colorName);
        }
        return ParseColorOrResource(part);
    }
    
    private static IBrush? ParseColorOrResource(string name)
    {
        if (name.StartsWith("#") || byte.TryParse(name, out _))
        {
            return Color.TryParse(name, out var color) ? new SolidColorBrush(color) : null;
        }
        return new DynamicResourceExtension(name);
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}