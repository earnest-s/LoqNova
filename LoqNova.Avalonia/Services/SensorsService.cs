using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Controllers.Sensors;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Adapter over the existing <see cref="ISensorsController"/>. Sampling runs on a
/// timer thread, so every published change is marshalled onto the UI thread
/// before it reaches a binding.
/// </summary>
public class SensorsService : ISensorsService, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ISensorsController _sensorsController;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly ILogger<SensorsService> _logger;

    private System.Timers.Timer? _timer;
    private bool _disposed;

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

    public SensorsService(
        ISensorsController sensorsController,
        IMainThreadDispatcher dispatcher,
        ILogger<SensorsService> logger)
    {
        _sensorsController = sensorsController;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        if (!await _sensorsController.IsSupportedAsync().ConfigureAwait(false))
        {
            _logger.LogWarning("Sensors controller is not supported on this machine.");
            return;
        }

        await _sensorsController.PrepareAsync().ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);

        StartTimer();
    }

    private void StartTimer()
    {
        if (_timer is not null)
            return;

        _timer = new System.Timers.Timer(PollInterval.TotalMilliseconds)
        {
            AutoReset = true
        };
        _timer.Elapsed += OnTimerElapsed;
        _timer.Start();

        _logger.LogInformation("Sensors polling every {Interval}.", PollInterval);
    }

    private async void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sensor refresh failed.");
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var data = await _sensorsController.GetDataAsync().ConfigureAwait(false);

            if (data.CPU.Utilization >= 0)
                await PublishAsync(() =>
                {
                    CpuUsage = data.CPU.Utilization;
                    CpuUsageChanged?.Invoke(CpuUsage);
                }).ConfigureAwait(false);

            if (data.GPU.Utilization >= 0)
                await PublishAsync(() =>
                {
                    GpuUsage = data.GPU.Utilization;
                    GpuUsageChanged?.Invoke(GpuUsage);
                }).ConfigureAwait(false);

            if (data.CPU.Temperature >= 0)
                await PublishAsync(() =>
                {
                    CpuTemperature = data.CPU.Temperature;
                    CpuTemperatureChanged?.Invoke(CpuTemperature);
                }).ConfigureAwait(false);

            if (data.GPU.Temperature >= 0)
                await PublishAsync(() =>
                {
                    GpuTemperature = data.GPU.Temperature;
                    GpuTemperatureChanged?.Invoke(GpuTemperature);
                }).ConfigureAwait(false);

            if (data.CPU.FanSpeed >= 0)
                await PublishAsync(() =>
                {
                    FanSpeedRpm = data.CPU.FanSpeed;
                    FanSpeedChanged?.Invoke(FanSpeedRpm);
                }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read sensor data.");
        }
    }

    private Task PublishAsync(Action update)
    {
        if (_dispatcher.CheckAccess())
        {
            update();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(update);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_timer is null)
            return;

        _timer.Elapsed -= OnTimerElapsed;
        _timer.Stop();
        _timer.Dispose();
        _timer = null;
    }
}
