using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Controllers;
using LoqNova.Lib.Settings;
using LoqNova.Lib;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

public class RgbService : IRgbService
{
    private readonly RGBKeyboardBacklightController _controller;
    private readonly RGBKeyboardSettings _settings;
    private readonly ILogger<RgbService> _logger;

    public bool IsSupported { get; private set; } = false;
    public bool IsSpectrumKeyboard { get; private set; } = false;

    public RgbPreset CurrentPreset { get; private set; } = RgbPreset.Off;
    public RgbEffect CurrentEffect { get; private set; } = RgbEffect.Static;
    public RgbSpeed CurrentSpeed { get; private set; } = RgbSpeed.Fast;
    public RgbBrightness CurrentBrightness { get; private set; } = RgbBrightness.High;
    
    public RgbZoneColor Zone1Color { get; private set; } = new(255, 0, 0);
    public RgbZoneColor Zone2Color { get; private set; } = new(0, 255, 0);
    public RgbZoneColor Zone3Color { get; private set; } = new(0, 0, 255);
    public RgbZoneColor Zone4Color { get; private set; } = new(255, 255, 0);
    
    public bool ZonesSynchronized { get; private set; } = false;

    public event Action<RgbPreset>? PresetChanged;
    public event Action<RgbEffect>? EffectChanged;
    public event Action<RgbSpeed>? SpeedChanged;
    public event Action<RgbBrightness>? BrightnessChanged;
    public event Action<int, RgbZoneColor>? ZoneColorChanged;
    public event Action<bool>? SynchronizationChanged;

    public RgbService(RGBKeyboardBacklightController controller, RGBKeyboardSettings settings, ILogger<RgbService> logger)
    {
        _controller = controller;
        _settings = settings;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsSupported = await _controller.IsSupportedAsync().ConfigureAwait(false);
            
            if (IsSupported)
            {
                var state = await _controller.GetStateAsync().ConfigureAwait(false);
                UpdateFromState(state);
                _logger.LogInformation("RGB service initialized. Preset: {Preset}", CurrentPreset);
            }
            else
            {
                _logger.LogWarning("RGB keyboard not supported on this hardware");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RGB service");
            IsSupported = false;
        }
    }

