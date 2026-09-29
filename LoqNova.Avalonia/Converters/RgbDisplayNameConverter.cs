using System;
using System.Globalization;
using Avalonia.Data.Converters;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Converters;

/// <summary>
/// User-facing names for RGB values. The library enum and its internal prefixes are
/// never shown: <c>AudioVisualizer</c> renders as "Audio Visualizer" and the wave
/// effects use their direction arrows, matching the names WPF presents.
/// </summary>
public sealed class RgbDisplayNameConverter : IValueConverter
{
    public static RgbDisplayNameConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            RgbEffect.WaveRightToLeft => "Wave R→L",
            RgbEffect.WaveLeftToRight => "Wave L→R",
            RgbEffect.BreathingColorCycle => "Breathing Color Cycle",
            RgbEffect.RainbowWave => "Rainbow Wave",
            RgbEffect.SwipeCleanWithBlack => "Swipe Clean With Black",
            RgbEffect.AudioVisualizer => "Audio Visualizer",
            RgbEffect e => Humanise(e.ToString()),
            RgbSpeed s => Humanise(s.ToString()),
            RgbBrightness b => Humanise(b.ToString()),
            RgbPreset p => PresetName(p),
            _ => value?.ToString() ?? string.Empty
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("RgbDisplayNameConverter is one-way.");

    /// <summary>Preset buttons keep the numbering WPF shows rather than the enum name.</summary>
    private static string PresetName(RgbPreset preset) => preset switch
    {
        RgbPreset.Off => "Off",
        RgbPreset.Preset1 => "Preset 1",
        RgbPreset.Preset2 => "Preset 2",
        RgbPreset.Preset3 => "Preset 3",
        RgbPreset.Preset4 => "Preset 4",
        _ => Humanise(preset.ToString())
    };

    /// <summary>Splits PascalCase into spaced words for the remaining values.</summary>
    private static string Humanise(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var builder = new System.Text.StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                builder.Append(' ');

            builder.Append(name[i]);
        }

        return builder.ToString();
    }
}
