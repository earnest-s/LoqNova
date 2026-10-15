using System;
using Avalonia;
using Avalonia.Controls;
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
                
                var trueBrush = GetBrush(truePart);
                var falseBrush = GetBrush(falsePart);
                
                return boolValue ? trueBrush : falseBrush;
            }
        }
        return Brushes.Transparent;
    }
    
    private static IBrush GetBrush(string name)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) return Brushes.Transparent;

        if (Application.Current is { } app)
        {
            // Pass the active variant: the palette lives in theme dictionaries,
            // and a null variant would skip them.
            if (app.TryGetResource(name, app.ActualThemeVariant, out var resource))
            {
                if (resource is IBrush brush) return brush;
                if (resource is Color colorRes) return new SolidColorBrush(colorRes);
            }
        }

        if (Color.TryParse(name, out var color))
        {
            return new SolidColorBrush(color);
        }

        return Brushes.Transparent;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}