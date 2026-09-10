using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class AddDashboardItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _selectedCategory = "System";
    
    [ObservableProperty]
    private string _selectedItem = "";
    
    public ObservableCollection<string> Categories { get; } = new()
    {
        "System", "Performance", "RGB", "Hardware", "Power", "Custom"
    };
    
    public ObservableCollection<string> SystemItems { get; } = new()
    {
        "AlwaysOnUSB", "BatteryMode", "BatteryNightCharge", "DpiScale", "FlipToStart",
        "FnLock", "HDR", "HybridMode", "InstantBoot", "Microphone", "OneLevelWhiteKB",
        "OverDrive", "PanelLogoBacklight", "PortsBacklight", "PowerMode", "RefreshRate",
        "Resolution", "TouchpadLock", "WhiteKBBacklight", "WinKey"
    };
    
    public ObservableCollection<string> PerformanceItems { get; } = new()
    {
        "DiscreteGPU", "OverclockDiscreteGPU", "GodModeValue", "FanCurve"
    };
    
    public ObservableCollection<string> RGBItems { get; } = new()
    {
        "RGBPreset", "RGBEffect", "RGBSpeed", "RGBBrightness", "RGBZoneColors"
    };
    
    public AddDashboardItemViewModel()
    {
    }
    
    [RelayCommand]
    private async Task AddAsync()
    {
        // Add item to dashboard
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}