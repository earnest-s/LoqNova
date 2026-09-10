using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public interface IThermalService
{
    double CpuTemperature { get; }
    double GpuTemperature { get; }
    int FanSpeedRpm { get; }
    int FanSpeedPercent { get; }
    bool IsFanControlSupported { get; }
    
    event Action<double>? CpuTemperatureChanged;
    event Action<double>? GpuTemperatureChanged;
    event Action<int>? FanSpeedChanged;
    
    Task InitializeAsync();
    Task SetFanSpeedAsync(int percent);
    Task SetFanCurveAsync(FanCurvePoint[] curve);
}

public struct FanCurvePoint
{
    public double Temperature;
    public int FanSpeedPercent;
    
    public FanCurvePoint(double temperature, int fanSpeedPercent)
    {
        Temperature = temperature;
        FanSpeedPercent = fanSpeedPercent;
    }
}