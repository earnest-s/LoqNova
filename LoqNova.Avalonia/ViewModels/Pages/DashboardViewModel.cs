using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Avalonia.ViewModels.Dialogs;
using LoqNova.Lib;

namespace LoqNova.Avalonia.ViewModels.Pages;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IPerformanceService _performanceService;
    private readonly IRgbService _rgbService;
    private readonly IThermalService _thermalService;
    private readonly IBatteryService _batteryService;
    private readonly ISensorsService _sensorsService;
    private readonly INavigationService _navigationService;
    private bool _suppressModeWrite;
    
    // Sensor channels start as "unknown" (-1) and are only ever set from a real
    // reading. Placeholder percentages or temperatures are never displayed.
    [ObservableProperty]
    private double _cpuUsage = -1;
    
    [ObservableProperty]
    private double _gpuUsage = -1;
    
    [ObservableProperty]
    private double _cpuTemperature = -1;
    
    [ObservableProperty]
    private double _gpuTemperature = -1;
    
    [ObservableProperty]
    private int _fanSpeedRpm = -1;
    
    [ObservableProperty]
    private int _fanSpeedPercent = -1;
    
    [ObservableProperty]
    private PowerModeState _currentPowerMode = PowerModeState.Balance;
    
    /// <summary>
    /// Brush for the active power mode. Typed as <see cref="IBrush"/> because
    /// SensorsPanel.PowerModeColor is an IBrush styled property; it previously
    /// exposed a hex string, which could never bind.
    /// </summary>
    [ObservableProperty]
    private IBrush _powerModeColor = Brushes.Transparent;

    /// <summary>God Mode is only offered when the device reports support.</summary>
    public bool IsGodModeSupported => _performanceService.IsSupported;

    public ObservableCollection<PowerModeState> PowerModeItems { get; } = new()
    {
        PowerModeState.Quiet,
        PowerModeState.Balance,
        PowerModeState.Performance,
        PowerModeState.GodMode
    };

    public ObservableCollection<DashboardWidgetViewModel> Widgets { get; } = new();

    /// <summary>
    /// Non-sensor widgets. Sensor channels are already presented by
    /// SensorsPanel, so they are excluded here to avoid showing them twice.
    /// </summary>
    public ObservableCollection<DashboardWidgetViewModel> ControlWidgets { get; } = new();

    public DashboardViewModel(
        IPerformanceService performanceService,
        IRgbService rgbService,
        IThermalService thermalService,
        IBatteryService batteryService,
        ISensorsService sensorsService,
        INavigationService navigationService)
    {
        _performanceService = performanceService;
        _rgbService = rgbService;
        _thermalService = thermalService;
        _batteryService = batteryService;
        _sensorsService = sensorsService;
        _navigationService = navigationService;
        
        InitializeWidgets();
        SubscribeToEvents();
        SyncFromService();
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
            Title = "Power Mode",            Value = "Balance",
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

        // Sensor channels are rendered by SensorsPanel, so only the control
        // widgets belong on the dashboard's own widget grid.
        foreach (var widget in Widgets.Where(w => w.Type != WidgetType.Sensor))
        {
            ControlWidgets.Add(widget);
        }
    }
    
    private void SubscribeToEvents()
    {
        _performanceService.ModeChanged += mode => 
        {
            _suppressModeWrite = true;
            CurrentPowerMode = mode;
            _suppressModeWrite = false;
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

    /// <summary>Adopts whatever the performance service already reports.</summary>
    private void SyncFromService()
    {
        _suppressModeWrite = true;
        CurrentPowerMode = _performanceService.CurrentMode;
        _suppressModeWrite = false;
        PowerModeColor = GetPowerModeColor(CurrentPowerMode);
    }

    partial void OnCurrentPowerModeChanged(PowerModeState value)
    {
        PowerModeColor = GetPowerModeColor(value);

        if (_suppressModeWrite)
        {
            return;
        }

        _ = _performanceService.SetModeAsync(value);
    }

    /// <summary>
    /// Resolves the mode accent from the design system so the palette stays
    /// defined in one place.
    /// </summary>
    private static IBrush GetPowerModeColor(PowerModeState mode)
    {
        var key = mode switch
        {
            PowerModeState.Quiet => "QuietModeBrush",
            PowerModeState.Balance => "BalanceModeBrush",
            PowerModeState.Performance => "PerformanceModeBrush",
            PowerModeState.GodMode => "GodModeBrush",
            _ => "TextSecondaryBrush"
        };

        if (global::Avalonia.Application.Current is { } app &&
            app.TryGetResource(key, null, out var resource) &&
            resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _sensorsService.InitializeAsync();
        SyncFromService();
    }

    [RelayCommand]
    private async Task OpenGodModeAsync()
    {
        await _performanceService.ApplyGodModeSettingsAsync();
    }

    [RelayCommand]
    private Task EditDashboardAsync() => _navigationService.NavigateToDialogAsync<EditDashboardViewModel>();
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