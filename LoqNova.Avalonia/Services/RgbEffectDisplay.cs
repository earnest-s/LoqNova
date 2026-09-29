using System;
using System.Linq;
using LoqNova.Lib;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Presentation-only mapping for RGB backlight values. The library enums are the
/// source of truth; these helpers translate between them and the Avalonia-facing
/// enums, and produce the same display names WPF shows via <c>GetDisplayName()</c>.
/// </summary>
public static class RgbEffectDisplay
{
    /// <summary>Every library effect, in enum order: firmware effects then software effects.</summary>
    public static RgbEffect[] AllEffects() => Enum.GetValues<RGBKeyboardBacklightEffect>().Select(FromLibEffect).ToArray();

    public static RgbSpeed[] AllSpeeds() => Enum.GetValues<RGBKeyboardBacklightSpeed>().Select(FromLibSpeed).ToArray();

    public static RgbBrightness[] AllBrightness() => Enum.GetValues<RGBKeyboardBacklightBrightness>().Select(FromLibBrightness).ToArray();

    public static RgbEffect FromLibEffect(RGBKeyboardBacklightEffect effect) => effect switch
    {
        RGBKeyboardBacklightEffect.Static => RgbEffect.Static,
        RGBKeyboardBacklightEffect.Breath => RgbEffect.Breath,
        RGBKeyboardBacklightEffect.Smooth => RgbEffect.Smooth,
        RGBKeyboardBacklightEffect.WaveRTL => RgbEffect.WaveRightToLeft,
        RGBKeyboardBacklightEffect.WaveLTR => RgbEffect.WaveLeftToRight,
        RGBKeyboardBacklightEffect.Disco => RgbEffect.Disco,
        RGBKeyboardBacklightEffect.Swipe => RgbEffect.Swipe,
        RGBKeyboardBacklightEffect.SwipeFill => RgbEffect.SwipeFill,
        RGBKeyboardBacklightEffect.SwipeCleanWithBlack => RgbEffect.SwipeCleanWithBlack,
        RGBKeyboardBacklightEffect.Lightning => RgbEffect.Lightning,
        RGBKeyboardBacklightEffect.Christmas => RgbEffect.Christmas,
        RGBKeyboardBacklightEffect.Temperature => RgbEffect.Temperature,
        RGBKeyboardBacklightEffect.RainbowWave => RgbEffect.RainbowWave,
        RGBKeyboardBacklightEffect.Fade => RgbEffect.Fade,
        RGBKeyboardBacklightEffect.Ripple => RgbEffect.Ripple,
        RGBKeyboardBacklightEffect.Ambient => RgbEffect.Ambient,
        RGBKeyboardBacklightEffect.BreathingColorCycle => RgbEffect.BreathingColorCycle,
        RGBKeyboardBacklightEffect.Strobe => RgbEffect.Strobe,
        RGBKeyboardBacklightEffect.AudioVisualizer => RgbEffect.AudioVisualizer,
        _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, null)
    };

    public static RGBKeyboardBacklightEffect ToLibEffect(RgbEffect effect) => effect switch
    {
        RgbEffect.Static => RGBKeyboardBacklightEffect.Static,
        RgbEffect.Breath => RGBKeyboardBacklightEffect.Breath,
        RgbEffect.Smooth => RGBKeyboardBacklightEffect.Smooth,
        RgbEffect.WaveRightToLeft => RGBKeyboardBacklightEffect.WaveRTL,
        RgbEffect.WaveLeftToRight => RGBKeyboardBacklightEffect.WaveLTR,
        RgbEffect.Disco => RGBKeyboardBacklightEffect.Disco,
        RgbEffect.Swipe => RGBKeyboardBacklightEffect.Swipe,
        RgbEffect.SwipeFill => RGBKeyboardBacklightEffect.SwipeFill,
        RgbEffect.SwipeCleanWithBlack => RGBKeyboardBacklightEffect.SwipeCleanWithBlack,
        RgbEffect.Lightning => RGBKeyboardBacklightEffect.Lightning,
        RgbEffect.Christmas => RGBKeyboardBacklightEffect.Christmas,
        RgbEffect.Temperature => RGBKeyboardBacklightEffect.Temperature,
        RgbEffect.RainbowWave => RGBKeyboardBacklightEffect.RainbowWave,
        RgbEffect.Fade => RGBKeyboardBacklightEffect.Fade,
        RgbEffect.Ripple => RGBKeyboardBacklightEffect.Ripple,
        RgbEffect.Ambient => RGBKeyboardBacklightEffect.Ambient,
        RgbEffect.BreathingColorCycle => RGBKeyboardBacklightEffect.BreathingColorCycle,
        RgbEffect.Strobe => RGBKeyboardBacklightEffect.Strobe,
        RgbEffect.AudioVisualizer => RGBKeyboardBacklightEffect.AudioVisualizer,
        _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, null)
    };

    public static RgbSpeed FromLibSpeed(RGBKeyboardBacklightSpeed speed) => speed switch
    {
        RGBKeyboardBacklightSpeed.Slowest => RgbSpeed.Slowest,
        RGBKeyboardBacklightSpeed.Slow => RgbSpeed.Slow,
        RGBKeyboardBacklightSpeed.Fast => RgbSpeed.Fast,
        RGBKeyboardBacklightSpeed.Fastest => RgbSpeed.Fastest,
        _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, null)
    };

    public static RGBKeyboardBacklightSpeed ToLibSpeed(RgbSpeed speed) => speed switch
    {
        RgbSpeed.Slowest => RGBKeyboardBacklightSpeed.Slowest,
        RgbSpeed.Slow => RGBKeyboardBacklightSpeed.Slow,
        RgbSpeed.Fast => RGBKeyboardBacklightSpeed.Fast,
        RgbSpeed.Fastest => RGBKeyboardBacklightSpeed.Fastest,
        _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, null)
    };

    public static RgbBrightness FromLibBrightness(RGBKeyboardBacklightBrightness brightness) => brightness switch
    {
        RGBKeyboardBacklightBrightness.Low => RgbBrightness.Low,
        RGBKeyboardBacklightBrightness.High => RgbBrightness.High,
        _ => throw new ArgumentOutOfRangeException(nameof(brightness), brightness, null)
    };

    public static RGBKeyboardBacklightBrightness ToLibBrightness(RgbBrightness brightness) => brightness switch
    {
        RgbBrightness.Low => RGBKeyboardBacklightBrightness.Low,
        RgbBrightness.High => RGBKeyboardBacklightBrightness.High,
        _ => throw new ArgumentOutOfRangeException(nameof(brightness), brightness, null)
    };
}
