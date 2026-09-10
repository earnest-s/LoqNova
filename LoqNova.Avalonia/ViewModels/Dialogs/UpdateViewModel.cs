using System;
using System.Collections.ObjectCollection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class UpdateViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _updateAvailable = false;
    
    [ObservableProperty]
    private string _currentVersion = "3.1.0";
    
    [ObservableProperty]
    private string _latestVersion = "3.1.1";
    
    [ObservableProperty]
    private string _releaseNotes = "• Fixed RGB audio visualizer frequency detection\n• Improved fan curve editor\n• Updated translations\n• Various bug fixes";
    
    [ObservableProperty]
    private DateTime _releaseDate = DateTime.Now;
    
    [ObservableProperty]
    private string _downloadUrl = "https://github.com/earnest/LOQ-Nova/releases";
    
    public UpdateViewModel()
    {
    }
    
    [RelayCommand]
    private async Task DownloadAsync()
    {
        // Open download page
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}