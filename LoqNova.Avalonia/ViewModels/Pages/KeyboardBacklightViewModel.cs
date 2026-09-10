using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Controllers.CustomRGBEffects;

namespace LoqNova.Avalonia.ViewModels.Pages;

public partial class KeyboardBacklightViewModel : ViewModelBase
{
    private readonly IRgbService _rgbService;
    private readonly ISettingsService _settingsService;
    
    [ObservableProperty]
    private bool _isRgbKeyboard = true;
    
    [ObservableProperty]
    private bool _isSpectrumKeyboard = false;
    
    [ObservableProperty]
    private RgbPreset _selectedPreset = RgbPreset.Preset1;
    
    [ObservableProperty]
    private RgbEffect _selectedEffect = RgbEffect.Static;
    
    [ObservableProperty]
    private RgbSpeed _selectedSpeed = RgbSpeed.Fast;
    
    [ObservableProperty]
    private RgbBrightness _selectedBrightness = RgbBrightness.High;
    
    [ObservableProperty]
    private RgbZoneColor _zone1Color = new(255, 0, 0);
    
    [ObservableProperty]
    private RgbZoneColor _zone2Color = new(0, 255, 0);
    
    [ObservableProperty]
    private RgbZoneColor _zone3Color = new(0, 0, 255);
    
    [ObservableProperty]
    private RgbZoneColor _zone4Color = new(255, 255, 0);
    
    [ObservableProperty]
    private bool _zonesSynchronized = false;
    
    [ObservableProperty]
    private bool _isCustomEffect = false;
    
    public ObservableCollection<RgbPreset> Presets { get; } = new()
    {
        RgbPreset.Off, RgbPreset.Preset1, RgbPreset.Preset2, RgbPreset.Preset3, RgbPreset.Preset4
    };
    
    public ObservableCollection<RgbEffect> Effects { get; } = new()
    {
        RgbEffect.Static, RgbEffect.Breath, RgbEffect.WaveRightToLeft, 
        RgbEffect.WaveLeftToRight, RgbEffect.Smooth
    };
    
    public ObservableCollection<RgbEffect> CustomEffects { get; } = new()
    {
        RgbEffect.Ambient, RgbEffect.AudioVisualizer, RgbEffect.BreathingColorCycle,
        RgbEffect.Christmas, RgbEffect.Disco, RgbEffect.Fade, RgbEffect.Lightning,
        RgbEffect.RainbowWave, RgbEffect.Ripple, RgbEffect.Strobe, RgbEffect.Swipe,
        RgbEffect.Temperature
    };
    
    public ObservableCollection<RgbSpeed> Speeds { get; } = new()
    {
        RgbSpeed.Slowest, RgbSpeed.Slow, RgbSpeed.Fast, RgbSpeed.Fastest
    };
    
    public ObservableCollection<RgbBrightness> BrightnessLevels { get; } = new()
    {
        RgbBrightness.Off, RgbBrightness.Low, RgbBrightness.High
    };
    
    public KeyboardBacklightViewModel(IRgbService rgbService, ISettingsService settingsService)
    {
        _rgbService = rgbService;
        _settingsService = settingsService;
        
        SubscribeToEvents();
        LoadCurrentState();
    }
    
    private void SubscribeToEvents()
    {
        _rgbService.PresetChanged += preset => SelectedPreset = preset;
        _rgbService.EffectChanged += effect => SelectedEffect = effect;
        _rgbService.SpeedChanged += speed => SelectedSpeed = speed;
        _rgbService.BrightnessChanged += brightness => SelectedBrightness = brightness;
        _rgbService.ZoneColorChanged += (zone, color) => 
        {
            switch (zone)
            {
                case 1: Zone1Color = color; break;
                case 2: Zone2Color = color; break;
                case 3: Zone3Color = color; break;
                case 4: Zone4Color = color; break;
            }
        };
        _rgbService.SynchronizationChanged += sync => ZonesSynchronized = sync;
    }
    
    private void LoadCurrentState()
    {
        SelectedPreset = _rgbService.CurrentPreset;
        SelectedEffect = _rgbService.CurrentEffect;
        SelectedSpeed = _rgbService.CurrentSpeed;
        SelectedBrightness = _rgbService.CurrentBrightness;
        Zone1Color = _rgbService.Zone1Color;
        Zone2Color = _rgbService.Zone2Color;
        Zone3Color = _rgbService.Zone3Color;
        Zone4Color = _rgbService.Zone4Color;
        ZonesSynchronized = _rgbService.ZonesSynchronized;
        IsCustomEffect = IsCustomEffectType(_rgbService.CurrentEffect);
    }
    
    private bool IsCustomEffectType(RgbEffect effect)
    {
        return effect >= RgbEffect.Ambient;
    }
    
    partial void OnSelectedPresetChanged(RgbPreset value)
    {
        if (!IsCustomEffectType(value))
        {
            _ = _rgbService.SetPresetAsync(value);
            IsCustomEffect = false;
        }
    }
    
    partial void OnSelectedEffectChanged(RgbEffect value)
    {
        if (IsCustomEffectType(value))
        {
            _ = _rgbService.SetEffectAsync(value);
            IsCustomEffect = true;
        }
    }
    
    partial void OnSelectedSpeedChanged(RgbSpeed value)
    {
        _ = _rgbService.SetSpeedAsync(value);
    }
    
    partial void OnSelectedBrightnessChanged(RgbBrightness value)
    {
        _ = _rgbService.SetBrightnessAsync(value);
    }
    
    partial void OnZone1ColorChanged(RgbZoneColor value)
    {
        _ = _rgbService.SetZoneColorAsync(1, value);
    }
    
    partial void OnZone2ColorChanged(RgbZoneColor value)
    {
        _ = _rgbService.SetZoneColorAsync(2, value);
    }
    
    partial void OnZone3ColorChanged(RgbZoneColor value)
    {
        _ = _rgbService.SetZoneColorAsync(3, value);
    }
    
    partial void OnZone4ColorChanged(RgbZoneColor value)
    {
        _ = _rgbService.SetZoneColorAsync(4, value);
    }
    
    partial void OnZonesSynchronizedChanged(bool value)
    {
        _ = _rgbService.SetZonesSynchronizedAsync(value);
    }
    
    [RelayCommand]
    private async Task SynchronizeAllZonesAsync()
    {
        await _rgbService.SetZoneColorAsync(2, Zone1Color);
        await _rgbService.SetZoneColorAsync(3, Zone1Color);
        await _rgbService.SetZoneColorAsync(4, Zone1Color);
    }
}