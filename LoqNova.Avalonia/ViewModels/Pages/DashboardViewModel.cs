using System;
using System.Collections.ObjectCollection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.ViewModels.Pages;

public enum PowerModeState
{
    Quiet,
    Balance,
    Performance,
    GodMode
}

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IPerformanceService _performanceService;
    private readonly IRgbService _rgbService;
    private readonly IThermalService _thermalService;
    private readonly IBatteryService _batteryService;
    private readonly ISensorsService _sensorsService;
    
    [ObservableProperty]
    private double _cpuUsage = 12;
    
    [ObservableProperty]
    private double _gpuUsage = 3;
    
    [ObservableProperty]
    private double _cpuTemperature = 54;
    
    [ObservableProperty]
    private double _gpuTemperature = 49;
    
    [ObservableProperty]
    private int _fanSpeedRpm = 2400;
    
    [ObservableProperty]
    private int _fanSpeedPercent = 45;
    
    [ObservableProperty]
    private PowerModeState _currentPowerMode = PowerModeState.Balance;
    
    [ObservableProperty]
    private string _powerModeColor = "#FFFFFF";
    
    public ObservableCollection<DashboardWidgetViewModel> Widgets { get; } = new();
    
    public DashboardViewModel(
        IPerformanceService performanceService,
        IRgbService rgbService,
        IThermalService thermalService,
        IBatteryService batteryService,
        ISensorsService sensorsService)
    {
        _performanceService = performanceService;
        _rgbService = rgbService;
        _thermalService = thermalService;
        _batteryService = batteryService;
        _sensorsService = sensorsService;
        
        InitializeWidgets();
        SubscribeToEvents();
    }
    
    private void InitializeWidgets()
    {
        // Add sensor widgets
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "CPU",
            Value = "12%",
            Unit = "%",
            Icon = "Cpu64",
            Type = WidgetType.Sensor,
            MinValue = 0,
            MaxValue = 100,
            CurrentValue = 12,
            Color = "#0078D4"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "GPU",
            Value = "3%",
            Unit = "%",
            Icon = "Gpu64",
            Type = WidgetType.Sensor,
            MinValue = 0,
            MaxValue = 100,
            CurrentValue = 3,
            Color = "#00B294"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "CPU Temp",
            Value = "54°C",
            Unit = "°C",
            Icon = "Thermometer64",
            Type = WidgetType.Sensor,
            MinValue = 30,
            MaxValue = 100,
            CurrentValue = 54,
            Color = "#E81123"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "GPU Temp",
            Value = "49°C",
            Unit = "°C",
            Icon = "Thermometer64",
            Type = WidgetType.Sensor,
            MinValue = 30,
            MaxValue = 95,
            CurrentValue = 49,
            Color = "#E81123"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "Fan Speed",
            Value = "2400 RPM",
            Unit = "RPM",
            Icon = "Fan64",
            Type = WidgetType.Sensor,
            MinValue = 0,
            MaxValue = 6000,
            CurrentValue = 2400,
            Color = "#744DA9"
        });
        
        // Add feature control widgets
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "Power Mode",
            Value = "Balance",
            Icon = "Bolt64",
            Type = WidgetType.ComboBox,
            Items = new ObservableCollection<string> { "Quiet", "Balance", "Performance", "GodMode" },
            SelectedItem = "Balance",
            Color = "#FFFFFF"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "Battery",
            Value = "Normal",
            Icon = "Battery64",
            Type = WidgetType.ComboBox,
            Items = new ObservableCollection<string> { "Normal", "Rapid Charge", "Conservation" },
            SelectedItem = "Normal",
            Color = "#00B294"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "Fn Lock",
            Value = "Off",
            Icon = "Key64",
            Type = WidgetType.Toggle,
            IsOn = false,
            Color = "#744DA9"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "Win Key",
            Value = "Off",
            Icon = "Window64",
            Type = WidgetType.Toggle,
            IsOn = false,
            Color = "#744DA9"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "Microphone",
            Value = "On",
            Icon = "Mic64",
            Type = WidgetType.Toggle,
            IsOn = true,
            Color = "#00B294"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "Touchpad Lock",
            Value = "Off",
            Icon = "Touchpad64",
            Type = WidgetType.Toggle,
            IsOn = false,
            Color = "#744DA9"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "HDR",
            Value = "Off",
            Icon = "Display64",
            Type = WidgetType.Toggle,
            IsOn = false,
            Color = "#E81123",
            IsBlocked = true
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "Hybrid Mode",
            Value = "Off",
            Icon = "Gpu64",
            Type = WidgetType.Toggle,
            IsOn = false,
            Color = "#744DA9"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "Instant Boot",
            Value = "On",
            Icon = "Flash64",
            Type = WidgetType.ComboBox,
            Items = new ObservableCollection<string> { "Disabled", "Enabled", "Enabled (Fast)" },
            SelectedItem = "Enabled",
            Color = "#744DA9"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "Panel Logo",
            Value = "Off",
            Icon = "Badge64",
            Type = WidgetType.Toggle,
            IsOn = false,
            Color = "#744DA9"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "Ports Backlight",
            Value = "Off",
            Icon = "UsbC64",
            Type = WidgetType.Toggle,
            IsOn = false,
            Color = "#744DA9"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "White KB",
            Value = "Off",
            Icon = "Keyboard64",
            Type = WidgetType.ComboBox,
            Items = new ObservableCollection<string> { "Off", "Level 1", "Level 2" },
            SelectedItem = "Off",
            Color = "#FFFFFF"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "OverDrive",
            Value = "Off",
            Icon = "Rocket64",
            Type = WidgetType.Toggle,
            IsOn = false,
            Color = "#E81123"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "dGPU",
            Value = "Active",
            Icon = "Gpu64",
            Type = WidgetType.Custom,
            Color = "#00B294"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "GPU Overclock",
            Value = "Off",
            Icon = "SpeedHigh64",
            Type = WidgetType.Toggle,
            IsOn = false,
            Color = "#E81123"
        });
        
        Widgets.Add(new DashboardWidgetViewModel
        {
            Title = "Turn Off Monitors",
            Value = "Click",
            Icon = "DisplayOff64",
            Type = WidgetType.Button,
            Color = "#E81123"
        });
    }
    
    private void SubscribeToEvents()
    {
        _performanceService.ModeChanged += mode => 
        {
            CurrentPowerMode = mode;
            PowerModeColor = GetPowerModeColor(mode);
        };
        
        _thermalService.CpuTemperatureChanged += temp => CpuTemperature = temp;
        _thermalService.GpuTemperatureChanged += temp => GpuTemperature = temp;
        _thermalService.FanSpeedChanged += rpm => 
        {
            FanSpeedRpm = rpm;
            FanSpeedPercent = (int)(rpm / 6000.0 * 100);
        };
    }
    
    private string GetPowerModeColor(PowerModeState mode)
    {
        return mode switch
        {
            PowerModeState.Quiet => "#0078D4",      // Blue
            PowerModeState.Balance => "#FFFFFF",    // White
            PowerModeState.Performance => "#E81123", // Red
            PowerModeState.GodMode => "#B400FF",    // Purple
            _ => "#FFFFFF"
        };
    }
    
    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Trigger sensor refresh
    }
}

public partial class DashboardWidgetViewModel : ViewModelBase
{
    public string Title { get; init; } = "";
    public string Value { get; set; } = "";
    public string Unit { get; init; } = "";
    public string Icon { get; init; } = "";
    public WidgetType Type { get; init; }
    public double MinValue { get; init; }
    public double MaxValue { get; init; }
    public double CurrentValue { get; set; }
    public string Color { get; init; } = "#FFFFFF";
    public ObservableCollection<string> Items { get; init; } = new();
    public string SelectedItem { get; set; } = "";
    public bool IsOn { get; set; }
    public bool IsBlocked { get; init; } = false;
}

public enum WidgetType
{
    Sensor,
    Toggle,
    ComboBox,
    Button,
    Custom
}