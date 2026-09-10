using System.Threading.Tasks;
using LoqNova.Lib.Controllers.CustomRGBEffects;

namespace LoqNova.Avalonia.Services;

public enum RgbPreset
{
    Off = 0,
    Preset1 = 1,
    Preset2 = 2,
    Preset3 = 3,
    Preset4 = 4
}

public enum RgbEffect
{
    Static = 0,
    Breath = 1,
    WaveRightToLeft = 2,
    WaveLeftToRight = 3,
    Smooth = 4,
    Ambient = 100,
    AudioVisualizer = 101,
    BreathingColorCycle = 102,
    Christmas = 103,
    Disco = 104,
    Fade = 105,
    Lightning = 106,
    RainbowWave = 107,
    Ripple = 108,
    Strobe = 109,
    Swipe = 110,
    Temperature = 111
}

public enum RgbSpeed
{
    Slowest = 1,
    Slow = 2,
    Fast = 3,
    Fastest = 4
}

public enum RgbBrightness
{
    Off = 0,
    Low = 1,
    High = 2
}

public struct RgbZoneColor
{
    public byte R, G, B;
    
    public RgbZoneColor(byte r, byte g, byte b) { R = r; G = g; B = b; }
    
    public static RgbZoneColor FromArgb(int argb) => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    public int ToArgb() => (R << 16) | (G << 8) | B;
}

public interface IRgbService
{
    bool IsSupported { get; }
    bool IsSpectrumKeyboard { get; }
    RgbPreset CurrentPreset { get; }
    RgbEffect CurrentEffect { get; }
    RgbSpeed CurrentSpeed { get; }
    RgbBrightness CurrentBrightness { get; }
    RgbZoneColor Zone1Color { get; }
    RgbZoneColor Zone2Color { get; }
    RgbZoneColor Zone3Color { get; }
    RgbZoneColor Zone4Color { get; }
    bool ZonesSynchronized { get; }
    
    event Action<RgbPreset>? PresetChanged;
    event Action<RgbEffect>? EffectChanged;
    event Action<RgbSpeed>? SpeedChanged;
    event Action<RgbBrightness>? BrightnessChanged;
    event Action<int, RgbZoneColor>? ZoneColorChanged;
    event Action<bool>? SynchronizationChanged;
    
    Task InitializeAsync();
    Task SetPresetAsync(RgbPreset preset);
    Task SetEffectAsync(RgbEffect effect);
    Task SetSpeedAsync(RgbSpeed speed);
    Task SetBrightnessAsync(RgbBrightness brightness);
    Task SetZoneColorAsync(int zone, RgbZoneColor color);
    Task SetZonesSynchronizedAsync(bool synchronized);
}