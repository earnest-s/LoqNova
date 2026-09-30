using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LoqNova.Lib.Automation.Pipeline;
using LoqNova.Lib.Automation.Pipeline.Triggers;
using LoqNova.Lib.Automation.Steps;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// The Automation editor's contract with the real backend.
///
/// There is deliberately no Avalonia domain model here. Pipelines, triggers and steps
/// are the backend's own types; this interface only names the operations the editor
/// performs on the draft it owns. Any view model wraps a live backend object rather
/// than copying it.
/// </summary>
public interface IAutomationService
{
    /// <summary>The one global automation state, owned by the backend.</summary>
    bool IsEnabled { get; }

    /// <summary>Live draft pipelines that have a trigger, in evaluation order.</summary>
    IReadOnlyList<AutomationPipeline> AutomaticPipelines { get; }

    /// <summary>Live draft pipelines without a trigger: the quick actions.</summary>
    IReadOnlyList<AutomationPipeline> ManualPipelines { get; }

    event Action? DraftChanged;

    event Action? EnabledChanged;

    Task InitializeAsync();

    Task SetEnabledAsync(bool enabled);

    /// <summary>Adds a pipeline to the draft. Automatic when <paramref name="trigger"/> is set.</summary>
    Task AddPipelineAsync(string? name, IAutomationPipelineTrigger? trigger);

    /// <summary>Removes a pipeline from the draft only. Save is what persists it.</summary>
    Task RemovePipelineAsync(AutomationPipeline pipeline);

    Task RenamePipelineAsync(AutomationPipeline pipeline, string? name);

    Task SetIconAsync(AutomationPipeline pipeline, string? iconName);

    /// <summary>Replaces a pipeline's trigger. A null trigger makes it a quick action.</summary>
    Task SetTriggerAsync(AutomationPipeline pipeline, IAutomationPipelineTrigger? trigger);

    Task MovePipelineAsync(AutomationPipeline pipeline, int delta);

    Task AddStepAsync(AutomationPipeline pipeline, IAutomationStep step);

    Task RemoveStepAsync(AutomationPipeline pipeline, IAutomationStep step);

    Task MoveStepAsync(AutomationPipeline pipeline, IAutomationStep step, int delta);

    /// <summary>Replaces a step with a reconfigured copy of itself.</summary>
    Task ReplaceStepAsync(AutomationPipeline pipeline, IAutomationStep oldStep, IAutomationStep newStep);

    /// <summary>Executes through the real processor.</summary>
    Task RunNowAsync(AutomationPipeline pipeline);

    /// <summary>Discards the draft and reloads from the backend. Writes nothing.</summary>
    Task RevertAsync();

    /// <summary>The single write path.</summary>
    Task SaveAsync();

    /// <summary>Every real step type the backend supports, for the add-step picker.</summary>
    IReadOnlyList<AutomationStepOption> AvailableSteps { get; }

    /// <summary>Every real trigger type the backend supports, for the trigger picker.</summary>
    IReadOnlyList<TriggerOption> AvailableTriggers { get; }
}

/// <summary>A real backend step type, with the display name shown to the user.</summary>
public sealed record AutomationStepOption(string TypeName, string DisplayName, Func<IAutomationStep> Create);

/// <summary>A real backend trigger type, with a prototype and its display name.</summary>
public sealed record TriggerOption(string TypeName, string DisplayName, Func<IAutomationPipelineTrigger> Create);
