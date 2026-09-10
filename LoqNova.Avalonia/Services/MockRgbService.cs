using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class MockRgbService : IRgbService
{
    public bool IsSupported { get; } = true;
    public bool IsSpectrumKeyboard { get; } = false;
    
    private RgbPreset _currentPreset = RgbPreset.Preset1;
    private RgbEffect _currentEffect = RgbEffect.Static;
    private RgbSpeed _currentSpeed = RgbSpeed.Fast;
    private RgbBrightness _currentBrightness = RgbBrightness.High;
    private RgbZoneColor _zone1 = new(255, 0, 0);
    private RgbZoneColor _zone2 = new(0, 255, 0);
    private RgbZoneColor _zone3 = new(0, 0, 255);
    private RgbZoneColor _zone4 = new(255, 255, 0);
    private bool _zonesSynchronized = false;
    
    public RgbPreset CurrentPreset => _currentPreset;
    public RgbEffect CurrentEffect => _currentEffect;
    public RgbSpeed CurrentSpeed => _currentSpeed;
    public RgbBrightness CurrentBrightness => _currentBrightness;
    public RgbZoneColor Zone1Color => _zone1;
    public RgbZoneColor Zone2Color => _zone2;
    public RgbZoneColor Zone3Color => _zone3;
    public RgbZoneColor Zone4Color => _zone4;
    public bool ZonesSynchronized => _zonesSynchronized;
    
    public event Action<RgbPreset>? PresetChanged;
    public event Action<RgbEffect>? EffectChanged;
    public event Action<RgbSpeed>? SpeedChanged;
    public event Action<RgbBrightness>? BrightnessChanged;
    public event Action<int, RgbZoneColor>? ZoneColorChanged;
    public event Action<bool>? SynchronizationChanged;
    
    public Task InitializeAsync() => Task.CompletedTask;
    
    public Task SetPresetAsync(RgbPreset preset)
    {
        _currentPreset = preset;
        PresetChanged?.Invoke(preset);
        return Task.CompletedTask;
    }
    
    public Task SetEffectAsync(RgbEffect effect)
    {
        _currentEffect = effect;
        EffectChanged?.Invoke(effect);
        return Task.CompletedTask;
    }
    
    public Task SetSpeedAsync(RgbSpeed speed)
    {
        _currentSpeed = speed;
        SpeedChanged?.Invoke(speed);
        return Task.CompletedTask;
    }
    
    public Task SetBrightnessAsync(RgbBrightness brightness)
    {
        _currentBrightness = brightness;
        BrightnessChanged?.Invoke(brightness);
        return Task.CompletedTask;
    }
    
    public Task SetZoneColorAsync(int zone, RgbZoneColor color)
    {
        switch (zone)
        {
            case 1: _zone1 = color; break;
            case 2: _zone2 = color; break;
            case 3: _zone3 = color; break;
            case 4: _zone4 = color; break;
        }
        ZoneColorChanged?.Invoke(zone, color);
        return Task.CompletedTask;
    }
    
    public Task SetZonesSynchronizedAsync(bool synchronized)
    {
        _zonesSynchronized = synchronized;
        SynchronizationChanged?.Invoke(synchronized);
        return Task.CompletedTask;
    }
}