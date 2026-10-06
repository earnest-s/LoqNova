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

    private async void OnAddStepClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        // The button carries the pipeline it belongs to, so each pipeline adds the step
        // chosen in its own picker. A RelayCommand cannot wrap a parameterless async
        // Task, which is why this is a click handler rather than a command binding.
        if (sender is not Button { Tag: AutomationPipelineViewModel pipeline })
        {
            return;
        }

        await RunGuardedAsync(() => pipeline.AddSelectedStepAsync());
    }

    private async void OnPipelineMoveUpClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: AutomationPipelineViewModel pipeline })
        {
            await RunGuardedAsync(() => pipeline.MoveUpAsync());
        }
    }

    private async void OnPipelineMoveDownClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: AutomationPipelineViewModel pipeline })
        {
            await RunGuardedAsync(() => pipeline.MoveDownAsync());
        }
    }

    private async void OnPipelineRemoveClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: AutomationPipelineViewModel pipeline })
        {
            await RunGuardedAsync(() => pipeline.RemoveAsync());
        }
    }

    private async void OnPipelineRunNowClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: AutomationPipelineViewModel pipeline })
        {
            await RunGuardedAsync(() => pipeline.RunNowAsync());
        }
    }

    private async void OnStepMoveUpClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: AutomationStepViewModel step })
        {
            await RunGuardedAsync(() => step.MoveUpAsync());
        }
    }

    private async void OnStepMoveDownClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: AutomationStepViewModel step })
        {
            await RunGuardedAsync(() => step.MoveDownAsync());
        }
    }

    private async void OnStepRemoveClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: AutomationStepViewModel step })
        {
            await RunGuardedAsync(() => step.RemoveAsync());
        }
    }

    /// <summary>
    /// Runs an editor operation, containing any failure. These are all async void event
    /// handlers, where an escaping exception terminates the process.
    /// </summary>
    private static async Task RunGuardedAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Automation operation failed: {ex}");
        }
    }

    private async void OnAttached(object? sender, EventArgs e)
    {
        AutoDiag.Mark("PAGE OnAttached begin");

        if (DataContext is not AutomationViewModel viewModel)
        {
            AutoDiag.Mark("PAGE OnAttached: no AutomationViewModel DataContext");
            return;
        }

        try
        {
            AutoDiag.Mark("PAGE calling RefreshStepConfigurationsAsync");
            await viewModel.RefreshStepConfigurationsAsync();
            AutoDiag.Mark("PAGE RefreshStepConfigurationsAsync done");
        }
        catch (Exception ex)
        {
            AutoDiag.Mark($"PAGE refresh threw {ex.GetType().Name}: {ex.Message}");
            Debug.WriteLine($"Automation step configuration refresh failed: {ex}");
        }
    }
}
