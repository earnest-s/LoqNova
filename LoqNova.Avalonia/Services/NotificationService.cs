using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class NotificationService : INotificationService
{
    public event Action<NotificationMessage>? NotificationRequested;
    
    private WindowNotificationManager? _notificationManager;
    private readonly ISettingsService _settings;
    
    public NotificationService(ISettingsService settings)
    {
        _settings = settings;
    }
    
    public async Task InitializeAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _notificationManager = new WindowNotificationManager(desktop.MainWindow!)
            {
                Position = NotificationPosition.BottomRight,
                MaxItems = 5
            };
        }
    }
    
    public Task ShowAsync(NotificationMessage message)
    {
        if (!_settings.NotificationsEnabled) return Task.CompletedTask;
        
        var notificationType = message.Type switch
        {
            NotificationType.Success => NotificationType.Success,
            NotificationType.Warning => NotificationType.Warning,
            NotificationType.Error => NotificationType.Error,
            _ => NotificationType.Info
        };
        
        _notificationManager?.Show(new Notification(message.Title, message.Message ?? "", notificationType)
        {
            Duration = message.Duration ?? TimeSpan.FromSeconds(5)
        });
        
        NotificationRequested?.Invoke(message);
        return Task.CompletedTask;
    }
    
    public Task ShutdownAsync() => Task.CompletedTask;
}