using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class CreateAutomationPipelineViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _selectedTriggerType = "Process Started";
    
    [ObservableProperty]
    private string _pipelineName = "New Pipeline";
    
    [ObservableProperty]
    private string _pipelineIcon = "Rocket";
    
    public ObservableCollection<string> TriggerTypes { get; } = new()
    {
        "Game Started", "Process Started", "Process Stopped", "Time Schedule",
        "User Inactivity", "WiFi Connected", "Device Connected", "GodMode Preset", "Periodic Action"
    };
    
    public ObservableCollection<string> Icons { get; } = new()
    {
        "Rocket", "Game", "Flash", "Clock", "User", "Wifi", "Device", "Cpu", "Repeat"
    };
    
    public CreateAutomationPipelineViewModel()
    {
    }
    
    [RelayCommand]
    private async Task CreateAsync()
    {
        // Create pipeline with selected trigger
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}