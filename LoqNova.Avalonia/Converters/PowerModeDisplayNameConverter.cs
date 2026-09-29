using System;
using System.Globalization;
using Avalonia.Data.Converters;
using LoqNova.Lib;

namespace LoqNova.Avalonia.Converters;

/// <summary>
/// User-facing name for a value. The backend enum keeps its original members
/// (<c>PowerModeState.GodMode</c> is the fourth power mode); the application
/// presents that mode as "Custom Mode", matching the WPF resource strings
/// ("Custom Mode settings", "Switch to Custom Mode with Fn+Q", ...).
/// </summary>
public sealed class PowerModeDisplayNameConverter : IValueConverter
{
    public static PowerModeDisplayNameConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            PowerModeState.Quiet => "Quiet",
            PowerModeState.Balance => "Balance",
            PowerModeState.Performance => "Performance",
            PowerModeState.GodMode => "Custom Mode",
            _ => value?.ToString() ?? string.Empty
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("PowerModeDisplayNameConverter is one-way.");
}
