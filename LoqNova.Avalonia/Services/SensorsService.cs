using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LoqNova.Avalonia.Settings;
using LoqNova.Lib;
using LoqNova.Lib.Controllers.Sensors;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Telemetry adapter over the library's <see cref="ISensorsController"/>.
/// <para>
/// Reproduces WPF's <c>SensorsControl</c> refresh model rather than inventing one:
/// a self-rescheduling loop on a background task that reads, publishes, then waits
/// the configured interval, started when the panel becomes visible and stopped when
/// it is hidden. The interval comes from the shared dashboard settings
/// (1/2/3/5 seconds, default 1), exactly as in WPF - not a fixed timer.
/// </para>
/// </summary>
public class SensorsService : ISensorsService, IDisposable
{
    /// <summary>Intervals WPF offers in the refresh-interval context menu.</summary>
    public static readonly int[] SelectableIntervalsSeconds = [1, 2, 3, 5];

    private readonly IMainThreadDispatcher _dispatcher;
    private readonly DashboardSettings _dashboardSettings;
    private readonly ILogger<SensorsService> _logger;

    private ISensorsController? _sensorsController;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    // CPU
    private int _cpuUtilization = -1;
    private int _cpuMaxUtilization = -1;
    private int _cpuCoreClock = -1;
    private int _cpuMaxCoreClock = -1;
    private int _cpuTemperature = -1;
    private int _cpuMaxTemperature = -1;
    private int _cpuFanSpeed = -1;
    private int _cpuMaxFanSpeed = -1;

    // GPU
    private int _gpuUtilization = -1;
    private int _gpuMaxUtilization = -1;
    private int _gpuCoreClock = -1;
    private int _gpuMaxCoreClock = -1;
    private int _gpuMemoryClock = -1;
    private int _gpuMaxMemoryClock = -1;
    private int _gpuTemperature = -1;
    private int _gpuMaxTemperature = -1;
    private int _gpuFanSpeed = -1;
    private int _gpuMaxFanSpeed = -1;

    public int CpuUtilization => _cpuUtilization;
    public int CpuMaxUtilization => _cpuMaxUtilization;
    public int CpuCoreClock => _cpuCoreClock;
    public int CpuMaxCoreClock => _cpuMaxCoreClock;
    public int CpuTemperature => _cpuTemperature;
    public int CpuMaxTemperature => _cpuMaxTemperature;
    public int CpuFanSpeed => _cpuFanSpeed;
    public int CpuMaxFanSpeed => _cpuMaxFanSpeed;

    public int GpuUtilization => _gpuUtilization;
    public int GpuMaxUtilization => _gpuMaxUtilization;
    public int GpuCoreClock => _gpuCoreClock;
    public int GpuMaxCoreClock => _gpuMaxCoreClock;
    public int GpuMemoryClock => _gpuMemoryClock;
    public int GpuMaxMemoryClock => _gpuMaxMemoryClock;
    public int GpuTemperature => _gpuTemperature;
    public int GpuMaxTemperature => _gpuMaxTemperature;
    public int GpuFanSpeed => _gpuFanSpeed;
    public int GpuMaxFanSpeed => _gpuMaxFanSpeed;

    /// <summary>False when the machine has no supported sensor source; the panel hides itself.</summary>
    public bool IsSupported { get; private set; }

    /// <summary>True while the refresh loop is running.</summary>
    public bool IsRefreshing => _cts is not null;

    /// <summary>Interval the loop waits between reads.</summary>
    public int RefreshIntervalSeconds => Math.Clamp(_dashboardSettings.Store.SensorsRefreshIntervalSeconds, 1, 60);

    public event Action? Updated;
    public event Action? SupportChanged;

