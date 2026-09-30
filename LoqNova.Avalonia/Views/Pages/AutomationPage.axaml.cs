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

    private async void OnAddStepClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // The button carries the pipeline it belongs to, so each pipeline adds the step
        // chosen in its own picker. A RelayCommand cannot wrap a parameterless async
        // Task, which is why this is a click handler rather than a command binding.
        if (sender is not Button { Tag: AutomationPipelineViewModel pipeline })
        {
            return;
        }

        try
        {
            await pipeline.AddSelectedStepAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Adding an automation step failed: {ex}");
        }
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
