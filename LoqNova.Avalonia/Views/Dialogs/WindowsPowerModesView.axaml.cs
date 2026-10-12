using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoqNova.Avalonia.Views.Dialogs;

/// <summary>Host view for <see cref="ViewModels.Dialogs.WindowsPowerModesViewModel"/>.</summary>
public partial class WindowsPowerModesView : UserControl
{
    public WindowsPowerModesView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
