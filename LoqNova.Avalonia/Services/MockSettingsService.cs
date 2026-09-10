using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class MockSettingsService : ISettingsService
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public string AccentColor { get; set; } = "#0078D4";
    public bool UseSystemAccent { get; set; } = true;
    public string Language { get; set; } = "en-US";
    public bool TemperatureUnitFahrenheit { get; set; } = false;
    public bool AutorunEnabled { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool MinimizeOnClose { get; set; } = false;
    public bool VantageDisabled { get; set; } = false;
    public bool LegionZoneDisabled { get; set; } = false;
    public bool FnKeysDisabled { get; set; } = false;
    public bool SmartFnLockEnabled { get; set; } = true;
    public int SmartFnLockModifierKey { get; set; } = 0;
    public string SmartKeySinglePressAction { get; set; } = "OpenDashboard";
    public string SmartKeyDoublePressAction { get; set; } = "TogglePerformanceMode";
    public bool GodModeFnQSwitchable { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool CliEnabled { get; set; } = true;
    public bool HwInfoEnabled { get; set; } = false;
    public string HwInfoSharedMemoryPath { get; set; } = "";
    public bool SyncBrightnessToAllPowerPlans { get; set; } = true;
    public bool ResetBatteryOnSinceOnReboot { get; set; } = true;
    
    public event Action? SettingsChanged;
    
    public Task InitializeAsync() => Task.CompletedTask;
    
    public Task SaveAsync()
    {
        SettingsChanged?.Invoke();
        return Task.CompletedTask;
    }
}