using System;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Live telemetry, mirroring WPF's <c>SensorsControl</c>. Every value and its
/// maximum come from the library's sensor controller; -1 means "not reported" and is
/// never rendered as a reading.
/// </summary>
public interface ISensorsService
{
    int CpuUtilization { get; }
    int CpuMaxUtilization { get; }
    int CpuCoreClock { get; }
    int CpuMaxCoreClock { get; }
    int CpuTemperature { get; }
    int CpuMaxTemperature { get; }
    int CpuFanSpeed { get; }
    int CpuMaxFanSpeed { get; }

    int GpuUtilization { get; }
    int GpuMaxUtilization { get; }
    int GpuCoreClock { get; }
    int GpuMaxCoreClock { get; }
    int GpuMemoryClock { get; }
    int GpuMaxMemoryClock { get; }
    int GpuTemperature { get; }
    int GpuMaxTemperature { get; }
    int GpuFanSpeed { get; }
    int GpuMaxFanSpeed { get; }

    /// <summary>False when the machine reports no supported sensor source.</summary>
    bool IsSupported { get; }

    /// <summary>True while the refresh loop is running.</summary>
    bool IsRefreshing { get; }

    /// <summary>Seconds between reads, from the shared dashboard settings.</summary>
    int RefreshIntervalSeconds { get; }

    /// <summary>Raised on the UI thread after each successful publish.</summary>
    event Action? Updated;

    /// <summary>Raised on the UI thread when support is (re)evaluated.</summary>
    event Action? SupportChanged;

    Task InitializeAsync();

    /// <summary>Starts the refresh loop. Called when the panel becomes visible.</summary>
    void Start();

    /// <summary>Stops the refresh loop. Called when the panel is hidden.</summary>
    void Stop();

    /// <summary>Persists a new interval and restarts the loop so it takes effect.</summary>
    void SetRefreshInterval(int seconds);
}
