using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoqNova.Avalonia.Views.Windows;

/// <summary>
/// The notification-area hover popup: WPF's <c>StatusWindow</c>, reproduced.
///
/// It is a decoration-less, non-activating, always-on-top window that is created
/// per hover and placed beside the pointer, which is what WPF does. The window
/// knows only about placing itself; the state it shows comes from
/// <see cref="ViewModels.Windows.StatusTrayPopupViewModel"/>, and when it opens and
/// closes is decided by <c>TrayHoverService</c>.
/// </summary>
public partial class StatusTrayPopupWindow : Window
{
    /// <summary>WPF's offset between the pointer and the popup, in screen pixels.</summary>
    private const int Offset = 8;

    public StatusTrayPopupWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The popup's rectangle in screen pixels, which the hover test uses to decide
    /// whether the pointer is still inside the popup.
    /// </summary>
    public PixelRect ScreenBounds => new(Position, ScreenSize());

    /// <summary>
    /// Shows the popup beside a pointer at <paramref name="cursor"/> (screen pixels).
    ///
    /// <c>Show</c> runs the layout pass before the platform window is painted, so the
    /// size read immediately afterwards is final and the window is moved before it
    /// can appear anywhere else: the first frame is already in place and there is no
    /// jump or flicker.
    /// </summary>
    public void ShowNextToCursor(PixelPoint cursor)
    {
        Show();
        Position = Place(cursor, ScreenSize());
    }

    /// <summary>
    /// WPF's placement: eight pixels below and right of the pointer, flipped to the
    /// other side of the pointer when the popup would not fit, then kept inside the
    /// working area of the pointer's screen.
    /// </summary>
    private PixelPoint Place(PixelPoint cursor, PixelSize size)
    {
        var x = cursor.X + Offset;
        var y = cursor.Y + Offset;

        if (Screens?.ScreenFromPoint(cursor) is { } screen)
        {
            var area = screen.WorkingArea;

            // Flip rather than slide, as WPF does, so the popup never covers the
            // icon the pointer is on.
            if (x + size.Width > area.Right)
                x = cursor.X - Offset - size.Width;

            if (y + size.Height > area.Bottom)
                y = cursor.Y - Offset - size.Height;

            x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - size.Width));
            y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
        }

        return new PixelPoint(x, y);
    }

    /// <summary>
    /// The client area in screen pixels. Layout has run by the time this is called,
    /// and the window has no decoration, so the client area is the whole window.
    /// </summary>
    private PixelSize ScreenSize()
    {
        var scaling = RenderScaling;
        if (scaling <= 0)
            scaling = 1.0;

        return new PixelSize(
            Math.Max(1, (int)Math.Ceiling(ClientSize.Width * scaling)),
            Math.Max(1, (int)Math.Ceiling(ClientSize.Height * scaling)));
    }
}
