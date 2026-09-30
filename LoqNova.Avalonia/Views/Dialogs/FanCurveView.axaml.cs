using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoqNova.Avalonia.Views.Dialogs;

/// <summary>
/// Host view for <see cref="ViewModels.Dialogs.FanCurveViewModel"/>. The data
/// context is assigned by the dialog service.
/// </summary>
public partial class FanCurveView : UserControl
{
    public FanCurveView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
