using System;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public interface ISettingsService
{
    AppTheme Theme { get; set; }
    string AccentColor { get; set; }
    bool UseSystemAccent { get; set; }
    string Language { get; set; }
    bool TemperatureUnitFahrenheit { get; set; }
    bool AutorunEnabled { get; set; }
    bool MinimizeToTray { get; set; }
    bool MinimizeOnClose { get; set; }
    bool VantageDisabled { get; set; }
    bool LegionZoneDisabled { get; set; }
    bool FnKeysDisabled { get; set; }
    bool SmartFnLockEnabled { get; set; }
    int SmartFnLockModifierKey { get; set; }
    string SmartKeySinglePressAction { get; set; }
    string SmartKeyDoublePressAction { get; set; }
    bool GodModeFnQSwitchable { get; set; }
    bool NotificationsEnabled { get; set; }
    bool CliEnabled { get; set; }
    bool HwInfoEnabled { get; set; }
    string HwInfoSharedMemoryPath { get; set; }
    bool SyncBrightnessToAllPowerPlans { get; set; }
    bool ResetBatteryOnSinceOnReboot { get; set; }
    
    event Action? SettingsChanged;
    
    Task InitializeAsync();
    Task SaveAsync();
}