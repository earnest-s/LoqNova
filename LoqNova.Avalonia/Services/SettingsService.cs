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
    private ApplicationSettings? _settings;

    private ApplicationSettings Settings => _settings ?? throw new InvalidOperationException("SettingsService.InitializeAsync must complete before settings are read.");
    private readonly ILogger<SettingsService> _logger;

    public AppTheme Theme
    {
        get => MapFromLibTheme(Settings.Store.Theme);
        set
        {
            Settings.Store.Theme = MapToLibTheme(value);
            Settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }

    public string AccentColor
    {
        get => Settings.Store.AccentColor?.ToString() ?? "#0078D4";
        set
        {
            if (System.Drawing.ColorTranslator.FromHtml(value) is { } color)
            {
                Settings.Store.AccentColor = new LoqNova.Lib.RGBColor(color.R, color.G, color.B);
                Settings.SynchronizeStore();
                SettingsChanged?.Invoke();
            }
        }
    }

    public bool UseSystemAccent
    {
        get => Settings.Store.AccentColorSource == LoqNova.Lib.AccentColorSource.System;
        set
        {
            Settings.Store.AccentColorSource = value ? LoqNova.Lib.AccentColorSource.System : LoqNova.Lib.AccentColorSource.Custom;
            Settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }

    public string Language { get; set; } = "en";
    public bool TemperatureUnitFahrenheit
    {
        get => Settings.Store.TemperatureUnit == LoqNova.Lib.TemperatureUnit.F;
        set
        {
            Settings.Store.TemperatureUnit = value ? LoqNova.Lib.TemperatureUnit.F : LoqNova.Lib.TemperatureUnit.C;
            Settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }

    public bool AutorunEnabled { get; set; } = false;
    public bool MinimizeToTray
    {
        get => Settings.Store.MinimizeToTray;
        set
        {
            Settings.Store.MinimizeToTray = value;
            Settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }
    
    public bool MinimizeOnClose
    {
        get => Settings.Store.MinimizeOnClose;
        set
        {
            Settings.Store.MinimizeOnClose = value;
            Settings.SynchronizeStore();
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
        get => !Settings.Store.DontShowNotifications;
        set
        {
            Settings.Store.DontShowNotifications = !value;
            Settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }
    
    public bool CliEnabled { get; set; } = false;
    public bool HwInfoEnabled { get; set; } = false;
    public string HwInfoSharedMemoryPath { get; set; } = "";
    public bool SyncBrightnessToAllPowerPlans
    {
        get => Settings.Store.SynchronizeBrightnessToAllPowerPlans;
        set
        {
            Settings.Store.SynchronizeBrightnessToAllPowerPlans = value;
            Settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }
    
    public bool ResetBatteryOnSinceOnReboot
    {
        get => Settings.Store.ResetBatteryOnSinceTimerOnReboot;
        set
        {
            Settings.Store.ResetBatteryOnSinceTimerOnReboot = value;
            Settings.SynchronizeStore();
            SettingsChanged?.Invoke();
        }
    }

    public event Action? SettingsChanged;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        _settings = LoqNova.Lib.IoCContainer.Resolve<ApplicationSettings>();

        try
        {
            await Task.Run(() => Settings.LoadStore()).ConfigureAwait(false);
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
            await Task.Run(() => Settings.SynchronizeStore()).ConfigureAwait(false);
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