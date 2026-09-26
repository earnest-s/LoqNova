using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Controllers.Sensors;
using LoqNova.Lib.Utils;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

public class ThermalService : IThermalService
{
    private readonly ISensorsController _sensorsController;
    private readonly ILogger<ThermalService> _logger;
    private System.Timers.Timer? _timer;

    public double CpuTemperature { get; private set; } = -1;
    public double GpuTemperature { get; private set; } = -1;
    public int FanSpeedRpm { get; private set; } = -1;
    public int FanSpeedPercent { get; private set; } = -1;
    public bool IsFanControlSupported => false;

    public event Action<double>? CpuTemperatureChanged;
    public event Action<double>? GpuTemperatureChanged;
    public event Action<int>? FanSpeedChanged;

    public ThermalService(ISensorsController sensorsController, ILogger<ThermalService> logger)
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
                
                _logger.LogInformation("Thermal service initialized successfully");
            }
            else
            {
                _logger.LogWarning("Thermal service not supported on this hardware");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize thermal service");
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var data = await _sensorsController.GetDataAsync().ConfigureAwait(false);
            
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
                FanSpeedPercent = data.CPU.MaxFanSpeed > 0 
                    ? (int)Math.Clamp((double)data.CPU.FanSpeed / data.CPU.MaxFanSpeed * 100, 0, 100)
                    : -1;
                FanSpeedChanged?.Invoke(FanSpeedRpm);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh thermal data");
        }
    }

    public Task SetFanSpeedAsync(int percent)
    {
        _logger.LogWarning("Fan speed control not implemented");
        return Task.CompletedTask;
    }

    public Task SetFanCurveAsync(FanCurvePoint[] curve)
    {
        _logger.LogWarning("Fan curve control not implemented");
        return Task.CompletedTask;
    }
}