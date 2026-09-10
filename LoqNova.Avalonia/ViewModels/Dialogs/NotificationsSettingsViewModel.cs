using System;
using System.Collections.ObjectCollection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class NotificationsSettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _enableAll = true;
    
    [ObservableProperty]
    private ObservableCollection<NotificationTypeItem> _notificationTypes = new();
    
    public NotificationsSettingsViewModel()
    {
        NotificationTypes.Add(new NotificationTypeItem { Type = "AC Connected", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "AC Disconnected", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "FnLock Changed", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "CapsLock Changed", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "Microphone Changed", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "Keyboard Changed", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "Refresh Rate Changed", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "Touchpad Changed", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "Update Available", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "Smart Key Triggered", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "Always On Top", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "All Screens", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "Duration", Enabled = true });
        NotificationTypes.Add(new NotificationTypeItem { Type = "Position", Enabled = true });
    }
    
    partial void OnEnableAllChanged(bool value)
    {
        foreach (var item in NotificationTypes)
        {
            item.Enabled = value;
        }
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Save notification settings
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
    }
}

public partial class NotificationTypeItem : ViewModelBase
{
    [ObservableProperty]
    private string _type = "";
    
    [ObservableProperty]
    private bool _enabled = true;
}