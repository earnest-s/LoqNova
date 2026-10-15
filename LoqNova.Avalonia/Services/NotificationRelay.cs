using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using LoqNova.Avalonia.Localization;
using LoqNova.Lib.Messaging;
using LoqNova.Lib.Settings;
using Microsoft.Extensions.Logging;
using PubSub;
using LibNotificationMessage = LoqNova.Lib.Messaging.Messages.NotificationMessage;
using LibNotificationType = LoqNova.Lib.NotificationType;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Turns the shared library's notification messages into shell toasts, applying the
/// same gate WPF's <c>NotificationsManager</c> applies.
///
/// <para>
/// The gate is the reason this exists: the "Notifications" settings decide which
/// categories are delivered, and until this relay existed nothing read those switches,
/// so every category switch was inert and Fn-key / power-mode / adapter notifications
/// were never shown at all.
/// </para>
///
/// <para>
/// Unlike WPF, this honours the Caps/Num Lock, Fn Lock and Power Mode switches rather
/// than hard-coding them to on. WPF's manager returns <c>true</c> for those types, so
/// its matching cards do nothing; honouring them is what the cards promise, and the
/// delay between the two front ends is recorded in the migration notes.
/// </para>
/// </summary>
public sealed class NotificationRelay : INotificationRelay
{
    /// <summary>Per-type guard against duplicate toasts, as in WPF.</summary>
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(250);

    private readonly INotificationService _notifications;
    private readonly ApplicationSettings _settings;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly ILogger<NotificationRelay> _logger;

    private bool _started;
    private LibNotificationType? _lastDebounceType;
    private DateTime _lastDebounceUtc = DateTime.MinValue;
    private LibNotificationType? _lastPowerModeType;

