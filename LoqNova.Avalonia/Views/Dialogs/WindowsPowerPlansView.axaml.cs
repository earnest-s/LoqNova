using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoqNova.Avalonia.Views.Dialogs;

/// <summary>Host view for <see cref="ViewModels.Dialogs.WindowsPowerPlansViewModel"/>.</summary>
public partial class WindowsPowerPlansView : UserControl
{
    public WindowsPowerPlansView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
