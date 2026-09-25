using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Views.Controls.RGB;

public partial class ZoneColorPicker : UserControl
{
    public static readonly StyledProperty<int> ZoneNumberProperty =
        AvaloniaProperty.Register<ZoneColorPicker, int>(nameof(ZoneNumber));

    public static readonly StyledProperty<RgbZoneColor> ColorProperty =
        AvaloniaProperty.Register<ZoneColorPicker, RgbZoneColor>(nameof(Color), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

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

    public ZoneColorPicker()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}