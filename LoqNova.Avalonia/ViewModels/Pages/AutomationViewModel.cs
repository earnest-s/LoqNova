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

    /// <summary>
    /// WPF excludes QuickActionAutomationStep from a manual pipeline's step catalogue,
    /// so the picker for Quick Actions omits it too.
    /// </summary>
    public IReadOnlyList<string> ManualStepTypes =>
        [.. StepFactory.SupportedStepTypes.Where(t => t != nameof(QuickActionAutomationStep))];

    /// <summary>The step type chosen in a pipeline's picker.</summary>
    [ObservableProperty]
    private string? _selectedStepType;

    /// <summary>WPF asks for a quick action's name inline, capped at 50 characters.</summary>
    [ObservableProperty]
    private string? _pendingManualName;

    [ObservableProperty]
    private bool _isManualNamePromptOpen;

    /// <summary>Real backend trigger types.</summary>
    public IReadOnlyList<string> TriggerTypes =>
        [.. StepFactory.SupportedTriggers.Select(t => t.GetType().Name)];

    public AutomationViewModel(IAutomationService automationService)
    {
        _automationService = automationService;

        _automationService.PipelinesChanged += OnPipelinesChanged;

        IsAutomationEnabled = _automationService.IsEnabled;

        // The service loads during app startup, which happens before this view model is
        // constructed, so the initial publish has already been missed. Populate from
        // whatever the service currently holds rather than waiting for the next change.
        OnPipelinesChanged();
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

        // The service holds the projected pipelines; the view binds to these copies, so
        // they have to be rebuilt whenever the backend republishes. This previously only
        // refreshed the toggle, which left both containers permanently empty.
        AutomaticPipelines.Clear();
        ManualPipelines.Clear();

        foreach (var pipeline in _automationService.AutomaticPipelines)
        {
            AutomaticPipelines.Add(AutomationPipelineViewModel.Create(pipeline, isManual: false));
        }

        foreach (var pipeline in _automationService.ManualPipelines)
        {
            ManualPipelines.Add(AutomationPipelineViewModel.Create(pipeline, isManual: true));
        }
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
        // WPF creates the pipeline with a real, chosen trigger rather than a decorative
        // one, so the trigger type is picked first and a real backend trigger is built.
        var triggerTypeName = SelectedTriggerType
            ?? StepFactory.SupportedTriggers[0].GetType().Name;

        var pipeline = new AutomationPipeline
        {
            Name = "New Pipeline",
            TriggerTypeName = triggerTypeName
        };

        await _automationService.AddPipelineAsync(pipeline, isManual: false).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task AddManualPipelineAsync()
    {
        // WPF asks for the name inline and aborts on a blank entry, with no duplicate
        // checking. The inline field replaces that prompt.
        var name = PendingManualName?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await _automationService
            .AddPipelineAsync(new AutomationPipeline { Name = name[..Math.Min(name.Length, 50)] }, isManual: true)
            .ConfigureAwait(true);

        PendingManualName = null;
        IsManualNamePromptOpen = false;

        IsDirty = true;
    }

    /// <summary>WPF shows an inline name box rather than opening a window.</summary>
    [RelayCommand]
    private void OpenManualNamePrompt() => IsManualNamePromptOpen = true;

    [RelayCommand]
    private void CancelManualNamePrompt()
    {
        PendingManualName = null;
        IsManualNamePromptOpen = false;
    }

    /// <summary>Real backend trigger types, for the Add Automatic picker.</summary>
    [ObservableProperty]
    private string? _selectedTriggerType;

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
    private async Task AddStepAsync(AutomationStepRequest? request)
    {
        if (request is null || string.IsNullOrEmpty(request.TypeName))
        {
            return;
        }

        await _automationService.AddStepAsync(
            request.PipelineId, new AutomationStep { TypeName = request.TypeName }).ConfigureAwait(true);

        IsDirty = true;
    }

    [RelayCommand]
    private async Task RemoveStepAsync(AutomationStepViewModel? step)
    {
        if (step is null)
        {
            return;
        }

        await _automationService.RemoveStepAsync(step.PipelineId, step.Index).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task MoveStepUpAsync(AutomationStepViewModel? step)
    {
        if (step is null || step.Index <= 0)
        {
            return;
        }

        await _automationService.MoveStepAsync(step.PipelineId, step.Index, step.Index - 1)
            .ConfigureAwait(true);

        IsDirty = true;
    }

    [RelayCommand]
    private async Task MoveStepDownAsync(AutomationStepViewModel? step)
    {
        if (step is null)
        {
            return;
        }

        var pipelineId = step.PipelineId;

        if (step.Index < 0 || step.Index >= step.LastIndex)
        {
            return;
        }

        await _automationService.MoveStepAsync(step.PipelineId, step.Index, step.Index + 1)
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
    private async Task RenamePipelineAsync(PipelineRenameRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return;
        }

        await _automationService.UpdatePipelineAsync(
            new AutomationPipeline
            {
                Id = request.PipelineId,
                Name = request.Name,
                Icon = request.Icon
            })
            .ConfigureAwait(true);

        IsDirty = true;
    }

    [RelayCommand]
    private async Task SetTriggerAsync(PipelineTriggerRequest? request)
    {
        if (request is null)
        {
            return;
        }

        await _automationService.SetTriggerAsync(request.PipelineId, request.TriggerTypeName)
            .ConfigureAwait(true);

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

    /// <summary>
    /// Projects the service's pipeline for binding. The step rows carry their position
    /// because Move Up/Down and Delete are index based, exactly as WPF's are.
    /// </summary>
    public static AutomationPipelineViewModel Create(AutomationPipeline source, bool isManual)
    {
        var viewModel = new AutomationPipelineViewModel
        {
            Id = source.Id,
            IsManual = isManual,
            Name = source.Name,
            Icon = source.Icon,
            TriggerTypeName = source.TriggerTypeName
        };

        for (var i = 0; i < source.Steps.Count; i++)
        {
            viewModel.Steps.Add(new AutomationStepViewModel
            {
                Index = i,
                LastIndex = source.Steps.Count - 1,
                PipelineId = source.Id,
                TypeName = source.Steps[i].TypeName
            });
        }

        return viewModel;
    }
}

/// <summary>A step projected from a real <see cref="IAutomationStep"/>.</summary>
/// <summary>Parameter for rename: which pipeline, and the new name.</summary>
public sealed class PipelineRenameRequest
{
    public Guid PipelineId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Icon { get; init; } = string.Empty;
}

/// <summary>Parameter for the trigger command: which pipeline, and which real trigger type.</summary>
public sealed class PipelineTriggerRequest
{
    public Guid PipelineId { get; init; }

    public string TriggerTypeName { get; init; } = string.Empty;
}

/// <summary>Parameter for the add-step command: which pipeline, and which real step type.</summary>
public sealed class AutomationStepRequest
{
    public Guid PipelineId { get; init; }

    public string TypeName { get; init; } = string.Empty;
}

public partial class AutomationStepViewModel : ViewModelBase
{
    public int Index { get; init; }

    public int LastIndex { get; init; }

    public Guid PipelineId { get; init; }

    [ObservableProperty]
    private string _typeName = string.Empty;
}