    public SensorsService(
        IMainThreadDispatcher dispatcher,
        DashboardSettings dashboardSettings,
        ILogger<SensorsService> logger)
    {
        _dispatcher = dispatcher;
        _dashboardSettings = dashboardSettings;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        _sensorsController = LoqNova.Lib.IoCContainer.Resolve<ISensorsController>();

        IsSupported = await _sensorsController.IsSupportedAsync().ConfigureAwait(false);

        if (!IsSupported)
        {
            _logger.LogInformation("Sensors controller is not supported on this machine.");
            await _dispatcher.InvokeAsync(() => SupportChanged?.Invoke()).ConfigureAwait(false);
            return;
        }

        await _sensorsController.PrepareAsync().ConfigureAwait(false);

        await ReadOnceAsync().ConfigureAwait(false);

        await _dispatcher.InvokeAsync(() => SupportChanged?.Invoke()).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the refresh loop, mirroring WPF's <c>IsVisibleChanged</c> handler:
    /// only runs while the panel is visible.
    /// </summary>
    public void Start()
    {
        if (_disposed || _sensorsController is null || !IsSupported || _cts is not null)
            return;

        var cts = new CancellationTokenSource();
        _cts = cts;

        _ = Task.Run(() => RefreshLoopAsync(cts.Token), CancellationToken.None);
    }

    /// <summary>Stops the loop when the panel is hidden.</summary>
    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    private async Task RefreshLoopAsync(CancellationToken token)
    {
        _logger.LogInformation("Sensors refresh started (every {Interval}s).", RefreshIntervalSeconds);

        try
        {
            while (!token.IsCancellationRequested)
            {
                await ReadOnceAsync(token).ConfigureAwait(false);

                await Task.Delay(TimeSpan.FromSeconds(RefreshIntervalSeconds), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown of the loop.
        }
        catch (Exception ex)
        {
            // WPF collapses the control when the refresh task faults.
            _logger.LogError(ex, "Sensors refresh failed.");
        }
        finally
        {
            _logger.LogInformation("Sensors refresh stopped.");
        }
    }

    /// <summary>One read-and-publish pass. Never throws.</summary>
    private async Task ReadOnceAsync(CancellationToken token = default)
    {
        if (_sensorsController is null)
            return;

        SensorsData data;

        try
        {
            data = await _sensorsController.GetDataAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read sensor data.");
            // WPF falls back to SensorData.Empty on a read failure.
            data = SensorsData.Empty;
        }

        token.ThrowIfCancellationRequested();

        var supported = await _sensorsController.IsSupportedAsync().ConfigureAwait(false);

        await _dispatcher.InvokeAsync(() =>
        {
            IsSupported = supported;

            if (!supported)
            {
                Apply(data);
                return;
            }

            Apply(data);
            Updated?.Invoke();
        }).ConfigureAwait(false);
    }

    private void Apply(SensorsData data)
    {
        _cpuUtilization = data.CPU.Utilization;
        _cpuMaxUtilization = data.CPU.MaxUtilization;
        _cpuCoreClock = data.CPU.CoreClock;
        _cpuMaxCoreClock = data.CPU.MaxCoreClock;
        _cpuTemperature = data.CPU.Temperature;
        _cpuMaxTemperature = data.CPU.MaxTemperature;
        _cpuFanSpeed = data.CPU.FanSpeed;
        _cpuMaxFanSpeed = data.CPU.MaxFanSpeed;

        _gpuUtilization = data.GPU.Utilization;
        _gpuMaxUtilization = data.GPU.MaxUtilization;
        _gpuCoreClock = data.GPU.CoreClock;
        _gpuMaxCoreClock = data.GPU.MaxCoreClock;
        _gpuMemoryClock = data.GPU.MemoryClock;
        _gpuMaxMemoryClock = data.GPU.MaxMemoryClock;
        _gpuTemperature = data.GPU.Temperature;
        _gpuMaxTemperature = data.GPU.MaxTemperature;
        _gpuFanSpeed = data.GPU.FanSpeed;
        _gpuMaxFanSpeed = data.GPU.MaxFanSpeed;
    }

    /// <summary>Persists a new refresh interval and restarts the loop so it takes effect.</summary>
    public void SetRefreshInterval(int seconds)
    {
        if (!SelectableIntervalsSeconds.Contains(seconds))
            return;

        _dashboardSettings.Store.SensorsRefreshIntervalSeconds = seconds;
        _dashboardSettings.Save();

        if (!IsRefreshing)
            return;

        Stop();
        Start();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }
}
