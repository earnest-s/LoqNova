using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace LoqNova.Avalonia.Views.Controls.Sensors;

public partial class SensorsPanel : UserControl
{
    public static readonly StyledProperty<double> CpuUsageProperty =
        AvaloniaProperty.Register<SensorsPanel, double>(nameof(CpuUsage));

    public static readonly StyledProperty<double> GpuUsageProperty =
        AvaloniaProperty.Register<SensorsPanel, double>(nameof(GpuUsage));

    public static readonly StyledProperty<double> CpuTemperatureProperty =
        AvaloniaProperty.Register<SensorsPanel, double>(nameof(CpuTemperature));

    public static readonly StyledProperty<double> GpuTemperatureProperty =
        AvaloniaProperty.Register<SensorsPanel, double>(nameof(GpuTemperature));

    public static readonly StyledProperty<int> FanSpeedRpmProperty =
        AvaloniaProperty.Register<SensorsPanel, int>(nameof(FanSpeedRpm));

    public static readonly StyledProperty<double> FanSpeedPercentProperty =
        AvaloniaProperty.Register<SensorsPanel, double>(nameof(FanSpeedPercent));

    public static readonly StyledProperty<IBrush?> PowerModeColorProperty =
        AvaloniaProperty.Register<SensorsPanel, IBrush?>(nameof(PowerModeColor));

    public double CpuUsage
    {
        get => GetValue(CpuUsageProperty);
        set => SetValue(CpuUsageProperty, value);
    }

    public double GpuUsage
    {
        get => GetValue(GpuUsageProperty);
        set => SetValue(GpuUsageProperty, value);
    }

    public double CpuTemperature
    {
        get => GetValue(CpuTemperatureProperty);
        set => SetValue(CpuTemperatureProperty, value);
    }

    public double GpuTemperature
    {
        get => GetValue(GpuTemperatureProperty);
        set => SetValue(GpuTemperatureProperty, value);
    }

    public int FanSpeedRpm
    {
        get => GetValue(FanSpeedRpmProperty);
        set => SetValue(FanSpeedRpmProperty, value);
    }

    public double FanSpeedPercent
    {
        get => GetValue(FanSpeedPercentProperty);
        set => SetValue(FanSpeedPercentProperty, value);
    }

    public IBrush? PowerModeColor
    {
        get => GetValue(PowerModeColorProperty);
        set => SetValue(PowerModeColorProperty, value);
    }

    public SensorsPanel()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}