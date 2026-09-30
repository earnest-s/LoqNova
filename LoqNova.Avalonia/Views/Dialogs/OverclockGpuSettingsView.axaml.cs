using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoqNova.Avalonia.Views.Dialogs;

/// <summary>
/// Host view for <see cref="ViewModels.Dialogs.OverclockGpuSettingsViewModel"/>. The
/// data context is assigned by the dialog service.
/// </summary>
public partial class OverclockGpuSettingsView : UserControl
{
    public OverclockGpuSettingsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
