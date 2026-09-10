using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Collections.ObjectModel;

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
            CloseCommand = new Avalonia.Input.RelayCommand(() => Messages.Remove(msg))
        };
        Messages.Add(msg);
        
        // Auto-remove after 5 seconds
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(5000);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Messages.Remove(msg));
        });
    }
}

public class SnackbarMessage
{
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Icon { get; set; } = "";
    public string IconColor { get; set; } = "";
    public System.Windows.Input.ICommand CloseCommand { get; set; } = null!;
}