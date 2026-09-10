using System;
using System.Collections.ObjectCollection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class DeviceInformationViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _modelName = "LOQ 15IRH8";
    
    [ObservableProperty]
    private string _serialNumber = "PF12345678";
    
    [ObservableProperty]
    private string _biosVersion = "2.14";
    
    [ObservableProperty]
    private string _ecVersion = "1.08";
    
    [ObservableProperty]
    private string _cpuModel = "Intel Core i7-13620H";
    
    [ObservableProperty]
    private string _gpuModel = "NVIDIA GeForce RTX 4060";
    
    [ObservableProperty]
    private string _memory = "16 GB DDR5-5200";
    
    [ObservableProperty]
    private string _storage = "1 TB NVMe SSD";
    
    [ObservableProperty]
    private string _display = "15.6\" 1920x1080 144Hz";
    
    [ObservableProperty]
    private string _windowsVersion = "Windows 11 Pro 23H2";
    
    [ObservableProperty]
    private string _appVersion = "3.1.0";
    
    public DeviceInformationViewModel()
    {
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}