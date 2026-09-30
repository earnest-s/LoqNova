using System;
using System.Threading.Tasks;
using LoqNova.Lib;
using LoqNova.Lib.Features;
using LoqNova.Lib.System;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Battery adapter over the existing library pieces:
/// <c>Battery.GetBatteryInformation</c> for telemetry,
/// <c>BatteryFeature</c> / <c>BatteryNightChargeFeature</c> for charge modes and
/// <c>Power.IsPowerAdapterConnectedAsync</c> for adapter state. Nothing is
/// synthesised; unknown values stay -1.
/// </summary>
public class BatteryService : IBatteryService, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IMainThreadDispatcher _dispatcher;
    private readonly ILogger<BatteryService> _logger;

    private BatteryFeature? _batteryFeature;
    private BatteryNightChargeFeature? _nightChargeFeature;

    private System.Timers.Timer? _timer;
    private bool _disposed;

    public int Percentage { get; private set; } = -1;
    public string StatusText { get; private set; } = "Unknown";
    public bool IsCharging { get; private set; }
    public bool IsLowBattery { get; private set; }
    public bool IsLowWattageCharger { get; private set; }
    public double TemperatureC { get; private set; } = -1;
    public double TemperatureF { get; private set; } = -1;
    public double DischargeRate { get; private set; }
    public double MinDischargeRate { get; private set; }
    public double MaxDischargeRate { get; private set; }
    public int CurrentCapacity { get; private set; } = -1;
    public int FullChargeCapacity { get; private set; } = -1;
    public int DesignCapacity { get; private set; } = -1;
    public int HealthPercent { get; private set; } = -1;
    public TimeSpan OnBatteryDuration { get; private set; }
    public DateTime? OnBatterySince { get; private set; }
    public int CycleCount { get; private set; } = -1;
    public DateTime? ManufactureDate { get; private set; }
    public DateTime? FirstUseDate { get; private set; }
    public BatteryState CurrentMode { get; private set; }
    public BatteryNightChargeState NightChargeMode { get; private set; }
    public bool IsPowerAdapterConnected { get; private set; }
    public bool IsSupported { get; private set; }

    public event Action<int>? PercentageChanged;
    public event Action<bool>? ChargingChanged;
    public event Action<BatteryState>? ModeChanged;
    public event Action<BatteryNightChargeState>? NightChargeChanged;

    public BatteryService(
        IMainThreadDispatcher dispatcher,
        ILogger<BatteryService> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        // Resolved after the shared container is up so construction never races it.
        _batteryFeature = LoqNova.Lib.IoCContainer.Resolve<BatteryFeature>();
        _nightChargeFeature = LoqNova.Lib.IoCContainer.Resolve<BatteryNightChargeFeature>();

        IsSupported = await _batteryFeature.IsSupportedAsync().ConfigureAwait(false);

        if (!IsSupported)
        {
            _logger.LogInformation("No battery detected.");
            return;
        }

        await ReadModeAsync().ConfigureAwait(false);
        await ReadNightChargeAsync().ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);

        StartTimer();
    }

    private void StartTimer()
    {
        if (_timer is not null)
            return;

        _timer = new System.Timers.Timer(PollInterval.TotalMilliseconds) { AutoReset = true };
        _timer.Elapsed += OnTimerElapsed;
        _timer.Start();

        _logger.LogInformation("Battery polling every {Interval}.", PollInterval);
    }

    private async void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Battery refresh failed.");
        }
    }

    public async Task RefreshAsync()
    {
        if (!IsSupported)
            return;

        try
        {
            var info = await Task.Run(Battery.GetBatteryInformation).ConfigureAwait(false);

            var oldPercentage = Percentage;
            var oldIsCharging = IsCharging;
            var oldAdapterStatus = IsPowerAdapterConnected
                ? PowerAdapterStatus.Connected
                : PowerAdapterStatus.Disconnected;

            Percentage = info.BatteryPercentage;
            IsCharging = info.IsCharging;
            IsLowBattery = info.IsLowBattery;
            TemperatureC = info.BatteryTemperatureC ?? -1;
            TemperatureF = info.BatteryTemperatureC is { } c ? c * 9 / 5 + 32 : -1;
                // WPF formats these as $"{DischargeRate / 1000.0:+0.00;-0.00;0.00} W",
                // so the sign carries meaning: positive while charging, negative while
                // discharging. Math.Abs used to be applied here, which flattened that to a
                // bare 0.0 W and made a discharging battery look identical to an idle one.
                DischargeRate = info.DischargeRate / 1000.0;
                MinDischargeRate = info.MinDischargeRate / 1000.0;
                MaxDischargeRate = info.MaxDischargeRate / 1000.0;
            CurrentCapacity = info.EstimateChargeRemaining;
            FullChargeCapacity = info.FullChargeCapacity;
            DesignCapacity = info.DesignCapacity;
            HealthPercent = (int)info.BatteryHealth;
            CycleCount = info.CycleCount;
            ManufactureDate = info.ManufactureDate;
            FirstUseDate = info.FirstUseDate;
            StatusText = info.IsCharging
                ? "Charging"
                : info.IsLowBattery
                    ? "Low Battery"
                    : "On Battery";

            // Adapter status is a separate query: a fully charged battery on AC
            // reports IsCharging == false, so it cannot stand in for the adapter.
            var adapter = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);
            IsPowerAdapterConnected = adapter is PowerAdapterStatus.Connected or PowerAdapterStatus.ConnectedLowWattage;
            IsLowWattageCharger = adapter is PowerAdapterStatus.ConnectedLowWattage;

            OnBatterySince = await Task.Run(Battery.GetOnBatterySince).ConfigureAwait(false);
            OnBatteryDuration = OnBatterySince is { } since ? DateTime.Now - since : TimeSpan.Zero;

            await PublishAsync(() =>
            {
                if (Percentage != oldPercentage)
                    PercentageChanged?.Invoke(Percentage);

                if (IsCharging != oldIsCharging)
                    ChargingChanged?.Invoke(IsCharging);

                // Adapter state has no dedicated event; subscribers to the battery
                // are re-notified so adapter and low-wattage flags are picked up.
                if (adapter != oldAdapterStatus)
                    PercentageChanged?.Invoke(Percentage);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read battery data.");
        }
    }

    public async Task SetModeAsync(BatteryState mode)
    {
        if (_batteryFeature is null)
            return;

        try
        {
            await _batteryFeature.SetStateAsync(mode).ConfigureAwait(false);

            // Read back so the UI reflects the machine, not the request.
            var actual = await _batteryFeature.GetStateAsync().ConfigureAwait(false);
            await PublishAsync(() =>
            {
                CurrentMode = actual;
                ModeChanged?.Invoke(actual);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set battery mode to {Mode}.", mode);
            await ReadModeAsync().ConfigureAwait(false);
        }
    }

    public async Task SetNightChargeAsync(BatteryNightChargeState state)
    {
        if (_nightChargeFeature is null)
            return;

        try
        {
            await _nightChargeFeature.SetStateAsync(state).ConfigureAwait(false);

            var actual = await _nightChargeFeature.GetStateAsync().ConfigureAwait(false);
            await PublishAsync(() =>
            {
                NightChargeMode = actual;
                NightChargeChanged?.Invoke(actual);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set night charge to {State}.", state);
            await ReadNightChargeAsync().ConfigureAwait(false);
        }
    }

    private async Task ReadModeAsync()
    {
        if (_batteryFeature is null)
            return;

        try
        {
            var mode = await _batteryFeature.GetStateAsync().ConfigureAwait(false);
            await PublishAsync(() =>
            {
                CurrentMode = mode;
                ModeChanged?.Invoke(mode);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read battery mode.");
        }
    }

    private async Task ReadNightChargeAsync()
    {
        if (_nightChargeFeature is null)
            return;

        try
        {
            var state = await _nightChargeFeature.GetStateAsync().ConfigureAwait(false);
            await PublishAsync(() =>
            {
                NightChargeMode = state;
                NightChargeChanged?.Invoke(state);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read night charge state.");
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
