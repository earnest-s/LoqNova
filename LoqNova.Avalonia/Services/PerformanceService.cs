using System;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Controllers;
using LoqNova.Lib.Settings;
using LoqNova.Lib.Utils;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

public class PerformanceService : IPerformanceService
{
    private readonly WindowsPowerModeController _powerModeController;
    private readonly ApplicationSettings _settings;
    private readonly ILogger<PerformanceService> _logger;
    private PowerModeState _currentMode = PowerModeState.Balance;
    private bool _isGodModeEnabled = false;
    private double _cpuPowerLimit = 45;
    private double _gpuPowerLimit = 80;
    private double _cpuThermalLimit = 85;
    private double _gpuThermalLimit = 83;

    public PowerModeState CurrentMode => _currentMode;
    public bool IsSupported => true;
    public bool IsGodModeEnabled => _isGodModeEnabled;

    public double CpuPowerLimit
    {
        get => _cpuPowerLimit;
        set
        {
            if (Math.Abs(_cpuPowerLimit - value) > 0.01)
            {
                _cpuPowerLimit = value;
                CpuPowerLimitChanged?.Invoke(value);
            }
        }
    }

    public double GpuPowerLimit
    {
        get => _gpuPowerLimit;
        set
        {
            if (Math.Abs(_gpuPowerLimit - value) > 0.01)
            {
                _gpuPowerLimit = value;
                GpuPowerLimitChanged?.Invoke(value);
            }
        }
    }

    public double CpuThermalLimit
    {
        get => _cpuThermalLimit;
        set => _cpuThermalLimit = value;
    }

    public double GpuThermalLimit
    {
        get => _gpuThermalLimit;
        set => _gpuThermalLimit = value;
    }

    public event Action<PowerModeState>? ModeChanged;
    public event Action<double>? CpuPowerLimitChanged;
    public event Action<double>? GpuPowerLimitChanged;

    public PerformanceService(WindowsPowerModeController powerModeController, ApplicationSettings settings, ILogger<PerformanceService> logger)
    {
        _powerModeController = powerModeController;
        _settings = settings;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _currentMode = _settings.Store.PowerModeState;
            _isGodModeEnabled = _settings.Store.GodModeEnabled;
            _logger.LogInformation("Performance service initialized. Current mode: {Mode}, GodMode: {GodMode}", _currentMode, _isGodModeEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize performance service");
            _currentMode = PowerModeState.Balance;
        }
    }

    public async Task SetModeAsync(PowerModeState mode)
    {
        try
        {
            if (_currentMode != mode)
            {
                _currentMode = mode;
                _settings.Store.PowerModeState = mode;
                _settings.SynchronizeStore();
                
                await _powerModeController.SetPowerModeAsync(mode).ConfigureAwait(false);
                ModeChanged?.Invoke(mode);
                
                _logger.LogInformation("Power mode changed to {Mode}", mode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set power mode to {Mode}", mode);
        }
    }

    public async Task ApplyGodModeSettingsAsync()
    {
        try
        {
            _isGodModeEnabled = !_isGodModeEnabled;
            _settings.Store.GodModeEnabled = _isGodModeEnabled;
            _settings.SynchronizeStore();
            
            _logger.LogInformation("GodMode {Status}", _isGodModeEnabled ? "enabled" : "disabled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle GodMode");
        }
    }
}