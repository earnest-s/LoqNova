using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class NotificationViewModel : ViewModelBase
{
    [ObservableProperty]
    private NotificationType _type = NotificationType.Info;
    
    [ObservableProperty]
    private string _title = "";
    
    [ObservableProperty]
    private string _message = "";
    
    [ObservableProperty]
    private string _icon = "";
    
    public NotificationViewModel()
    {
    }
    
    public void SetNotification(NotificationMessage msg)
    {
        Type = msg.Type;
        Title = msg.Title;
        Message = msg.Message ?? "";
        
        Icon = msg.Type switch
        {
            NotificationType.Success => "CheckmarkCircle",
            NotificationType.Warning => "Alert",
            NotificationType.Error => "ErrorCircle",
            _ => "InformationCircle"
        };
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}