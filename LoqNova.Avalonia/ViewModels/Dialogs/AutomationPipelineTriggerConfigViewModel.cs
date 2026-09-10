using System;
using System.Collections.ObjectCollection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class AutomationPipelineTriggerConfigViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _selectedTriggerType = "Process Started";
    
    // Process trigger config
    [ObservableProperty]
    private string _processName = "";
    
    [ObservableProperty]
    private bool _matchExact = true;
    
    // Time trigger config
    [ObservableProperty]
    private DateTime _triggerTime = DateTime.Now.AddHours(1);
    
    [ObservableProperty]
    private bool _repeatDaily = true;
    
    // Inactivity trigger config
    [ObservableProperty]
    private int _inactivityMinutes = 30;
    
    // WiFi trigger config
    [ObservableProperty]
    private string _wifiSsid = "";
    
    // Device trigger config
    [ObservableProperty]
    private string _deviceId = "";
    
    // Periodic trigger config
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
        // Save trigger configuration
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}