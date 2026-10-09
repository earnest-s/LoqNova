using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    event Action<AppTheme>? ThemeChanged;
    
    Task InitializeAsync();
    Task SetThemeAsync(AppTheme theme);

    /// <summary>
    /// Re-reads the accent colour from the real <c>ApplicationSettings</c> store and
    /// repaints the accent resources. This is Avalonia's counterpart to WPF's
    /// <c>ThemeManager.Apply()</c>, which is a WPF-only type.
    /// </summary>
    Task ApplyAccentAsync();

    /// <summary>
    /// The accent currently in effect, as a hex string. Avalonia's counterpart to WPF's
    /// <c>ThemeManager.GetAccentColor()</c>, which the accent picker uses to seed itself.
    /// </summary>
    string CurrentAccentColor { get; }
}