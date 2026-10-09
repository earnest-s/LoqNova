using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Extensions;
using LoqNova.Lib.Features;
using LoqNova.Lib.Integrations;
using LoqNova.Lib.Settings;
using LoqNova.Lib.SoftwareDisabler;
using LoqNova.Lib.System;
using LoqNova.Lib.System.Management;
using LoqNova.Lib.Utils;

namespace LoqNova.Avalonia.ViewModels.Pages;

/// <summary>
/// The Settings page. This reproduces WPF's <c>SettingsPage.xaml</c> and
/// <c>SettingsPage.xaml.cs</c>: the same cards in the same order, the same options
/// taken from the real enums, the same hardware-driven visibility rules, and the same
/// immediate persistence through <c>ApplicationSettings</c> and the real disablers.
/// Nothing here keeps a local model of a setting: every value is read from and written
/// back to the backend that owns it.
/// </summary>
public partial class SettingsViewModel : ViewModelBase, INavigationAware
{
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly IThemeService _themeService;

    private ApplicationSettings? _applicationSettings;
    private IntegrationsSettings? _integrationsSettings;
    private VantageDisabler? _vantageDisabler;
    private LegionZoneDisabler? _legionZoneDisabler;
    private FnKeysDisabler? _fnKeysDisabler;
    private PowerModeFeature? _powerModeFeature;
    private HWiNFOIntegration? _hwinfoIntegration;

    /// <summary>Mirrors WPF's <c>_isRefreshing</c>: user edits are ignored while loading.</summary>
    private bool _isRefreshing;

    private bool _isPowerModeFeatureSupported;

    // General
    [ObservableProperty]
    private AppTheme _theme;

    [ObservableProperty]
    private AccentColorSource _accentColorSource;

    [ObservableProperty]
    private string _accentColor = "#0078D4";

    [ObservableProperty]
    private TemperatureUnit _temperatureUnit;

    [ObservableProperty]
    private AutorunState _autorunState;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _minimizeOnClose;

    // Software disablers
    [ObservableProperty]
    private bool _isVantageSupported;

    [ObservableProperty]
    private bool _isVantageDisabled;

    [ObservableProperty]
    private bool _isLegionZoneSupported;

    [ObservableProperty]
    private bool _isLegionZoneDisabled;

    [ObservableProperty]
    private bool _isLenovoHotkeysSupported;

    [ObservableProperty]
    private bool _areLenovoHotkeysDisabled;

    [ObservableProperty]
    private ModifierKey _smartFnLockFlags;

    // Update
    [ObservableProperty]
    private bool _isBootLogoSupported;

    // Power
    [ObservableProperty]
    private bool _isGodModeFnQSupported;

    [ObservableProperty]
    private bool _isGodModeFnQEnabled;

    [ObservableProperty]
    private PowerModeMappingMode _powerModeMappingMode;

    [ObservableProperty]
    private bool _isResetBatterySinceEnabled;

    // Integrations
    [ObservableProperty]
    private bool _isHWiNFOEnabled;

    [ObservableProperty]
    private bool _isCliEnabled;

    [ObservableProperty]
    private bool _isCliPathEnabled;

    // In-flight guards, matching WPF's <c>IsEnabled = false</c> around async toggles.
    [ObservableProperty]
    private bool _isVantageBusy;

    [ObservableProperty]
    private bool _isLegionZoneBusy;

    [ObservableProperty]
    private bool _isLenovoHotkeysBusy;

    [ObservableProperty]
    private bool _isGodModeBusy;

    public ObservableCollection<ThemeOption> Themes { get; } = new();

    public ObservableCollection<TemperatureUnitOption> TemperatureUnits { get; } = new();

    public ObservableCollection<AccentColorSourceOption> AccentColorSources { get; } = new();

    public ObservableCollection<AutorunStateOption> AutorunStates { get; } = new();

    public ObservableCollection<ModifierKeyOption> SmartFnLockOptions { get; } = new();

    public ObservableCollection<PowerModeMappingOption> PowerModeMappingOptions { get; } = new();

    public ObservableCollection<LanguageOption> Languages { get; } = new();

    [ObservableProperty]
    private LanguageOption? _language;

    /// <summary>WPF collapses the whole language card when only one language ships.</summary>
    [ObservableProperty]
    private bool _isLanguageSupported = true;

    /// <summary>
    /// WPF reveals the SmartKey, Notifications and ExcludeRefreshRates rows only while
    /// the Lenovo hotkeys are disabled, because those features are what hotkeys trigger.
    /// </summary>
    [ObservableProperty]
    private bool _areSmartKeyFeaturesVisible;

