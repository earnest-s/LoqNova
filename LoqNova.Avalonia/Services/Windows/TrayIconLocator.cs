using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;

namespace LoqNova.Avalonia.Services.Windows;

/// <summary>
/// Finds the notification-area icon that <c>TrayIcon</c> registers and reports
/// where it is on screen, without modifying the icon in any way.
///
/// Avalonia registers its tray icon with the classic <c>Shell_NotifyIcon</c> call
/// and never asks for notification-icon version 4, so the shell raises no hover
/// messages (<c>NIN_POPUPOPEN</c>/<c>NIN_POPUPCLOSE</c>) that WPF's
/// <c>TrayHelper</c> listens for. The only read-only way left to learn where the
/// icon sits is <c>Shell_NotifyIconGetRect</c>, which needs the id of the icon and
/// the window it was registered against. Avalonia's message window is internal and
/// its class name is generated per run (<c>"AvaloniaMessageWindow " + Guid</c>), so
/// the window is located by class-name prefix among this process's top-level
/// windows and the id is discovered by asking the shell about each candidate. The
/// probe is a read: an id that is not registered simply fails and the search moves
/// on, so nothing is added, removed or changed on the icon.
/// </summary>
internal static class TrayIconLocator
{
    /// <summary>
    /// Avalonia's message window class, as produced by <c>Win32Platform</c>:
    /// <c>"AvaloniaMessageWindow " + Guid.NewGuid()</c>.
    /// </summary>
    private const string MessageWindowClassPrefix = "AvaloniaMessageWindow ";

    /// <summary>
    /// Highest tray id to probe. Avalonia hands out ids from one upwards and this
    /// process creates a single tray icon, so the first probe normally settles it
    /// and this ceiling merely bounds the search.
    /// </summary>
    private const uint MaxIconId = 8;

    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;

    private static readonly object Gate = new();

    private static bool _resolved;
    private static nint _window;
    private static uint _iconId;

    /// <summary>Reads the pointer position in physical screen pixels.</summary>
    public static bool TryGetCursorPosition(out PixelPoint position)
    {
        if (GetCursorPos(out var native))
        {
            position = new PixelPoint(native.X, native.Y);
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>
    /// True while either mouse button is held. A press on the tray icon belongs to
    /// the icon's own gestures (restore the window, open the menu), so the hover
    /// popup stays out of the way while it happens.
    /// </summary>
    public static bool IsPointerButtonDown() =>
        IsKeyDown(VkLeftButton) || IsKeyDown(VkRightButton);

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>
    /// The icon's rectangle in physical screen pixels. False when the icon is not
    /// on screen at all — for instance while it lives in the taskbar's overflow
    /// flyout — or when it cannot be located.
    /// </summary>
    public static bool TryGetIconRect(out PixelRect rect)
    {
        lock (Gate)
        {
            if (!_resolved && !Resolve())
            {
                rect = default;
                return false;
            }

            if (TryQueryCached(out rect))
                return rect.Width > 0 && rect.Height > 0;

            // The cached window/id went stale, which in practice means Explorer
            // restarted and the icon was re-registered. Look it up once more.
            _resolved = false;

            if (!Resolve() || !TryQueryCached(out rect))
            {
                rect = default;
                return false;
            }

            return rect.Width > 0 && rect.Height > 0;
        }
    }

    /// <summary>Asks the shell about the icon found last time.</summary>
    private static bool TryQueryCached(out PixelRect rect)
    {
        rect = default;

        if (!_resolved)
            return false;

        return TryQuery(_window, _iconId, out rect);
    }

    /// <summary>
    /// Asks the shell where one candidate icon is. A candidate that is not
    /// registered simply fails; an icon that exists but is not in the notification
    /// area (it is in the overflow flyout) reports an empty rectangle, which counts
    /// as "not on screen".
    /// </summary>
    private static bool TryQuery(nint window, uint id, out PixelRect rect)
    {
        rect = default;

        if (Shell_NotifyIconGetRect(Identifier(window, id), out var native) < 0)
            return false;

        rect = new PixelRect(native.Left, native.Top, native.Right - native.Left, native.Bottom - native.Top);
        return true;
    }

    private static bool Resolve()
    {
        foreach (var window in CandidateWindows())
        {
            foreach (var id in ProbeOrder())
            {
                // The icon must actually be on screen: an empty rectangle means it is
                // in the taskbar's overflow flyout, which is not a usable position.
                if (!TryQuery(window, id, out var rect) || rect.Width <= 0 || rect.Height <= 0)
                    continue;

                _window = window;
                _iconId = id;
                _resolved = true;
                return true;
            }
        }

        _resolved = false;
        return false;
    }

    /// <summary>
    /// The ids to try, the one found last time first. Avalonia numbers its tray
    /// icons from one, so the first probe normally settles it; the rest of the
    /// range is only tried if that fails.
    /// </summary>
    private static IEnumerable<uint> ProbeOrder()
    {
        if (_iconId != 0)
            yield return _iconId;

        for (var id = 1u; id <= MaxIconId; id++)
        {
            if (id != _iconId)
                yield return id;
        }
    }

    private static NotifyIconIdentifier Identifier(nint window, uint id) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
        hWnd = window,
        uID = id
    };

    /// <summary>
    /// This process's top-level windows, with Avalonia's message window first.
    /// The tray icon is registered against that window; the remaining windows are
    /// only walked if it is not found there, so a future Avalonia that renames the
    /// class would still work.
    /// </summary>
    private static IEnumerable<nint> CandidateWindows()
    {
        var processId = (uint)Environment.ProcessId;
        var messageWindows = new List<nint>();
        var otherWindows = new List<nint>();

        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);

            if (owner != processId)
                return true;

            if (ClassName(window).StartsWith(MessageWindowClassPrefix, StringComparison.Ordinal))
                messageWindows.Add(window);
            else
                otherWindows.Add(window);

            return true;
        }, 0);

        foreach (var window in messageWindows)
            yield return window;

        foreach (var window in otherWindows)
            yield return window;
    }

    private static string ClassName(nint window)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassNameW(window, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(in NotifyIconIdentifier identifier, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// The shell's <c>NOTIFYICONIDENTIFIER</c>. The layout and size are the
    /// shell's: a 4-byte size, a pointer-sized window, a 4-byte id, then the
    /// 16-byte GUID (40 bytes on x64). <c>cbSize</c> is filled from
    /// <c>Marshal.SizeOf</c> rather than hard-coded.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public Guid guidItem;
    }
}
