using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class WindowsPowerPlansViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<PowerPlanMappingItem> _mappings = new();
    
    public WindowsPowerPlansViewModel()
    {
        Mappings.Add(new PowerPlanMappingItem { LoqMode = "Quiet", PlanName = "Power Saver", PlanGuid = "a1841308-3541-4fab-bc81-f71556f20b4a" });
        Mappings.Add(new PowerPlanMappingItem { LoqMode = "Balance", PlanName = "Balanced", PlanGuid = "381b4222-f694-41f0-9685-ff5bb260df2e" });
        Mappings.Add(new PowerPlanMappingItem { LoqMode = "Performance", PlanName = "High Performance", PlanGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" });
        Mappings.Add(new PowerPlanMappingItem { LoqMode = "GodMode", PlanName = "Ultimate Performance", PlanGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61" });
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Save power plan mappings
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}

public partial class PowerPlanMappingItem : ViewModelBase
{
    [ObservableProperty]
    private string _loqMode = "";
    
    [ObservableProperty]
    private string _planName = "";
    
    [ObservableProperty]
    private string _planGuid = "";
}