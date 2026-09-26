using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Controllers.Sensors;
using LoqNova.Lib.Utils;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

public class SensorsService : ISensorsService
{
    private readonly ISensorsController _sensorsController;
    private readonly ILogger<SensorsService> _logger;
    private System.Timers.Timer? _timer;

    public double CpuUsage { get; private set; } = -1;
    public double GpuUsage { get; private set; } = -1;
    public double CpuTemperature { get; private set; } = -1;
    public double GpuTemperature { get; private set; } = -1;
    public int FanSpeedRpm { get; private set; } = -1;

    public event Action<double>? CpuUsageChanged;
    public event Action<double>? GpuUsageChanged;
    public event Action<double>? CpuTemperatureChanged;
    public event Action<double>? GpuTemperatureChanged;
    public event Action<int>? FanSpeedChanged;

    public SensorsService(ISensorsController sensorsController, ILogger<SensorsService> logger)
    {
        _sensorsController = sensorsController;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var supported = await _sensorsController.IsSupportedAsync().ConfigureAwait(false);
            if (supported)
            {
                await _sensorsController.PrepareAsync().ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
                
                _timer = new System.Timers.Timer(2000);
                _timer.Elapsed += async (_, _) => await RefreshAsync().ConfigureAwait(false);
                _timer.AutoReset = true;
                _timer.Start();
                
                _logger.LogInformation("Sensors service initialized successfully");
            }
            else
            {
                _logger.LogWarning("Sensors controller not supported on this hardware");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize sensors service");
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var data = await _sensorsController.GetDataAsync().ConfigureAwait(false);
            
            if (data.CPU.Utilization >= 0)
            {
                CpuUsage = data.CPU.Utilization;
                CpuUsageChanged?.Invoke(CpuUsage);
            }
            
            if (data.GPU.Utilization >= 0)
            {
                GpuUsage = data.GPU.Utilization;
                GpuUsageChanged?.Invoke(GpuUsage);
            }
            
            if (data.CPU.Temperature >= 0)
            {
                CpuTemperature = data.CPU.Temperature;
                CpuTemperatureChanged?.Invoke(CpuTemperature);
            }
            
            if (data.GPU.Temperature >= 0)
            {
                GpuTemperature = data.GPU.Temperature;
                GpuTemperatureChanged?.Invoke(GpuTemperature);
            }
            
            if (data.CPU.FanSpeed >= 0)
            {
                FanSpeedRpm = data.CPU.FanSpeed;
                FanSpeedChanged?.Invoke(FanSpeedRpm);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh sensor data");
        }
    }
}