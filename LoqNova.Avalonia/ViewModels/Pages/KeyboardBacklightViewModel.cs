using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;

namespace LoqNova.Avalonia.ViewModels.Pages;

/// <summary>
/// RGB keyboard backlight, mirroring WPF's <c>RGBKeyboardBacklightControl</c>:
/// every control change writes the whole description of the selected preset through
/// one state write, and the authoritative state is re-read afterwards. The option
/// lists come from the library enums, so no effect, speed or brightness level is
/// invented here.
/// </summary>
public partial class KeyboardBacklightViewModel : ViewModelBase
{
    private readonly IRgbService _rgbService;
    private readonly ISettingsService _settingsService;

    /// <summary>Set while a backend state is being adopted, so it is not written back.</summary>
    private bool _applyingState;

    [ObservableProperty]
    private bool _isRgbKeyboard = true;

    [ObservableProperty]
    private bool _isSpectrumKeyboard;

    [ObservableProperty]
    private RgbPreset _selectedPreset = RgbPreset.Off;

    [ObservableProperty]
    private RgbEffect _selectedEffect = RgbEffect.Static;

    [ObservableProperty]
    private RgbSpeed _selectedSpeed = RgbSpeed.Fast;

    [ObservableProperty]
    private RgbBrightness _selectedBrightness = RgbBrightness.High;

    [ObservableProperty]
    private RgbZoneColor _zone1Color;

    [ObservableProperty]
    private RgbZoneColor _zone2Color;

    [ObservableProperty]
    private RgbZoneColor _zone3Color;

    [ObservableProperty]
    private RgbZoneColor _zone4Color;

    /// <summary>True while Lenovo Vantage is running; WPF disables every control then.</summary>
    [ObservableProperty]
    private bool _isVantageEnabled;

    /// <summary>Backend-reported capability: the current effect exposes a speed.</summary>
    [ObservableProperty]
    private bool _speedEnabled;

    /// <summary>Backend-reported capability: the current effect uses zone colours.</summary>
    [ObservableProperty]
    private bool _zonesEnabled;

    /// <summary>
    /// One combined effect list, derived from the library's effect enum, so firmware
    /// and software effects cannot drift apart again.
    /// </summary>
    public ObservableCollection<RgbEffect> Effects { get; } = BuildEffects();

    public ObservableCollection<RgbSpeed> Speeds { get; } = BuildSpeeds();

    public ObservableCollection<RgbBrightness> BrightnessLevels { get; } = BuildBrightness();

    public KeyboardBacklightViewModel(IRgbService rgbService, ISettingsService settingsService)
    {
        _rgbService = rgbService;
        _settingsService = settingsService;

        SubscribeToEvents();
    }

    /// <summary>
    /// The complete supported effect set, taken from the library enum. Ordering
    /// follows the enum, which lists the firmware effects first and the software
    /// effects after them.
    /// </summary>
    private static ObservableCollection<RgbEffect> BuildEffects()
        =>
        [
            .. Enum.GetValues<RGBKeyboardBacklightEffect>()
                .Select(RgbEffectDisplay.FromLibEffect)
        ];

    private static ObservableCollection<RgbSpeed> BuildSpeeds()
        =>
        [
            .. Enum.GetValues<RGBKeyboardBacklightSpeed>()
                .Select(RgbEffectDisplay.FromLibSpeed)
        ];

    private static ObservableCollection<RgbBrightness> BuildBrightness()
        =>
        [
            .. Enum.GetValues<RGBKeyboardBacklightBrightness>()
                .Select(RgbEffectDisplay.FromLibBrightness)
        ];

    private void SubscribeToEvents()
    {
        _rgbService.PresetChanged += _ => _dispatcher.Post(ApplyStateAsync);
        _rgbService.EffectChanged += _ => _dispatcher.Post(ApplyStateAsync);
        _rgbService.SpeedChanged += _ => _dispatcher.Post(ApplyStateAsync);
        _rgbService.BrightnessChanged += _ => _dispatcher.Post(ApplyStateAsync);
        _rgbService.ZoneColorChanged += (_, _) => _dispatcher.Post(ApplyStateAsync);

        // Live preview: the single frame output the keyboard renders. This is the
        // only path that updates the preview, so firmware commands, custom effects
        // and performance-mode overrides all reach it without a second engine.
        _rgbService.FrameRendered += (z1, z2, z3, z4) => _dispatcher.Post(() =>
        {
            _applyingState = true;
            try
            {
                PreviewZone1 = z1;
                PreviewZone2 = z2;
                PreviewZone3 = z3;
                PreviewZone4 = z4;
            }
            finally
            {
                _applyingState = false;
            }
        });
    }

    // Preview colours are driven by rendered frames rather than by the selected
    // settings, so the preview cannot drift from what the hardware is showing.
    [ObservableProperty] private RgbZoneColor _previewZone1;
    [ObservableProperty] private RgbZoneColor _previewZone2;
    [ObservableProperty] private RgbZoneColor _previewZone3;
    [ObservableProperty] private RgbZoneColor _previewZone4;

    public IMainThreadDispatcherBridge Dispatcher => new Bridge(this);

    private readonly ISettingsService _settings = null!;

