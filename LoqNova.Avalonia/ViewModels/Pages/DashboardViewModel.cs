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
    private readonly IMainThreadDispatcher _dispatcher;
    private bool _suppressModeWrite;
    
    // Telemetry: every value and its maximum come from the library sensor
    // controller. -1 means "not reported" and is rendered as "--", never as a value.
    [ObservableProperty] private int _cpuUtilization = -1;
    [ObservableProperty] private int _cpuMaxUtilization = -1;
    [ObservableProperty] private int _cpuCoreClock = -1;
    [ObservableProperty] private int _cpuMaxCoreClock = -1;
    [ObservableProperty] private int _cpuTemperature = -1;
    [ObservableProperty] private int _cpuMaxTemperature = -1;
    [ObservableProperty] private int _cpuFanSpeed = -1;
    [ObservableProperty] private int _cpuMaxFanSpeed = -1;

    [ObservableProperty] private int _gpuUtilization = -1;
    [ObservableProperty] private int _gpuMaxUtilization = -1;
    [ObservableProperty] private int _gpuCoreClock = -1;
    [ObservableProperty] private int _gpuMaxCoreClock = -1;
    [ObservableProperty] private int _gpuMemoryClock = -1;
    [ObservableProperty] private int _gpuMaxMemoryClock = -1;
    [ObservableProperty] private int _gpuTemperature = -1;
    [ObservableProperty] private int _gpuMaxTemperature = -1;
    [ObservableProperty] private int _gpuFanSpeed = -1;
    [ObservableProperty] private int _gpuMaxFanSpeed = -1;

    /// <summary>False when the machine reports no supported sensor source.</summary>
    [ObservableProperty]
    private bool _isSensorsSupported;

    /// <summary>True while the sensor refresh loop is running.</summary>
    [ObservableProperty]
    private bool _isRefreshing;
    
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

    /// <summary>Charge modes the machine's battery feature accepts.</summary>
    public ObservableCollection<BatteryState> BatteryModes { get; } =
        [BatteryState.Normal, BatteryState.RapidCharge, BatteryState.Conservation];

    public ObservableCollection<BatteryNightChargeState> NightChargeModes { get; } =
        [BatteryNightChargeState.On, BatteryNightChargeState.Off];

    /// <summary>
    /// Feature widgets backed by live library features. Only widgets the machine
    /// reports as supported are added. Sensor channels are presented separately by
    /// SensorsPanel, so they are not repeated here.
    /// </summary>
    public ObservableCollection<LoqNova.Avalonia.ViewModels.Controls.FeatureWidgetViewModel> ControlWidgets { get; } = new();

    /// <summary>Telemetry cards, in WPF's SensorsControl order.</summary>
    public ObservableCollection<LoqNova.Avalonia.ViewModels.Controls.SensorMetricViewModel> SensorMetrics { get; } = new();

    private readonly LoqNova.Avalonia.ViewModels.Controls.SensorMetricViewModel _cpuUtilizationMetric = New("CPU", "%", "SensorCpuBrush", "Cpu");
    private readonly LoqNova.Avalonia.ViewModels.Controls.SensorMetricViewModel _cpuCoreClockMetric = New("CPU CLOCK", "MHz", "SensorCpuBrush", "Cpu");
    private readonly LoqNova.Avalonia.ViewModels.Controls.SensorMetricViewModel _cpuTemperatureMetric = New("CPU TEMP", "°C", "SensorTempBrush", "Thermometer");
    private readonly LoqNova.Avalonia.ViewModels.Controls.SensorMetricViewModel _cpuFanSpeedMetric = New("CPU FAN", "RPM", "SensorFanBrush", "Fan");
    private readonly LoqNova.Avalonia.ViewModels.Controls.SensorMetricViewModel _gpuUtilizationMetric = New("GPU", "%", "SensorGpuBrush", "Gpu");
    private readonly LoqNova.Avalonia.ViewModels.Controls.SensorMetricViewModel _gpuCoreClockMetric = New("GPU CLOCK", "MHz", "SensorGpuBrush", "Gpu");
    private readonly LoqNova.Avalonia.ViewModels.Controls.SensorMetricViewModel _gpuMemoryClockMetric = New("GPU MEM", "MHz", "SensorGpuBrush", "Gpu");
    private readonly LoqNova.Avalonia.ViewModels.Controls.SensorMetricViewModel _gpuTemperatureMetric = New("GPU TEMP", "°C", "SensorTempBrush", "Thermometer");
    private readonly LoqNova.Avalonia.ViewModels.Controls.SensorMetricViewModel _gpuFanSpeedMetric = New("GPU FAN", "RPM", "SensorFanBrush", "Fan");

    private static LoqNova.Avalonia.ViewModels.Controls.SensorMetricViewModel New(
        string label, string unit, string accent, string icon) =>
        new() { Label = label, Unit = unit, AccentKey = accent, IconKey = icon };

    public DashboardViewModel(
        IPerformanceService performanceService,
        IRgbService rgbService,
        IThermalService thermalService,
        IBatteryService batteryService,
        ISensorsService sensorsService,
        INavigationService navigationService,
        IMainThreadDispatcher dispatcher)
    {
        _performanceService = performanceService;
        _rgbService = rgbService;
        _thermalService = thermalService;
        _batteryService = batteryService;
        _sensorsService = sensorsService;
        _navigationService = navigationService;
        _dispatcher = dispatcher;

        InitializeWidgets();
        SubscribeToEvents();
        SyncTelemetry();
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
        _ = BuildWidgetsAsync();
    }

    /// <summary>
    /// Creates one widget per WPF dashboard feature. Every widget resolves its
    /// library <c>IFeature&lt;T&gt;</c> and hides itself when the backend reports
    /// the feature as unsupported, so the dashboard only ever shows controls that
    /// actually work on this machine.
    /// </summary>
    private async Task BuildWidgetsAsync()
    {
        var widgets = await DashboardFeatureRegistry.CreateAllAsync(
            DashboardFeatureRegistry.DefaultFeatures, _dispatcher);

        var available = widgets.Where(w => w.IsAvailable).ToList();

        await _dispatcher.InvokeAsync(() =>
        {
            ControlWidgets.Clear();
            foreach (var widget in available)
                ControlWidgets.Add(widget);
        });

        await _batteryService.InitializeAsync();
    }

    private void SubscribeToEvents()
    {
        _performanceService.ModeChanged += _ => _dispatcher.Post(SyncTelemetry);

        // One publish per read, matching WPF's single UpdateValues call.
        _sensorsService.Updated += () => _dispatcher.Post(SyncTelemetry);
        _sensorsService.SupportChanged += () => _dispatcher.Post(SyncTelemetry);
    }

    /// <summary>Copies the live backend state onto the view model. Never invents values.</summary>
    private void SyncTelemetry()
    {
        CurrentPowerMode = _performanceService.CurrentMode;

        IsSensorsSupported = _sensorsService.IsSupported;
        IsRefreshing = _sensorsService.IsRefreshing;

        if (!_sensorsService.IsSupported)
        {
            ResetTelemetry();
            return;
        }

        CpuUtilization = _sensorsService.CpuUtilization;
        CpuMaxUtilization = _sensorsService.CpuMaxUtilization;
        CpuCoreClock = _sensorsService.CpuCoreClock;
        CpuMaxCoreClock = _sensorsService.CpuMaxCoreClock;
        CpuTemperature = _sensorsService.CpuTemperature;
        CpuMaxTemperature = _sensorsService.CpuMaxTemperature;
        CpuFanSpeed = _sensorsService.CpuFanSpeed;
        CpuMaxFanSpeed = _sensorsService.CpuMaxFanSpeed;

        GpuUtilization = _sensorsService.GpuUtilization;
        GpuMaxUtilization = _sensorsService.GpuMaxUtilization;
        GpuCoreClock = _sensorsService.GpuCoreClock;
        GpuMaxCoreClock = _sensorsService.GpuMaxCoreClock;
        GpuMemoryClock = _sensorsService.GpuMemoryClock;
        GpuMaxMemoryClock = _sensorsService.GpuMaxMemoryClock;
        GpuTemperature = _sensorsService.GpuTemperature;
        GpuMaxTemperature = _sensorsService.GpuMaxTemperature;
        GpuFanSpeed = _sensorsService.GpuFanSpeed;
        GpuMaxFanSpeed = _sensorsService.GpuMaxFanSpeed;

        PublishMetrics();
    }

    /// <summary>Mirrors the readings into the metric cards the sensors panel renders.</summary>
    private void PublishMetrics()
    {
        _cpuUtilizationMetric.Update(CpuUtilization, CpuMaxUtilization);
        _cpuCoreClockMetric.Update(CpuCoreClock, CpuMaxCoreClock);
        _cpuTemperatureMetric.Update(CpuTemperature, CpuMaxTemperature);
        _cpuFanSpeedMetric.Update(CpuFanSpeed, CpuMaxFanSpeed);

        _gpuUtilizationMetric.Update(GpuUtilization, GpuMaxUtilization);
        _gpuCoreClockMetric.Update(GpuCoreClock, GpuMaxCoreClock);
        _gpuMemoryClockMetric.Update(GpuMemoryClock, GpuMaxMemoryClock);
        _gpuTemperatureMetric.Update(GpuTemperature, GpuMaxTemperature);
        _gpuFanSpeedMetric.Update(GpuFanSpeed, GpuMaxFanSpeed);
    }

    /// <summary>Clears every telemetry channel to "not reported" when the source is unsupported.</summary>
    private void ResetTelemetry()
    {
        CpuUtilization = CpuMaxUtilization = CpuCoreClock = CpuMaxCoreClock = -1;
        CpuTemperature = CpuMaxTemperature = CpuFanSpeed = CpuMaxFanSpeed = -1;
        GpuUtilization = GpuMaxUtilization = GpuCoreClock = GpuMaxCoreClock = -1;
        GpuMemoryClock = GpuMaxMemoryClock = GpuTemperature = GpuMaxTemperature = -1;
        GpuFanSpeed = GpuMaxFanSpeed = -1;
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
        await _batteryService.RefreshAsync();
        SyncTelemetry();
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

/// <summary>
/// A dashboard control widget bound to a live backend feature. Values are only
/// ever populated from a real reading; nothing is pre-filled.
