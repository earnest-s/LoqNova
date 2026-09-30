using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoqNova.Avalonia.ViewModels.Pages;

namespace LoqNova.Avalonia.Views.Pages;

public partial class AutomationPage : UserControl
{
    public AutomationPage()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Loads each step's selectable configuration values once the page is on screen.
    ///
    /// This is deliberately not done in the view models' constructors: a step's values
    /// come from the backend feature, which resolves from the global IoC container and
    /// can block, and doing that during layout hung the window.
    /// </summary>
    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is not AutomationViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.RefreshStepConfigurationsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Automation step configuration refresh failed: {ex}");
        }
    }
}
