using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoqNova.Avalonia.Views.Dialogs;

/// <summary>Host view for <see cref="ViewModels.Dialogs.NotificationsSettingsViewModel"/>.</summary>
public partial class NotificationsSettingsView : UserControl
{
    public NotificationsSettingsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
