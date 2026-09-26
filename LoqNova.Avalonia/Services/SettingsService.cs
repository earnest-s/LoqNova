using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Settings;
using LoqNova.Lib.Utils;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

public class SettingsService : ISettingsService
{
    private readonly ApplicationSettings _settings;
    private readonly ILogger<SettingsService> _logger;

    public AppTheme Theme
    {
        get => MapFromLibTheme(_settings.Store.Theme);
        set
        {
            _settings.Store.Theme = MapToLibTheme(value);
            _settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }

    public string AccentColor
    {
        get => _settings.Store.AccentColor?.ToString() ?? "#0078D4";
        set
        {
            if (System.Drawing.ColorTranslator.FromHtml(value) is { } color)
            {
                _settings.Store.AccentColor = new LoqNova.Lib.RGBColor(color.R, color.G, color.B);
                _settings.SynchronizeStore();
                SettingsChanged?.Invoke();
            }
        }
    }

    public bool UseSystemAccent
    {
        get => _settings.Store.AccentColorSource == LoqNova.Lib.AccentColorSource.System;
        set
        {
            _settings.Store.AccentColorSource = value ? LoqNova.Lib.AccentColorSource.System : LoqNova.Lib.AccentColorSource.Custom;
            _settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }

    public string Language { get; set; } = "en";
    public bool TemperatureUnitFahrenheit
    {
        get => _settings.Store.TemperatureUnit == LoqNova.Lib.TemperatureUnit.F;
        set
        {
            _settings.Store.TemperatureUnit = value ? LoqNova.Lib.TemperatureUnit.F : LoqNova.Lib.TemperatureUnit.C;
            _settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }

    public bool AutorunEnabled { get; set; } = false;
    public bool MinimizeToTray
    {
        get => _settings.Store.MinimizeToTray;
        set
        {
            _settings.Store.MinimizeToTray = value;
            _settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }
    
    public bool MinimizeOnClose
    {
        get => _settings.Store.MinimizeOnClose;
        set
        {
            _settings.Store.MinimizeOnClose = value;
            _settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }

    public bool VantageDisabled { get; set; } = false;
    public bool LegionZoneDisabled { get; set; } = false;
    public bool FnKeysDisabled { get; set; } = false;
    public bool SmartFnLockEnabled { get; set; } = false;
    public int SmartFnLockModifierKey { get; set; } = 0;
    public string SmartKeySinglePressAction { get; set; } = "";
    public string SmartKeyDoublePressAction { get; set; } = "";
    public bool GodModeFnQSwitchable { get; set; } = false;
    public bool NotificationsEnabled 
    { 
        get => !_settings.Store.DontShowNotifications;
        set
        {
            _settings.Store.DontShowNotifications = !value;
            _settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }
    
    public bool CliEnabled { get; set; } = false;
    public bool HwInfoEnabled { get; set; } = false;
    public string HwInfoSharedMemoryPath { get; set; } = "";
    public bool SyncBrightnessToAllPowerPlans
    {
        get => _settings.Store.SynchronizeBrightnessToAllPowerPlans;
        set
        {
            _settings.Store.SynchronizeBrightnessToAllPowerPlans = value;
            _settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }
    
    public bool ResetBatteryOnSinceOnReboot
    {
        get => _settings.Store.ResetBatteryOnSinceTimerOnReboot;
        set
        {
            _settings.Store.ResetBatteryOnSinceTimerOnReboot = value;
            _settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }

    public event Action? SettingsChanged;

    public SettingsService(ApplicationSettings settings, ILogger<SettingsService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await Task.Run(() => _settings.LoadStore()).ConfigureAwait(false);
            _logger.LogInformation("Settings service initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize settings service");
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            await Task.Run(() => _settings.SynchronizeStore()).ConfigureAwait(false);
            _logger.LogInformation("Settings saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
        }
    }

    private static AppTheme MapFromLibTheme(LoqNova.Lib.Theme theme) => theme switch
    {
        LoqNova.Lib.Theme.System => AppTheme.System,
        LoqNova.Lib.Theme.Light => AppTheme.Light,
        LoqNova.Lib.Theme.Dark => AppTheme.Dark,
        _ => AppTheme.System
    };

    private static LoqNova.Lib.Theme MapToLibTheme(AppTheme theme) => theme switch
    {
        AppTheme.System => LoqNova.Lib.Theme.System,
        AppTheme.Light => LoqNova.Lib.Theme.Light,
        AppTheme.Dark => LoqNova.Lib.Theme.Dark,
        _ => LoqNova.Lib.Theme.System
    };
}