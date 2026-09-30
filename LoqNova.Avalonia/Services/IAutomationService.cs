using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// A pipeline as the Avalonia views see it. This is a projection of the real
/// <c>LoqNova.Lib.Automation.Pipeline.AutomationPipeline</c>, not a second model:
/// <see cref="IAutomationService"/> owns the backend instances and keeps this in step
/// with them. Note there is deliberately no per-pipeline or per-step Enabled flag,
/// because the backend has no such concept.
/// </summary>
public sealed class AutomationPipeline
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;

    /// <summary>Backend trigger type name; empty for a manual quick action.</summary>
    public string TriggerTypeName { get; set; } = string.Empty;

    public bool IsManual { get; init; }

    public ObservableCollection<AutomationStep> Steps { get; init; } = [];
}

/// <summary>A step as the views see it, backed by a real <c>IAutomationStep</c>.</summary>
public sealed class AutomationStep
{
    public string TypeName { get; set; } = string.Empty;
}

public interface IAutomationService
{
    /// <summary>The one global automation state, owned by the backend.</summary>
    bool IsEnabled { get; }

    ObservableCollection<AutomationPipeline> AutomaticPipelines { get; }

    ObservableCollection<AutomationPipeline> ManualPipelines { get; }

    event Action? PipelinesChanged;

    Task InitializeAsync();

    Task SetEnabledAsync(bool enabled);

    Task AddPipelineAsync(AutomationPipeline pipeline, bool isManual);

    Task RemovePipelineAsync(Guid id);

    Task UpdatePipelineAsync(AutomationPipeline pipeline);

    Task MovePipelineAsync(Guid id, int newIndex);

    Task SetTriggerAsync(Guid pipelineId, string triggerTypeName);

    Task AddStepAsync(Guid pipelineId, AutomationStep step);

    Task RemoveStepAsync(Guid pipelineId, int stepIndex);

    Task MoveStepAsync(Guid pipelineId, int fromIndex, int toIndex);

    /// <summary>Executes a manual pipeline through the real backend.</summary>
    Task RunNowAsync(Guid pipelineId);

    /// <summary>Discards uncommitted editor state by reloading from the backend.</summary>
    Task RevertAsync();

    /// <summary>The single write path.</summary>
    Task SaveAsync();
}
