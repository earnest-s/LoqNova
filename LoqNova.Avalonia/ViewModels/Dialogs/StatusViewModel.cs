using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// Summary status (the WPF StatusWindow equivalent). Every value is read from a
/// live backend adapter; unavailable values are reported as such rather than
/// filled in with example numbers.
/// </summary>
public partial class StatusViewModel : ViewModelBase
{
    private const string Unknown = "--";

    private readonly IPerformanceService _performanceService;
    private readonly ISensorsService _sensorsService;
    private readonly IThermalService _thermalService;
    private readonly IBatteryService _batteryService;
    private readonly IRgbService _rgbService;
    private readonly IMainThreadDispatcher _dispatcher;

    [ObservableProperty]
    private string _batteryStatus = Unknown;

    [ObservableProperty]
    private string _powerModeStatus = Unknown;

    [ObservableProperty]
    private string _cpuTempStatus = Unknown;

    [ObservableProperty]
    private string _gpuTempStatus = Unknown;

    [ObservableProperty]
    private string _fanStatus = Unknown;

    [ObservableProperty]
    private string _rgbStatus = Unknown;

    [ObservableProperty]
    private bool _isCharging;

    public StatusViewModel(
        IPerformanceService performanceService,
        ISensorsService sensorsService,
        IThermalService thermalService,
        IBatteryService batteryService,
        IRgbService rgbService,
        IMainThreadDispatcher dispatcher)
    {
        _performanceService = performanceService;
        _sensorsService = sensorsService;
        _thermalService = thermalService;
        _batteryService = batteryService;
        _rgbService = rgbService;
        _dispatcher = dispatcher;

        Subscribe();
    }

    private void Subscribe()
    {
        _performanceService.ModeChanged += _ => _dispatcher.Post(Update);
        _batteryService.PercentageChanged += _ => _dispatcher.Post(Update);
        _batteryService.ChargingChanged += _ => _dispatcher.Post(Update);
        _sensorsService.Updated += () => _dispatcher.Post(Update);
        _thermalService.CpuTemperatureChanged += _ => _dispatcher.Post(Update);
        _thermalService.FanSpeedChanged += _ => _dispatcher.Post(Update);
        _rgbService.PresetChanged += _ => _dispatcher.Post(Update);
        _rgbService.EffectChanged += _ => _dispatcher.Post(Update);

        Update();
    }

    public Task InitializeAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _performanceService.RefreshAsync();
        await _batteryService.RefreshAsync();
        Update();
    }

    private void Update()
    {
        PowerModeStatus = _performanceService.IsSupported
            ? _performanceService.CurrentMode.ToString()
            : Unknown;

        IsCharging = _batteryService.IsCharging;
        BatteryStatus = !_batteryService.IsSupported
            ? Unknown
            : _batteryService.Percentage >= 0
                ? $"{_batteryService.Percentage}% - {_batteryService.StatusText}"
                : $"{_batteryService.StatusText}";

        CpuTempStatus = _thermalService.CpuTemperature >= 0
            ? $"{_thermalService.CpuTemperature:0}°C"
            : Unknown;

        GpuTempStatus = _thermalService.GpuTemperature >= 0
            ? $"{_thermalService.GpuTemperature:0}°C"
            : Unknown;

        FanStatus = _thermalService.FanSpeedRpm >= 0
            ? _thermalService.FanSpeedPercent >= 0
                ? $"{_thermalService.FanSpeedRpm} RPM ({_thermalService.FanSpeedPercent}%)"
                : $"{_thermalService.FanSpeedRpm} RPM"
            : Unknown;

        RgbStatus = !_rgbService.IsSupported
            ? Unknown
            : $"{_rgbService.CurrentPreset} - {_rgbService.CurrentEffect}";
    }

    [RelayCommand]
    private Task CloseAsync() => Task.CompletedTask;
}
