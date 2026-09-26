using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.System;
using LoqNova.Lib.Utils;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

public class BatteryService : IBatteryService
{
    private readonly ILogger<BatteryService> _logger;
    private System.Timers.Timer? _timer;

    public int Percentage { get; private set; } = -1;
    public string StatusText { get; private set; } = "Unknown";
    public bool IsCharging { get; private set; } = false;
    public bool IsLowBattery { get; private set; } = false;
    public bool IsLowWattageCharger { get; private set; } = false;
    public double TemperatureC { get; private set; } = -1;
    public double TemperatureF { get; private set; } = -1;
    public double DischargeRate { get; private set; } = 0;
    public double MinDischargeRate { get; private set; } = 0;
    public double MaxDischargeRate { get; private set; } = 0;
    public int CurrentCapacity { get; private set; } = -1;
    public int FullChargeCapacity { get; private set; } = -1;
    public int DesignCapacity { get; private set; } = -1;
    public int HealthPercent { get; private set; } = -1;
    public TimeSpan OnBatteryDuration { get; private set; }
    public DateTime? OnBatterySince { get; private set; }
    public int CycleCount { get; private set; } = -1;
    public DateTime? ManufactureDate { get; private set; }
    public DateTime? FirstUseDate { get; private set; }
    public BatteryState CurrentMode { get; private set; } = BatteryState.Normal;
    public BatteryNightChargeState NightChargeMode { get; private set; } = BatteryNightChargeState.Disabled;
    public bool IsPowerAdapterConnected { get; private set; } = false;

    public event Action<int>? PercentageChanged;
    public event Action<bool>? ChargingChanged;
    public event Action<BatteryState>? ModeChanged;

    public BatteryService(ILogger<BatteryService> logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
            
            _timer = new System.Timers.Timer(5000);
            _timer.Elapsed += async (_, _) => await RefreshAsync().ConfigureAwait(false);
            _timer.AutoReset = true;
            _timer.Start();
            
            _logger.LogInformation("Battery service initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize battery service");
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var info = await Task.Run(() => Battery.GetBatteryInformation()).ConfigureAwait(false);
            
            var oldPercentage = Percentage;
            var oldIsCharging = IsCharging;
            
            Percentage = info.BatteryPercentage;
            IsCharging = info.IsCharging;
            IsLowBattery = info.IsLowBattery;
            TemperatureC = info.BatteryTemperatureC ?? -1;
            TemperatureF = info.BatteryTemperatureC.HasValue ? info.BatteryTemperatureC.Value * 9 / 5 + 32 : -1;
            DischargeRate = Math.Abs(info.DischargeRate);
            MinDischargeRate = Math.Abs(info.MinDischargeRate);
            MaxDischargeRate = Math.Abs(info.MaxDischargeRate);
            CurrentCapacity = info.EstimateChargeRemaining;
            FullChargeCapacity = info.FullChargeCapacity;
            DesignCapacity = info.DesignCapacity;
            HealthPercent = (int)info.BatteryHealth;
            CycleCount = info.CycleCount;
            ManufactureDate = info.ManufactureDate;
            FirstUseDate = info.FirstUseDate;
            IsPowerAdapterConnected = info.IsCharging;
            
            StatusText = info.IsCharging ? "Charging" : (info.IsLowBattery ? "Low Battery" : "On Battery");
            
            OnBatterySince = await Task.Run(() => Battery.GetOnBatterySince()).ConfigureAwait(false);
            OnBatteryDuration = OnBatterySince.HasValue ? DateTime.Now - OnBatterySince.Value : TimeSpan.Zero;
            
            if (Percentage != oldPercentage)
            {
                PercentageChanged?.Invoke(Percentage);
            }
            
            if (IsCharging != oldIsCharging)
            {
                ChargingChanged?.Invoke(IsCharging);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh battery data");
        }
    }

    public Task SetModeAsync(BatteryState mode)
    {
        if (CurrentMode != mode)
        {
            CurrentMode = mode;
            ModeChanged?.Invoke(mode);
            _logger.LogInformation("Battery mode changed to {Mode}", mode);
        }
        return Task.CompletedTask;
    }

    public Task SetNightChargeAsync(BatteryNightChargeState state)
    {
        if (NightChargeMode != state)
        {
            NightChargeMode = state;
            _logger.LogInformation("Battery night charge mode changed to {State}", state);
        }
        return Task.CompletedTask;
    }
}