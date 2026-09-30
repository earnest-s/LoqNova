using System;
using System.Diagnostics;
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

        // Loading a step's selectable values calls the backend feature, which resolves
        // from the global IoC container and can block. Doing that in the view models'
        // constructors, during layout, hung the window - so it is deferred to here.
        AttachedToVisualTree += OnAttached;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnAttached(object? sender, EventArgs e)
    {
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
            Debug.WriteLine($"Automation step configuration refresh failed: {ex}");
        }
    }
}
