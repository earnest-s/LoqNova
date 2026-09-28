using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Controllers.Sensors;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Adapter over the existing <see cref="ISensorsController"/> for temperature and
/// fan telemetry. Sampling runs on a timer thread, so every published change is
/// marshalled onto the UI thread before it reaches a binding.
/// </summary>
public class ThermalService : IThermalService, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ISensorsController _sensorsController;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly ILogger<ThermalService> _logger;

    private System.Timers.Timer? _timer;
    private bool _disposed;

    public double CpuTemperature { get; private set; } = -1;
    public double GpuTemperature { get; private set; } = -1;
    public int FanSpeedRpm { get; private set; } = -1;
    public int FanSpeedPercent { get; private set; } = -1;

    /// <summary>
    /// Fan speed and curve writes are driven by God Mode presets through
    /// <c>IGodModeController</c>, which this adapter does not own yet. Reported as
    /// unsupported so callers do not present controls that cannot work.
    /// </summary>
    public bool IsFanControlSupported => false;

    public event Action<double>? CpuTemperatureChanged;
    public event Action<double>? GpuTemperatureChanged;
    public event Action<int>? FanSpeedChanged;

    public ThermalService(
        ISensorsController sensorsController,
        IMainThreadDispatcher dispatcher,
        ILogger<ThermalService> logger)
    {
        _sensorsController = sensorsController;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (!await _sensorsController.IsSupportedAsync().ConfigureAwait(false))
        {
            _logger.LogWarning("Thermal telemetry is not supported on this machine.");
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

        _logger.LogInformation("Thermal polling every {Interval}.", PollInterval);
    }

    private async void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Thermal refresh failed.");
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var data = await _sensorsController.GetDataAsync().ConfigureAwait(false);

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
                    FanSpeedPercent = data.CPU.MaxFanSpeed > 0
                        ? (int)Math.Clamp(data.CPU.FanSpeed / (double)data.CPU.MaxFanSpeed * 100, 0, 100)
                        : -1;
                    FanSpeedChanged?.Invoke(FanSpeedRpm);
                }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read thermal data.");
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

    public Task SetFanSpeedAsync(int percent)
    {
        _logger.LogWarning("Fan speed writes require a wired God Mode controller; ignoring {Percent}%.", percent);
        return Task.CompletedTask;
    }

    public Task SetFanCurveAsync(FanCurvePoint[] curve)
    {
        _logger.LogWarning("Fan curve writes require a wired God Mode controller; ignoring {Count} points.", curve.Length);
        return Task.CompletedTask;
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
