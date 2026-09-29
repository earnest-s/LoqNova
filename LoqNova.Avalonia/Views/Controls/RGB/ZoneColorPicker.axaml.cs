using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Views.Controls.RGB;

/// <summary>
/// One RGB zone's colour. The swatch is interactive, so the zone can actually be
/// changed, and its context menu carries WPF's "Synchronise zones" action. The
/// colour itself is owned by the selected preset description in the backend; this
/// control only edits it.
/// </summary>
public partial class ZoneColorPicker : UserControl
{
    public static readonly StyledProperty<int> ZoneNumberProperty =
        AvaloniaProperty.Register<ZoneColorPicker, int>(nameof(ZoneNumber));

    public static readonly StyledProperty<RgbZoneColor> ColorProperty =
        AvaloniaProperty.Register<ZoneColorPicker, RgbZoneColor>(nameof(Color), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Mirrors the page's availability rule, which is driven by the backend.</summary>
    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<ZoneColorPicker, bool>(nameof(IsInteractive));

    /// <summary>Raised when the user asks to change this zone's colour.</summary>
    public event EventHandler? ColourRequested;

    /// <summary>Raised when the user asks to apply this zone's colour to all zones.</summary>
    public event EventHandler? SynchroniseRequested;

    public int ZoneNumber
    {
        get => GetValue(ZoneNumberProperty);
        set => SetValue(ZoneNumberProperty, value);
    }

    public RgbZoneColor Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public ZoneColorPicker() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnSwatchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ColourRequested?.Invoke(this, EventArgs.Empty);

    private void OnPickColourClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ColourRequested?.Invoke(this, EventArgs.Empty);

    private void OnSynchroniseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => SynchroniseRequested?.Invoke(this, EventArgs.Empty);
}
