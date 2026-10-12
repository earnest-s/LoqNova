using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Extensions;
using LoqNova.Lib.Settings;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// Notifications settings, reproducing WPF's <c>NotificationsSettingsWindow</c>.
/// <para>
/// Every switch writes straight to <see cref="ApplicationSettings"/> and calls
/// <c>SynchronizeStore</c>, exactly as WPF does: there is no Save button, so a change
/// is persisted the moment it is made. The detail cards are disabled while
/// "Don't show notifications" is on, which is WPF's <c>RefreshCards</c>.
/// </para>
/// </summary>
public partial class NotificationsSettingsViewModel : DialogViewModelBase
{
    private readonly IMainThreadDispatcher _dispatcher;

    private ApplicationSettings? _settings;
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _dontShowNotifications;

    [ObservableProperty]
    private bool _notificationAlwaysOnTop;

    [ObservableProperty]
    private bool _notificationOnAllScreens;

    [ObservableProperty]
    private NotificationPosition _notificationPosition;

    [ObservableProperty]
    private NotificationDuration _notificationDuration;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private bool _automationNotification;

    [ObservableProperty]
    private bool _capsNumLock;

    [ObservableProperty]
    private bool _fnLock;

    [ObservableProperty]
    private bool _touchpadLock;

    [ObservableProperty]
    private bool _smartKey;

    [ObservableProperty]
    private bool _keyboardBacklight;

    [ObservableProperty]
    private bool _cameraLock;

    [ObservableProperty]
    private bool _microphone;

    [ObservableProperty]
    private bool _powerMode;

    [ObservableProperty]
    private bool _refreshRate;

    [ObservableProperty]
    private bool _acAdapter;

    public ObservableCollection<NotificationPositionOption> NotificationPositions { get; } = new();

    public ObservableCollection<NotificationDurationOption> NotificationDurations { get; } = new();

    public NotificationsSettingsViewModel(IMainThreadDispatcher dispatcher, IDialogService dialogs)
        : base(dialogs)
    {
        _dispatcher = dispatcher;

        foreach (var value in Enum.GetValues<NotificationPosition>())
        {
            NotificationPositions.Add(new NotificationPositionOption(value, value.GetDisplayName()));
        }

        foreach (var value in Enum.GetValues<NotificationDuration>())
        {
            NotificationDurations.Add(new NotificationDurationOption(value, value.GetDisplayName()));
        }
    }

    /// <summary>WPF's <c>RefreshCards</c>: the detail cards follow "Don't show notifications".</summary>
    public bool AreDetailsEnabled => !DontShowNotifications;

    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        _settings = LoqNova.Lib.IoCContainer.Resolve<ApplicationSettings>();
        var store = _settings.Store;

        await _dispatcher.InvokeAsync(() =>
        {
            _isRefreshing = true;
            try
            {
                DontShowNotifications = store.DontShowNotifications;
                NotificationAlwaysOnTop = store.NotificationAlwaysOnTop;
                NotificationOnAllScreens = store.NotificationOnAllScreens;
                NotificationPosition = store.NotificationPosition;
                NotificationDuration = store.NotificationDuration;

                UpdateAvailable = store.Notifications.UpdateAvailable;
                AutomationNotification = store.Notifications.AutomationNotification;
                CapsNumLock = store.Notifications.CapsNumLock;
                FnLock = store.Notifications.FnLock;
                TouchpadLock = store.Notifications.TouchpadLock;
                SmartKey = store.Notifications.SmartKey;
                KeyboardBacklight = store.Notifications.KeyboardBacklight;
                CameraLock = store.Notifications.CameraLock;
                Microphone = store.Notifications.Microphone;
                PowerMode = store.Notifications.PowerMode;
                RefreshRate = store.Notifications.RefreshRate;
                ACAdapter = store.Notifications.ACAdapter;
            }
            finally
            {
                _isRefreshing = false;
            }
        }).ConfigureAwait(false);
    }

    private void Persist(Action<ApplicationSettings.ApplicationSettingsStore> mutate)
    {
        if (_isRefreshing || _settings is null)
        {
            return;
        }

        mutate(_settings.Store);
        _settings.SynchronizeStore();
    }

    partial void OnDontShowNotificationsChanged(bool value)
    {
        Persist(store => store.DontShowNotifications = value);
        OnPropertyChanged(nameof(AreDetailsEnabled));
    }

    partial void OnNotificationAlwaysOnTopChanged(bool value) =>
        Persist(store => store.NotificationAlwaysOnTop = value);

    partial void OnNotificationOnAllScreensChanged(bool value) =>
        Persist(store => store.NotificationOnAllScreens = value);

    partial void OnNotificationPositionChanged(NotificationPosition value) =>
        Persist(store => store.NotificationPosition = value);

    partial void OnNotificationDurationChanged(NotificationDuration value) =>
        Persist(store => store.NotificationDuration = value);

    partial void OnUpdateAvailableChanged(bool value) =>
        Persist(store => store.Notifications.UpdateAvailable = value);

    partial void OnAutomationNotificationChanged(bool value) =>
        Persist(store => store.Notifications.AutomationNotification = value);

    partial void OnCapsNumLockChanged(bool value) =>
        Persist(store => store.Notifications.CapsNumLock = value);

    partial void OnFnLockChanged(bool value) =>
        Persist(store => store.Notifications.FnLock = value);

    partial void OnTouchpadLockChanged(bool value) =>
        Persist(store => store.Notifications.TouchpadLock = value);

    partial void OnSmartKeyChanged(bool value) =>
        Persist(store => store.Notifications.SmartKey = value);

    partial void OnKeyboardBacklightChanged(bool value) =>
        Persist(store => store.Notifications.KeyboardBacklight = value);

    partial void OnCameraLockChanged(bool value) =>
        Persist(store => store.Notifications.CameraLock = value);

    partial void OnMicrophoneChanged(bool value) =>
        Persist(store => store.Notifications.Microphone = value);

    partial void OnPowerModeChanged(bool value) =>
        Persist(store => store.Notifications.PowerMode = value);

    partial void OnRefreshRateChanged(bool value) =>
        Persist(store => store.Notifications.RefreshRate = value);

    partial void OnACAdapterChanged(bool value) =>
        Persist(store => store.Notifications.ACAdapter = value);
}

public sealed record NotificationPositionOption(NotificationPosition Value, string Label);

public sealed record NotificationDurationOption(NotificationDuration Value, string Label);
