using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.ViewModels.Pages;

/// <summary>
/// RGB keyboard backlight, mirroring WPF's <c>RGBKeyboardBacklightControl</c>.
/// Every control change funnels through one state write - WPF's <c>SaveState</c> -
/// and the authoritative state is re-read afterwards, so a firmware effect is written
/// exactly like a software effect and a zone edit never resets the other fields.
/// The option lists are derived from the library enums.
/// </summary>
public partial class KeyboardBacklightViewModel : ViewModelBase
{
    private readonly IRgbService _rgbService;
    private readonly IMainThreadDispatcher _dispatcher;

    /// <summary>Set while a backend state is being adopted, so it is not written back.</summary>
    private bool _applyingState;

    [ObservableProperty]
    private bool _isSpectrumKeyboard;

    [ObservableProperty]
    private RgbPreset _selectedPreset = RgbPreset.Off;

    [ObservableProperty]
    private RgbEffect _selectedEffect = RgbEffect.Static;

    [ObservableProperty]
    private RgbSpeed _selectedSpeed = RgbSpeed.Fast;

    /// <summary>The single authoritative brightness state; the preview does not own one.</summary>
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

    /// <summary>Explains why Speed is disabled, so the control is never silently inert.</summary>
    public string? SpeedToolTip => SpeedEnabled
        ? null
        : $"{SelectedEffect} has no adjustable speed.";

    /// <summary>
    /// The zone "Synchronise All Zones" takes its colour from. Chosen by the user
    /// from a zone card, so the single action is deterministic rather than implied.
    /// </summary>
    [ObservableProperty]
    private int _sourceZone = 1;

    public bool IsSourceZone1 => SourceZone == 1;
    public bool IsSourceZone2 => SourceZone == 2;
    public bool IsSourceZone3 => SourceZone == 3;
    public bool IsSourceZone4 => SourceZone == 4;

    partial void OnSourceZoneChanged(int value)
    {
        OnPropertyChanged(nameof(IsSourceZone1));
        OnPropertyChanged(nameof(IsSourceZone2));
        OnPropertyChanged(nameof(IsSourceZone3));
        OnPropertyChanged(nameof(IsSourceZone4));
    }

    /// <summary>Backend-reported capability: the current effect uses per-zone colours.</summary>
    [ObservableProperty]
    private bool _zonesEnabled;

    // Preview colours follow rendered frames, so the preview cannot drift from the
    // hardware. They are separate from the selected settings on purpose.
    [ObservableProperty] private RgbZoneColor _previewZone1;
    [ObservableProperty] private RgbZoneColor _previewZone2;
    [ObservableProperty] private RgbZoneColor _previewZone3;
    [ObservableProperty] private RgbZoneColor _previewZone4;

    /// <summary>One combined effect list, derived from the library effect enum.</summary>
    public ObservableCollection<RgbEffect> Effects { get; } = [.. RgbEffectDisplay.AllEffects()];

    public ObservableCollection<RgbSpeed> Speeds { get; } = [.. RgbEffectDisplay.AllSpeeds()];

    /// <summary>The library exposes exactly Low and High; nothing extra is offered.</summary>
    public ObservableCollection<RgbBrightness> BrightnessLevels { get; } = [.. RgbEffectDisplay.AllBrightness()];

    /// <summary>Controls are interactive only when the hardware allows it and Vantage is not running.</summary>
    public bool IsInteractive => _rgbService.IsSupported
        && !IsVantageEnabled
        && SelectedPreset != RgbPreset.Off;

    /// <summary>
    /// Preset selection stays available while the backlight is Off. WPF re-enables
    /// every preset button after each refresh and only disables the effect, speed,
    /// brightness and zone controls when Off, so the user can always switch back
    /// on. Gating the preset buttons on <see cref="IsInteractive"/> would strand
    /// the user on Off with no way back.
    /// </summary>
    public bool CanSelectPreset => _rgbService.IsSupported && !IsVantageEnabled;

    public KeyboardBacklightViewModel(
        IRgbService rgbService,
        IMainThreadDispatcher dispatcher)
    {
        _rgbService = rgbService;
        _dispatcher = dispatcher;

        SubscribeToEvents();
    }

