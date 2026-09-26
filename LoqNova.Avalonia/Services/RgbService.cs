using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Controllers;
using LoqNova.Lib.Settings;
using LoqNova.Lib.Utils;
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
            var presets = state.Presets;
            var currentPreset = state.SelectedPreset;
            
            if (presets.TryGetValue(currentPreset, out var description))
            {
                var newDescription = description with { Effect = libEffect };
                presets[currentPreset] = newDescription;
                
                _settings.Store.State = new(currentPreset, presets);
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
            var presets = state.Presets;
            var currentPreset = state.SelectedPreset;
            
            if (presets.TryGetValue(currentPreset, out var description))
            {
                var newDescription = description with { Speed = libSpeed };
                presets[currentPreset] = newDescription;
                
                _settings.Store.State = new(currentPreset, presets);
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
            var presets = state.Presets;
            var currentPreset = state.SelectedPreset;
            
            if (presets.TryGetValue(currentPreset, out var description))
            {
                var newDescription = description with { Brightness = libBrightness };
                presets[currentPreset] = newDescription;
                
                _settings.Store.State = new(currentPreset, presets);
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
            var presets = state.Presets;
            var currentPreset = state.SelectedPreset;
            
            if (presets.TryGetValue(currentPreset, out var description))
            {
                var zone1 = zone == 1 ? libColor : description.Zone1;
                var zone2 = zone == 2 ? libColor : description.Zone2;
                var zone3 = zone == 3 ? libColor : description.Zone3;
                var zone4 = zone == 4 ? libColor : description.Zone4;
                
                var newDescription = description with 
                { 
                    Zone1 = zone1, 
                    Zone2 = zone2, 
                    Zone3 = zone3, 
                    Zone4 = zone4 
                };
                presets[currentPreset] = newDescription;
                
                _settings.Store.State = new(currentPreset, presets);
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
            var presets = state.Presets;
            var currentPreset = state.SelectedPreset;
            
            if (presets.TryGetValue(currentPreset, out var description))
            {
                var syncColor = description.Zone1;
                var newDescription = description with 
                { 
                    Zone1 = syncColor, 
                    Zone2 = syncColor, 
                    Zone3 = syncColor, 
                    Zone4 = syncColor 
                };
                presets[currentPreset] = newDescription;
                
                _settings.Store.State = new(currentPreset, presets);
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

    private void UpdateFromState(LoqNova.Lib.Structs.RGBKeyboardBacklightState state)
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

    private static RgbPreset MapFromLibPreset(LoqNova.Lib.RGBKeyboardBacklightPreset preset) => preset switch
    {
        LoqNova.Lib.Enums.RGBKeyboardBacklightPreset.Off => RgbPreset.Off,
        LoqNova.Lib.Enums.RGBKeyboardBacklightPreset.One => RgbPreset.Preset1,
        LoqNova.Lib.Enums.RGBKeyboardBacklightPreset.Two => RgbPreset.Preset2,
        LoqNova.Lib.Enums.RGBKeyboardBacklightPreset.Three => RgbPreset.Preset3,
        LoqNova.Lib.Enums.RGBKeyboardBacklightPreset.Four => RgbPreset.Preset4,
        _ => RgbPreset.Off
    };

    private static LoqNova.Lib.RGBKeyboardBacklightPreset MapToLibPreset(RgbPreset preset) => preset switch
    {
        RgbPreset.Off => LoqNova.Lib.Enums.RGBKeyboardBacklightPreset.Off,
        RgbPreset.Preset1 => LoqNova.Lib.Enums.RGBKeyboardBacklightPreset.One,
        RgbPreset.Preset2 => LoqNova.Lib.Enums.RGBKeyboardBacklightPreset.Two,
        RgbPreset.Preset3 => LoqNova.Lib.Enums.RGBKeyboardBacklightPreset.Three,
        RgbPreset.Preset4 => LoqNova.Lib.Enums.RGBKeyboardBacklightPreset.Four,
        _ => LoqNova.Lib.Enums.RGBKeyboardBacklightPreset.Off
    };

    private static RgbEffect MapFromLibEffect(LoqNova.Lib.RGBKeyboardBacklightEffect effect) => effect switch
    {
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Static => RgbEffect.Static,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Breath => RgbEffect.Breath,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.WaveRTL => RgbEffect.WaveRightToLeft,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.WaveLTR => RgbEffect.WaveLeftToRight,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Smooth => RgbEffect.Smooth,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Ambient => RgbEffect.Ambient,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.AudioVisualizer => RgbEffect.AudioVisualizer,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.BreathingColorCycle => RgbEffect.BreathingColorCycle,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Christmas => RgbEffect.Christmas,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Disco => RgbEffect.Disco,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Fade => RgbEffect.Fade,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Lightning => RgbEffect.Lightning,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.RainbowWave => RgbEffect.RainbowWave,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Ripple => RgbEffect.Ripple,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Strobe => RgbEffect.Strobe,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Swipe => RgbEffect.Swipe,
        LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Temperature => RgbEffect.Temperature,
        _ => RgbEffect.Static
    };

    private static LoqNova.Lib.RGBKeyboardBacklightEffect MapToLibEffect(RgbEffect effect) => effect switch
    {
        RgbEffect.Static => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Static,
        RgbEffect.Breath => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Breath,
        RgbEffect.WaveRightToLeft => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.WaveRTL,
        RgbEffect.WaveLeftToRight => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.WaveLTR,
        RgbEffect.Smooth => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Smooth,
        RgbEffect.Ambient => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Ambient,
        RgbEffect.AudioVisualizer => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.AudioVisualizer,
        RgbEffect.BreathingColorCycle => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.BreathingColorCycle,
        RgbEffect.Christmas => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Christmas,
        RgbEffect.Disco => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Disco,
        RgbEffect.Fade => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Fade,
        RgbEffect.Lightning => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Lightning,
        RgbEffect.RainbowWave => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.RainbowWave,
        RgbEffect.Ripple => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Ripple,
        RgbEffect.Strobe => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Strobe,
        RgbEffect.Swipe => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Swipe,
        RgbEffect.Temperature => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Temperature,
        _ => LoqNova.Lib.Enums.RGBKeyboardBacklightEffect.Static
    };

    private static RgbSpeed MapFromLibSpeed(LoqNova.Lib.RGBKeyboardBacklightSpeed speed) => speed switch
    {
        LoqNova.Lib.Enums.RGBKeyboardBacklightSpeed.Slowest => RgbSpeed.Slowest,
        LoqNova.Lib.Enums.RGBKeyboardBacklightSpeed.Slow => RgbSpeed.Slow,
        LoqNova.Lib.Enums.RGBKeyboardBacklightSpeed.Fast => RgbSpeed.Fast,
        LoqNova.Lib.Enums.RGBKeyboardBacklightSpeed.Fastest => RgbSpeed.Fastest,
        _ => RgbSpeed.Fast
    };

    private static LoqNova.Lib.RGBKeyboardBacklightSpeed MapToLibSpeed(RgbSpeed speed) => speed switch
    {
        RgbSpeed.Slowest => LoqNova.Lib.Enums.RGBKeyboardBacklightSpeed.Slowest,
        RgbSpeed.Slow => LoqNova.Lib.Enums.RGBKeyboardBacklightSpeed.Slow,
        RgbSpeed.Fast => LoqNova.Lib.Enums.RGBKeyboardBacklightSpeed.Fast,
        RgbSpeed.Fastest => LoqNova.Lib.Enums.RGBKeyboardBacklightSpeed.Fastest,
        _ => LoqNova.Lib.Enums.RGBKeyboardBacklightSpeed.Fast
    };

    private static RgbBrightness MapFromLibBrightness(LoqNova.Lib.RGBKeyboardBacklightBrightness brightness) => brightness switch
    {
        LoqNova.Lib.Enums.RGBKeyboardBacklightBrightness.Off => RgbBrightness.Off,
        LoqNova.Lib.Enums.RGBKeyboardBacklightBrightness.Low => RgbBrightness.Low,
        LoqNova.Lib.Enums.RGBKeyboardBacklightBrightness.High => RgbBrightness.High,
        _ => RgbBrightness.High
    };

    private static LoqNova.Lib.Enums.RGBKeyboardBacklightBrightness MapToLibBrightness(RgbBrightness brightness) => brightness switch
    {
        RgbBrightness.Off => LoqNova.Lib.Enums.RGBKeyboardBacklightBrightness.Off,
        RgbBrightness.Low => LoqNova.Lib.Enums.RGBKeyboardBacklightBrightness.Low,
        RgbBrightness.High => LoqNova.Lib.Enums.RGBKeyboardBacklightBrightness.High,
        _ => LoqNova.Lib.Enums.RGBKeyboardBacklightBrightness.High
    };
}