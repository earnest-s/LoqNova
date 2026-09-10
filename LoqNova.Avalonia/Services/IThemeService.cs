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
    event Action<AppTheme> ThemeChanged;
    
    Task InitializeAsync();
    Task SetThemeAsync(AppTheme theme);
}