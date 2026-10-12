using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoqNova.Avalonia.Views.Dialogs;

/// <summary>Host view for <see cref="ViewModels.Dialogs.SelectSmartKeyPipelinesViewModel"/>.</summary>
public partial class SelectSmartKeyPipelinesView : UserControl
{
    public SelectSmartKeyPipelinesView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
