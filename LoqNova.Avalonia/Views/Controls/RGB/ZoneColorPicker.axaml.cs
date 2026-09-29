using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Views.Controls.RGB;

/// <summary>One palette entry, with the selected state tracked so the ring can bind to it.</summary>
public sealed class ZoneSwatch
{
    public RgbZoneColor Color { get; }

    /// <summary>True when this entry matches the zone's current colour.</summary>
    public bool IsSelected { get; internal set; }

    public ZoneSwatch(RgbZoneColor color) => Color = color;
}

/// <summary>
/// One RGB zone, presented as a card: a large preview of the zone's current colour,
/// a compact palette and a single accent ring marking the selected entry.
/// <para>
/// The colour is the authoritative backend value. Selecting a swatch writes it
/// through the TwoWay <see cref="ColorProperty"/> binding, which the ViewModel sends
/// to the RGB controller in a single state write. The control holds no RGB state of
/// its own, and renders no animation.
/// </para>
/// </summary>
public partial class ZoneColorPicker : UserControl
{
    /// <summary>
    /// Palette offered per zone. These are the colours the control has always
    /// offered; no new RGB values are introduced here.
    /// </summary>
    private static readonly RgbZoneColor[] PaletteValues =
    [
        new(255, 255, 255),
        new(255, 0, 0),
        new(0, 255, 0),
        new(0, 0, 255),
        new(255, 255, 0),
        new(0, 255, 255),
        new(255, 0, 255),
        new(142, 255, 0),
        new(186, 0, 255),
        new(101, 0, 255),
        new(212, 255, 0),
        new(90, 90, 90)
    ];

    public static readonly StyledProperty<int> ZoneNumberProperty =
        AvaloniaProperty.Register<ZoneColorPicker, int>(nameof(ZoneNumber));

    public static readonly StyledProperty<RgbZoneColor> ColorProperty =
        AvaloniaProperty.Register<ZoneColorPicker, RgbZoneColor>(
            nameof(Color), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Mirrors the page's availability rule, which comes from the backend.</summary>
    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<ZoneColorPicker, bool>(nameof(IsInteractive));

    /// <summary>True for the zone that "Synchronise All Zones" takes its colour from.</summary>
    public static readonly StyledProperty<bool> IsSourceProperty =
        AvaloniaProperty.Register<ZoneColorPicker, bool>(nameof(IsSource));

    /// <summary>Raised when the user asks to apply this zone's colour to all four.</summary>
    public event EventHandler? SynchroniseRequested;

    public ObservableCollection<ZoneSwatch> Swatches { get; } = new();

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

    /// <summary>True for the zone the user last asked to synchronise from.</summary>
    public static readonly StyledProperty<bool> IsSourceProperty =
        AvaloniaProperty.Register<ZoneColorPicker, bool>(nameof(IsSource));

    public bool IsSource
    {
        get => GetValue(IsSourceProperty);
        set => SetValue(IsSourceProperty, value);
    }

    public ZoneColorPicker()
    {
        foreach (var colour in PaletteValues)
            Swatches.Add(new ZoneSwatch(colour));

        InitializeComponent();

        PropertyChanged += OnSelfPropertyChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Keeps the selected ring in step with the authoritative zone colour.</summary>
    private void OnSelfPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ColorProperty)
            UpdateSelection();
    }

    /// <summary>Marks exactly one palette entry as selected.</summary>
    private void UpdateSelection()
    {
        foreach (var swatch in Swatches)
            swatch.IsSelected = swatch.Color.R == Color.R
                && swatch.Color.G == Color.G
                && swatch.Color.B == Color.B;
    }

    /// <summary>The zone number is carried on the control so the page can route the
    /// synchronise gesture back to the matching zone.</summary>
    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: RgbZoneColor colour })
            Color = colour;
    }

    private void OnSynchroniseClick(object? sender, RoutedEventArgs e)
        => SynchroniseRequested?.Invoke(this, EventArgs.Empty);
}
