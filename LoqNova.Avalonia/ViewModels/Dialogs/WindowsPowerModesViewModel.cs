using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class WindowsPowerModesViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<PowerModeMappingItem> _mappings = new();
    
    public WindowsPowerModesViewModel()
    {
        Mappings.Add(new PowerModeMappingItem { LoqMode = "Quiet", WindowsMode = "Power Saver" });
        Mappings.Add(new PowerModeMappingItem { LoqMode = "Balance", WindowsMode = "Balanced" });
        Mappings.Add(new PowerModeMappingItem { LoqMode = "Performance", WindowsMode = "High Performance" });
        Mappings.Add(new PowerModeMappingItem { LoqMode = "GodMode", WindowsMode = "Ultimate Performance" });
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Save power mode mappings
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}

public partial class PowerModeMappingItem : ViewModelBase
{
    [ObservableProperty]
    private string _loqMode = "";
    
    [ObservableProperty]
    private string _windowsMode = "";
}