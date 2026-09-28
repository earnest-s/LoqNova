using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;

namespace LoqNova.Avalonia.ViewModels.Pages;

public partial class BatteryViewModel : ViewModelBase
{
    private readonly IBatteryService _batteryService;
    private readonly ISettingsService _settingsService;
    
    [ObservableProperty]
    private int _percentage = -1;
    
    [ObservableProperty]
    private string _statusText = "On battery";
    
    [ObservableProperty]
    private bool _isCharging = false;
    
    [ObservableProperty]
    private bool _isLowBattery = false;
    
    [ObservableProperty]
    private bool _isLowWattageCharger = false;
    
    [ObservableProperty]
    private double _temperatureC = -1;
    
    [ObservableProperty]
    private double _temperatureF = -1;
    
    [ObservableProperty]
    private double _dischargeRate = -1;
    
    [ObservableProperty]
    private double _minDischargeRate = -1;
    
    [ObservableProperty]
    private double _maxDischargeRate = -1;
    
    [ObservableProperty]
    private int _currentCapacity = -1;
    
    [ObservableProperty]
    private int _fullChargeCapacity = -1;
    
    [ObservableProperty]
    private int _designCapacity = -1;
    
    [ObservableProperty]
    private int _healthPercent = -1;
    
    [ObservableProperty]
    private TimeSpan _onBatteryDuration = TimeSpan.FromHours(2.5);
    
    [ObservableProperty]
    private DateTime? _onBatterySince = DateTime.Now.AddHours(-2.5);
    
    [ObservableProperty]
    private int _cycleCount = -1;
    
    [ObservableProperty]
    private DateTime? _manufactureDate = new DateTime(2024, 3, 15);
    
    [ObservableProperty]
    private DateTime? _firstUseDate = new DateTime(2024, 5, 20);
    
    [ObservableProperty]
    private BatteryState _currentMode = BatteryState.Normal;
    
    [ObservableProperty]
    private BatteryNightChargeState _nightChargeMode = BatteryNightChargeState.Off;
    
    [ObservableProperty]
    private bool _isPowerAdapterConnected = false;
    
    [ObservableProperty]
    private bool _useFahrenheit = false;
    
    public ObservableCollection<BatteryState> BatteryModes { get; } = new()
    {
        BatteryState.Normal, BatteryState.RapidCharge, BatteryState.Conservation
    };
    
    public ObservableCollection<BatteryNightChargeState> NightChargeModes { get; } = new()
    {
        BatteryNightChargeState.Off, BatteryNightChargeState.On
    };
    
    public BatteryViewModel(IBatteryService batteryService, ISettingsService settingsService)
    {
        _batteryService = batteryService;
        _settingsService = settingsService;
        
        _useFahrenheit = _settingsService.TemperatureUnitFahrenheit;
        
        SubscribeToEvents();
        LoadCurrentState();
    }
    
    private void SubscribeToEvents()
    {
        _batteryService.PercentageChanged += p => Percentage = p;
        _batteryService.ChargingChanged += c => IsCharging = c;
        _batteryService.ModeChanged += m => CurrentMode = m;
    }
    
    private void LoadCurrentState()
    {
        Percentage = _batteryService.Percentage;
        StatusText = _batteryService.StatusText;
        IsCharging = _batteryService.IsCharging;
        IsLowBattery = _batteryService.IsLowBattery;
        IsLowWattageCharger = _batteryService.IsLowWattageCharger;
        TemperatureC = _batteryService.TemperatureC;
        TemperatureF = _batteryService.TemperatureF;
        DischargeRate = _batteryService.DischargeRate;
        MinDischargeRate = _batteryService.MinDischargeRate;
        MaxDischargeRate = _batteryService.MaxDischargeRate;
        CurrentCapacity = _batteryService.CurrentCapacity;
        FullChargeCapacity = _batteryService.FullChargeCapacity;
        DesignCapacity = _batteryService.DesignCapacity;
        HealthPercent = _batteryService.HealthPercent;
        OnBatteryDuration = _batteryService.OnBatteryDuration;
        OnBatterySince = _batteryService.OnBatterySince;
        CycleCount = _batteryService.CycleCount;
        ManufactureDate = _batteryService.ManufactureDate;
        FirstUseDate = _batteryService.FirstUseDate;
        CurrentMode = _batteryService.CurrentMode;
        NightChargeMode = _batteryService.NightChargeMode;
        IsPowerAdapterConnected = _batteryService.IsPowerAdapterConnected;
    }
    
    partial void OnCurrentModeChanged(BatteryState value)
    {
        _ = _batteryService.SetModeAsync(value);
    }
    
    partial void OnNightChargeModeChanged(BatteryNightChargeState value)
    {
        _ = _batteryService.SetNightChargeAsync(value);
    }
    
    partial void OnUseFahrenheitChanged(bool value)
    {
        _settingsService.TemperatureUnitFahrenheit = value;
        OnPropertyChanged(nameof(TemperatureText));
    }

    /// <summary>
    /// Temperature in the unit the user selected, or "--" when the machine has not
    /// reported one. -1 is the "unknown" sentinel, so it is never displayed as a
    /// reading.
    /// </summary>
    public string TemperatureText
    {
        get
        {
            var value = UseFahrenheit ? _temperatureF : _temperatureC;
            return value < 0 ? "--" : $"{value:F1} °{(UseFahrenheit ? "F" : "C")}";
        }
    }

    partial void OnTemperatureCChanged(double value) => OnPropertyChanged(nameof(TemperatureText));

    partial void OnTemperatureFChanged(double value) => OnPropertyChanged(nameof(TemperatureText));
    
    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Trigger refresh
    }
}