    public NotificationRelay(
        INotificationService notifications,
        ApplicationSettings settings,
        IMainThreadDispatcher dispatcher,
        ILogger<NotificationRelay> logger)
    {
        _notifications = notifications;
        _settings = settings;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Start()
    {
        if (_started)
        {
            return;
        }

        Hub.Default.Subscribe<LibNotificationMessage>(this, OnNotificationReceived);
        _started = true;

        _notifications.SetPosition(MapPosition(_settings.Store.NotificationPosition));
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        Hub.Default.Unsubscribe<LibNotificationMessage>(this);
        _started = false;
    }

    private void OnNotificationReceived(LibNotificationMessage message)
    {
        // Deliver on the UI thread. Doing the gating there too serialises the debounce
        // state, which is what WPF's dispatcher-based manager gets for free.
        _ = _dispatcher.InvokeAsync(() => ShowAsync(message));
    }

    private async Task ShowAsync(LibNotificationMessage message)
    {
        try
        {
            var store = _settings.Store;

            if (store.DontShowNotifications)
            {
                return;
            }

            if (!store.NotificationAlwaysOnTop && IsAnyApplicationFullscreen())
            {
                return;
            }

            if (!IsAllowed(store.Notifications, message.Type))
            {
                return;
            }

            if (IsDebounced(message.Type))
            {
                return;
            }

            _notifications.SetPosition(MapPosition(store.NotificationPosition));

            var text = BuildText(message);

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            await _notifications.ShowAsync(new NotificationMessage(
                SeverityOf(message.Type),
                text,
                null,
                DurationOf(store.NotificationDuration))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Showing notification {Type} failed", message.Type);
        }
    }

    private bool IsDebounced(LibNotificationType type)
    {
        var now = DateTime.UtcNow;

        if (_lastDebounceType == type && now - _lastDebounceUtc < DebounceWindow)
        {
            return true;
        }

        _lastDebounceType = type;
        _lastDebounceUtc = now;

        // Power-mode changes are reported twice (the immediate UI path and the later WMI
        // confirmation). WPF collapses the pair so one change shows one toast.
        if (type is LibNotificationType.PowerModeQuiet
            or LibNotificationType.PowerModeBalance
            or LibNotificationType.PowerModePerformance
            or LibNotificationType.PowerModeGodMode)
        {
            if (_lastPowerModeType == type)
            {
                return true;
            }

            _lastPowerModeType = type;
        }

        return false;
    }

    /// <summary>
    /// WPF's category gate. Every notification type maps to the switch the Notifications
    /// dialog exposes for it.
    /// </summary>
    private static bool IsAllowed(ApplicationSettings.Notifications settings, LibNotificationType type) => type switch
    {
        LibNotificationType.ACAdapterConnected => settings.ACAdapter,
        LibNotificationType.ACAdapterConnectedLowWattage => settings.ACAdapter,
        LibNotificationType.ACAdapterDisconnected => settings.ACAdapter,
        LibNotificationType.AutomationNotification => settings.AutomationNotification,
        LibNotificationType.CameraOn => settings.CameraLock,
        LibNotificationType.CameraOff => settings.CameraLock,
        LibNotificationType.CapsLockOn => settings.CapsNumLock,
        LibNotificationType.CapsLockOff => settings.CapsNumLock,
        LibNotificationType.NumLockOn => settings.CapsNumLock,
        LibNotificationType.NumLockOff => settings.CapsNumLock,
        LibNotificationType.FnLockOn => settings.FnLock,
        LibNotificationType.FnLockOff => settings.FnLock,
        LibNotificationType.MicrophoneOn => settings.Microphone,
        LibNotificationType.MicrophoneOff => settings.Microphone,
        LibNotificationType.PanelLogoLightingOn => settings.KeyboardBacklight,
        LibNotificationType.PanelLogoLightingOff => settings.KeyboardBacklight,
        LibNotificationType.PortLightingOn => settings.KeyboardBacklight,
        LibNotificationType.PortLightingOff => settings.KeyboardBacklight,
        LibNotificationType.RGBKeyboardBacklightChanged => settings.KeyboardBacklight,
        LibNotificationType.RGBKeyboardBacklightOff => settings.KeyboardBacklight,
        LibNotificationType.SpectrumBacklightChanged => settings.KeyboardBacklight,
        LibNotificationType.SpectrumBacklightOff => settings.KeyboardBacklight,
        LibNotificationType.SpectrumBacklightPresetChanged => settings.KeyboardBacklight,
        LibNotificationType.WhiteKeyboardBacklightChanged => settings.KeyboardBacklight,
        LibNotificationType.WhiteKeyboardBacklightOff => settings.KeyboardBacklight,
        LibNotificationType.PowerModeQuiet => settings.PowerMode,
        LibNotificationType.PowerModeBalance => settings.PowerMode,
        LibNotificationType.PowerModePerformance => settings.PowerMode,
        LibNotificationType.PowerModeGodMode => settings.PowerMode,
        LibNotificationType.RefreshRate => settings.RefreshRate,
        LibNotificationType.SmartKeyDoublePress => settings.SmartKey,
        LibNotificationType.SmartKeySinglePress => settings.SmartKey,
        LibNotificationType.TouchpadOn => settings.TouchpadLock,
        LibNotificationType.TouchpadOff => settings.TouchpadLock,
        LibNotificationType.UpdateAvailable => settings.UpdateAvailable,
        _ => false
    };

    /// <summary>
    /// WPF's OSD text. Types whose text carries a value format the message's first
    /// argument, exactly as WPF does.
    /// </summary>
    private static string? BuildText(LibNotificationMessage message)
    {
        var value = message.Args.Length > 0 ? message.Args[0]?.ToString() ?? string.Empty : string.Empty;

        return message.Type switch
        {
            LibNotificationType.ACAdapterConnected => Resource("Notification_ACAdapterConnected"),
            LibNotificationType.ACAdapterConnectedLowWattage => Resource("Notification_ACAdapterConnectedLowWattage"),
            LibNotificationType.ACAdapterDisconnected => Resource("Notification_ACAdapterDisconnected"),
            LibNotificationType.AutomationNotification => value,
            LibNotificationType.CameraOn => Resource("Notification_CameraOn"),
            LibNotificationType.CameraOff => Resource("Notification_CameraOff"),
            LibNotificationType.CapsLockOn => Resource("Notification_CapsLockOn"),
            LibNotificationType.CapsLockOff => Resource("Notification_CapsLockOff"),
            LibNotificationType.FnLockOn => Resource("Notification_FnLockOn"),
            LibNotificationType.FnLockOff => Resource("Notification_FnLockOff"),
            LibNotificationType.MicrophoneOn => Resource("Notification_MicrophoneOn"),
            LibNotificationType.MicrophoneOff => Resource("Notification_MicrophoneOff"),
            LibNotificationType.NumLockOn => Resource("Notification_NumLockOn"),
            LibNotificationType.NumLockOff => Resource("Notification_NumLockOff"),
            LibNotificationType.PanelLogoLightingOn => Resource("Notification_PanelLogoLightingOn"),
            LibNotificationType.PanelLogoLightingOff => Resource("Notification_PanelLogoLightingOff"),
            LibNotificationType.PortLightingOn => Resource("Notification_PortLightingOn"),
            LibNotificationType.PortLightingOff => Resource("Notification_PortLightingOff"),
            LibNotificationType.PowerModeQuiet => value,
            LibNotificationType.PowerModeBalance => value,
            LibNotificationType.PowerModePerformance => value,
            LibNotificationType.PowerModeGodMode => value,
            LibNotificationType.RefreshRate => value,
            LibNotificationType.RGBKeyboardBacklightChanged => value,
            LibNotificationType.RGBKeyboardBacklightOff => value,
            LibNotificationType.SmartKeyDoublePress => value,
            LibNotificationType.SmartKeySinglePress => value,
            LibNotificationType.SpectrumBacklightChanged => Format("Notification_SpectrumKeyboardBacklight_Brightness", value),
            LibNotificationType.SpectrumBacklightOff => Format("Notification_SpectrumKeyboardBacklight_Backlight", value),
            LibNotificationType.SpectrumBacklightPresetChanged => Format("Notification_SpectrumKeyboardBacklight_Profile", value),
            LibNotificationType.TouchpadOn => Resource("Notification_TouchpadOn"),
            LibNotificationType.TouchpadOff => Resource("Notification_TouchpadOff"),
            LibNotificationType.UpdateAvailable => Format("Notification_UpdateAvailable", value),
            LibNotificationType.WhiteKeyboardBacklightChanged => Format("Notification_WhiteKeyboardBacklight", value),
            LibNotificationType.WhiteKeyboardBacklightOff => Format("Notification_WhiteKeyboardBacklight", value),
            _ => null
        };
    }

    /// <summary>
    /// Toast severity. WPF's OSD varies its symbol per type; Avalonia's toast has four
    /// severities, so "switched off / disconnected" reads as a warning and
    /// "switched on / connected" as a success.
    /// </summary>
    private static NotificationType SeverityOf(LibNotificationType type) => type switch
    {
        LibNotificationType.ACAdapterDisconnected => NotificationType.Warning,
        LibNotificationType.CameraOff => NotificationType.Warning,
        LibNotificationType.CapsLockOff => NotificationType.Warning,
        LibNotificationType.NumLockOff => NotificationType.Warning,
        LibNotificationType.FnLockOff => NotificationType.Warning,
        LibNotificationType.MicrophoneOff => NotificationType.Warning,
        LibNotificationType.PanelLogoLightingOff => NotificationType.Warning,
        LibNotificationType.PortLightingOff => NotificationType.Warning,
        LibNotificationType.RGBKeyboardBacklightOff => NotificationType.Warning,
        LibNotificationType.SpectrumBacklightOff => NotificationType.Warning,
        LibNotificationType.TouchpadOff => NotificationType.Warning,
        LibNotificationType.WhiteKeyboardBacklightOff => NotificationType.Warning,

        LibNotificationType.ACAdapterConnected => NotificationType.Success,
        LibNotificationType.ACAdapterConnectedLowWattage => NotificationType.Success,
        LibNotificationType.CameraOn => NotificationType.Success,
        LibNotificationType.CapsLockOn => NotificationType.Success,
        LibNotificationType.NumLockOn => NotificationType.Success,
        LibNotificationType.FnLockOn => NotificationType.Success,
        LibNotificationType.MicrophoneOn => NotificationType.Success,
        LibNotificationType.PanelLogoLightingOn => NotificationType.Success,
        LibNotificationType.PortLightingOn => NotificationType.Success,
        LibNotificationType.TouchpadOn => NotificationType.Success,

        _ => NotificationType.Info
    };

    /// <summary>WPF's OSD durations: short 500 ms, normal 1 s, long 2.5 s.</summary>
    private static TimeSpan DurationOf(LoqNova.Lib.NotificationDuration duration) => duration switch
    {
        LoqNova.Lib.NotificationDuration.Short => TimeSpan.FromMilliseconds(500),
        LoqNova.Lib.NotificationDuration.Long => TimeSpan.FromMilliseconds(2500),
        _ => TimeSpan.FromMilliseconds(1000)
    };

    /// <summary>
    /// WPF's notification position. Avalonia's toast manager supports the four corners
    /// and top/bottom centre only, so the centre placements fall back to bottom centre.
    /// </summary>
    private static NotificationPosition MapPosition(LoqNova.Lib.NotificationPosition position) => position switch
    {
        LoqNova.Lib.NotificationPosition.TopLeft => NotificationPosition.TopLeft,
        LoqNova.Lib.NotificationPosition.TopCenter => NotificationPosition.TopCenter,
        LoqNova.Lib.NotificationPosition.TopRight => NotificationPosition.TopRight,
        LoqNova.Lib.NotificationPosition.BottomLeft => NotificationPosition.BottomLeft,
        LoqNova.Lib.NotificationPosition.BottomCenter => NotificationPosition.BottomCenter,
        LoqNova.Lib.NotificationPosition.BottomRight => NotificationPosition.BottomRight,
        _ => NotificationPosition.BottomCenter
    };

    private static string Resource(string key) => LocalizationHelper.GetString(key);

    private static string Format(string key, string value) => string.Format(LocalizationHelper.GetString(key), value);

    /// <summary>
    /// WPF's fullscreen suppression: notifications are held back while another
    /// application covers a screen, unless "always on top" is on. Implemented here with
    /// the same foreground-window comparison WPF uses.
    /// </summary>
    private static bool IsAnyApplicationFullscreen()
    {
        try
        {
            var handle = GetForegroundWindow();

            if (handle == IntPtr.Zero)
            {
                return false;
            }

            if (!GetWindowRect(handle, out var bounds))
            {
                return false;
            }

            var screens = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow?.Screens;

            if (screens is null)
            {
                return false;
            }

            var coversFullscreen = false;

            foreach (var screen in screens.All)
            {
                var screenBounds = screen.Bounds;

                if (bounds.Right - bounds.Left == screenBounds.Width &&
                    bounds.Bottom - bounds.Top == screenBounds.Height)
                {
                    coversFullscreen = true;
                    break;
                }
            }

            if (!coversFullscreen)
            {
                return false;
            }

            _ = GetWindowThreadProcessId(handle, out var processId);

            if (processId == 0)
            {
                return false;
            }

            // The shell's desktop window also covers the screen; it is not "an app".
            using var process = Process.GetProcessById((int)processId);
            return !string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Same as WPF: a failed check must never hold up a notification.
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
