using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.ColorPicker;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LoqNova.Avalonia.Converters;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Views.Controls.RGB;

/// <summary>
/// One RGB zone: identity, a large preview of the authoritative colour, and
/// Avalonia's native <c>ColorView</c> as the editor.
/// <para>
/// The zone colour lives in the RGB state and is edited through the built-in
/// <see cref="ColorProperty"/> binding, so a change takes the single state-write
/// path to the backend. The control holds no colour state of its own and renders
/// no animation.
/// </para>
/// <para>
/// WPF exposes "Synchronise zones" as a per-zone context-menu item rather than a
/// button, so that is reproduced here: right-click the zone card.
/// </para>
/// </summary>
public partial class ZoneColorPicker : UserControl
{
    public static readonly StyledProperty<int> ZoneNumberProperty =
        AvaloniaProperty.Register<ZoneColorPicker, int>(nameof(ZoneNumber));

    public static readonly StyledProperty<RgbZoneColor> ColorProperty =
        AvaloniaProperty.Register<ZoneColorPicker, RgbZoneColor>(
            nameof(Color), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Mirrors the page's availability rule, which comes from the backend.</summary>
    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<ZoneColorPicker, bool>(nameof(IsInteractive));

    /// <summary>Raised when the user asks to apply this zone's colour to all four.</summary>
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
        BuildColourFlyout();
        BuildSynchroniseMenu();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The large preview opens Avalonia's native colour editor in a flyout, so the
    /// heavy spectrum and hex entry stay out of the page until they are wanted.
    /// <para>
    /// The flyout's editor is bound straight to this control's
    /// <see cref="ColorProperty"/>, which is itself bound to the page's zone colour.
    /// That keeps one chain: editor to control to ViewModel to backend. The flyout
    /// is outside the visual tree, so the binding names this instance as its source
    /// rather than relying on an inherited DataContext.
    /// </para>
    /// </summary>
    private void BuildColourFlyout()
    {
        if (this.FindControl<Button>("PickButton") is not { } button)
            return;

        var editor = new ColorView
        {
            IsAlphaVisible = false,
            Width = 260
        };

        editor.Bind(ColorView.ColorProperty, new Binding(nameof(Color))
        {
            Source = this,
            Mode = BindingMode.TwoWay,
            Converter = RgbZoneColorConverter.Instance
        });

        button.Flyout = new Flyout
        {
            Content = editor,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft
        };
    }

    /// <summary>
    /// WPF's per-zone "Synchronise zones" context-menu item, applying this zone's
    /// colour to all four in one backend state write. Built in code so the menu is
    /// wired to this instance and carries this zone's number.
    /// </summary>
    private void BuildSynchroniseMenu()
    {
        var item = new MenuItem { Header = "Synchronise zones" };
        item.Click += (_, _) => SynchroniseRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenu { ItemsSource = new[] { item } };
        menu.Opened += (_, _) => menu.DataContext = this;

        if (this.FindControl<Border>("Card") is { } card)
            card.ContextMenu = menu;
    }
}
