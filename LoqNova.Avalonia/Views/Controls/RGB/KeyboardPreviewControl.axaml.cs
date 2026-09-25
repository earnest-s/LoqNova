using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Views.Controls.RGB;

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

    public static readonly StyledProperty<RgbBrightness> BrightnessProperty =
        AvaloniaProperty.Register<KeyboardPreviewControl, RgbBrightness>(nameof(Brightness));

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

    public RgbBrightness Brightness
    {
        get => GetValue(BrightnessProperty);
        set => SetValue(BrightnessProperty, value);
    }

    public KeyboardPreviewControl()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}