using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Views.Controls.RGB;

/// <summary>
/// One RGB zone. The palette swatches set the zone's colour through the TwoWay
/// <see cref="ColorProperty"/> binding, so the change takes the ViewModel's single
/// state-write path into the backend. "Synchronise zones" is the explicit action WPF
/// exposes from the zone colour picker's context menu, not a persistent mode.
/// </summary>
public partial class ZoneColorPicker : UserControl
{
    /// <summary>Palette offered per zone. Chosen once here, never per render.</summary>
    private static readonly RgbZoneColor[] Palette =
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
        AvaloniaProperty.Register<ZoneColorPicker, RgbZoneColor>(nameof(Color), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Mirrors the page's availability rule, which comes from the backend.</summary>
    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<ZoneColorPicker, bool>(nameof(IsInteractive));

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

    public ZoneColorPicker()
    {
        InitializeComponent();
        BuildPalette();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Creates the swatch row once, in code, so no external control is needed.</summary>
    private void BuildPalette()
    {
        if (this.FindControl<Panel>("PaletteHost") is not { } host)
            return;

        var wrap = new WrapPanel { ItemWidth = 22, ItemHeight = 22 };

        foreach (var colour in Palette)
        {
            var button = new Button
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(colour.R, colour.G, colour.B)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Tag = colour
            };

            button.Click += (_, _) => Color = colour;

            wrap.Children.Add(button);
        }

        host.Children.Add(wrap);
    }

    private void OnSynchroniseClick(object? sender, RoutedEventArgs e)
        => SynchroniseRequested?.Invoke(this, EventArgs.Empty);
}
