using System;
using System.Collections.ObjectCollection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class SelectSmartKeyPipelinesViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<PipelineSelectionItem> _availablePipelines = new();
    
    [ObservableProperty]
    private PipelineSelectionItem? _selectedSinglePress;
    
    [ObservableProperty]
    private PipelineSelectionItem? _selectedDoublePress;
    
    public SelectSmartKeyPipelinesViewModel()
    {
        AvailablePipelines.Add(new PipelineSelectionItem { Name = "Gaming Mode", Icon = "Game" });
        AvailablePipelines.Add(new PipelineSelectionItem { Name = "Battery Saver", Icon = "BatterySaver" });
        AvailablePipelines.Add(new PipelineSelectionItem { Name = "Quick: Toggle GPU", Icon = "Gpu" });
        AvailablePipelines.Add(new PipelineSelectionItem { Name = "Quick: Silent Mode", Icon = "VolumeMute" });
        AvailablePipelines.Add(new PipelineSelectionItem { Name = "None", Icon = "Block" });
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Save smart key pipeline selections
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}

public partial class PipelineSelectionItem : ViewModelBase
{
    [ObservableProperty]
    private string _name = "";
    
    [ObservableProperty]
    private string _icon = "";
}