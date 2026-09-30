using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using LoqNova.Lib;
using LoqNova.Lib.Automation;
using LoqNova.Lib.Automation.Pipeline.Triggers;
using LoqNova.Lib.Automation.Steps;
using RealPipeline = LoqNova.Lib.Automation.Pipeline.AutomationPipeline;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Adapter over the real <see cref="AutomationProcessor"/>. This owns no automation
/// logic: every load, mutation, persistence and execution decision belongs to the
/// backend. The service keeps an editor draft so that Delete and Revert behave the way
/// WPF's do - uncommitted until Save.
/// </summary>
public sealed class RealAutomationService : IAutomationService, IDisposable
{
    private readonly IMainThreadDispatcher _dispatcher;

    private AutomationProcessor? _processor;
    private bool _initialised;

    /// <summary>Uncommitted editor state. Save is what hands this to the backend.</summary>
    private readonly List<RealPipeline> _draft = [];

    public RealAutomationService(IMainThreadDispatcher dispatcher) => _dispatcher = dispatcher;

    public ObservableCollection<AutomationPipeline> AutomaticPipelines { get; } = [];

    public ObservableCollection<AutomationPipeline> ManualPipelines { get; } = [];

    public event Action? PipelinesChanged;

    public bool IsEnabled => _processor?.IsEnabled ?? _fallbackEnabled;

    private bool _fallbackEnabled = true;

    public async Task InitializeAsync()
    {
        if (_initialised)
        {
            return;
        }

        // Resolved after the readiness gate and off the UI thread: IoCContainer.Resolve
        // holds a global lock, and InitializeAsync subscribes native listeners.
        _processor = await Task.Run(() => IoCContainer.Resolve<AutomationProcessor>())
            .ConfigureAwait(false);

        await _processor.InitializeAsync().ConfigureAwait(false);

        _processor.PipelinesChanged += OnPipelinesChanged;

        _fallbackEnabled = _processor.IsEnabled;
        _initialised = true;

        await ReloadAsync().ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (_processor is null)
        {
            return;
        }

        await _processor.SetEnabledAsync(enabled).ConfigureAwait(false);

        // Read back from the backend rather than assuming the write took effect.
        _fallbackEnabled = _processor.IsEnabled;

        await NotifyAsync().ConfigureAwait(false);
    }

    public Task AddPipelineAsync(AutomationPipeline pipeline, bool isManual)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        var model = new RealPipeline(pipeline.Name) { IconName = pipeline.Icon };

        // A pipeline is automatic when it has a trigger; a quick action has none.
        if (!isManual)
        {
            model.Trigger = new OnStartupAutomationPipelineTrigger();
        }

        _draft.Add(model);

