using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
/// </summary>
public class ThemeService : IThemeService
{
    private const string DefaultAccent = "#1E9EFF";

    public AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    public event Action<AppTheme>? ThemeChanged;

    /// <summary>Raised after the accent resources change, so pickers can follow along.</summary>
    public event Action? AccentApplied;

    public string CurrentAccentColor { get; private set; } = DefaultAccent;

    private readonly ISettingsService _settings;

    public ThemeService(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task InitializeAsync()
    {
        CurrentTheme = _settings.Theme;
        ApplyTheme(CurrentTheme);

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

        if (Application.Current is null)
        {
            return;
        }

        var brush = new SolidColorBrush(Color.Parse(CurrentAccentColor));

        Application.Current.Resources["AccentBrush"] = brush;
        Application.Current.Resources["AccentColor"] = CurrentAccentColor;

        AccentApplied?.Invoke();
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
}