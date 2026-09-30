using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Automation.Pipeline.Triggers;
using LoqNova.Lib.Automation.Steps;

namespace LoqNova.Avalonia.ViewModels.Pages;

/// <summary>
/// Drives the Automation page from the real backend. There is no per-pipeline or
/// per-step enable flag, because the backend has no such concept: WPF exposes one
/// global toggle (AutomationProcessor.IsEnabled) and a pipeline is either automatic
/// (it has a trigger) or a manual quick action (it has none).
/// </summary>
public partial class AutomationViewModel : ViewModelBase
{
    private readonly IAutomationService _automationService;

    [ObservableProperty]
    private bool _isAutomationEnabled;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True while there are uncommitted edits, which is when Save/Revert show.</summary>
    [ObservableProperty]
    private bool _isDirty;

    public ObservableCollection<AutomationPipelineViewModel> AutomaticPipelines { get; } = new();

    public ObservableCollection<AutomationPipelineViewModel> ManualPipelines { get; } = new();

    /// <summary>Real backend step types, not a hand-written list of names.</summary>
    public IReadOnlyList<string> StepTypes => StepFactory.SupportedStepTypes;

    /// <summary>Real backend trigger types.</summary>
    public IReadOnlyList<string> TriggerTypes =>
        [.. StepFactory.SupportedTriggers.Select(t => t.GetType().Name)];

    public AutomationViewModel(IAutomationService automationService)
    {
        _automationService = automationService;

        _automationService.PipelinesChanged += OnPipelinesChanged;

        IsAutomationEnabled = _automationService.IsEnabled;
    }

    /// <summary>
    /// Called once the backend container is ready. Resolving the processor and running
    /// its initialization both happen off the UI thread and behind the readiness gate,
    /// so this must not be awaited from a constructor.
    /// </summary>
    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsLoading = true;

