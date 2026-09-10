using System;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public enum PowerModeState
{
    Quiet,
    Balance,
    Performance,
    GodMode
}

public interface IPerformanceService
{
    PowerModeState CurrentMode { get; }
    bool IsSupported { get; }
    bool IsGodModeEnabled { get; }
    double CpuPowerLimit { get; set; }
    double GpuPowerLimit { get; set; }
    double CpuThermalLimit { get; set; }
    double GpuThermalLimit { get; set; }
    
    event Action<PowerModeState>? ModeChanged;
    event Action<double>? CpuPowerLimitChanged;
    event Action<double>? GpuPowerLimitChanged;
    
    Task InitializeAsync();
    Task SetModeAsync(PowerModeState mode);
    Task ApplyGodModeSettingsAsync();
}