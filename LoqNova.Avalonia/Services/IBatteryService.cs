using System;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public enum BatteryState
{
    Normal,
    RapidCharge,
    Conservation
}

public enum BatteryNightChargeState
{
    Disabled,
    Enabled
}

public interface IBatteryService
{
    int Percentage { get; }
    string StatusText { get; }
    bool IsCharging { get; }
    bool IsLowBattery { get; }
    bool IsLowWattageCharger { get; }
    double TemperatureC { get; }
    double TemperatureF { get; }
    double DischargeRate { get; }
    double MinDischargeRate { get; }
    double MaxDischargeRate { get; }
    int CurrentCapacity { get; }
    int FullChargeCapacity { get; }
    int DesignCapacity { get; }
    int HealthPercent { get; }
    TimeSpan OnBatteryDuration { get; }
    DateTime? OnBatterySince { get; }
    int CycleCount { get; }
    DateTime? ManufactureDate { get; }
    DateTime? FirstUseDate { get; }
    BatteryState CurrentMode { get; }
    BatteryNightChargeState NightChargeMode { get; }
    bool IsPowerAdapterConnected { get; }
    
    event Action<int>? PercentageChanged;
    event Action<bool>? ChargingChanged;
    event Action<BatteryState>? ModeChanged;
    
    Task InitializeAsync();
    Task SetModeAsync(BatteryState mode);
    Task SetNightChargeAsync(BatteryNightChargeState state);
}