    /// <summary>Adopts the authoritative state read back from the controller.</summary>
    public async Task ApplyStateAsync()
    {
        if (!_rgbService.IsSupported)
            return;

        await _rgbService.InitializeAsync();

        _applyingState = true;
        try
        {
            SelectedPreset = _rgbService.CurrentPreset;
            SelectedEffect = _rgbService.CurrentEffect;
            SelectedSpeed = _rgbService.CurrentSpeed;
            SelectedBrightness = _rgbService.CurrentBrightness;
            Zone1Color = _rgbService.Zone1Color;
            Zone2Color = _rgbService.Zone2Color;
            Zone3Color = _rgbService.Zone3Color;
            Zone4Color = _rgbService.Zone4Color;
            IsVantageEnabled = _rgbService.IsVantageEnabled;

            var isOff = SelectedPreset == RgbPreset.Off;

            SpeedEnabled = !isOff && _rgbService.SupportsSpeed(SelectedEffect);
            ZonesEnabled = !isOff && _rgbService.SupportsZoneColors(SelectedEffect);

            // Firmware effects do not emit per-frame callbacks, so the preview shows
            // a static snapshot of the zones until a frame arrives. WPF does the same.
            if (!_rgbService.IsCustomEffect(SelectedEffect) && !isOff)
            {
                PreviewZone1 = Zone1Color;
                PreviewZone2 = Zone2Color;
                PreviewZone3 = Zone3Color;
                PreviewZone4 = Zone4Color;
            }
            else if (isOff)
            {
                var off = new RgbZoneColor(0, 0, 0);
                PreviewZone1 = PreviewZone2 = PreviewZone3 = PreviewZone4 = off;
            }
        }
        finally
        {
            _applyingState = false;
        }

        OnPropertyChanged(nameof(IsInteractive));
    }

    /// <summary>Controls are only interactive when the hardware and Vantage allow it.</summary>
    public bool IsInteractive => _rgbService.IsSupported && !IsVantageEnabled && SelectedPreset != RgbPreset.Off;

    /// <summary>
    /// The single write path, equivalent to WPF's <c>SaveState</c>. Every control
    /// change funnels through here, so a zone edit never resets the effect, speed,
    /// brightness or the other zones, and a firmware effect is written just like a
    /// software effect.
    /// </summary>
    [RelayCommand]
    private async Task SaveStateAsync()
    {
        if (_applyingState || !_rgbService.IsSupported || IsVantageEnabled)
            return;

        if (SelectedPreset == RgbPreset.Off)
            return;

        await _rgbService.SaveStateAsync(
            SelectedEffect, SelectedSpeed, SelectedBrightness,
            Zone1Color, Zone2Color, Zone3Color, Zone4Color);

        await ApplyStateAsync();
    }

    /// <summary>Preset selection, mirroring WPF: switch preset, then re-read everything.</summary>
    [RelayCommand]
    private async Task SetPresetAsync(RgbPreset preset)
    {
        if (!_rgbService.IsSupported)
            return;

        await _rgbService.SetPresetAsync(preset);
        await ApplyStateAsync();
    }

    /// <summary>
    /// "Synchronise zones": an explicit action that applies the given zone's colour to
    /// all four zones in one state write, matching WPF's context-menu item.
    /// </summary>
    [RelayCommand]
    private async Task SynchroniseZonesAsync(int zoneNumber)
    {
        var color = zoneNumber switch
        {
            1 => Zone1Color,
            2 => Zone2Color,
            3 => Zone3Color,
            4 => Zone4Color,
            _ => Zone1Color
        };

        _applyingState = true;
        try
        {
            Zone1Color = color;
            Zone2Color = color;
            Zone3Color = color;
            Zone4Color = color;
        }
        finally
        {
            _applyingState = false;
        }

        await SaveStateAsync();
        await ApplyStateAsync();
    }

    partial void OnSelectedEffectChanged(RgbEffect value)
    {
        SpeedEnabled = _rgbService.SupportsSpeed(value);
        ZonesEnabled = _rgbService.SupportsZoneColors(value);
        _ = SaveStateAsync();
    }

    partial void OnSelectedSpeedChanged(RgbSpeed value) => _ = SaveStateAsync();

    partial void OnSelectedBrightnessChanged(RgbBrightness value) => _ = SaveStateAsync();

    partial void OnZone1ColorChanged(RgbZoneColor value) => _ = SaveStateAsync();

    partial void OnZone2ColorChanged(RgbZoneColor value) => _ = SaveStateAsync();

    partial void OnZone3ColorChanged(RgbZoneColor value) => _ = SaveStateAsync();

    partial void OnZone4ColorChanged(RgbZoneColor value) => _ = SaveStateAsync();

    private sealed class Bridge(KeyboardBacklightViewModel owner)
        : LoqNova.Avalonia.Services.IMainThreadDispatcher
    {
        private readonly LoqNova.Avalonia.Services.IMainThreadDispatcher _inner =
            LoqNova.Lib.IoCContainer.Resolve<LoqNova.Avalonia.Services.IMainThreadDispatcher>();

        public void Post(Action action) => _inner.Post(action);

        public Task InvokeAsync(Action action) => _inner.InvokeAsync(action);

        public Task<T> InvokeAsync<T>(Func<T> func) => _inner.InvokeAsync(func);

        public Task InvokeAsync(Func<Task> func) => _inner.InvokeAsync(func);

        public bool CheckAccess() => _inner.CheckAccess();
    }
}
