using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public interface ISensorsService
{
    double CpuUsage { get; }
    double GpuUsage { get; }
    double CpuTemperature { get; }
    double GpuTemperature { get; }
    int FanSpeedRpm { get; }
    
    event Action<double>? CpuUsageChanged;
    event Action<double>? GpuUsageChanged;
    event Action<double>? CpuTemperatureChanged;
    event Action<double>? GpuTemperatureChanged;
    event Action<int>? FanSpeedChanged;
    
    Task InitializeAsync();
}