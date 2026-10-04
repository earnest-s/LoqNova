using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LoqNova.Lib;
using LoqNova.Lib.Automation;
using LoqNova.Lib.Automation.Pipeline;
using LoqNova.Lib.Automation.Pipeline.Triggers;
using LoqNova.Lib.Automation.Steps;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Owns the editable draft of the automation configuration and delegates every
/// decision to the real <see cref="AutomationProcessor"/>.
///
/// The draft holds the backend's own <see cref="AutomationPipeline"/> objects, taken
/// from <c>GetPipelinesAsync</c> and handed to <c>ReloadPipelinesAsync</c> on Save.
/// Nothing is re-projected or rebuilt behind the editor's back, so in-place edits to
/// the draft survive; only Revert and Save swap the collection wholesale, which is what
/// WPF does.
/// </summary>
public sealed class RealAutomationService : IAutomationService, IDisposable
{
    private readonly IMainThreadDispatcher _dispatcher;

    private AutomationProcessor? _processor;
    private bool _initialised;

    /// <summary>The editor's working copy. Mutated in place; Save is what persists it.</summary>
    private readonly List<AutomationPipeline> _draft = [];

    public RealAutomationService(IMainThreadDispatcher dispatcher) => _dispatcher = dispatcher;

    public IReadOnlyList<AutomationStepOption> AvailableSteps => StepFactory.Steps;

    public IReadOnlyList<TriggerOption> AvailableTriggers => StepFactory.Triggers;

    public event Action? PipelinesReloaded;

    public event Action<AutomationPipeline>? PipelineEdited;

    public event Action? EnabledChanged;

    public bool IsEnabled => _processor?.IsEnabled ?? _enabledBeforeInit;

    private bool _enabledBeforeInit = true;

    public IReadOnlyList<AutomationPipeline> AutomaticPipelines =>
        [.. _draft.Where(p => p.Trigger is not null)];

    public IReadOnlyList<AutomationPipeline> ManualPipelines =>
        [.. _draft.Where(p => p.Trigger is null)];

    public async Task InitializeAsync()
    {
        if (_initialised)
        {
            return;
        }

        // Resolved after the readiness gate and off the UI thread: IoCContainer.Resolve
        // holds a global lock and InitializeAsync subscribes native listeners.
        _processor = await Task.Run(() => IoCContainer.Resolve<AutomationProcessor>())
            .ConfigureAwait(false);

        await _processor.InitializeAsync().ConfigureAwait(false);

        _processor.PipelinesChanged += OnPipelinesChanged;

        _enabledBeforeInit = _processor.IsEnabled;
        _initialised = true;

        await ReloadDraftAsync().ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (_processor is null)
        {
            return;
        }

        await _processor.SetEnabledAsync(enabled).ConfigureAwait(false);

        // Read back rather than trusting the request; the backend owns this state.
        _enabledBeforeInit = _processor.IsEnabled;

        await _dispatcher.InvokeAsync(() => EnabledChanged?.Invoke()).ConfigureAwait(false);
    }

    public async Task AddPipelineAsync(string? name, IAutomationPipelineTrigger? trigger)
    {
        var pipeline = new AutomationPipeline(name ?? string.Empty) { Trigger = trigger };

        _draft.Add(pipeline);

        await PublishAsync().ConfigureAwait(false);
    }

    public Task RemovePipelineAsync(AutomationPipeline pipeline)
    {
        // Draft only. The processor keeps running this pipeline until Save, as in WPF.
        _draft.Remove(pipeline);

    }

    public Task RenamePipelineAsync(AutomationPipeline pipeline, string? name)
    {
        pipeline.Name = name;

    }

    public Task SetIconAsync(AutomationPipeline pipeline, string? iconName)
    {
        pipeline.IconName = iconName;

    }

    public Task SetTriggerAsync(AutomationPipeline pipeline, IAutomationPipelineTrigger? trigger)
    {
        // A null trigger is what makes a pipeline a manual quick action.
        pipeline.Trigger = trigger;

    }

    public Task MovePipelineAsync(AutomationPipeline pipeline, int delta)
    {
        var index = _draft.IndexOf(pipeline);
        var target = index + delta;

        // One position at a time, clamped, exactly as WPF's Move Up/Down does.
        if (index >= 0 && target >= 0 && target < _draft.Count)
        {
            _draft.RemoveAt(index);
            _draft.Insert(target, pipeline);
        }

    }

    public Task AddStepAsync(AutomationPipeline pipeline, IAutomationStep step)
    {
        pipeline.Steps.Add(step);

    }

    public Task RemoveStepAsync(AutomationPipeline pipeline, IAutomationStep step)
    {
        pipeline.Steps.Remove(step);

    }

    public Task MoveStepAsync(AutomationPipeline pipeline, IAutomationStep step, int delta)
    {
        var index = pipeline.Steps.IndexOf(step);
        var target = index + delta;

        if (index >= 0 && target >= 0 && target < pipeline.Steps.Count)
        {
            pipeline.Steps.RemoveAt(index);
            pipeline.Steps.Insert(target, step);
        }

    }

    public Task ReplaceStepAsync(AutomationPipeline pipeline, IAutomationStep oldStep, IAutomationStep newStep)
    {
        var index = pipeline.Steps.IndexOf(oldStep);

        if (index >= 0)
        {
            pipeline.Steps[index] = newStep;
        }

    }

    public async Task RunNowAsync(AutomationPipeline pipeline)
    {
        if (_processor is null)
        {
            return;
        }

        // The draft is a deep copy, so the persisted pipeline with this id is what runs.
        await _processor.RunNowAsync(pipeline.Id).ConfigureAwait(false);
    }

    public async Task RevertAsync() => await ReloadDraftAsync().ConfigureAwait(false);

    public async Task SaveAsync()
    {
        if (_processor is null)
        {
            return;
        }

        // The one write path, as WPF uses it: the processor deep-copies, persists
        // automation.json, re-evaluates its listeners and raises PipelinesChanged.
        await _processor.ReloadPipelinesAsync([.. _draft]).ConfigureAwait(false);
    }

    private async Task ReloadDraftAsync()
    {
        if (_processor is null)
        {
            return;
        }

        var pipelines = await _processor.GetPipelinesAsync().ConfigureAwait(false);

        _draft.Clear();
        _draft.AddRange(pipelines);

        await PublishAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The draft was reloaded wholesale (initial load, Revert or a backend change), so
    /// every wrapper must be rebuilt.
    /// </summary>
    private Task PublishReloadedAsync() =>
        _dispatcher.InvokeAsync(() => PipelinesReloaded?.Invoke());

    /// <summary>
    /// One pipeline changed in place. Its wrapper is kept and only its contents are
    /// refreshed, so the rest of the editor keeps its state.
    /// </summary>
    private Task PublishEditedAsync(AutomationPipeline pipeline) =>
        _dispatcher.InvokeAsync(() => PipelineEdited?.Invoke(pipeline));

    private void OnPipelinesChanged(object? sender, List<AutomationPipeline> pipelines) => _ = ReloadDraftAsync();

    public void Dispose()
    {
        if (_processor is not null)
        {
            _processor.PipelinesChanged -= OnPipelinesChanged;
        }
    }
}
