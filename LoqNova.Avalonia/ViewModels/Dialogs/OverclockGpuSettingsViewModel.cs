using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class OverclockGpuSettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isEnabled = false;
    
    [ObservableProperty]
    private int _coreClockOffset = 0;
    
    [ObservableProperty]
    private int _memoryClockOffset = 0;
    
    [ObservableProperty]
    private int _powerLimitPercent = 100;
    
    [ObservableProperty]
    private int _temperatureLimit = 83;
    
    [ObservableProperty]
    private int _coreVoltageOffset = 0;
    
    public OverclockGpuSettingsViewModel()
    {
    }
    
    [RelayCommand]
    private async Task ApplyAsync()
    {
        // Apply overclock settings
    }
    
    [RelayCommand]
    private async Task ResetDefaultsAsync()
    {
        CoreClockOffset = 0;
        MemoryClockOffset = 0;
        PowerLimitPercent = 100;
        TemperatureLimit = 83;
        CoreVoltageOffset = 0;
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}