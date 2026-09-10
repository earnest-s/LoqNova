using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class ExtendedHybridModeInfoViewModel : ViewModelBase
{
    public ExtendedHybridModeInfoViewModel()
    {
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}