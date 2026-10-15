using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Settings;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Applies the theme and accent. This is Avalonia's counterpart to WPF's
/// <c>ThemeManager</c>, which is a WPF-only type: it reads the same real
/// <see cref="ApplicationSettings"/> store and repaints the same resources, using
/// Avalonia's theming API instead of WPF's.
/// <para>
/// The palette itself (surfaces, lines, text, semantic states) lives in
/// <c>Styles/AppResources.axaml</c> under <c>ResourceDictionary.ThemeDictionaries</c>,
/// so selecting Light or Dark here selects a complete, coherent palette rather
/// than a dark palette under light control templates.
/// </para>
/// <para>
/// The accent is the one part of the palette that is not a fixed token: it is
/// user configurable, so it is written into the application resources at runtime.
/// Every accent surface in the app derives from <c>PrimaryColor</c>, and the
/// built-in Fluent controls derive from <c>SystemAccentColor</c>, so both families
/// are repainted together. <c>PrimaryMutedColor</c> (the tint used behind selected
/// rows and preset chips) is mixed against the active theme background, which is
/// why the accent is re-applied whenever the theme variant changes.
/// </para>
/// </summary>
public class ThemeService : IThemeService
{
    private const string DefaultAccent = "#1E9EFF";
    private const string DarkBackground = "#0B0D11";
    private const string LightBackground = "#FFFFFF";

    /// <summary>How much accent is bled into the background to build the muted tint.</summary>
    private const double MutedAccentWeightDark = 0.22;
    private const double MutedAccentWeightLight = 0.12;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    public event Action<AppTheme>? ThemeChanged;

    /// <summary>Raised after the accent resources change, so pickers can follow along.</summary>
    public event Action? AccentApplied;

    public string CurrentAccentColor { get; private set; } = DefaultAccent;

    private readonly ISettingsService _settings;
    private bool _watchingThemeVariant;

    public ThemeService(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task InitializeAsync()
    {
        CurrentTheme = _settings.Theme;
        ApplyTheme(CurrentTheme);
        WatchThemeVariant();

        await ApplyAccentAsync().ConfigureAwait(false);
    }

    public Task SetThemeAsync(AppTheme theme)
    {
        CurrentTheme = theme;
        _settings.Theme = theme;
        ApplyTheme(theme);
        ThemeChanged?.Invoke(theme);
        return Task.CompletedTask;
    }

    public async Task ApplyAccentAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        var settings = IoCContainer.Resolve<ApplicationSettings>();

        CurrentAccentColor = settings.Store.AccentColorSource == AccentColorSource.Custom &&
                             settings.Store.AccentColor is { } custom
            ? $"#{custom.R:X2}{custom.G:X2}{custom.B:X2}"
            : _settings.AccentColor;

        ApplyAccentResources();
        AccentApplied?.Invoke();
    }

    /// <summary>
    /// Writes the accent family into the application resources. Called on startup,
    /// after an accent change and after a theme variant change (the muted tint is
    /// derived from the theme background).
    /// </summary>
    private void ApplyAccentResources()
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var accent = Color.TryParse(CurrentAccentColor, out var parsed) ? parsed : Color.Parse(DefaultAccent);
        var isLight = app.ActualThemeVariant == ThemeVariant.Light;
        var background = Color.Parse(isLight ? LightBackground : DarkBackground);
        var muted = Mix(accent, background, isLight ? MutedAccentWeightLight : MutedAccentWeightDark);

        var resources = app.Resources;

        // The brand accent family used by navigation, buttons, rails and chips.
        resources["PrimaryColor"] = accent;
        resources["PrimaryHoverColor"] = Shift(accent, 0.16);
        resources["PrimaryPressedColor"] = Shift(accent, -0.16);
        resources["PrimaryMutedColor"] = muted;
        resources["AccentColor"] = accent;

        // The Fluent theme paints check boxes, radio buttons, sliders, progress
        // bars, selection highlights and focus visuals from these keys, so the
        // accent has to reach them as well or the app shows two different blues.
        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorLight1"] = Shift(accent, 0.12);
        resources["SystemAccentColorLight2"] = Shift(accent, 0.24);
        resources["SystemAccentColorLight3"] = Shift(accent, 0.36);
        resources["SystemAccentColorDark1"] = Shift(accent, -0.12);
        resources["SystemAccentColorDark2"] = Shift(accent, -0.24);
        resources["SystemAccentColorDark3"] = Shift(accent, -0.36);
    }

    private void WatchThemeVariant()
    {
        if (_watchingThemeVariant || Application.Current is not { } app)
        {
            return;
        }

        _watchingThemeVariant = true;
        app.ActualThemeVariantChanged += (_, _) => ApplyAccentResources();
    }

    private void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        var variant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        Application.Current.RequestedThemeVariant = variant;
    }

    /// <summary>Blends two colours; <paramref name="weight"/> is the share of <paramref name="colour"/>.</summary>
    private static Color Mix(Color colour, Color background, double weight)
    {
        weight = Math.Clamp(weight, 0, 1);
        byte C(byte a, byte b) => (byte)Math.Round(a * weight + b * (1 - weight));

        return Color.FromArgb(255, C(colour.R, background.R), C(colour.G, background.G), C(colour.B, background.B));
    }

    /// <summary>Lightens (positive) or darkens (negative) a colour by a fraction of full scale.</summary>
    private static Color Shift(Color colour, double amount)
    {
        byte C(byte channel)
        {
            var value = amount >= 0
                ? channel + (255 - channel) * amount
                : channel * (1 + amount);
            return (byte)Math.Clamp(Math.Round(value), 0, 255);
        }

        return Color.FromArgb(colour.A, C(colour.R), C(colour.G), C(colour.B));
    }
}
