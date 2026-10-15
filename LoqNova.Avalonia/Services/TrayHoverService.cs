using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LoqNova.Avalonia.Services.Windows;
using LoqNova.Avalonia.ViewModels.Windows;
using LoqNova.Avalonia.Views.Windows;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// WPF's tray hover behaviour, rebuilt on top of Avalonia's tray icon.
///
/// WPF registers its icon with notification-icon version 4, so the shell tells it
/// when the pointer enters and leaves the icon (<c>NIN_POPUPOPEN</c> /
/// <c>NIN_POPUPCLOSE</c>): it waits half a second, then shows <c>StatusWindow</c>,
/// and hides it again when the pointer leaves. Avalonia's tray implementation
/// never asks for version 4, so those notifications never arrive and there is no
/// hover API to use instead. This service therefore does the one thing WPF's
/// notifications would have done — watch the pointer — while leaving the icon,
/// its menu, its click behaviour and its lifetime completely untouched: the icon
/// is only ever read, never modified.
///
/// Timing and dismissal follow WPF: half a second on the icon before the popup
/// appears, and the popup closes once the pointer has been away from both the icon
/// and the popup for a short grace period. The grace period is what keeps the
/// popup from flickering shut as the pointer crosses the few pixels between the
/// icon and the popup. Because the shell's own notification is an entry, not a
/// presence, the popup appears once per hover: it comes back only after the
/// pointer has left the icon.
/// </summary>
public sealed class TrayHoverService : IDisposable
{
    /// <summary>How often the pointer is inspected. Cheap: a cursor read and a hit test.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>WPF's delay between the pointer reaching the icon and the popup opening.</summary>
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>How long the pointer may be off both the icon and the popup before it closes.</summary>
    private static readonly TimeSpan HideGrace = TimeSpan.FromMilliseconds(300);

    /// <summary>How often the icon's position is re-read from the shell.</summary>
    private static readonly TimeSpan IconRectRefreshInterval = TimeSpan.FromSeconds(1);

    /// <summary>Within this distance of the icon, its position is re-read immediately.</summary>
    private const int IconRectRefreshRadius = 32;

    /// <summary>
    /// Slack around the icon and the popup when testing the pointer, so crossing
    /// the gap between them is never seen as leaving.
    /// </summary>
    private const int HoverPadding = 12;

    /// <summary>
    /// Avalonia shows its tray menu in a window it names
    /// <c>"AvaloniaTrayPopupRoot_" + tooltip</c>. WPF skips the popup while its
    /// context menu is open; this is the same rule for Avalonia's menu.
    /// </summary>
    private const string TrayMenuWindowNamePrefix = "AvaloniaTrayPopupRoot";

    private readonly IPerformanceService _performanceService;
    private readonly IBatteryService _batteryService;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TrayHoverService> _logger;

    private DispatcherTimer? _timer;
    private StatusTrayPopupWindow? _window;

    /// <summary>The icon's last known rectangle, or null when it is not on screen.</summary>
    private PixelRect? _iconRect;
    private DateTime _iconRectReadAt = DateTime.MinValue;
    private bool _iconRectReadInFlight;

    /// <summary>When the pointer reached the icon; the show delay is counted from here.</summary>
    private DateTime? _showRequestedAt;

    /// <summary>When the pointer was last seen off both the icon and the popup.</summary>
    private DateTime? _outsideSince;

    /// <summary>
    /// Whether a new hover may open the popup. The shell announces entering the
    /// icon, not being on it, so the popup appears once per entry: it re-arms when
    /// the pointer leaves the icon and is disarmed by a click or by the tray menu.
    /// </summary>
    private bool _armed = true;

    private bool _disposed;

    public TrayHoverService(
        IPerformanceService performanceService,
        IBatteryService batteryService,
        IMainThreadDispatcher dispatcher,
        ILoggerFactory loggerFactory)
    {
        _performanceService = performanceService;
        _batteryService = batteryService;
        _dispatcher = dispatcher;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TrayHoverService>();
    }

    /// <summary>
    /// Starts watching. Called once the tray icon exists, because until then there
    /// is nothing to hover.
    /// </summary>
    public void Start()
    {
        if (_disposed || _timer is not null)
            return;

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        if (!TrayIconLocator.TryGetCursorPosition(out var cursor))
            return;

        RefreshIconRectIfDue(cursor);

        // A press on the icon is the icon's own gesture: it restores the window or
        // opens the tray menu. WPF hides the popup in the same situation, and does
        // not show it again until the pointer has left the icon.
        if (TrayIconLocator.IsPointerButtonDown())
        {
            _showRequestedAt = null;
            _armed = false;
            Dismiss();
            return;
        }

        if (_iconRect is not { } icon)
        {
            // The icon is in the taskbar's overflow area, or Explorer is restarting.
            _showRequestedAt = null;
            _armed = true;
            Dismiss();
            return;
        }

        if (!Contains(Inflate(icon, HoverPadding), cursor))
            _armed = true;

        if (_window is { IsVisible: true } window)
        {
            var hovered = Contains(Inflate(icon, HoverPadding), cursor) ||
                          Contains(Inflate(window.ScreenBounds, HoverPadding), cursor);

            if (hovered)
            {
                _outsideSince = null;
                return;
            }

            // The pointer has to be away from both for a moment before the popup
            // closes, so crossing the gap between them is not seen as leaving.
            _outsideSince ??= DateTime.UtcNow;

            if (DateTime.UtcNow - _outsideSince.Value >= HideGrace)
                Dismiss();

            return;
        }

        _outsideSince = null;

        var menuOpen = IsTrayMenuOpen();

        // Avalonia's tray menu is a window of its own; the popup must not appear
        // over it, and picking an item from that menu is not a new hover.
        if (!Contains(Inflate(icon, HoverPadding), cursor) || menuOpen)
        {
            _showRequestedAt = null;

            if (menuOpen)
                _armed = false;

            // Also drops a popup that is still being prepared: the pointer is no
            // longer on the icon, so nothing should appear.
            Dismiss();
            return;
        }

        if (!_armed)
            return;

        _showRequestedAt ??= DateTime.UtcNow;

        if (DateTime.UtcNow - _showRequestedAt.Value >= ShowDelay)
            ShowPopup();
    }

