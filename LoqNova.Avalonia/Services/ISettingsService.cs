using System;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public interface ISettingsService
{
    AppTheme Theme { get; set; }
    string AccentColor { get; set; }
    bool UseSystemAccent { get; set; }
    bool TemperatureUnitFahrenheit { get; set; }
    bool MinimizeToTray { get; set; }
    bool MinimizeOnClose { get; set; }
    bool NotificationsEnabled { get; set; }
    bool SyncBrightnessToAllPowerPlans { get; set; }
    bool ResetBatteryOnSinceOnReboot { get; set; }
    
    event Action? SettingsChanged;
    
    Task InitializeAsync();
    Task SaveAsync();
}