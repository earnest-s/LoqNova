using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class ThemeService : IThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.System;
    public event Action<AppTheme>? ThemeChanged;
    
    private readonly ISettingsService _settings;
    
    public ThemeService(ISettingsService settings)
    {
        _settings = settings;
    }
    
    public async Task InitializeAsync()
    {
        CurrentTheme = _settings.Theme;
        ApplyTheme(CurrentTheme);
        await Task.CompletedTask;
    }
    
    public Task SetThemeAsync(AppTheme theme)
    {
        CurrentTheme = theme;
        _settings.Theme = theme;
        ApplyTheme(theme);
        ThemeChanged?.Invoke(theme);
        return Task.CompletedTask;
    }
    
    private void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is not null)
        {
            var variant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
            Application.Current.RequestedThemeVariant = variant;
        }
    }
}