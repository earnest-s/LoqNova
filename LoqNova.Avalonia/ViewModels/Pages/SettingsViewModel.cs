using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Localization;
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
using IMainThreadDispatcher = LoqNova.Avalonia.Services.IMainThreadDispatcher;

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
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly MainWindowViewModel _mainWindowViewModel;

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
    private Color _selectedAccentColor = Color.Parse("#0078D4");

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

    [ObservableProperty]
    private bool _syncBrightnessToAllPowerPlans;

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
        IThemeService themeService,
        IMainThreadDispatcher dispatcher,
        MainWindowViewModel mainWindowViewModel)
    {
        _notificationService = notificationService;
        _dialogService = dialogService;
        _themeService = themeService;
        _dispatcher = dispatcher;
        _mainWindowViewModel = mainWindowViewModel;

        BuildOptions();
    }

    private void BuildOptions()
    {
        // The combo box binds SelectedValue to the page's AppTheme property, so the
        // option values must be AppTheme too (the labels still come from Lib's Theme
        // display names, so they match WPF exactly).
        foreach (var value in Enum.GetValues<AppTheme>())
        {
            Themes.Add(new ThemeOption(value, MapLibTheme(value).GetDisplayName()));
        }

        foreach (var value in Enum.GetValues<TemperatureUnit>())
        {
            if (value is TemperatureUnit.C or TemperatureUnit.F)
            {
                TemperatureUnits.Add(new TemperatureUnitOption(value, value == TemperatureUnit.C ? "°C" : "°F"));
            }
        }

        foreach (var value in Enum.GetValues<AccentColorSource>())
        {
            AccentColorSources.Add(new AccentColorSourceOption(value, value.GetDisplayName()));
        }

        foreach (var value in Enum.GetValues<AutorunState>())
        {
            AutorunStates.Add(new AutorunStateOption(value, value.GetDisplayName()));
        }

        // WPF offers exactly these three, in this order.
        SmartFnLockOptions.Add(new ModifierKeyOption(ModifierKey.None, "Off"));
        SmartFnLockOptions.Add(new ModifierKeyOption(
            ModifierKey.Alt, ModifierKey.Alt.GetFlagsDisplayName(ModifierKey.None)));
        SmartFnLockOptions.Add(new ModifierKeyOption(
            ModifierKey.Alt | ModifierKey.Ctrl | ModifierKey.Shift,
            (ModifierKey.Alt | ModifierKey.Ctrl | ModifierKey.Shift).GetFlagsDisplayName(ModifierKey.None)));

        foreach (var value in Enum.GetValues<PowerModeMappingMode>())
        {
            PowerModeMappingOptions.Add(new PowerModeMappingOption(value, value.GetDisplayName()));
        }
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

            // Resolving the controllers above and reading their status can query WMI, so
            // the reads are gathered off the UI thread first and every bound property is
            // only assigned on the UI thread afterwards, the same way the dashboard does.
            var temperatureUnit = store.TemperatureUnit;
            var theme = MapTheme(store.Theme);
            var accentSource = store.AccentColorSource;
            var accentColor = ParseAccentColor(_themeService.CurrentAccentColor);

            // WPF assigns the file-backed settings before it touches any hardware/OS probe,
            // so a failing probe can never stop the user's own settings from being shown.
            // Keep that guarantee: every probe below is fault-tolerant and falls back to a
            // safe value, otherwise a single WMI/software-disabler failure would leave the
            // whole page at its view-model defaults (so the page would disagree with the
            // store and later writes could become no-ops).
            var autorunState = TryRead(() => Autorun.State, AutorunState.Disabled, "Autorun state");
            var minimizeToTray = store.MinimizeToTray;
            var minimizeOnClose = store.MinimizeOnClose;

            var vantageStatus = await TryReadAsync(_vantageDisabler.GetStatusAsync, SoftwareStatus.NotFound, "Vantage status").ConfigureAwait(false);
            var legionZoneStatus = await TryReadAsync(_legionZoneDisabler.GetStatusAsync, SoftwareStatus.NotFound, "Legion Zone status").ConfigureAwait(false);
            var fnKeysStatus = await TryReadAsync(_fnKeysDisabler.GetStatusAsync, SoftwareStatus.NotFound, "Lenovo hotkeys status").ConfigureAwait(false);

            var smartFnLockFlags = store.SmartFnLockFlags;
            var syncBrightness = store.SynchronizeBrightnessToAllPowerPlans;
            var resetBatterySince = store.ResetBatteryOnSinceTimerOnReboot;
            var bootLogoSupported = await TryReadAsync(BootLogo.IsSupportedAsync, false, "Boot logo support").ConfigureAwait(false);
            var godMode = await TryReadAsync(ReadGodModeFnQAsync, (Supported: false, Enabled: false), "GodMode Fn+Q status").ConfigureAwait(false);
            var powerModeMappingMode = store.PowerModeMappingMode;
            var powerModeSupported = await TryReadAsync(_powerModeFeature.IsSupportedAsync, false, "Power mode support").ConfigureAwait(false);
            var hwinfo = _integrationsSettings.Store.HWiNFO;
            var cli = _integrationsSettings.Store.CLI;
            var cliPath = TryRead(() => SystemPath.HasCLI(), false, "CLI PATH check");

            await _dispatcher.InvokeAsync(() =>
            {
                LoadLanguages();
                TemperatureUnit = temperatureUnit;
                Theme = theme;
                AccentColorSource = accentSource;
                SelectedAccentColor = accentColor;

                AutorunState = autorunState;
                MinimizeToTray = minimizeToTray;
                MinimizeOnClose = minimizeOnClose;

                IsVantageSupported = vantageStatus != SoftwareStatus.NotFound;
                IsVantageDisabled = vantageStatus == SoftwareStatus.Disabled;

                IsLegionZoneSupported = legionZoneStatus != SoftwareStatus.NotFound;
                IsLegionZoneDisabled = legionZoneStatus == SoftwareStatus.Disabled;

                IsLenovoHotkeysSupported = fnKeysStatus != SoftwareStatus.NotFound;
                AreLenovoHotkeysDisabled = fnKeysStatus == SoftwareStatus.Disabled;

                SmartFnLockFlags = smartFnLockFlags;
                SyncBrightnessToAllPowerPlans = syncBrightness;
                IsResetBatterySinceEnabled = resetBatterySince;

                // The SmartKey, Notifications and ExcludeRefreshRates rows hang off the
                // Lenovo hotkey state, exactly as in WPF.
                AreSmartKeyFeaturesVisible = fnKeysStatus != SoftwareStatus.Enabled;

                IsBootLogoSupported = bootLogoSupported;

                IsGodModeFnQSupported = godMode.Supported;
                IsGodModeFnQEnabled = godMode.Enabled;

                PowerModeMappingMode = powerModeMappingMode;
                _isPowerModeFeatureSupported = powerModeSupported;

                IsHWiNFOEnabled = hwinfo;
                IsCliEnabled = cli;
                IsCliPathEnabled = cliPath;

                OnPropertyChanged(nameof(ShowPowerModes));
                OnPropertyChanged(nameof(ShowWindowsPowerPlans));
                OnPropertyChanged(nameof(IsPowerModeMappingSupported));
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings refresh failed: {ex}");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// Runs a hardware/OS probe that is allowed to fail. WPF reads these same values and
    /// lets an exception escape (breaking the whole refresh); Avalonia traps each one so a
    /// missing device or unavailable service only hides its own card instead of the page.
    /// </summary>
    private static async Task<T> TryReadAsync<T>(Func<Task<T>> read, T fallback, string what)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{what} could not be read: {ex}");
            return fallback;
        }
    }

    private static T TryRead<T>(Func<T> read, T fallback, string what)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{what} could not be read: {ex}");
            return fallback;
        }
    }

    private void LoadLanguages()
    {
        var languages = LocalizationHelper.SupportedLanguages;
        var current = LocalizationHelper.CurrentLanguageCode;

        Languages.Clear();

        // WPF orders the list by display name using the invariant comparer.
        foreach (var code in languages.OrderBy(LocalizationHelper.LanguageDisplayName, StringComparer.InvariantCultureIgnoreCase))
        {
            Languages.Add(new LanguageOption(code, LocalizationHelper.LanguageDisplayName(code)));
        }

        IsLanguageSupported = Languages.Count > 1;
        Language = Languages.FirstOrDefault(l => string.Equals(l.Value, current, StringComparison.OrdinalIgnoreCase))
                   ?? Languages.FirstOrDefault();
    }

    /// <summary>
    /// Reads the GodModeFnQ capability off the UI thread and returns it, so the caller can
    /// assign the bound properties on the UI thread. WPF reads the same capability and
    /// hides the card when the firmware does not report it.
    /// </summary>
    private static async Task<(bool Supported, bool Enabled)> ReadGodModeFnQAsync()
    {
        try
        {
            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

            if (!mi.Features[CapabilityID.GodModeFnQSwitchable])
            {
                return (false, false);
            }

            var enabled = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.GodModeFnQSwitchable).ConfigureAwait(false) == 1;
            return (true, enabled);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read GodModeFnQSwitchable: {ex}");
            return (false, false);
        }
    }

    // WPF reveals the power plan and power mode rows based on the mapping mode.
    public bool ShowPowerModes =>
        _isPowerModeFeatureSupported && PowerModeMappingMode == PowerModeMappingMode.WindowsPowerMode;

    public bool ShowWindowsPowerPlans =>
        _isPowerModeFeatureSupported && PowerModeMappingMode == PowerModeMappingMode.WindowsPowerPlan;

    /// <summary>WPF hides the whole power-mode mapping card when the feature is unsupported.</summary>
    public bool IsPowerModeMappingSupported => _isPowerModeFeatureSupported;

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
        OnPropertyChanged(nameof(IsPowerModeMappingSupported));
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

    partial void OnSelectedAccentColorChanged(Color value)
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

        _applicationSettings.Store.AccentColor = new RGBColor(value.R, value.G, value.B);
        _applicationSettings.SynchronizeStore();

        _ = _themeService.ApplyAccentAsync();
    }

    private static Color ParseAccentColor(string value) =>
        Color.TryParse(value, out var color) ? color : Color.Parse("#0078D4");

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

    partial void OnSyncBrightnessToAllPowerPlansChanged(bool value)
    {
        if (_isRefreshing || _applicationSettings is null)
        {
            return;
        }

        _applicationSettings.Store.SynchronizeBrightnessToAllPowerPlans = value;
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
        var applied = false;

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

            applied = true;
        }
        catch
        {
            await _notificationService.ShowAsync(new NotificationMessage(
                Services.NotificationType.Error,
                disable
                    ? LocalizationHelper.GetString("SettingsPage_DisableLenovoHotkeys_Error_Title")
                    : LocalizationHelper.GetString("SettingsPage_EnableLenovoHotkeys_Error_Title"),
                disable
                    ? LocalizationHelper.GetString("SettingsPage_DisableLenovoHotkeys_Error_Message")
                    : LocalizationHelper.GetString("SettingsPage_EnableLenovoHotkeys_Error_Message"))).ConfigureAwait(false);
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsLenovoHotkeysBusy = false;

            if (applied)
            {
                AreLenovoHotkeysDisabled = disable;

                // WPF shows the SmartKey, Notifications and ExcludeRefreshRates rows only
                // while the hotkeys are disabled.
                AreSmartKeyFeaturesVisible = disable;
            }
        }).ConfigureAwait(false);
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
        var applied = false;

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

            applied = true;
        }
        catch
        {
            await _notificationService.ShowAsync(new NotificationMessage(
                Services.NotificationType.Error,
                disable
                    ? LocalizationHelper.GetString("SettingsPage_DisableVantage_Error_Title")
                    : LocalizationHelper.GetString("SettingsPage_EnableVantage_Error_Title"),
                disable
                    ? LocalizationHelper.GetString("SettingsPage_DisableVantage_Error_Message")
                    : LocalizationHelper.GetString("SettingsPage_EnableVantage_Error_Message"))).ConfigureAwait(false);
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsVantageBusy = false;

            if (applied)
            {
                IsVantageDisabled = disable;
            }
        }).ConfigureAwait(false);
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
        var applied = false;

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

            applied = true;
        }
        catch
        {
            await _notificationService.ShowAsync(new NotificationMessage(
                Services.NotificationType.Error,
                disable
                    ? LocalizationHelper.GetString("SettingsPage_DisableLegionZone_Error_Title")
                    : LocalizationHelper.GetString("SettingsPage_EnableLegionZone_Error_Title"),
                disable
                    ? LocalizationHelper.GetString("SettingsPage_DisableLegionZone_Error_Message")
                    : LocalizationHelper.GetString("SettingsPage_EnableLegionZone_Error_Message"))).ConfigureAwait(false);
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsLegionZoneBusy = false;

            if (applied)
            {
                IsLegionZoneDisabled = disable;
            }
        }).ConfigureAwait(false);
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
        var applied = false;

        try
        {
            await WMI.LenovoOtherMethod
                .SetFeatureValueAsync(CapabilityID.GodModeFnQSwitchable, enabled ? 1 : 0)
                .ConfigureAwait(false);

            applied = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set GodModeFnQSwitchable: {ex}");
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsGodModeBusy = false;

            if (applied)
            {
                IsGodModeFnQEnabled = enabled;
            }
        }).ConfigureAwait(false);
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
    /// Starts or stops the command-line IPC server, exactly as WPF's
    /// <c>CLIInterfaceToggle_Click</c> does. The server lives in the shared container,
    /// so the instance started at launch is the same one toggled here.
    /// </summary>
    [RelayCommand]
    private async Task SetCliAsync(bool enabled)
    {
        if (_isRefreshing || _integrationsSettings is null)
        {
            return;
        }

        _integrationsSettings.Store.CLI = enabled;
        _integrationsSettings.SynchronizeStore();

        if (IoCContainer.TryResolve<LoqNova.Avalonia.CLI.IpcServer>() is { } ipcServer)
        {
            await ipcServer.StartStopIfNeededAsync().ConfigureAwait(false);
        }
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
        Process.Start(new ProcessStartInfo("control", "/name Microsoft.PowerOptions"));

    // WPF opens these six as separate Windows. Avalonia presents them as overlay dialogs
    // through the shared DialogService, resolving a fresh dialog each time so a previous
    // session's state never leaks into the next opening.
    [RelayCommand]
    private async Task OpenSmartKeyPipelines(string isDoublePress)
    {
        var dialog = _dialogService.Resolve<Dialogs.SelectSmartKeyPipelinesViewModel>();
        dialog.IsDoublePress = bool.TryParse(isDoublePress, out var doublePress) && doublePress;
        await _mainWindowViewModel.ShowDialogAsync(dialog);
    }

    [RelayCommand]
    private async Task OpenNotifications()
    {
        var dialog = _dialogService.Resolve<Dialogs.NotificationsSettingsViewModel>();
        await _mainWindowViewModel.ShowDialogAsync(dialog);
    }

    [RelayCommand]
    private async Task OpenExcludeRefreshRates()
    {
        var dialog = _dialogService.Resolve<Dialogs.ExcludeRefreshRatesViewModel>();
        await _mainWindowViewModel.ShowDialogAsync(dialog);
    }

    [RelayCommand]
    private async Task OpenBootLogo()
    {
        var dialog = _dialogService.Resolve<Dialogs.BootLogoViewModel>();
        await _mainWindowViewModel.ShowDialogAsync(dialog);
    }

    [RelayCommand]
    private async Task OpenWindowsPowerModes()
    {
        var dialog = _dialogService.Resolve<Dialogs.WindowsPowerModesViewModel>();
        await _mainWindowViewModel.ShowDialogAsync(dialog);
    }

    [RelayCommand]
    private async Task OpenWindowsPowerPlans()
    {
        var dialog = _dialogService.Resolve<Dialogs.WindowsPowerPlansViewModel>();
        await _mainWindowViewModel.ShowDialogAsync(dialog);
    }

    private static AppTheme MapTheme(LoqNova.Lib.Theme theme) => theme switch
    {
        LoqNova.Lib.Theme.Light => AppTheme.Light,
        LoqNova.Lib.Theme.Dark => AppTheme.Dark,
        _ => AppTheme.System
    };

    private static LoqNova.Lib.Theme MapLibTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => LoqNova.Lib.Theme.Light,
        AppTheme.Dark => LoqNova.Lib.Theme.Dark,
        _ => LoqNova.Lib.Theme.System
    };
}

public sealed record ThemeOption(AppTheme Value, string Label);

public sealed record TemperatureUnitOption(TemperatureUnit Value, string Label);

public sealed record AccentColorSourceOption(AccentColorSource Value, string Label);

public sealed record AutorunStateOption(AutorunState Value, string Label);

public sealed record ModifierKeyOption(ModifierKey Value, string Label);

public sealed record PowerModeMappingOption(PowerModeMappingMode Value, string Label);

public sealed record LanguageOption(string Value, string Label);