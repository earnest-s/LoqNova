using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class MockSensorsService : ISensorsService
{
    private double _cpuUsage = 12;
    private double _gpuUsage = 3;
    private double _cpuTemp = 54;
    private double _gpuTemp = 49;
    private int _fanRpm = 2400;
    
    public double CpuUsage => _cpuUsage;
    public double GpuUsage => _gpuUsage;
    public double CpuTemperature => _cpuTemp;
    public double GpuTemperature => _gpuTemp;
    public int FanSpeedRpm => _fanRpm;
    
    public event Action<double>? CpuUsageChanged;
    public event Action<double>? GpuUsageChanged;
    public event Action<double>? CpuTemperatureChanged;
    public event Action<double>? GpuTemperatureChanged;
    public event Action<int>? FanSpeedChanged;
    
    private readonly Random _random = new();
    private readonly System.Timers.Timer _timer;
    
    public MockSensorsService()
    {
        _timer = new System.Timers.Timer(1000);
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
        _cpuUsage += (_random.NextDouble() - 0.5) * 10;
        _cpuUsage = Math.Clamp(_cpuUsage, 0, 100);
        CpuUsageChanged?.Invoke(_cpuUsage);
        
        _gpuUsage += (_random.NextDouble() - 0.5) * 5;
        _gpuUsage = Math.Clamp(_gpuUsage, 0, 100);
        GpuUsageChanged?.Invoke(_gpuUsage);
        
        _cpuTemp += (_random.NextDouble() - 0.5) * 2;
        _cpuTemp = Math.Clamp(_cpuTemp, 35, 90);
        CpuTemperatureChanged?.Invoke(_cpuTemp);
        
        _gpuTemp += (_random.NextDouble() - 0.5) * 2;
        _gpuTemp = Math.Clamp(_gpuTemp, 30, 85);
        GpuTemperatureChanged?.Invoke(_gpuTemp);
        
        _fanRpm += (int)((_random.NextDouble() - 0.5) * 300);
        _fanRpm = Math.Clamp(_fanRpm, 1000, 6000);
        FanSpeedChanged?.Invoke(_fanRpm);
    }
}