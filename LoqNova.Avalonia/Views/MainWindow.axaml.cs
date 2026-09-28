using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LoqNova.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// The window uses a custom title bar (native chrome is suppressed with the
    /// ExtendClientArea* hints), so dragging the window has to be handled here.
    /// Presses that originate on an interactive control are ignored so the
    /// caption buttons keep working.
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            e.Source is not Interactive)
        {
            BeginMoveDrag(e);
        }
    }
}