        try
        {
            await _automationService.InitializeAsync().ConfigureAwait(true);

            IsAutomationEnabled = _automationService.IsEnabled;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnPipelinesChanged()
    {
        IsAutomationEnabled = _automationService.IsEnabled;
    }

    partial void OnIsAutomationEnabledChanged(bool value) => _ = SetEnabledAsync(value);

    [RelayCommand]
    private async Task SetEnabledAsync(bool enabled)
    {
        await _automationService.SetEnabledAsync(enabled).ConfigureAwait(true);

        // Re-read rather than trusting the toggle: the backend is the source of truth.
        IsAutomationEnabled = _automationService.IsEnabled;
    }

    [RelayCommand]
    private async Task AddAutomaticPipelineAsync()
    {
        await _automationService.AddPipelineAsync(
            new AutomationPipeline { Name = "New Pipeline" }, isManual: false).ConfigureAwait(true);

        IsDirty = true;
    }

    [RelayCommand]
    private async Task AddManualPipelineAsync()
    {
        await _automationService.AddPipelineAsync(
            new AutomationPipeline { Name = "New Quick Action" }, isManual: true).ConfigureAwait(true);

        IsDirty = true;
    }

    [RelayCommand]
    private async Task RemovePipelineAsync(AutomationPipelineViewModel? pipeline)
    {
        if (pipeline is null)
        {
            return;
        }

        // WPF deletes from the editor only; the backend is untouched until Save.
        await _automationService.RemovePipelineAsync(pipeline.Id).ConfigureAwait(true);

        IsDirty = true;
    }

    [RelayCommand]
    private async Task MovePipelineUpAsync(AutomationPipelineViewModel? pipeline)
    {
        if (pipeline is null)
        {
            return;
        }

        var list = pipeline.IsManual ? ManualPipelines : AutomaticPipelines;
        var index = list.IndexOf(pipeline);

        if (index <= 0)
        {
            return;
        }

        await _automationService.MovePipelineAsync(pipeline.Id, index - 1).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task MovePipelineDownAsync(AutomationPipelineViewModel? pipeline)
    {
        if (pipeline is null)
        {
            return;
        }

        var list = pipeline.IsManual ? ManualPipelines : AutomaticPipelines;
        var index = list.IndexOf(pipeline);

        if (index < 0 || index >= list.Count - 1)
        {
            return;
        }

        await _automationService.MovePipelineAsync(pipeline.Id, index + 1).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task AddStepAsync(AutomationPipelineViewModel? pipeline, string? typeName)
    {
        if (pipeline is null || string.IsNullOrEmpty(typeName))
        {
            return;
        }

        await _automationService.AddStepAsync(
            pipeline.Id, new AutomationStep { TypeName = typeName }).ConfigureAwait(true);

        IsDirty = true;
    }

    [RelayCommand]
    private async Task RemoveStepAsync(AutomationPipelineViewModel? pipeline, AutomationStepViewModel? step)
    {
        if (pipeline is null || step is null)
        {
            return;
        }

        await _automationService.RemoveStepAsync(pipeline.Id, step.Index).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task MoveStepUpAsync(AutomationPipelineViewModel? pipeline, AutomationStepViewModel? step)
    {
        if (pipeline is null || step is null || step.Index <= 0)
        {
            return;
        }

        await _automationService.MoveStepAsync(pipeline.Id, step.Index, step.Index - 1)
            .ConfigureAwait(true);

        IsDirty = true;
    }

    [RelayCommand]
    private async Task MoveStepDownAsync(AutomationPipelineViewModel? pipeline, AutomationStepViewModel? step)
    {
        if (pipeline is null || step is null)
        {
            return;
        }

        var last = pipeline.Steps.Count - 1;

        if (step.Index < 0 || step.Index >= last)
        {
            return;
        }

        await _automationService.MoveStepAsync(pipeline.Id, step.Index, step.Index + 1)
            .ConfigureAwait(true);

        IsDirty = true;
    }

    /// <summary>Runs a manual quick action through the real backend.</summary>
    [RelayCommand]
    private async Task RunNowAsync(AutomationPipelineViewModel? pipeline)
    {
        if (pipeline is null)
        {
            return;
        }

        await _automationService.RunNowAsync(pipeline.Id).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RenamePipelineAsync(AutomationPipelineViewModel? pipeline, string? name)
    {
        if (pipeline is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await _automationService.UpdatePipelineAsync(
            new AutomationPipeline { Id = pipeline.Id, Name = name, Icon = pipeline.Icon })
            .ConfigureAwait(true);

        IsDirty = true;
    }

    [RelayCommand]
    private async Task SetTriggerAsync(AutomationPipelineViewModel? pipeline, string? triggerTypeName)
    {
        if (pipeline is null || string.IsNullOrEmpty(triggerTypeName))
        {
            return;
        }

        await _automationService.SetTriggerAsync(pipeline.Id, triggerTypeName).ConfigureAwait(true);
        IsDirty = true;
    }

    /// <summary>The single write path, exactly as WPF uses it.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        await _automationService.SaveAsync().ConfigureAwait(true);
        IsDirty = false;
    }

    /// <summary>WPF's Revert: rebuild from the backend and discard uncommitted edits.</summary>
    [RelayCommand]
    private async Task RevertAsync()
    {
        await _automationService.RevertAsync().ConfigureAwait(true);
        IsDirty = false;
    }
}

/// <summary>A pipeline projected from the real backend model.</summary>
public partial class AutomationPipelineViewModel : ViewModelBase
{
    public Guid Id { get; init; }

    public bool IsManual { get; init; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private string _triggerTypeName = string.Empty;

    public ObservableCollection<AutomationStepViewModel> Steps { get; } = [];
}

/// <summary>A step projected from a real <see cref="IAutomationStep"/>.</summary>
public partial class AutomationStepViewModel : ViewModelBase
{
    public int Index { get; init; }

    [ObservableProperty]
    private string _typeName = string.Empty;
}