    /// <summary>
    /// True while Avalonia's tray menu is open. Avalonia renders that menu in a
    /// window of its own, and the desktop lifetime lists every window the process
    /// has opened, so the menu can be recognised without touching it.
    /// </summary>
    private static bool IsTrayMenuOpen()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        foreach (var window in desktop.Windows)
        {
            if (window is StatusTrayPopupWindow)
                continue;

            if (window.Name is { Length: > 0 } name &&
                name.StartsWith(TrayMenuWindowNamePrefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the icon's rectangle from the shell when it is due.
    ///
    /// <c>Shell_NotifyIconGetRect</c> is a synchronous round-trip to Explorer, so it
    /// never runs on the UI thread; the tick tests against the cached rectangle.
    /// The cache is refreshed on a timer and immediately when the pointer comes
    /// close to it, so following the icon's real position costs one call per second
    /// at most while the pointer is elsewhere.
    /// </summary>
    private void RefreshIconRectIfDue(PixelPoint cursor)
    {
        if (_iconRectReadInFlight)
            return;

        var now = DateTime.UtcNow;

        var due = _iconRect is not { } cached ||
                  now - _iconRectReadAt >= IconRectRefreshInterval ||
                  Contains(Inflate(cached, IconRectRefreshRadius), cursor);

        if (!due)
            return;

        _iconRectReadAt = now;
        _iconRectReadInFlight = true;

        _ = Task.Run(() =>
        {
            var found = false;
            var rect = default(PixelRect);

            try
            {
                found = TrayIconLocator.TryGetIconRect(out rect);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Tray hover: reading the icon position failed");
            }

            _dispatcher.Post(() =>
            {
                _iconRect = found ? rect : null;
                _iconRectReadInFlight = false;
            });
        });
    }

    private void ShowPopup()
    {
        _showRequestedAt = null;

        // Shown once per entry; the pointer has to leave the icon to see it again,
        // the way the shell announces a hover.
        _armed = false;

        if (_window is not null)
            return;

        StatusTrayPopupWindow window;

        try
        {
            // A fresh view model per hover, reading the machine at that moment, which
            // is what WPF's StatusWindow.CreateAsync does.
            var viewModel = new StatusTrayPopupViewModel(
                _performanceService,
                _batteryService,
                _loggerFactory.CreateLogger<StatusTrayPopupViewModel>());

            window = new StatusTrayPopupWindow { DataContext = viewModel };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray hover: creating the status popup failed");
            return;
        }

        _window = window;
        window.Closed += OnPopupClosed;

        _ = ShowPopupAsync(window);
    }

    private async Task ShowPopupAsync(StatusTrayPopupWindow window)
    {
        try
        {
            if (window.DataContext is StatusTrayPopupViewModel viewModel)
                await viewModel.RefreshAsync().ConfigureAwait(true);

            // A click, or the pointer leaving, may have dismissed the popup while the
            // machine was being read. Nothing has been shown yet in that case.
            if (!ReferenceEquals(_window, window))
                return;

            if (!TrayIconLocator.TryGetCursorPosition(out var cursor))
                return;

            window.ShowNextToCursor(cursor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray hover: showing the status popup failed");
            Dismiss();
        }
    }

    private void Dismiss()
    {
        _outsideSince = null;

        var window = _window;
        _window = null;

        if (window is null)
            return;

        window.Closed -= OnPopupClosed;

        // A window that has not been shown yet is simply dropped; the pending show
        // checks that it is still the current one before showing anything.
        if (!window.IsVisible)
            return;

        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray hover: closing the status popup failed");
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (sender is StatusTrayPopupWindow window)
            window.Closed -= OnPopupClosed;

        if (ReferenceEquals(_window, null) || ReferenceEquals(_window, sender))
            _window = null;

        _outsideSince = null;
    }

    private static bool Contains(PixelRect rect, PixelPoint point) =>
        point.X >= rect.X && point.X < rect.Right && point.Y >= rect.Y && point.Y < rect.Bottom;

    private static PixelRect Inflate(PixelRect rect, int amount) =>
        new(rect.X - amount, rect.Y - amount, rect.Width + amount * 2, rect.Height + amount * 2);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }

        Dismiss();
    }
}