    public SettingsViewModel(
        INotificationService notificationService,
        IDialogService dialogService,
        IThemeService themeService)
    {
        _notificationService = notificationService;
        _dialogService = dialogService;
        _themeService = themeService;

        BuildOptions();
    }

    private void BuildOptions()
    {
        Themes.AddRange(Enum.GetValues<Theme>().Select(v => new ThemeOption(v, v.GetDisplayName())));
        TemperatureUnits.AddRange(Enum.GetValues<TemperatureUnit>()
            .Where(v => v is TemperatureUnit.C or TemperatureUnit.F)
            .Select(v => new TemperatureUnitOption(v, v == TemperatureUnit.C ? "°C" : "°F")));
        AccentColorSources.AddRange(Enum.GetValues<AccentColorSource>()
            .Select(v => new AccentColorSourceOption(v, v.GetDisplayName())));
        AutorunStates.AddRange(Enum.GetValues<AutorunState>()
            .Select(v => new AutorunStateOption(v, v.GetDisplayName())));

        // WPF offers exactly these three, in this order.
        SmartFnLockOptions.Add(new ModifierKeyOption(ModifierKey.None, "Off"));
        SmartFnLockOptions.Add(new ModifierKeyOption(ModifierKey.Alt, ModifierKey.Alt.GetFlagsDisplayName(ModifierKey.None)));
        SmartFnLockOptions.Add(new ModifierKeyOption(
            ModifierKey.Alt | ModifierKey.Ctrl | ModifierKey.Shift,
            (ModifierKey.Alt | ModifierKey.Ctrl | ModifierKey.Shift).GetFlagsDisplayName(ModifierKey.None)));

        PowerModeMappingOptions.AddRange(Enum.GetValues<PowerModeMappingMode>()
            .Select(v => new PowerModeMappingOption(v, v.GetDisplayName())));
    }

    /// <summary>WPF reloads every control whenever the page becomes visible.</summary>
    async Task INavigationAware.OnNavigatedToAsync() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        _isRefreshing = true;

        try
        {
            await LibContainer.Initialization.ConfigureAwait(false);

            _applicationSettings ??= IoCContainer.Resolve<ApplicationSettings>();
            _integrationsSettings ??= IoCContainer.Resolve<IntegrationsSettings>();
            _vantageDisabler ??= IoCContainer.Resolve<VantageDisabler>();
            _legionZoneDisabler ??= IoCContainer.Resolve<LegionZoneDisabler>();
            _fnKeysDisabler ??= IoCContainer.Resolve<FnKeysDisabler>();
            _powerModeFeature ??= IoCContainer.Resolve<PowerModeFeature>();
            _hwinfoIntegration ??= IoCContainer.Resolve<HWiNFOIntegration>();

            var store = _applicationSettings.Store;

            await LoadLanguagesAsync();

            TemperatureUnit = store.TemperatureUnit;
            Theme = MapTheme(store.Theme);
            AccentColorSource = store.AccentColorSource;
            AccentColor = store.AccentColor is { } rgb ? $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}" : "#0078D4";

            AutorunState = Autorun.State;
            MinimizeToTray = store.MinimizeToTray;
            MinimizeOnClose = store.MinimizeOnClose;

            var vantageStatus = await _vantageDisabler.GetStatusAsync().ConfigureAwait(false);
            IsVantageSupported = vantageStatus != SoftwareStatus.NotFound;
            IsVantageDisabled = vantageStatus == SoftwareStatus.Disabled;

            var legionZoneStatus = await _legionZoneDisabler.GetStatusAsync().ConfigureAwait(false);
            IsLegionZoneSupported = legionZoneStatus != SoftwareStatus.NotFound;
            IsLegionZoneDisabled = legionZoneStatus == SoftwareStatus.Disabled;

            var fnKeysStatus = await _fnKeysDisabler.GetStatusAsync().ConfigureAwait(false);
            IsLenovoHotkeysSupported = fnKeysStatus != SoftwareStatus.NotFound;
            AreLenovoHotkeysDisabled = fnKeysStatus == SoftwareStatus.Disabled;

            SmartFnLockFlags = store.SmartFnLockFlags;
            IsResetBatterySinceEnabled = store.ResetBatteryOnSinceTimerOnReboot;

            // The SmartKey, Notifications and ExcludeRefreshRates rows hang off the
            // Lenovo hotkey state, exactly as in WPF.
            AreSmartKeyFeaturesVisible = fnKeysStatus != SoftwareStatus.Enabled;

