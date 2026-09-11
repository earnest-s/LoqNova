using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class StatusViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _batteryStatus = "82% - On battery";
    
    [ObservableProperty]
    private string _powerModeStatus = "Balance";
    
    [ObservableProperty]
    private string _cpuTempStatus = "54°C";
    
    [ObservableProperty]
    private string _gpuTempStatus = "49°C";
    
    [ObservableProperty]
    private string _fanStatus = "2400 RPM (45%)";
    
    [ObservableProperty]
    private string _rgbStatus = "Preset 1 - Static";
    
    [ObservableProperty]
    private bool _isCharging = false;
    
    public StatusViewModel()
    {
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}