        return PublishAsync();
    }

    public Task RemovePipelineAsync(Guid id)
    {
        // Editor-only, as in WPF: the backend keeps running this pipeline until Save.
        _draft.RemoveAll(p => p.Id == id);

        return PublishAsync();
    }

    public Task UpdatePipelineAsync(AutomationPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        var model = _draft.FirstOrDefault(p => p.Id == pipeline.Id);

        if (model is null)
        {
            return Task.CompletedTask;
        }

        model.Name = pipeline.Name;
        model.IconName = pipeline.Icon;

        return PublishAsync();
    }

    public Task MovePipelineAsync(Guid id, int newIndex)
    {
        var index = _draft.FindIndex(p => p.Id == id);

        if (index < 0)
        {
            return Task.CompletedTask;
        }

        newIndex = Math.Clamp(newIndex, 0, _draft.Count - 1);

        var model = _draft[index];
        _draft.RemoveAt(index);
        _draft.Insert(newIndex, model);

        return PublishAsync();
    }

    public Task SetTriggerAsync(Guid pipelineId, string triggerTypeName)
    {
        var model = _draft.FirstOrDefault(p => p.Id == pipelineId);

        if (model is null)
        {
            return Task.CompletedTask;
        }

        // A null trigger makes the pipeline a manual quick action, which is how the
        // backend distinguishes the two kinds.
        model.Trigger = string.IsNullOrEmpty(triggerTypeName)
            ? null
            : StepFactory.CreateTrigger(triggerTypeName);

        return PublishAsync();
    }

    public Task AddStepAsync(Guid pipelineId, AutomationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        IAutomationStep? backendStep = StepFactory.Create(step.TypeName);

        if (backendStep is null)
        {
            throw new InvalidOperationException($"Unknown step type: {step.TypeName}");
        }

        _draft.FirstOrDefault(p => p.Id == pipelineId)?.Steps.Add(backendStep);

        return PublishAsync();
    }

    public Task RemoveStepAsync(Guid pipelineId, int stepIndex)
    {
        var model = _draft.FirstOrDefault(p => p.Id == pipelineId);

        if (model is not null && stepIndex >= 0 && stepIndex < model.Steps.Count)
        {
            model.Steps.RemoveAt(stepIndex);
        }

        return PublishAsync();
    }

    public Task MoveStepAsync(Guid pipelineId, int fromIndex, int toIndex)
    {
        var model = _draft.FirstOrDefault(p => p.Id == pipelineId);

        if (model is null || fromIndex < 0 || fromIndex >= model.Steps.Count)
        {
            return Task.CompletedTask;
        }

        toIndex = Math.Clamp(toIndex, 0, model.Steps.Count - 1);

        var step = model.Steps[fromIndex];
        model.Steps.RemoveAt(fromIndex);
        model.Steps.Insert(toIndex, step);

        return PublishAsync();
    }

    public async Task RunNowAsync(Guid pipelineId)
    {
        if (_processor is null)
        {
            return;
        }

        await _processor.RunNowAsync(pipelineId).ConfigureAwait(false);
    }

    /// <summary>
    /// WPF's Revert. The backend exposes no discard API, so this reloads the in-memory
    /// pipelines and rebuilds the editor state, writing nothing to disk.
    /// </summary>
    public async Task RevertAsync() => await ReloadAsync().ConfigureAwait(false);

    public async Task SaveAsync()
    {
        if (_processor is null)
        {
            return;
        }

        // The one write path, as WPF uses it: the backend deep-copies, persists
        // automation.json, re-evaluates listeners and raises PipelinesChanged.
        await _processor.ReloadPipelinesAsync([.. _draft]).ConfigureAwait(false);
    }

    private async Task ReloadAsync()
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

    private Task PublishAsync()
    {
        AutomaticPipelines.Clear();
        ManualPipelines.Clear();

        foreach (var model in _draft)
        {
            var projected = Project(model, model.Trigger is null);

            if (model.Trigger is null)
            {
                ManualPipelines.Add(projected);
            }
            else
            {
                AutomaticPipelines.Add(projected);
            }
        }

        return NotifyAsync();
    }

    private static AutomationPipeline Project(RealPipeline model, bool isManual)
    {
        var projected = new AutomationPipeline
        {
            Id = model.Id,
            Name = model.Name ?? "Unnamed",
            Icon = model.IconName ?? string.Empty,
            TriggerTypeName = model.Trigger?.GetType().Name ?? string.Empty,
            IsManual = isManual
        };

        for (var i = 0; i < model.Steps.Count; i++)
        {
            projected.Steps.Add(new AutomationStep { TypeName = model.Steps[i].GetType().Name });
        }

        return projected;
    }

    private Task NotifyAsync() => _dispatcher.InvokeAsync(() => PipelinesChanged?.Invoke());

    private void OnPipelinesChanged(object? sender, List<RealPipeline> pipelines) => _ = ReloadAsync();

    public void Dispose()
    {
        if (_processor is not null)
        {
            _processor.PipelinesChanged -= OnPipelinesChanged;
        }
    }
}
