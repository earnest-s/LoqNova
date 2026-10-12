using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoqNova.Avalonia.Views.Dialogs;

/// <summary>Host view for <see cref="ViewModels.Dialogs.ExcludeRefreshRatesViewModel"/>.</summary>
public partial class ExcludeRefreshRatesView : UserControl
{
    public ExcludeRefreshRatesView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
