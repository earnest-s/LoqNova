using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Converters;

public class RgbZoneColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is RgbZoneColor color)
        {
            return new SolidColorBrush(new Color(255, color.R, color.G, color.B));
        }
        return Brushes.Transparent;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}