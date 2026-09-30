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
    /// Dismisses the dialog when the scrim is clicked. The scrim covers the page, so
    /// without this there is no way out except the dialog's own Close button.
    /// </summary>
    private void OnDialogScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
            vm.CloseDialog();
    }

    /// <summary>
    /// The window uses a custom title bar (native chrome is suppressed with the
    /// ExtendClientArea* hints), so dragging the window has to be handled here.
    /// Presses that originate on an interactive control are ignored so the
    /// caption buttons keep working.
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Only the caption buttons are excluded. The previous guard rejected anything
        // Interactive, which also threw away presses that landed on the labels and icons
        // filling most of the bar - so the window could not be dragged from most of the
        // area a user would naturally grab it by.
        if (e.Source is Button or ToggleButton or ComboBox)
        {
            return;
        }

        BeginMoveDrag(e);
    }

    /// <summary>Double-clicking the custom title bar maximises and restores, as native does.</summary>
    private void OnTitleBarTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Button or ToggleButton or ComboBox)
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
