using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoqNova.Avalonia.ViewModels.Pages;

namespace LoqNova.Avalonia.Views.Pages;

public partial class MacroPage : UserControl
{
    public MacroPage()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}