using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace LoqNova.Avalonia.Views.Controls.Notifications;

public partial class SnackbarHost : UserControl
{
    public ObservableCollection<SnackbarMessage> Messages { get; } = new();
    
    public SnackbarHost()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    public void Show(string title, string message, string icon, string iconColor)
    {
        var msg = new SnackbarMessage
        {
            Title = title,
            Message = message,
            Icon = icon,
            IconColor = iconColor,
            CloseCommand = new RelayCommand(() => { }) // Initialize first
        };
        
        // Update the close command to remove the message
        msg.CloseCommand = new RelayCommand(() => Messages.Remove(msg));
        
        Messages.Add(msg);
        
        // Auto-remove after 5 seconds
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            Dispatcher.UIThread.Post(() => Messages.Remove(msg));
        });
    }
}

public class SnackbarMessage
{
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Icon { get; set; } = "";
    public string IconColor { get; set; } = "";
    public ICommand CloseCommand { get; set; } = null!;
}