    public async Task SetPresetAsync(RgbPreset preset)
    {
        if (!IsSupported) return;

        try
        {
            var libPreset = MapToLibPreset(preset);
            await _controller.SetPresetAsync(libPreset).ConfigureAwait(false);
            
            var state = await _controller.GetStateAsync().ConfigureAwait(false);
            UpdateFromState(state);
            PresetChanged?.Invoke(CurrentPreset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set preset {Preset}", preset);
        }
    }

    public async Task SetEffectAsync(RgbEffect effect)
    {
        if (!IsSupported) return;

        try
        {
            var libEffect = MapToLibEffect(effect);
            var state = _settings.Store.State;
            var presets = new Dictionary<RGBKeyboardBacklightPreset, RGBKeyboardBacklightBacklightPresetDescription>(state.Presets);
            var currentPreset = state.SelectedPreset;
            
            if (presets.TryGetValue(currentPreset, out var description))
            {
                var newDescription = new RGBKeyboardBacklightBacklightPresetDescription(
                    libEffect,
                    description.Speed,
                    description.Brightness,
                    description.Zone1,
                    description.Zone2,
                    description.Zone3,
                    description.Zone4);
                presets[currentPreset] = newDescription;
                
                _settings.Store.State = new RGBKeyboardBacklightState(currentPreset, presets);
                _settings.SynchronizeStore();
                
                await _controller.SetStateAsync(_settings.Store.State).ConfigureAwait(false);
                
                state = await _controller.GetStateAsync().ConfigureAwait(false);
                UpdateFromState(state);
                EffectChanged?.Invoke(CurrentEffect);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set effect {Effect}", effect);
        }
    }

    public async Task SetSpeedAsync(RgbSpeed speed)
    {
        if (!IsSupported) return;

        try
        {
            var libSpeed = MapToLibSpeed(speed);
            var state = _settings.Store.State;
            var presets = new Dictionary<RGBKeyboardBacklightPreset, RGBKeyboardBacklightBacklightPresetDescription>(state.Presets);
            var currentPreset = state.SelectedPreset;
            
            if (presets.TryGetValue(currentPreset, out var description))
            {
                var newDescription = new RGBKeyboardBacklightBacklightPresetDescription(
                    description.Effect,
                    libSpeed,
                    description.Brightness,
                    description.Zone1,
                    description.Zone2,
                    description.Zone3,
                    description.Zone4);
                presets[currentPreset] = newDescription;
                
                _settings.Store.State = new RGBKeyboardBacklightState(currentPreset, presets);
                _settings.SynchronizeStore();
                
                await _controller.SetStateAsync(_settings.Store.State).ConfigureAwait(false);
                
                state = await _controller.GetStateAsync().ConfigureAwait(false);
                UpdateFromState(state);
                SpeedChanged?.Invoke(CurrentSpeed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set speed {Speed}", speed);
        }
    }

    public async Task SetBrightnessAsync(RgbBrightness brightness)
    {
        if (!IsSupported) return;

        try
        {
            var libBrightness = MapToLibBrightness(brightness);
            var state = _settings.Store.State;
            var presets = new Dictionary<RGBKeyboardBacklightPreset, RGBKeyboardBacklightBacklightPresetDescription>(state.Presets);
            var currentPreset = state.SelectedPreset;
            
            if (presets.TryGetValue(currentPreset, out var description))
            {
                var newDescription = new RGBKeyboardBacklightBacklightPresetDescription(
                    description.Effect,
                    description.Speed,
                    libBrightness,
                    description.Zone1,
                    description.Zone2,
                    description.Zone3,
                    description.Zone4);
                presets[currentPreset] = newDescription;
                
                _settings.Store.State = new RGBKeyboardBacklightState(currentPreset, presets);
                _settings.SynchronizeStore();
                
                await _controller.SetStateAsync(_settings.Store.State).ConfigureAwait(false);
                
                state = await _controller.GetStateAsync().ConfigureAwait(false);
                UpdateFromState(state);
                BrightnessChanged?.Invoke(CurrentBrightness);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set brightness {Brightness}", brightness);
        }
    }

    public async Task SetZoneColorAsync(int zone, RgbZoneColor color)
    {
        if (!IsSupported) return;

        try
        {
            var libColor = new LoqNova.Lib.RGBColor(color.R, color.G, color.B);
            var state = _settings.Store.State;
            var presets = new Dictionary<RGBKeyboardBacklightPreset, RGBKeyboardBacklightBacklightPresetDescription>(state.Presets);
            var currentPreset = state.SelectedPreset;
            
            if (presets.TryGetValue(currentPreset, out var description))
            {
                var zone1 = zone == 1 ? libColor : description.Zone1;
                var zone2 = zone == 2 ? libColor : description.Zone2;
                var zone3 = zone == 3 ? libColor : description.Zone3;
                var zone4 = zone == 4 ? libColor : description.Zone4;
                
                var newDescription = new RGBKeyboardBacklightBacklightPresetDescription(
                    description.Effect,
                    description.Speed,
                    description.Brightness,
                    zone1, zone2, zone3, zone4);
                presets[currentPreset] = newDescription;
                
                _settings.Store.State = new RGBKeyboardBacklightState(currentPreset, presets);
                _settings.SynchronizeStore();
                
                await _controller.SetStateAsync(_settings.Store.State).ConfigureAwait(false);
                
                state = await _controller.GetStateAsync().ConfigureAwait(false);
                UpdateFromState(state);
                ZoneColorChanged?.Invoke(zone, color);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set zone {Zone} color", zone);
        }
    }

    public async Task SetZonesSynchronizedAsync(bool synchronized)
    {
        if (!IsSupported) return;

        try
        {
            ZonesSynchronized = synchronized;
            var state = _settings.Store.State;
            var presets = new Dictionary<RGBKeyboardBacklightPreset, RGBKeyboardBacklightBacklightPresetDescription>(state.Presets);
            var currentPreset = state.SelectedPreset;
            
            if (presets.TryGetValue(currentPreset, out var description))
            {
                var syncColor = description.Zone1;
                var newDescription = new RGBKeyboardBacklightBacklightPresetDescription(
                    description.Effect,
                    description.Speed,
                    description.Brightness,
                    syncColor, syncColor, syncColor, syncColor);
                presets[currentPreset] = newDescription;
                
                _settings.Store.State = new RGBKeyboardBacklightState(currentPreset, presets);
                _settings.SynchronizeStore();
                
                await _controller.SetStateAsync(_settings.Store.State).ConfigureAwait(false);
                
                state = await _controller.GetStateAsync().ConfigureAwait(false);
                UpdateFromState(state);
                SynchronizationChanged?.Invoke(synchronized);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set zones synchronized: {Sync}", synchronized);
        }
    }

    private void UpdateFromState(RGBKeyboardBacklightState state)
    {
        CurrentPreset = MapFromLibPreset(state.SelectedPreset);
        
        if (state.Presets.TryGetValue(state.SelectedPreset, out var description))
        {
            CurrentEffect = MapFromLibEffect(description.Effect);
            CurrentSpeed = MapFromLibSpeed(description.Speed);
            CurrentBrightness = MapFromLibBrightness(description.Brightness);
            
            Zone1Color = new(description.Zone1.R, description.Zone1.G, description.Zone1.B);
            Zone2Color = new(description.Zone2.R, description.Zone2.G, description.Zone2.B);
            Zone3Color = new(description.Zone3.R, description.Zone3.G, description.Zone3.B);
            Zone4Color = new(description.Zone4.R, description.Zone4.G, description.Zone4.B);
            
            ZonesSynchronized = Zone1Color.Equals(Zone2Color) && Zone1Color.Equals(Zone3Color) && Zone1Color.Equals(Zone4Color);
        }
    }

    private static RgbPreset MapFromLibPreset(RGBKeyboardBacklightPreset preset) => preset switch
    {
        RGBKeyboardBacklightPreset.Off => RgbPreset.Off,
        RGBKeyboardBacklightPreset.One => RgbPreset.Preset1,
        RGBKeyboardBacklightPreset.Two => RgbPreset.Preset2,
        RGBKeyboardBacklightPreset.Three => RgbPreset.Preset3,
        RGBKeyboardBacklightPreset.Four => RgbPreset.Preset4,
        _ => RgbPreset.Off
    };

    private static RGBKeyboardBacklightPreset MapToLibPreset(RgbPreset preset) => preset switch
    {
        RgbPreset.Off => RGBKeyboardBacklightPreset.Off,
        RgbPreset.Preset1 => RGBKeyboardBacklightPreset.One,
        RgbPreset.Preset2 => RGBKeyboardBacklightPreset.Two,
        RgbPreset.Preset3 => RGBKeyboardBacklightPreset.Three,
        RgbPreset.Preset4 => RGBKeyboardBacklightPreset.Four,
        _ => RGBKeyboardBacklightPreset.Off
    };

    private static RgbEffect MapFromLibEffect(RGBKeyboardBacklightEffect effect) => effect switch
    {
        RGBKeyboardBacklightEffect.Static => RgbEffect.Static,
        RGBKeyboardBacklightEffect.Breath => RgbEffect.Breath,
        RGBKeyboardBacklightEffect.WaveRTL => RgbEffect.WaveRightToLeft,
        RGBKeyboardBacklightEffect.WaveLTR => RgbEffect.WaveLeftToRight,
        RGBKeyboardBacklightEffect.Smooth => RgbEffect.Smooth,
        RGBKeyboardBacklightEffect.Ambient => RgbEffect.Ambient,
        RGBKeyboardBacklightEffect.AudioVisualizer => RgbEffect.AudioVisualizer,
        RGBKeyboardBacklightEffect.BreathingColorCycle => RgbEffect.BreathingColorCycle,
        RGBKeyboardBacklightEffect.Christmas => RgbEffect.Christmas,
        RGBKeyboardBacklightEffect.Disco => RgbEffect.Disco,
        RGBKeyboardBacklightEffect.Fade => RgbEffect.Fade,
        RGBKeyboardBacklightEffect.Lightning => RgbEffect.Lightning,
        RGBKeyboardBacklightEffect.RainbowWave => RgbEffect.RainbowWave,
        RGBKeyboardBacklightEffect.Ripple => RgbEffect.Ripple,
        RGBKeyboardBacklightEffect.Strobe => RgbEffect.Strobe,
        RGBKeyboardBacklightEffect.Swipe => RgbEffect.Swipe,
        RGBKeyboardBacklightEffect.Temperature => RgbEffect.Temperature,
        _ => RgbEffect.Static
    };

    private static RGBKeyboardBacklightEffect MapToLibEffect(RgbEffect effect) => effect switch
    {
        RgbEffect.Static => RGBKeyboardBacklightEffect.Static,
        RgbEffect.Breath => RGBKeyboardBacklightEffect.Breath,
        RgbEffect.WaveRightToLeft => RGBKeyboardBacklightEffect.WaveRTL,
        RgbEffect.WaveLeftToRight => RGBKeyboardBacklightEffect.WaveLTR,
        RgbEffect.Smooth => RGBKeyboardBacklightEffect.Smooth,
        RgbEffect.Ambient => RGBKeyboardBacklightEffect.Ambient,
        RgbEffect.AudioVisualizer => RGBKeyboardBacklightEffect.AudioVisualizer,
        RgbEffect.BreathingColorCycle => RGBKeyboardBacklightEffect.BreathingColorCycle,
        RgbEffect.Christmas => RGBKeyboardBacklightEffect.Christmas,
        RgbEffect.Disco => RGBKeyboardBacklightEffect.Disco,
        RgbEffect.Fade => RGBKeyboardBacklightEffect.Fade,
        RgbEffect.Lightning => RGBKeyboardBacklightEffect.Lightning,
        RgbEffect.RainbowWave => RGBKeyboardBacklightEffect.RainbowWave,
        RgbEffect.Ripple => RGBKeyboardBacklightEffect.Ripple,
        RgbEffect.Strobe => RGBKeyboardBacklightEffect.Strobe,
        RgbEffect.Swipe => RGBKeyboardBacklightEffect.Swipe,
        RgbEffect.Temperature => RGBKeyboardBacklightEffect.Temperature,
        _ => RGBKeyboardBacklightEffect.Static
    };

    private static RgbSpeed MapFromLibSpeed(RGBKeyboardBacklightSpeed speed) => speed switch
    {
        RGBKeyboardBacklightSpeed.Slowest => RgbSpeed.Slowest,
        RGBKeyboardBacklightSpeed.Slow => RgbSpeed.Slow,
        RGBKeyboardBacklightSpeed.Fast => RgbSpeed.Fast,
        RGBKeyboardBacklightSpeed.Fastest => RgbSpeed.Fastest,
        _ => RgbSpeed.Fast
    };

    private static RGBKeyboardBacklightSpeed MapToLibSpeed(RgbSpeed speed) => speed switch
    {
        RgbSpeed.Slowest => RGBKeyboardBacklightSpeed.Slowest,
        RgbSpeed.Slow => RGBKeyboardBacklightSpeed.Slow,
        RgbSpeed.Fast => RGBKeyboardBacklightSpeed.Fast,
        RgbSpeed.Fastest => RGBKeyboardBacklightSpeed.Fastest,
        _ => RGBKeyboardBacklightSpeed.Fast
    };

    private static RgbBrightness MapFromLibBrightness(RGBKeyboardBacklightBrightness brightness) => brightness switch
    {
        RGBKeyboardBacklightBrightness.Off => RgbBrightness.Off,
        RGBKeyboardBacklightBrightness.Low => RgbBrightness.Low,
        RGBKeyboardBacklightBrightness.High => RgbBrightness.High,
        _ => RgbBrightness.High
    };

    private static RGBKeyboardBacklightBrightness MapToLibBrightness(RgbBrightness brightness) => brightness switch
    {
        RgbBrightness.Off => RGBKeyboardBacklightBrightness.Off,
        RgbBrightness.Low => RGBKeyboardBacklightBrightness.Low,
        RgbBrightness.High => RGBKeyboardBacklightBrightness.High,
        _ => RGBKeyboardBacklightBrightness.High
    };
}