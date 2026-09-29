using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Views.Controls.RGB;

/// <summary>
/// Read-only display of the RGB keyboard. It renders the four zone colours that
/// came from the library's frame dispatcher, so it mirrors what the hardware is
/// actually showing. It deliberately has no settings control: brightness, effect,
/// speed and zone colours are edited once, in the page's cards, so there is a
/// single authoritative RGB state.
/// </summary>
public partial class KeyboardPreviewControl : UserControl
{
    public static readonly StyledProperty<RgbZoneColor> Zone1ColorProperty =
        AvaloniaProperty.Register<KeyboardPreviewControl, RgbZoneColor>(nameof(Zone1Color));

    public static readonly StyledProperty<RgbZoneColor> Zone2ColorProperty =
        AvaloniaProperty.Register<KeyboardPreviewControl, RgbZoneColor>(nameof(Zone2Color));

    public static readonly StyledProperty<RgbZoneColor> Zone3ColorProperty =
        AvaloniaProperty.Register<KeyboardPreviewControl, RgbZoneColor>(nameof(Zone3Color));

    public static readonly StyledProperty<RgbZoneColor> Zone4ColorProperty =
        AvaloniaProperty.Register<KeyboardPreviewControl, RgbZoneColor>(nameof(Zone4Color));

    public RgbZoneColor Zone1Color
    {
        get => GetValue(Zone1ColorProperty);
        set => SetValue(Zone1ColorProperty, value);
    }

    public RgbZoneColor Zone2Color
    {
        get => GetValue(Zone2ColorProperty);
        set => SetValue(Zone2ColorProperty, value);
    }

    public RgbZoneColor Zone3Color
    {
        get => GetValue(Zone3ColorProperty);
        set => SetValue(Zone3ColorProperty, value);
    }

    public RgbZoneColor Zone4Color
    {
        get => GetValue(Zone4ColorProperty);
        set => SetValue(Zone4ColorProperty, value);
    }

    public KeyboardPreviewControl() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
