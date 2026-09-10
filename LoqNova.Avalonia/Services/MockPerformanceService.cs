using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class MockPerformanceService : IPerformanceService
{
    public PowerModeState CurrentMode { get; private set; } = PowerModeState.Balance;
    public bool IsSupported { get; } = true;
    public bool IsGodModeEnabled { get; private set; } = false;
    
    private double _cpuPowerLimit = 45;
    private double _gpuPowerLimit = 80;
    private double _cpuThermalLimit = 85;
    private double _gpuThermalLimit = 83;
    
    public double CpuPowerLimit
    {
        get => _cpuPowerLimit;
        set { _cpuPowerLimit = value; CpuPowerLimitChanged?.Invoke(value); }
    }
    
    public double GpuPowerLimit
    {
        get => _gpuPowerLimit;
        set { _gpuPowerLimit = value; GpuPowerLimitChanged?.Invoke(value); }
    }
    
    public double CpuThermalLimit
    {
        get => _cpuThermalLimit;
        set { _cpuThermalLimit = value; }
    }
    
    public double GpuThermalLimit
    {
        get => _gpuThermalLimit;
        set { _gpuThermalLimit = value; }
    }
    
    public event Action<PowerModeState>? ModeChanged;
    public event Action<double>? CpuPowerLimitChanged;
    public event Action<double>? GpuPowerLimitChanged;
    
    public Task InitializeAsync()
    {
        CurrentMode = PowerModeState.Balance;
        return Task.CompletedTask;
    }
    
    public Task SetModeAsync(PowerModeState mode)
    {
        CurrentMode = mode;
        ModeChanged?.Invoke(mode);
        return Task.CompletedTask;
    }
    
    public Task ApplyGodModeSettingsAsync()
    {
        IsGodModeEnabled = !IsGodModeEnabled;
        return Task.CompletedTask;
    }
}