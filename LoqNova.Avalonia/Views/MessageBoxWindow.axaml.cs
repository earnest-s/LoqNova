using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LoqNova.Avalonia.Views;

public partial class MessageBoxWindow : Window
{
    public MessageBoxWindow(string message, string title)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close();
}
