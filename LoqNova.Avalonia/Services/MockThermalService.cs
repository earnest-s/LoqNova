using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class MockThermalService : IThermalService
{
    private double _cpuTemp = 54;
    private double _gpuTemp = 49;
    private int _fanRpm = 2400;
    private int _fanPercent = 45;
    
    public double CpuTemperature => _cpuTemp;
    public double GpuTemperature => _gpuTemp;
    public int FanSpeedRpm => _fanRpm;
    public int FanSpeedPercent => _fanPercent;
    public bool IsFanControlSupported { get; } = true;
    
    public event Action<double>? CpuTemperatureChanged;
    public event Action<double>? GpuTemperatureChanged;
    public event Action<int>? FanSpeedChanged;
    
    private readonly Random _random = new();
    private readonly System.Timers.Timer _timer;
    
    public MockThermalService()
    {
        _timer = new System.Timers.Timer(2000);
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
        _cpuTemp += (_random.NextDouble() - 0.5) * 2;
        _cpuTemp = Math.Clamp(_cpuTemp, 40, 90);
        CpuTemperatureChanged?.Invoke(_cpuTemp);
        
        _gpuTemp += (_random.NextDouble() - 0.5) * 2;
        _gpuTemp = Math.Clamp(_gpuTemp, 35, 85);
        GpuTemperatureChanged?.Invoke(_gpuTemp);
        
        _fanRpm += (int)((_random.NextDouble() - 0.5) * 200);
        _fanRpm = Math.Clamp(_fanRpm, 1000, 5000);
        _fanPercent = (int)(_fanRpm / 5000.0 * 100);
        FanSpeedChanged?.Invoke(_fanRpm);
    }
    
    public Task SetFanSpeedAsync(int percent)
    {
        _fanPercent = Math.Clamp(percent, 0, 100);
        _fanRpm = (int)(_fanPercent / 100.0 * 5000);
        FanSpeedChanged?.Invoke(_fanRpm);
        return Task.CompletedTask;
    }
    
    public Task SetFanCurveAsync(FanCurvePoint[] curve) => Task.CompletedTask;
}