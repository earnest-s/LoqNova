using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class BalanceModeSettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _aiEnabled = false;
    
    [ObservableProperty]
    private string _cpuTarget = "Balanced";
    
    [ObservableProperty]
    private string _gpuTarget = "Balanced";
    
    [ObservableProperty]
    private string _thermalTarget = "Balanced";
    
    public ObservableCollection<string> TargetOptions { get; } = new()
    {
        "Power Saver", "Balanced", "Performance"
    };
    
    public BalanceModeSettingsViewModel()
    {
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Save balance mode settings
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}