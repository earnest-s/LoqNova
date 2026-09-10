using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class BootLogoViewModel : ViewModelBase
{
    private readonly IFileDialogService _fileDialogService;
    
    [ObservableProperty]
    private string _selectedImagePath = "";
    
    [ObservableProperty]
    private bool _isSupported = true;
    
    public BootLogoViewModel(IFileDialogService fileDialogService)
    {
        _fileDialogService = fileDialogService;
    }
    
    [RelayCommand]
    private async Task BrowseImageAsync()
    {
        var path = await _fileDialogService.ShowOpenFileDialogAsync(
            "Select Boot Logo Image", "Image Files|*.bmp;*.png;*.jpg;*.jpeg");
        if (!string.IsNullOrEmpty(path))
        {
            SelectedImagePath = path;
        }
    }
    
    [RelayCommand]
    private async Task ApplyAsync()
    {
        // Apply boot logo
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}