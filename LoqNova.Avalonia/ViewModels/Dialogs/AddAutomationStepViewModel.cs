using System;
using System.Collections.ObjectCollection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class AddAutomationStepViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _selectedStepType = "PowerMode";
    
    [ObservableProperty]
    private string _stepName = "";
    
    [ObservableProperty]
    private object? _stepConfig;
    
    public ObservableCollection<string> StepTypes { get; } = new()
    {
        "AlwaysOnUSB", "Battery", "BatteryNightCharge", "DeactivateGPU", "Delay",
        "DisplayBrightness", "DpiScale", "FlipToStart", "FnLock", "GodModePreset",
        "HDR", "HybridMode", "InstantBoot", "Macro", "Microphone", "Notification",
        "OneLevelWhiteKB", "OverclockDiscreteGPU", "OverDrive", "PanelLogoBacklight",
        "PlaySound", "PortsBacklight", "PowerMode", "QuickAction", "RefreshRate",
        "Resolution", "RGBKeyboardBacklight", "Run", "SpectrumBrightness", "SpectrumProfile",
        "SpectrumImportProfile", "TouchpadLock", "TurnOffMonitors", "TurnOffWiFi",
        "TurnOnWiFi", "WhiteKBBacklight", "WinKey"
    };
    
    public AddAutomationStepViewModel()
    {
    }
    
    [RelayCommand]
    private async Task AddAsync()
    {
        // Add step to pipeline
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}