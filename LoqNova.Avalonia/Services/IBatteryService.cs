using System;
using System.Threading.Tasks;
using LoqNova.Lib;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Battery telemetry and charge-mode control. Uses the library enums directly
/// (<see cref="BatteryState"/>, <see cref="BatteryNightChargeState"/>) instead of
/// Avalonia-local copies, so the values map one to one onto
/// <c>BatteryFeature</c> and <c>BatteryNightChargeFeature</c>.
/// </summary>
public interface IBatteryService
{
    /// <summary>Charge percentage, or -1 when unknown.</summary>
    int Percentage { get; }

    /// <summary>Charging / on battery / low battery, or "Unknown".</summary>
    string StatusText { get; }

    bool IsCharging { get; }
    bool IsLowBattery { get; }

    /// <summary>True when the attached AC adapter is below the machine's wattage.</summary>
    bool IsLowWattageCharger { get; }

    double TemperatureC { get; }
    double TemperatureF { get; }
    double DischargeRate { get; }
    double MinDischargeRate { get; }
    double MaxDischargeRate { get; }

    /// <summary>Charge remaining in mWh, or -1 when unavailable.</summary>
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

    /// <summary>False when the machine has no battery.</summary>
    bool IsSupported { get; }

    event Action<int>? PercentageChanged;
    event Action<bool>? ChargingChanged;
    event Action<BatteryState>? ModeChanged;
    event Action<BatteryNightChargeState>? NightChargeChanged;

    Task InitializeAsync();

    Task RefreshAsync();

    Task SetModeAsync(BatteryState mode);

    Task SetNightChargeAsync(BatteryNightChargeState state);
}
