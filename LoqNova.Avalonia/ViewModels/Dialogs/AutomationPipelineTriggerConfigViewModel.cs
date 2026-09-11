using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class AutomationPipelineTriggerConfigViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _selectedTriggerType = "Process Started";
    
    [ObservableProperty]
    private string _processName = "";
    
    [ObservableProperty]
    private bool _matchExact = true;
    
    [ObservableProperty]
    private DateTime _triggerTime = DateTime.Now.AddHours(1);
    
    [ObservableProperty]
    private bool _repeatDaily = true;
    
    [ObservableProperty]
    private int _inactivityMinutes = 30;
    
    [ObservableProperty]
    private string _wifiSsid = "";
    
    [ObservableProperty]
    private string _deviceId = "";
    
    [ObservableProperty]
    private int _intervalMinutes = 60;
    
    public ObservableCollection<string> TriggerTypes { get; } = new()
    {
        "Game Started", "Process Started", "Process Stopped", "Time Schedule",
        "User Inactivity", "WiFi Connected", "Device Connected", "GodMode Preset", "Periodic Action"
    };
    
    public AutomationPipelineTriggerConfigViewModel()
    {
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}