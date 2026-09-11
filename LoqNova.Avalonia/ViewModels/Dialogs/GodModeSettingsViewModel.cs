using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class GodModeSettingsViewModel : ViewModelBase
{
    private readonly IPerformanceService _performanceService;
    
    [ObservableProperty]
    private double _cpuPL1 = 45;
    
    [ObservableProperty]
    private double _cpuPL2 = 80;
    
    [ObservableProperty]
    private double _gpuPowerLimit = 80;
    
    [ObservableProperty]
    private double _cpuThermalLimit = 85;
    
    [ObservableProperty]
    private double _gpuThermalLimit = 83;
    
    [ObservableProperty]
    private bool _isEnabled = true;
    
    public GodModeSettingsViewModel(IPerformanceService performanceService)
    {
        _performanceService = performanceService;
        
        LoadCurrentSettings();
    }
    
    private void LoadCurrentSettings()
    {
        CpuPL1 = _performanceService.CpuPowerLimit;
        GpuPowerLimit = _performanceService.GpuPowerLimit;
        CpuThermalLimit = _performanceService.CpuThermalLimit;
        GpuThermalLimit = _performanceService.GpuThermalLimit;
        IsEnabled = _performanceService.IsGodModeEnabled;
    }
    
    partial void OnCpuPL1Changed(double value)
    {
        _performanceService.CpuPowerLimit = value;
    }
    
    partial void OnCpuPL2Changed(double value)
    {
        // PL2 handling
    }
    
    partial void OnGpuPowerLimitChanged(double value)
    {
        _performanceService.GpuPowerLimit = value;
    }
    
    partial void OnCpuThermalLimitChanged(double value)
    {
        _performanceService.CpuThermalLimit = value;
    }
    
    partial void OnGpuThermalLimitChanged(double value)
    {
        _performanceService.GpuThermalLimit = value;
    }
    
    [RelayCommand]
    private async Task ApplyAsync()
    {
        await _performanceService.ApplyGodModeSettingsAsync();
    }
    
    [RelayCommand]
    private async Task ResetDefaultsAsync()
    {
        CpuPL1 = 45;
        CpuPL2 = 80;
        GpuPowerLimit = 80;
        CpuThermalLimit = 85;
        GpuThermalLimit = 83;
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
        // Close dialog
    }
}