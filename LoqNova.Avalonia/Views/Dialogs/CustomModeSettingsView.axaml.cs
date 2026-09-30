using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoqNova.Avalonia.Views.Dialogs;

/// <summary>
/// Host view for <see cref="ViewModels.Dialogs.CustomModeSettingsViewModel"/>. The
/// data context is assigned by the dialog service.
/// </summary>
public partial class CustomModeSettingsView : UserControl
{
    public CustomModeSettingsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
