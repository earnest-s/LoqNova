using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class MockBatteryService : IBatteryService
{
    private int _percentage = 82;
    private bool _isCharging = false;
    private double _temperatureC = 32.5;
    private double _dischargeRate = 12.4;
    private double _minDischargeRate = 8.2;
    private double _maxDischargeRate = 28.7;
    private int _currentCapacity = 45600;
    private int _fullChargeCapacity = 55800;
    private int _designCapacity = 60000;
    private int _healthPercent = 93;
    private TimeSpan _onBatteryDuration = TimeSpan.FromHours(2.5);
    private DateTime _onBatterySince = DateTime.Now.AddHours(-2.5);
    private int _cycleCount = 127;
    private DateTime _manufactureDate = new DateTime(2024, 3, 15);
    private DateTime _firstUseDate = new DateTime(2024, 5, 20);
    private BatteryState _currentMode = BatteryState.Normal;
    private BatteryNightChargeState _nightChargeMode = BatteryNightChargeState.Disabled;
    private bool _isPowerAdapterConnected = false;
    
    public int Percentage => _percentage;
    public string StatusText => _isCharging ? "Charging" : (_isPowerAdapterConnected ? "Plugged in" : "On battery");
    public bool IsCharging => _isCharging;
    public bool IsLowBattery => _percentage < 20;
    public bool IsLowWattageCharger => false;
    public double TemperatureC => _temperatureC;
    public double TemperatureF => _temperatureC * 9 / 5 + 32;
    public double DischargeRate => _dischargeRate;
    public double MinDischargeRate => _minDischargeRate;
    public double MaxDischargeRate => _maxDischargeRate;
    public int CurrentCapacity => _currentCapacity;
    public int FullChargeCapacity => _fullChargeCapacity;
    public int DesignCapacity => _designCapacity;
    public int HealthPercent => _healthPercent;
    public TimeSpan OnBatteryDuration => _onBatteryDuration;
    public DateTime? OnBatterySince => _onBatterySince;
    public int CycleCount => _cycleCount;
    public DateTime? ManufactureDate => _manufactureDate;
    public DateTime? FirstUseDate => _firstUseDate;
    public BatteryState CurrentMode => _currentMode;
    public BatteryNightChargeState NightChargeMode => _nightChargeMode;
    public bool IsPowerAdapterConnected => _isPowerAdapterConnected;
    
    public event Action<int>? PercentageChanged;
    public event Action<bool>? ChargingChanged;
    public event Action<BatteryState>? ModeChanged;
    
    private readonly Random _random = new();
    private readonly System.Timers.Timer _timer;
    
    public MockBatteryService()
    {
        _timer = new System.Timers.Timer(3000);
        _timer.Elapsed += (_, _) => SimulateChanges();
        _timer.AutoReset = true;
    }
    
    public Task InitializeAsync()
    {
        _timer.Start();
        return Task.CompletedTask;
    }
    
    private void SimulateChanges()
    {
        if (!_isCharging && !_isPowerAdapterConnected)
        {
            _percentage = Math.Max(0, _percentage - _random.Next(0, 2));
            _dischargeRate = Math.Max(0, _dischargeRate + (_random.NextDouble() - 0.5) * 3);
            _onBatteryDuration = DateTime.Now - _onBatterySince;
        }
        
        _temperatureC += (_random.NextDouble() - 0.5) * 1.5;
        _temperatureC = Math.Clamp(_temperatureC, 25, 45);
        
        PercentageChanged?.Invoke(_percentage);
    }
    
    public Task SetModeAsync(BatteryState mode)
    {
        _currentMode = mode;
        ModeChanged?.Invoke(mode);
        return Task.CompletedTask;
    }
    
    public Task SetNightChargeAsync(BatteryNightChargeState state)
    {
        _nightChargeMode = state;
        return Task.CompletedTask;
    }
}