    private void SubscribeToEvents()
    {
        void Changed() => _dispatcher.Post(async () => await ApplyStateAsync());

        _rgbService.PresetChanged += _ => Changed();
        _rgbService.EffectChanged += _ => Changed();
        _rgbService.SpeedChanged += _ => Changed();
        _rgbService.BrightnessChanged += _ => Changed();
        _rgbService.ZoneColorChanged += (_, _) => Changed();

        // Live preview. This is the library's single frame output, forwarded by the
        // service, so firmware commands, custom effects (including AudioVisualizer)
        // and performance-mode overrides all reach the preview through one path.
        // It may arrive on a background thread, so the update is marshalled.
        _rgbService.FrameRendered += (z1, z2, z3, z4) => _dispatcher.Post(() =>
        {
            PreviewZone1 = z1;
            PreviewZone2 = z2;
            PreviewZone3 = z3;
            PreviewZone4 = z4;
        });
    }

    /// <summary>Hydrates from the authoritative state, mirroring WPF's <c>RefreshAsync</c>.</summary>
    public async Task ApplyStateAsync()
    {
        if (!_rgbService.IsSupported)
            return;

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

            if (isOff)
            {
                // Keyboard off: the preview goes black, as WPF does.
                var black = new RgbZoneColor(0, 0, 0);
                PreviewZone1 = PreviewZone2 = PreviewZone3 = PreviewZone4 = black;
            }
            else if (!_rgbService.IsCustomEffect(SelectedEffect))
            {
                // Firmware effects do not emit per-frame callbacks, so show a static
                // snapshot until the dispatcher produces a frame. WPF does the same.
                PreviewZone1 = Zone1Color;
                PreviewZone2 = Zone2Color;
                PreviewZone3 = Zone3Color;
                PreviewZone4 = Zone4Color;
            }
        }
        finally
        {
            _applyingState = false;
        }

        OnPropertyChanged(nameof(IsInteractive));
    }

    /// <summary>
    /// The single write path, equivalent to WPF's <c>SaveState</c>: the selected
    /// preset's description is replaced as a whole, so the effect, speed, brightness
    /// and all four zones are written together and no other preset is disturbed.
    /// </summary>
    [RelayCommand]
    private async Task SaveStateAsync()
    {
        if (_applyingState || !_rgbService.IsSupported || IsVantageEnabled)
            return;

        // WPF does not write while the Off preset is selected.
        if (SelectedPreset == RgbPreset.Off)
            return;

        await _rgbService.SaveStateAsync(
            SelectedEffect,
            SelectedSpeed,
            SelectedBrightness,
            Zone1Color,
            Zone2Color,
            Zone3Color,
            Zone4Color);

        await ApplyStateAsync();
    }

    /// <summary>Preset selection: switch preset, then re-read every displayed field.</summary>
    [RelayCommand]
    private async Task SetPresetAsync(RgbPreset preset)
    {
        if (!_rgbService.IsSupported)
            return;

        await _rgbService.SetPresetAsync(preset);
        await ApplyStateAsync();
    }

    /// <summary>Chooses which zone card feeds the single synchronise action.</summary>
    [RelayCommand]
    public void SelectSourceZone(int zoneNumber) => SourceZone = zoneNumber;

    /// <summary>
    /// The one synchronisation action. Applies the source zone's colour to all four
    /// zones in a single state write, then re-reads, so the keyboard, the
    /// authoritative state and the zone cards agree. This reproduces WPF's
    /// "synchronise zones" behaviour without duplicating a control per zone.
    /// </summary>
    [RelayCommand]
    public async Task SynchroniseAllZonesAsync() => await SynchroniseZonesAsync(SourceZone);

    private async Task SynchroniseZonesAsync(int zoneNumber)
    {
        var color = zoneNumber switch
        {
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

    partial void OnIsVantageEnabledChanged(bool value) => OnPropertyChanged(nameof(IsInteractive));

    partial void OnSelectedPresetChanged(RgbPreset value) => OnPropertyChanged(nameof(IsInteractive));
}
