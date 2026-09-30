using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoqNova.Avalonia.Views.Dialogs;

/// <summary>
/// Host view for <see cref="ViewModels.Dialogs.BalanceModeSettingsViewModel"/>. The
/// data context is assigned by <see cref="Services.DialogService"/> rather than
/// inherited, because a dialog is not part of the page's visual tree.
/// </summary>
public partial class BalanceModeSettingsView : UserControl
{
    public BalanceModeSettingsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