            IsBootLogoSupported = await BootLogo.IsSupportedAsync().ConfigureAwait(false);

            await LoadGodModeFnQAsync();

            PowerModeMappingMode = store.PowerModeMappingMode;
            _isPowerModeFeatureSupported = await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false);

            IsHWiNFOEnabled = _integrationsSettings.Store.HWiNFO;
            IsCliEnabled = _integrationsSettings.Store.CLI;
            IsCliPathEnabled = SystemPath.HasCLI();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings refresh failed: {ex}");
        }
        finally
        {
            _isRefreshing = false;
        }

        OnPropertyChanged(nameof(ShowPowerModes));
        OnPropertyChanged(nameof(ShowWindowsPowerPlans));
    }

    private async Task LoadLanguagesAsync()
    {
        var languages = LocalizationHelper.SupportedLanguages;
        var current = LocalizationHelper.CurrentLanguageCode;

        Languages.Clear();

        foreach (var code in languages)
        {
            Languages.Add(new LanguageOption(code, LocalizationHelper.LanguageDisplayName(code)));
        }

        IsLanguageSupported = Languages.Count > 1;
        Language = Languages.FirstOrDefault(l => string.Equals(l.Value, current, StringComparison.OrdinalIgnoreCase))
                   ?? Languages.FirstOrDefault();
    }

    private async Task LoadGodModeFnQAsync()
    {
        try
        {
            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

            if (!mi.Features[CapabilityID.GodModeFnQSwitchable])
            {
                IsGodModeFnQSupported = false;
                return;
            }

            IsGodModeFnQSupported = true;
            IsGodModeFnQEnabled =
                await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.GodModeFnQSwitchable).ConfigureAwait(false) == 1;
        }
        catch (Exception ex)
        {
            IsGodModeFnQSupported = false;
            System.Diagnostics.Debug.WriteLine($"Failed to read GodModeFnQSwitchable: {ex}");
        }
    }

    // WPF reveals the power plan and power mode rows based on the mapping mode.
    public bool ShowPowerModes =>
        _isPowerModeFeatureSupported && PowerModeMappingMode == PowerModeMappingMode.WindowsPowerMode;

    public bool ShowWindowsPowerPlans =>
        _isPowerModeFeatureSupported && PowerModeMappingMode == PowerModeMappingMode.WindowsPowerPlan;

    partial void OnPowerModeMappingModeChanged(PowerModeMappingMode value)
    {
        if (_isRefreshing)
        {
            return;
        }

        if (_applicationSettings is not null)
        {
            _applicationSettings.Store.PowerModeMappingMode = value;
            _applicationSettings.SynchronizeStore();
        }

        OnPropertyChanged(nameof(ShowPowerModes));
        OnPropertyChanged(nameof(ShowWindowsPowerPlans));
    }

    partial void OnLanguageChanged(LanguageOption? value)
    {
        if (_isRefreshing || value is null)
        {
            return;
        }

        _ = LocalizationHelper.SetLanguageAsync(value.Value);
    }

    partial void OnTemperatureUnitChanged(TemperatureUnit value)
    {
        if (_isRefreshing || _applicationSettings is null)
        {
            return;
        }

        _applicationSettings.Store.TemperatureUnit = value;
        _applicationSettings.SynchronizeStore();
    }

    partial void OnThemeChanged(AppTheme value)
    {
        if (_isRefreshing)
        {
            return;
        }

        if (_applicationSettings is not null)
        {
            _applicationSettings.Store.Theme = MapLibTheme(value);
            _applicationSettings.SynchronizeStore();
        }

        _ = _themeService.SetThemeAsync(value);
    }

    partial void OnAccentColorSourceChanged(AccentColorSource value)
    {
        if (_isRefreshing)
        {
            return;
        }

        if (_applicationSettings is not null)
        {
            _applicationSettings.Store.AccentColorSource = value;
            _applicationSettings.SynchronizeStore();
        }

        OnPropertyChanged(nameof(IsAccentColorPickerVisible));
        _ = _themeService.ApplyAccentAsync();
    }

    public bool IsAccentColorPickerVisible => AccentColorSource == AccentColorSource.Custom;

    partial void OnAccentColorChanged(string value)
    {
        if (_isRefreshing || _applicationSettings is null)
        {
            return;
        }

        // WPF only stores a custom colour while the source is Custom.
        if (_applicationSettings.Store.AccentColorSource != AccentColorSource.Custom)
        {
            return;
        }

        if (System.Drawing.ColorTranslator.FromHtml(value) is { } color)
        {
            _applicationSettings.Store.AccentColor = new RGBColor(color.R, color.G, color.B);
            _applicationSettings.SynchronizeStore();
        }

        _ = _themeService.ApplyAccentAsync();
    }

    partial void OnAutorunStateChanged(AutorunState value)
    {
        if (_isRefreshing)
        {
            return;
        }

        Autorun.Set(value);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_isRefreshing || _applicationSettings is null)
        {
            return;
        }

        _applicationSettings.Store.MinimizeToTray = value;
        _applicationSettings.SynchronizeStore();
    }

    partial void OnMinimizeOnCloseChanged(bool value)
    {
        if (_isRefreshing || _applicationSettings is null)
        {
            return;
        }

        _applicationSettings.Store.MinimizeOnClose = value;
        _applicationSettings.SynchronizeStore();
    }

    partial void OnSmartFnLockFlagsChanged(ModifierKey value)
    {
        if (_isRefreshing || _applicationSettings is null)
        {
            return;
        }

        _applicationSettings.Store.SmartFnLockFlags = value;
        _applicationSettings.SynchronizeStore();
    }

    partial void OnIsResetBatterySinceEnabledChanged(bool value)
    {
        if (_isRefreshing || _applicationSettings is null)
        {
            return;
        }

        _applicationSettings.Store.ResetBatteryOnSinceTimerOnReboot = value;
        _applicationSettings.SynchronizeStore();
    }

    /// <summary>
    /// WPF disables the Lenovo hotkeys, and the RGB light controller follows because
    /// Lenovo Vantage owns the keyboard backlight.
    /// </summary>
    [RelayCommand]
    private async Task SetLenovoHotkeysAsync(bool disable)
    {
        if (_isRefreshing || _fnKeysDisabler is null)
        {
            return;
        }

        IsLenovoHotkeysBusy = true;

        try
        {
            if (disable)
            {
                await _fnKeysDisabler.DisableAsync().ConfigureAwait(false);
            }
            else
            {
                await _fnKeysDisabler.EnableAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            await _notificationService.ShowAsync(new NotificationMessage(
                NotificationType.Error,
                disable ? "Failed to disable Lenovo Hotkeys" : "Failed to enable Lenovo Hotkeys",
                "An error occurred while trying to manage Lenovo Hotkeys.")).ConfigureAwait(false);

            return;
        }
        finally
        {
            IsLenovoHotkeysBusy = false;
        }

        IsLenovoHotkeysDisabled = disable;

        // WPF shows the SmartKey, Notifications and ExcludeRefreshRates rows only while
        // the hotkeys are disabled.
        AreSmartKeyFeaturesVisible = disable;
    }

    /// <summary>
    /// Disabling Lenovo Vantage transfers keyboard lighting back to the driver, which
    /// WPF does by claiming light control and starting Aurora; re-enabling hands it back.
    /// </summary>
    [RelayCommand]
    private async Task SetVantageAsync(bool disable)
    {
        if (_isRefreshing || _vantageDisabler is null)
        {
            return;
        }

        IsVantageBusy = true;

        try
        {
            if (disable)
            {
                await _vantageDisabler.DisableAsync().ConfigureAwait(false);

                try
                {
                    var rgb = IoCContainer.Resolve<LoqNova.Lib.Controllers.RGBKeyboardBacklightController>();

                    if (await rgb.IsSupportedAsync().ConfigureAwait(false))
                    {
                        await rgb.SetLightControlOwnerAsync(true, true).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Couldn't set light control owner or preset: {ex}");
                }

                await StartAuroraIfNeededAsync().ConfigureAwait(false);
            }
            else
            {
                try
                {
                    var rgb = IoCContainer.Resolve<LoqNova.Lib.Controllers.RGBKeyboardBacklightController>();

                    if (await rgb.IsSupportedAsync().ConfigureAwait(false))
                    {
                        await rgb.SetLightControlOwnerAsync(false).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Couldn't set light control owner: {ex}");
                }

                await StopAuroraIfNeededAsync().ConfigureAwait(false);

                await _vantageDisabler.EnableAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            await _notificationService.ShowAsync(new NotificationMessage(
                NotificationType.Error,
                disable ? "Failed to disable Lenovo Vantage" : "Failed to enable Lenovo Vantage",
                "An error occurred while trying to manage Lenovo Vantage.")).ConfigureAwait(false);

            return;
        }
        finally
        {
            IsVantageBusy = false;
        }

        IsVantageDisabled = disable;
    }

    private static async Task StartAuroraIfNeededAsync()
    {
        try
        {
            if (IoCContainer.TryResolve<LoqNova.Lib.Controllers.SpectrumKeyboardBacklightController>() is { } spectrum &&
                await spectrum.IsSupportedAsync().ConfigureAwait(false))
            {
                await spectrum.StartAuroraIfNeededAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Couldn't start Aurora if needed: {ex}");
        }
    }

    private static async Task StopAuroraIfNeededAsync()
    {
        try
        {
            if (IoCContainer.TryResolve<LoqNova.Lib.Controllers.SpectrumKeyboardBacklightController>() is { } spectrum &&
                await spectrum.IsSupportedAsync().ConfigureAwait(false))
            {
                await spectrum.StopAuroraIfNeededAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Couldn't stop Aurora: {ex}");
        }
    }

    [RelayCommand]
    private async Task SetLegionZoneAsync(bool disable)
    {
        if (_isRefreshing || _legionZoneDisabler is null)
        {
            return;
        }

        IsLegionZoneBusy = true;

        try
        {
            if (disable)
            {
                await _legionZoneDisabler.DisableAsync().ConfigureAwait(false);
            }
            else
            {
                await _legionZoneDisabler.EnableAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            await _notificationService.ShowAsync(new NotificationMessage(
                NotificationType.Error,
                disable ? "Failed to disable Lenovo Legion Zone" : "Failed to enable Lenovo Legion Zone",
                "An error occurred while trying to manage Lenovo Legion Zone.")).ConfigureAwait(false);

            return;
        }
        finally
        {
            IsLegionZoneBusy = false;
        }

        IsLegionZoneDisabled = disable;
    }

    /// <summary>Fn+Q is switched at the firmware level, so it writes straight to the WMI feature.</summary>
    [RelayCommand]
    private async Task SetGodModeFnQAsync(bool enabled)
    {
        if (_isRefreshing)
        {
            return;
        }

        IsGodModeBusy = true;

        try
        {
            await WMI.LenovoOtherMethod
                .SetFeatureValueAsync(CapabilityID.GodModeFnQSwitchable, enabled ? 1 : 0)
                .ConfigureAwait(false);

            IsGodModeFnQEnabled = enabled;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set GodModeFnQSwitchable: {ex}");
        }
        finally
        {
            IsGodModeBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetHWiNFOAsync(bool enabled)
    {
        if (_isRefreshing || _integrationsSettings is null || _hwinfoIntegration is null)
        {
            return;
        }

        _integrationsSettings.Store.HWiNFO = enabled;
        _integrationsSettings.SynchronizeStore();

        await _hwinfoIntegration.StartStopIfNeededAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// WPF also starts or stops its CLI IPC server here. That server is a WPF-only type
    /// (<c>LoqNova.WPF.CLI.IpcServer</c>) with no Avalonia counterpart, so this persists
    /// the real setting but cannot host the server.
    /// </summary>
    [RelayCommand]
    private void SetCliAsync(bool enabled)
    {
        if (_isRefreshing || _integrationsSettings is null)
        {
            return;
        }

        _integrationsSettings.Store.CLI = enabled;
        _integrationsSettings.SynchronizeStore();
    }

    [RelayCommand]
    private void SetCliPath(bool enabled)
    {
        if (_isRefreshing)
        {
            return;
        }

        SystemPath.SetCLI(enabled);
    }

    [RelayCommand]
    private void CheckForUpdates() =>
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/earnest-s/LoqNova/releases",
            UseShellExecute = true
        });

    [RelayCommand]
    private void OpenWindowsPowerPlansControlPanel() =>
        Process.Start("control", "/name", "Microsoft.PowerOptions");

    private static AppTheme MapTheme(Theme theme) => theme switch
    {
        Theme.Light => AppTheme.Light,
        Theme.Dark => AppTheme.Dark,
        _ => AppTheme.System
    };

    private static Theme MapLibTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => Theme.Light,
        AppTheme.Dark => Theme.Dark,
        _ => Theme.System
    };
}

public sealed record ThemeOption(Theme Value, string Label);

public sealed record TemperatureUnitOption(TemperatureUnit Value, string Label);

public sealed record AccentColorSourceOption(AccentColorSource Value, string Label);

public sealed record AutorunStateOption(AutorunState Value, string Label);

public sealed record ModifierKeyOption(ModifierKey Value, string Label);

public sealed record PowerModeMappingOption(PowerModeMappingMode Value, string Label);

public sealed record LanguageOption(string Value, string Label);