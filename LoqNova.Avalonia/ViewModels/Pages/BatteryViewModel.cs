using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.ViewModels.Pages;

public partial class BatteryViewModel : ViewModelBase
{
    private readonly IBatteryService _batteryService;
    private readonly ISettingsService _settingsService;
    
    [ObservableProperty]
    private int _percentage = 82;
    
    [ObservableProperty]
    private string _statusText = "On battery";
    
    [ObservableProperty]
    private bool _isCharging = false;
    
    [ObservableProperty]
    private bool _isLowBattery = false;
    
    [ObservableProperty]
    private bool _isLowWattageCharger = false;
    
    [ObservableProperty]
    private double _temperatureC = 32.5;
    
    [ObservableProperty]
    private double _temperatureF = 90.5;
    
    [ObservableProperty]
    private double _dischargeRate = 12.4;
    
    [ObservableProperty]
    private double _minDischargeRate = 8.2;
    
    [ObservableProperty]
    private double _maxDischargeRate = 28.7;
    
    [ObservableProperty]
    private int _currentCapacity = 45600;
    
    [ObservableProperty]
    private int _fullChargeCapacity = 55800;
    
    [ObservableProperty]
    private int _designCapacity = 60000;
    
    [ObservableProperty]
    private int _healthPercent = 93;
    
    [ObservableProperty]
    private TimeSpan _onBatteryDuration = TimeSpan.FromHours(2.5);
    
    [ObservableProperty]
    private DateTime? _onBatterySince = DateTime.Now.AddHours(-2.5);
    
    [ObservableProperty]
    private int _cycleCount = 127;
    
    [ObservableProperty]
    private DateTime? _manufactureDate = new DateTime(2024, 3, 15);
    
    [ObservableProperty]
    private DateTime? _firstUseDate = new DateTime(2024, 5, 20);
    
    [ObservableProperty]
    private BatteryState _currentMode = BatteryState.Normal;
    
    [ObservableProperty]
    private BatteryNightChargeState _nightChargeMode = BatteryNightChargeState.Disabled;
    
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
        BatteryNightChargeState.Disabled, BatteryNightChargeState.Enabled
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
        _ = _settingsService.SaveAsync();
    }
    
    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Trigger refresh
    }
}