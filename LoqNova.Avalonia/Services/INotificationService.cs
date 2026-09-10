using System;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error,
    ACConnected,
    ACDisconnected,
    FnLockChanged,
    CapsLockChanged,
    MicrophoneChanged,
    KeyboardChanged,
    RefreshRateChanged,
    TouchpadChanged,
    UpdateAvailable,
    SmartKeyTriggered,
    AlwaysOnTopChanged,
    AllScreensChanged,
    DurationChanged,
    PositionChanged
}

public class NotificationMessage
{
    public NotificationType Type { get; }
    public string Title { get; }
    public string? Message { get; }
    public TimeSpan? Duration { get; }
    
    public NotificationMessage(NotificationType type, string title, string? message = null, TimeSpan? duration = null)
    {
        Type = type;
        Title = title;
        Message = message;
        Duration = duration;
    }
}

public interface INotificationService
{
    event Action<NotificationMessage>? NotificationRequested;
    
    Task InitializeAsync();
    Task ShowAsync(NotificationMessage message);
    Task ShutdownAsync();
}