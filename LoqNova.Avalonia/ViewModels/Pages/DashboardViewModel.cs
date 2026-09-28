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

    /// <summary>God Mode is only offered when the machine reports it as available.</summary>
    public bool IsGodModeSupported => _performanceService.IsGodModeSupported;

    /// <summary>Populated from the machine's reported power modes.</summary>
    public ObservableCollection<PowerModeState> PowerModeItems { get; } = new();

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
        _ = InitializeServicesAsync();
    }

    /// <summary>
    /// Starts the real power mode and sensor backends. Failures are logged and
    /// leave the page in its "unknown" state rather than showing placeholder data.
    /// </summary>
    private async Task InitializeServicesAsync()
    {
        try
        {
            await _performanceService.InitializeAsync();

            PowerModeItems.Clear();
            foreach (var state in _performanceService.AvailableStates)
                PowerModeItems.Add(state);

            OnPropertyChanged(nameof(IsGodModeSupported));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Power mode initialization failed: {ex}");
        }

        try
        {
            await _sensorsService.InitializeAsync();
            await _thermalService.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sensor initialization failed: {ex}");
        }
    }
    
    /// <summary>
    /// Builds the dashboard's control widgets from live backend state only.
    /// Sensor channels are presented by SensorsPanel, so they are not repeated
    /// here. A widget is only created when a real adapter for that feature is
    /// registered; unsupported features are omitted rather than shown with
    /// placeholder values, mirroring WPF's AbstractRefreshingControl, which
    /// collapses itself when the backend reports NotSupportedException.
    /// </summary>
    private void InitializeWidgets()
    {
        if (_performanceService.IsSupported)
        {
            var powerMode = new DashboardWidgetViewModel
            {
                Title = "Power Mode",
                Icon = "Bolt64",
                Type = WidgetType.ComboBox,
                ItemsSource = PowerModeItems,
                SelectedItem = CurrentPowerMode,
                IsAvailable = true
            };

            powerMode.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DashboardWidgetViewModel.SelectedItem) &&
                    powerMode.SelectedItem is PowerModeState mode &&
                    !_suppressModeWrite)
                {
                    _ = _performanceService.SetModeAsync(mode);
                }
            };

            ControlWidgets.Add(powerMode);
        }

        if (_batteryService.IsSupported)
        {
            var battery = new DashboardWidgetViewModel
            {
                Title = "Battery Charge Mode",
                Icon = "Battery64",
                Type = WidgetType.ComboBox,
                ItemsSource = BatteryModes,
                SelectedItem = _batteryService.CurrentMode,
                IsAvailable = true
            };

            battery.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DashboardWidgetViewModel.SelectedItem) &&
                    battery.SelectedItem is BatteryState mode)
                {
                    _ = _batteryService.SetModeAsync(mode);
                }
            };

            _batteryService.ModeChanged += mode =>
            {
                battery.SelectedItem = mode;
                battery.Status = null;
            };

            ControlWidgets.Add(battery);

            var nightCharge = new DashboardWidgetViewModel
            {
                Title = "Night Charge",
                Icon = "Moon64",
                Type = WidgetType.ComboBox,
                ItemsSource = NightChargeModes,
                SelectedItem = _batteryService.NightChargeMode,
                IsAvailable = true
            };

            nightCharge.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DashboardWidgetViewModel.SelectedItem) &&
                    nightCharge.SelectedItem is BatteryNightChargeState state)
                {
                    _ = _batteryService.SetNightChargeAsync(state);
                }
            };

            _batteryService.NightChargeChanged += state =>
            {
                nightCharge.SelectedItem = state;
                nightCharge.Status = null;
            };

            ControlWidgets.Add(nightCharge);
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
        _thermalService.FanSpeedChanged += _ =>
        {
            FanSpeedRpm = _thermalService.FanSpeedRpm;
            FanSpeedPercent = _thermalService.FanSpeedPercent;
        };

        _sensorsService.CpuUsageChanged += usage => CpuUsage = usage;
        _sensorsService.GpuUsageChanged += usage => GpuUsage = usage;
    }

    /// <summary>Adopts whatever the backends already report.</summary>
    private void SyncFromService()
    {
        _suppressModeWrite = true;
        CurrentPowerMode = _performanceService.CurrentMode;
        _suppressModeWrite = false;
        PowerModeColor = GetPowerModeColor(CurrentPowerMode);

        if (_sensorsService.CpuUsage >= 0)
            CpuUsage = _sensorsService.CpuUsage;

        if (_sensorsService.GpuUsage >= 0)
            GpuUsage = _sensorsService.GpuUsage;

        if (_thermalService.CpuTemperature >= 0)
            CpuTemperature = _thermalService.CpuTemperature;

        if (_thermalService.GpuTemperature >= 0)
            GpuTemperature = _thermalService.GpuTemperature;

        if (_thermalService.FanSpeedRpm >= 0)
        {
            FanSpeedRpm = _thermalService.FanSpeedRpm;
            FanSpeedPercent = _thermalService.FanSpeedPercent;
        }
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
        await _performanceService.RefreshAsync();
        await _sensorsService.InitializeAsync();
        await _thermalService.InitializeAsync();
        SyncFromService();
    }

    [RelayCommand]
    private async Task OpenGodModeAsync()
    {
        if (!_performanceService.IsGodModeSupported)
            return;

        await _performanceService.SetModeAsync(PowerModeState.GodMode);
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
