using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class UnsupportedViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _message = "This feature is not supported on your device.";
    
    [ObservableProperty]
    private string _details = "";
    
    public UnsupportedViewModel()
    {
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}