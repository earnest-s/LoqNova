using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using LoqNova.Lib;
using LoqNova.Lib.Automation;
using LoqNova.Lib.Automation.Pipeline;
using LoqNova.Lib.Automation.Steps;
using RealPipeline = LoqNova.Lib.Automation.Pipeline.AutomationPipeline;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Adapter over the real <see cref="AutomationProcessor"/>. This is the only production
/// implementation of <see cref="IAutomationService"/>; it owns no automation logic of
/// its own, it only projects the backend's <see cref="RealPipeline"/> model onto the
/// shapes the Avalonia views bind to, and every mutation is written back through the
/// backend so that persistence and runtime behaviour are the backend's.
/// </summary>
public sealed class RealAutomationService : IAutomationService
{
    private readonly IMainThreadDispatcher _dispatcher;

    private AutomationProcessor? _processor;
    private bool _initialised;

    /// <summary>Working copy, edited by the views and handed to Save.</summary>
    private readonly List<RealPipeline> _draft = [];

    public RealAutomationService(IMainThreadDispatcher dispatcher) => _dispatcher = dispatcher;

    public ObservableCollection<AutomationPipeline> AutomaticPipelines { get; } = new();

    public ObservableCollection<AutomationPipeline> ManualPipelines { get; } = new();

    public event Action? PipelinesChanged;

    public bool IsEnabled
    {
        get => _processor?.IsEnabled ?? _isEnabledFallback;
        set
        {
            _isEnabledFallback = value;

            if (_processor is null)
            {
                return;
            }

            // The backend persists this and re-evaluates its listeners; the local field
            // is only a fallback for the window before the processor is resolved.
            _ = SetEnabledAsync(value);
        }
    }

    private bool _isEnabledFallback = true;

    public async Task InitializeAsync()
    {
        if (_initialised)
        {
            return;
        }

        // Resolved after the readiness gate, off the UI thread: IoCContainer.Resolve holds
        // a global lock, and InitializeAsync wires up native listeners.
        _processor = await Task.Run(() => IoCContainer.Resolve<AutomationProcessor>())
            .ConfigureAwait(false);

        await _processor.InitializeAsync().ConfigureAwait(false);

        _processor.PipelinesChanged += OnPipelinesChanged;

        _isEnabledFallback = _processor.IsEnabled;
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

        // Re-read from the backend rather than trusting the requested value.
        var actual = _processor.IsEnabled;
        _isEnabledFallback = actual;

        await _dispatcher.InvokeAsync(() =>
        {
            PipelinesChanged?.Invoke();
        }).ConfigureAwait(false);
    }

    public Task AddPipelineAsync(AutomationPipeline pipeline, bool isManual)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        var model = new RealPipeline(pipeline.Name)
        {
            IconName = pipeline.Icon
        };

        // A pipeline with a trigger is automatic; one without is a manual quick action.
        if (!isManual)
        {
            model.Trigger = new LoqNova.Lib.Automation.Pipeline.Triggers.OnStartupAutomationPipelineTrigger();
        }

        _draft.Add(model);
        return PublishAsync();
    }

    public Task RemovePipelineAsync(Guid id)
    {
        _draft.RemoveAll(p => p.Id == id);
        return PublishAsync();
    }

    public Task UpdatePipelineAsync(AutomationPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        var index = _draft.FindIndex(p => p.Id == pipeline.Id);
        if (index < 0)
        {
            _draft.Add(new RealPipeline(pipeline.Name) { IconName = pipeline.Icon });
            return PublishAsync();
        }

        _draft[index].Name = pipeline.Name;
        _draft[index].IconName = pipeline.Icon;

        return PublishAsync();
    }

    public Task MovePipelineAsync(Guid id, int newIndex)
    {
        var index = _draft.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        // Clamped: the UI disables the command at the ends, but a stale index must not
        // reorder the list into an invalid state.
        newIndex = Math.Clamp(newIndex, 0, _draft.Count - 1);

        var model = _draft[index];
        _draft.RemoveAt(index);
        _draft.Insert(newIndex, model);

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

        var pipeline = _draft.FirstOrDefault(p => p.Id == pipelineId);
        pipeline?.Steps.Add(backendStep);

        return PublishAsync();
    }

    public Task RemoveStepAsync(Guid pipelineId, int stepIndex)
    {
        var pipeline = _draft.FirstOrDefault(p => p.Id == pipelineId);

        if (pipeline is not null && stepIndex >= 0 && stepIndex < pipeline.Steps.Count)
        {
            pipeline.Steps.RemoveAt(stepIndex);
        }

        return PublishAsync();
    }

    public Task MoveStepAsync(Guid pipelineId, int fromIndex, int toIndex)
    {
        var pipeline = _draft.FirstOrDefault(p => p.Id == pipelineId);

        if (pipeline is null || fromIndex < 0 || fromIndex >= pipeline.Steps.Count)
        {
            return Task.CompletedTask;
        }

        toIndex = Math.Clamp(toIndex, 0, pipeline.Steps.Count - 1);

        var step = pipeline.Steps[fromIndex];
        pipeline.Steps.RemoveAt(fromIndex);
        pipeline.Steps.Insert(toIndex, step);

        return PublishAsync();
    }

    /// <summary>Runs a manual pipeline through the real backend, as WPF does.</summary>
    public async Task RunNowAsync(Guid pipelineId)
    {
        if (_processor is null)
        {
            return;
        }

        await _processor.RunNowAsync(pipelineId).ConfigureAwait(false);
    }

    /// <summary>
    /// WPF's Revert: rebuilds from the backend's in-memory pipelines and discards the
    /// uncommitted draft. The backend exposes no discard API, so this reloads rather
    /// than writing anything.
    /// </summary>
    public async Task RevertAsync()
    {
        await ReloadAsync().ConfigureAwait(false);
    }

    public async Task SaveAsync()
    {
        if (_processor is null)
        {
            return;
        }

        // The single write path, exactly as WPF uses it: the backend deep-copies,
        // persists automation.json, notifies listeners and raises PipelinesChanged.
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
            var projected = Project(model);

            // A pipeline with a trigger is automatic; one without is a quick action.
            if (model.Trigger is not null)
            {
                AutomaticPipelines.Add(projected);
            }
            else
            {
                ManualPipelines.Add(projected);
            }
        }

        return _dispatcher.InvokeAsync(() => PipelinesChanged?.Invoke());
    }

    private static AutomationPipeline Project(RealPipeline model) => new()
    {
        Id = model.Id,
        Name = model.Name ?? "Unnamed",
        Icon = model.IconName ?? string.Empty,
        Steps = new ObservableCollection<AutomationStep>(
            model.Steps.Select(s => new AutomationStep
            {
                Name = s.GetType().Name,
                TypeName = s.GetType().Name,
                Config = s
            }))
    };

    private void OnPipelinesChanged(object? sender, List<RealPipeline> pipelines) => _ = ReloadAsync();

    public void Dispose()
    {
        if (_processor is not null)
        {
            _processor.PipelinesChanged -= OnPipelinesChanged;
        }
    }
}
