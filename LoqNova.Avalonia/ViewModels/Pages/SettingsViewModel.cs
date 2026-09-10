using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.ViewModels.Pages;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IPerformanceService _performanceService;
    private readonly IRgbService _rgbService;
    private readonly INotificationService _notificationService;
    private readonly IFileDialogService _fileDialogService;
    
    // General
    [ObservableProperty]
    private AppTheme _theme = AppTheme.System;
    
    [ObservableProperty]
    private string _accentColor = "#0078D4";
    
    [ObservableProperty]
    private bool _useSystemAccent = true;
    
    [ObservableProperty]
    private string _language = "en-US";
    
    [ObservableProperty]
    private bool _temperatureUnitFahrenheit = false;
    
    [ObservableProperty]
    private bool _autorunEnabled = true;
    
    [ObservableProperty]
    private bool _minimizeToTray = true;
    
    [ObservableProperty]
    private bool _minimizeOnClose = false;
    
    // Device Integration
    [ObservableProperty]
    private bool _vantageDisabled = false;
    
    [ObservableProperty]
    private bool _legionZoneDisabled = false;
    
    [ObservableProperty]
    private bool _fnKeysDisabled = false;
    
    [ObservableProperty]
    private bool _smartFnLockEnabled = true;
    
    [ObservableProperty]
    private int _smartFnLockModifierKey = 0;
    
    [ObservableProperty]
    private string _smartKeySinglePressAction = "OpenDashboard";
    
    [ObservableProperty]
    private string _smartKeyDoublePressAction = "TogglePerformanceMode";
    
    [ObservableProperty]
    private bool _godModeFnQSwitchable = true;
    
    // Notifications
    [ObservableProperty]
    private bool _notificationsEnabled = true;
    
    // Integrations
    [ObservableProperty]
    private bool _cliEnabled = true;
    
    [ObservableProperty]
    private bool _hwInfoEnabled = false;
    
    [ObservableProperty]
    private string _hwInfoSharedMemoryPath = "";
    
    // System
    [ObservableProperty]
    private bool _syncBrightnessToAllPowerPlans = true;
    
    [ObservableProperty]
    private bool _resetBatteryOnSinceOnReboot = true;
    
    public ObservableCollection<AppTheme> Themes { get; } = new()
    {
        AppTheme.System, AppTheme.Light, AppTheme.Dark
    };
    
    public ObservableCollection<string> Languages { get; } = new()
    {
        "en-US", "zh-CN", "zh-TW", "ja-JP", "ko-KR", "de-DE", "fr-FR", "es-ES", "ru-RU"
    };
    
    public ObservableCollection<string> SmartFnLockKeys { get; } = new()
    {
        "Fn", "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt", "LeftShift", "RightShift"
    };
    
    public ObservableCollection<string> SmartKeyActions { get; } = new()
    {
        "OpenDashboard", "TogglePerformanceMode", "ToggleRGB", "ToggleFan", "OpenKeyboard",
        "OpenBattery", "OpenAutomation", "OpenMacros", "OpenPackages", "OpenSettings",
        "ToggleMicrophone", "ToggleTouchpad", "ToggleWinKey", "ToggleHDR"
    };
    
    public SettingsViewModel(
        ISettingsService settingsService,
        IPerformanceService performanceService,
        IRgbService rgbService,
        INotificationService notificationService,
        IFileDialogService fileDialogService)
    {
        _settingsService = settingsService;
        _performanceService = performanceService;
        _rgbService = rgbService;
        _notificationService = notificationService;
        _fileDialogService = fileDialogService;
        
        LoadSettings();
    }
    
    private void LoadSettings()
    {
        Theme = _settingsService.Theme;
        AccentColor = _settingsService.AccentColor;
        UseSystemAccent = _settingsService.UseSystemAccent;
        Language = _settingsService.Language;
        TemperatureUnitFahrenheit = _settingsService.TemperatureUnitFahrenheit;
        AutorunEnabled = _settingsService.AutorunEnabled;
        MinimizeToTray = _settingsService.MinimizeToTray;
        MinimizeOnClose = _settingsService.MinimizeOnClose;
        VantageDisabled = _settingsService.VantageDisabled;
        LegionZoneDisabled = _settingsService.LegionZoneDisabled;
        FnKeysDisabled = _settingsService.FnKeysDisabled;
        SmartFnLockEnabled = _settingsService.SmartFnLockEnabled;
        SmartFnLockModifierKey = _settingsService.SmartFnLockModifierKey;
        SmartKeySinglePressAction = _settingsService.SmartKeySinglePressAction;
        SmartKeyDoublePressAction = _settingsService.SmartKeyDoublePressAction;
        GodModeFnQSwitchable = _settingsService.GodModeFnQSwitchable;
        NotificationsEnabled = _settingsService.NotificationsEnabled;
        CliEnabled = _settingsService.CliEnabled;
        HwInfoEnabled = _settingsService.HwInfoEnabled;
        HwInfoSharedMemoryPath = _settingsService.HwInfoSharedMemoryPath;
        SyncBrightnessToAllPowerPlans = _settingsService.SyncBrightnessToAllPowerPlans;
        ResetBatteryOnSinceOnReboot = _settingsService.ResetBatteryOnSinceOnReboot;
    }
    
    partial void OnThemeChanged(AppTheme value)
    {
        _settingsService.Theme = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnAccentColorChanged(string value)
    {
        _settingsService.AccentColor = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnUseSystemAccentChanged(bool value)
    {
        _settingsService.UseSystemAccent = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnLanguageChanged(string value)
    {
        _settingsService.Language = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnTemperatureUnitFahrenheitChanged(bool value)
    {
        _settingsService.TemperatureUnitFahrenheit = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnAutorunEnabledChanged(bool value)
    {
        _settingsService.AutorunEnabled = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnMinimizeToTrayChanged(bool value)
    {
        _settingsService.MinimizeToTray = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnMinimizeOnCloseChanged(bool value)
    {
        _settingsService.MinimizeOnClose = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnVantageDisabledChanged(bool value)
    {
        _settingsService.VantageDisabled = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnLegionZoneDisabledChanged(bool value)
    {
        _settingsService.LegionZoneDisabled = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnFnKeysDisabledChanged(bool value)
    {
        _settingsService.FnKeysDisabled = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnSmartFnLockEnabledChanged(bool value)
    {
        _settingsService.SmartFnLockEnabled = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnSmartFnLockModifierKeyChanged(int value)
    {
        _settingsService.SmartFnLockModifierKey = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnSmartKeySinglePressActionChanged(string value)
    {
        _settingsService.SmartKeySinglePressAction = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnSmartKeyDoublePressActionChanged(string value)
    {
        _settingsService.SmartKeyDoublePressAction = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnGodModeFnQSwitchableChanged(bool value)
    {
        _settingsService.GodModeFnQSwitchable = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnNotificationsEnabledChanged(bool value)
    {
        _settingsService.NotificationsEnabled = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnCliEnabledChanged(bool value)
    {
        _settingsService.CliEnabled = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnHwInfoEnabledChanged(bool value)
    {
        _settingsService.HwInfoEnabled = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnHwInfoSharedMemoryPathChanged(string value)
    {
        _settingsService.HwInfoSharedMemoryPath = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnSyncBrightnessToAllPowerPlansChanged(bool value)
    {
        _settingsService.SyncBrightnessToAllPowerPlans = value;
        _ = _settingsService.SaveAsync();
    }
    
    partial void OnResetBatteryOnSinceOnRebootChanged(bool value)
    {
        _settingsService.ResetBatteryOnSinceOnReboot = value;
        _ = _settingsService.SaveAsync();
    }
    
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        // Trigger update check
    }
    
    [RelayCommand]
    private async Task OpenLogFolderAsync()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\LOQNova";
        await _fileDialogService.ShowFolderBrowserDialogAsync("Open Log Folder", path);
    }
    
    [RelayCommand]
    private async Task OpenBootLogoAsync()
    {
        // Open boot logo dialog
    }
    
    [RelayCommand]
    private async Task OpenWindowsPowerModesAsync()
    {
        // Open Windows power modes dialog
    }
    
    [RelayCommand]
    private async Task OpenWindowsPowerPlansAsync()
    {
        // Open Windows power plans dialog
    }
    
    [RelayCommand]
    private async Task OpenExcludeRefreshRatesAsync()
    {
        // Open exclude refresh rates dialog
    }
    
    [RelayCommand]
    private async Task OpenNotificationsSettingsAsync()
    {
        // Open notifications settings dialog
    }
    
    [RelayCommand]
    private async Task OpenSmartKeyPipelinesAsync()
    {
        // Open smart key pipelines dialog
    }
}