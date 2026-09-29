using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Views.Controls.RGB;

/// <summary>
/// One RGB zone: a colour swatch showing the zone's current colour, which opens
/// Avalonia's native colour editor when clicked.
/// <para>
/// The editor reports changes through <see cref="OnEditorColorChanged"/>, which writes
/// <see cref="ColorProperty"/>. That property is bound by the page to this zone's
/// ViewModel property, which saves through the single state write. This uses an
/// explicit event rather than a TwoWay binding through a converter, because the
/// editor's colour is a nullable Avalonia <see cref="Color"/> while the zone colour is
/// a value struct, and the conversion in between is exactly where the chain previously
/// broke silently.
/// </para>
/// <para>
/// WPF exposes "Synchronise zones" as a per-zone context-menu item, so that is
/// reproduced here: right-click the zone card.
/// </para>
/// </summary>
public partial class ZoneColorPicker : UserControl
{
    /// <summary>The native colour editor, created by the XAML name generator.</summary>
    private ColorView? ColourEditor => this.FindControl<ColorView>("Editor");

    public static readonly StyledProperty<int> ZoneNumberProperty =
        AvaloniaProperty.Register<ZoneColorPicker, int>(nameof(ZoneNumber));

    public static readonly StyledProperty<RgbZoneColor> ColorProperty =
        AvaloniaProperty.Register<ZoneColorPicker, RgbZoneColor>(nameof(Color));

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
        BuildSynchroniseMenu();

        PropertyChanged += OnSelfPropertyChanged;

        // Watch the editor's colour property directly. Its ColorChanged event is not
        // usable from here, and a binding through a converter previously lost the
        // value without any error.
        if (Editor is { } editor)
            editor.PropertyChanged += (_, e) =>
            {
                if (e.Property == ColorView.ColorProperty)
                    OnEditorColorChanged(editor, e);
            };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnSelfPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ColorProperty)
            SyncEditorFromZone();

        // Editing is unavailable while the backlight is Off or Vantage is running, so
        // the editor must not stay open across that change.
        if (e.Property == IsInteractiveProperty && !IsInteractive)
            SetEditorVisible(false);
    }

    /// <summary>
    /// Pushes the authoritative zone colour into the editor, so opening it always
    /// shows the colour the keyboard actually has. Guarded so it cannot loop with
    /// <see cref="OnEditorColorChanged"/>.
    /// </summary>
    private void SyncEditorFromZone()
    {
        if (Editor is not { } editor)
            return;

        var target = Color.FromRgb(Color.R, Color.G, Color.B);
        if (editor.Color != target)
            editor.Color = target;
    }

    private void OnPickClicked(object? sender, RoutedEventArgs e)
    {
        if (Editor is { } editor)
            SetEditorVisible(!editor.IsVisible);
    }

    private void SetEditorVisible(bool visible)
    {
        if (Editor is not { } editor)
            return;

        editor.IsVisible = visible;
        if (visible)
        {
            SyncEditorFromZone();
            editor.Focus();
        }
    }

    /// <summary>
    /// The editor's chosen colour becomes this zone's colour. The zone colour is then
    /// bound by the page to the ViewModel, which performs the single state write, so
    /// there is one path from the editor to the keyboard.
    /// </summary>
    private void OnEditorColorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ColorView.ColorProperty || e.NewValue is not Color chosen)
            return;

        var next = new RgbZoneColor(chosen.R, chosen.G, chosen.B);
        if (next == Color)
            return;

        Color = next;
    }

    /// <summary>
    /// WPF's per-zone "Synchronise zones" context-menu item, applying this zone's
    /// colour to all four in one backend state write.
    /// </summary>
    private void BuildSynchroniseMenu()
    {
        var item = new MenuItem { Header = "Synchronise zones" };
        item.Click += (_, _) => SynchroniseRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenu { ItemsSource = new[] { item } };

        if (this.FindControl<Border>("Card") is { } card)
            card.ContextMenu = menu;
    }
}
