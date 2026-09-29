using System;
using System.Threading.Tasks;

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
    SwipeFill = 111,
    SwipeCleanWithBlack = 112,
    Temperature = 113
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

/// <summary>
/// A zone colour. Immutable and value-comparable, which matters: a mutable struct with
/// no equality override compares by reference once boxed, so TwoWay bindings and the
/// observable-property setters fail to register a genuine colour change and the value
/// never reaches the ViewModel.
/// </summary>
public readonly struct RgbZoneColor : IEquatable<RgbZoneColor>
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public RgbZoneColor(byte r, byte g, byte b) { R = r; G = g; B = b; }

    public static RgbZoneColor FromArgb(int argb) => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    public int ToArgb() => (R << 16) | (G << 8) | B;

    public bool Equals(RgbZoneColor other) => R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is RgbZoneColor other && Equals(other);
    public override int GetHashCode() => (R << 16) | (G << 8) | B;
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    public static bool operator ==(RgbZoneColor left, RgbZoneColor right) => left.Equals(right);
    public static bool operator !=(RgbZoneColor left, RgbZoneColor right) => !left.Equals(right);
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

    /// <summary>
    /// Raised for every frame the keyboard actually renders, forwarded from the
    /// library's frame dispatcher. May arrive on a background thread.
    /// </summary>
    event Action<RgbZoneColor, RgbZoneColor, RgbZoneColor, RgbZoneColor>? FrameRendered;

    /// <summary>True when Lenovo Vantage is running and competing for keyboard control.</summary>
    bool IsVantageEnabled { get; }

    /// <summary>
    /// Why the last RGB write failed, or null after a successful one. Lets the page
    /// explain a rejected change instead of silently reverting it.
    /// </summary>
    string? LastError { get; }

    /// <summary>True when the effect exposes a speed, matching the backend's own rule.</summary>
    bool SupportsSpeed(RgbEffect effect);

    /// <summary>True when the effect uses per-zone colours, matching the backend's own rule.</summary>
    bool SupportsZoneColors(RgbEffect effect);

    /// <summary>True for the software effects driven by the custom effect controller.</summary>
    bool IsCustomEffect(RgbEffect effect);

    /// <summary>
    /// Writes the supplied values into the selected preset in a single state write,
    /// leaving the other presets and the other fields of the current preset intact,
    /// then re-reads the authoritative state.
    /// </summary>
    Task SaveStateAsync(
        RgbEffect effect,
        RgbSpeed speed,
        RgbBrightness brightness,
        RgbZoneColor zone1,
        RgbZoneColor zone2,
        RgbZoneColor zone3,
        RgbZoneColor zone4);

  /// <summary>Applies one colour to all four zones in a single state write.</summary>
  Task SynchroniseZonesAsync(RgbZoneColor color);

  /// <summary>
  /// Releases the global reactive-RGB service and the library's RGB hardware
  /// ownership on shutdown. Mirrors what WPF does when it closes.
  /// </summary>
  Task ShutdownAsync();

  Task InitializeAsync();
    Task SetPresetAsync(RgbPreset preset);
    Task SetEffectAsync(RgbEffect effect);
    Task SetSpeedAsync(RgbSpeed speed);
    Task SetBrightnessAsync(RgbBrightness brightness);
    Task SetZoneColorAsync(int zone, RgbZoneColor color);
    Task SetZonesSynchronizedAsync(bool synchronized);
}