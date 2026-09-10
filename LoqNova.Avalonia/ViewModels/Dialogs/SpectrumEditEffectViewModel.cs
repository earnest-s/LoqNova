using System;
using System.Collections.ObjectCollection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class SpectrumEditEffectViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _effectName = "Custom Effect";
    
    [ObservableProperty]
    private string _selectedEffectType = "Static";
    
    [ObservableProperty]
    private ObservableCollection<SpectrumZoneConfig> _zones = new();
    
    public ObservableCollection<string> EffectTypes { get; } = new()
    {
        "Static", "Breath", "Wave", "Smooth", "Custom"
    };
    
    public SpectrumEditEffectViewModel()
    {
        Zones.Add(new SpectrumZoneConfig { Name = "Keyboard", Color = "FF0000", Brightness = 100 });
        Zones.Add(new SpectrumZoneConfig { Name = "Front", Color = "00FF00", Brightness = 100 });
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Save effect
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}

public partial class SpectrumZoneConfig : ViewModelBase
{
    [ObservableProperty]
    private string _name = "";
    
    [ObservableProperty]
    private string _color = "";
    
    [ObservableProperty]
    private int _brightness = 100;
}