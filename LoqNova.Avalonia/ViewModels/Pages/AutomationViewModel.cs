using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Automation.Pipeline;
using LoqNova.Lib.Automation.Pipeline.Triggers;
using LoqNova.Lib.Automation.Steps;

namespace LoqNova.Avalonia.ViewModels.Pages;

/// <summary>
/// Drives the Automation page over the real backend. Every view model below wraps a
/// live backend object rather than copying it, so edits mutate the draft directly.
///
/// There is no per-pipeline or per-step enable flag, because the backend has no such
/// concept: WPF exposes one global toggle and distinguishes automatic pipelines (they
/// have a trigger) from quick actions (they do not).
/// </summary>
public partial class AutomationViewModel : ViewModelBase
{
    private readonly IAutomationService _automationService;

    /// <summary>Exposed so the per-pipeline wrapper can mutate the draft directly.</summary>
    internal IAutomationService Service => _automationService;

    internal void SetDirty() => IsDirty = true;

    [ObservableProperty]
    private bool _isAutomationEnabled;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True only while the draft differs from the persisted state.</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>Last failure from an editor operation, shown to the user.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<AutomationPipelineViewModel> AutomaticPipelines { get; } = [];

    public ObservableCollection<AutomationPipelineViewModel> ManualPipelines { get; } = [];

    /// <summary>Real backend step types, labelled with their display names.</summary>
    public IReadOnlyList<AutomationStepOption> AvailableSteps => _automationService.AvailableSteps;

    /// <summary>
    /// WPF excludes QuickActionAutomationStep from a quick action's step catalogue, so
    /// the picker for manual pipelines omits it too.
    /// </summary>
    public IReadOnlyList<AutomationStepOption> AvailableManualSteps =>
        [.. AvailableSteps.Where(s => s.TypeName != nameof(QuickActionAutomationStep))];

    /// <summary>Real backend trigger types, labelled by their own DisplayName.</summary>
    public IReadOnlyList<TriggerOption> AvailableTriggers => _automationService.AvailableTriggers;

    /// <summary>The trigger chosen for the next Add Automatic.</summary>
    [ObservableProperty]
    private TriggerOption? _selectedTriggerOption;

    /// <summary>WPF asks for a quick action's name inline, capped at 50 characters.</summary>
    [ObservableProperty]
    private string? _pendingManualName;

    [ObservableProperty]
    private bool _isManualNamePromptOpen;

    public AutomationViewModel(IAutomationService automationService)
    {
        _automationService = automationService;

        _automationService.PipelinesReloaded += Rebuild;
        _automationService.PipelineEdited += OnPipelineEdited;
        _automationService.EnabledChanged += OnEnabledChanged;

        // A valid trigger is preselected so Add Automatic can never produce a pipeline
        // whose trigger is null while still being listed as automatic.
        SelectedTriggerOption = AvailableTriggers.FirstOrDefault();

        Rebuild();
        IsAutomationEnabled = _automationService.IsEnabled;
    }

    /// <summary>
    /// Loads every step's selectable configuration values. Called once the page is
    /// attached, and off the UI thread's critical path, because the backend's feature
    /// calls can block.
    /// </summary>
    public async Task RefreshStepConfigurationsAsync()
    {
        var steps = AutomaticPipelines.SelectMany(p => p.Steps)
            .Concat(ManualPipelines.SelectMany(p => p.Steps))
            .ToList();

        foreach (var step in steps)
        {
            await step.RefreshAsync().ConfigureAwait(true);
        }
    }

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

    private void OnEnabledChanged() => IsAutomationEnabled = _automationService.IsEnabled;

    /// <summary>
    /// One pipeline changed in place. Its existing wrapper is refreshed rather than
    /// rebuilt, so the rest of the editor keeps its state. This is the difference
    /// between an edit and a reload: rebuilding everything on every change is what made
    /// the page appear to revert while the user was typing.
    /// </summary>
    private void OnPipelineEdited(AutomationPipeline model)
    {
        var existing = AutomaticPipelines.FirstOrDefault(p => p.Model == model)
                        ?? ManualPipelines.FirstOrDefault(p => p.Model == model);

        existing?.RefreshSteps();

        if (existing is not null)
        {
            existing.RefreshTrigger();
        }
    }

    /// <summary>
    /// Rebuilds the wrappers around the current draft. The draft itself is never
    /// replaced, so any in-place edit already applied to a step or trigger survives.
    /// </summary>
    private void Rebuild()
    {
        var selectedAutomatic = SelectedPipeline?.Model;
        var selectedManual = SelectedManual?.Model;

        AutomaticPipelines.Clear();
        ManualPipelines.Clear();

        foreach (var pipeline in _automationService.AutomaticPipelines)
        {
            AutomaticPipelines.Add(new AutomationPipelineViewModel(this, pipeline, isManual: false));
        }

        foreach (var pipeline in _automationService.ManualPipelines)
        {
            ManualPipelines.Add(new AutomationPipelineViewModel(this, pipeline, isManual: true));
        }

        SelectedPipeline = AutomaticPipelines.FirstOrDefault(p => p.Model == selectedAutomatic);
        SelectedManual = ManualPipelines.FirstOrDefault(p => p.Model == selectedManual);
    }

    [ObservableProperty]
    private AutomationPipelineViewModel? _selectedPipeline;

    [ObservableProperty]
    private AutomationPipelineViewModel? _selectedManual;

    partial void OnIsAutomationEnabledChanged(bool value) => _ = SetEnabledAsync(value);

    [RelayCommand]
    private async Task SetEnabledAsync(bool enabled)
    {
        await _automationService.SetEnabledAsync(enabled).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task AddAutomaticPipelineAsync()
    {
        // A real trigger instance, never null, so the pipeline is immediately valid.
        // Constructing one resolves its listener from the global IoC container, so it is
        // built off the UI thread, and a failure is reported rather than thrown: this is
        // an async void command, where an escaping exception ends the process.
        var factory = SelectedTriggerOption ?? AvailableTriggers[0];

        IAutomationPipelineTrigger trigger;

        try
        {
            trigger = await Task.Run(factory.Create).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ReportError($"Could not create a {factory.DisplayName} trigger: {ex.Message}");
            return;
        }

        // WPF creates an automatic pipeline with no name at all - `new AutomationPipeline(trigger)` -
        // and its header falls back to the trigger's display name. Inventing a "New Pipeline"
        // string here is what made a freshly added card look like fabricated data.
        await _automationService.AddPipelineAsync(name: null, trigger).ConfigureAwait(true);

        IsDirty = true;
    }

    /// <summary>Surfaces a failure to the user instead of letting it reach the dispatcher.</summary>
    internal void ReportError(string message)
    {
        System.Diagnostics.Debug.WriteLine($"Automation: {message}");

        // Also written to disk: the on-page banner truncates a long stack trace, and a
        // swallowed exception here is otherwise impossible to diagnose after the fact.
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LOQNova",
                "automation-errors.log");

            System.IO.Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(path)!);

            System.IO.File.AppendAllText(
                path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never be the thing that fails.
        }

        ErrorMessage = message;
    }

    [RelayCommand]
    private void OpenManualNamePrompt() => IsManualNamePromptOpen = true;

    [RelayCommand]
    private void CancelManualNamePrompt()
    {
        PendingManualName = null;
        IsManualNamePromptOpen = false;
    }

    [RelayCommand]
    private async Task AddManualPipelineAsync()
    {
        // WPF: blank input aborts silently, no duplicate-name validation.
        var name = PendingManualName?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // A null trigger is what makes this a quick action.
        await _automationService.AddPipelineAsync(name[..Math.Min(name.Length, 50)], trigger: null)
            .ConfigureAwait(true);

        PendingManualName = null;
        IsManualNamePromptOpen = false;

        IsDirty = true;
    }

    [RelayCommand]
    internal async Task RemovePipelineAsync(AutomationPipelineViewModel? pipeline)
    {
        if (pipeline is null)
        {
            return;
        }

        await _automationService.RemovePipelineAsync(pipeline.Model).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    internal async Task MovePipelineUpAsync(AutomationPipelineViewModel? pipeline)
    {
        if (pipeline is null)
        {
            return;
        }

        await _automationService.MovePipelineAsync(pipeline.Model, -1).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    internal async Task MovePipelineDownAsync(AutomationPipelineViewModel? pipeline)
    {
        if (pipeline is null)
        {
            return;
        }

        await _automationService.MovePipelineAsync(pipeline.Model, 1).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task RenamePipelineAsync(AutomationPipelineViewModel? pipeline)
    {
        if (pipeline is null || string.IsNullOrWhiteSpace(pipeline.Name))
        {
            return;
        }

        await _automationService.RenamePipelineAsync(pipeline.Model, pipeline.Name).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    internal async Task RemoveStepAsync(AutomationStepViewModel? step)
    {
        if (step?.Owner is not { } pipeline)
        {
            return;
        }

        await _automationService.RemoveStepAsync(pipeline.Model, step.Model).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    internal async Task MoveStepUpAsync(AutomationStepViewModel? step)
    {
        if (step?.Owner is not { } pipeline)
        {
            return;
        }

        await _automationService.MoveStepAsync(pipeline.Model, step.Model, -1).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    internal async Task MoveStepDownAsync(AutomationStepViewModel? step)
    {
        if (step?.Owner is not { } pipeline)
        {
            return;
        }

        await _automationService.MoveStepAsync(pipeline.Model, step.Model, 1).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    internal async Task RunNowAsync(AutomationPipelineViewModel? pipeline)
    {
        if (pipeline is null)
        {
            return;
        }

        await _automationService.RunNowAsync(pipeline.Model).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _automationService.SaveAsync().ConfigureAwait(true);
        IsDirty = false;
    }

    /// <summary>WPF's Revert: reload from the backend and discard every uncommitted edit.</summary>
    [RelayCommand]
    private async Task RevertAsync()
    {
        await _automationService.RevertAsync().ConfigureAwait(true);

        IsDirty = false;
        IsAutomationEnabled = _automationService.IsEnabled;
    }
}

/// <summary>A thin binding wrapper around a live backend <see cref="AutomationPipeline"/>.</summary>
public partial class AutomationPipelineViewModel : ViewModelBase
{
    private readonly AutomationViewModel _owner;
    private readonly IAutomationService _service;

    /// <summary>The real backend object. Never a copy; edits mutate the draft.</summary>
    public AutomationPipeline Model { get; }

    public Guid Id => Model.Id;

    public bool IsManual { get; }

    public ObservableCollection<AutomationStepViewModel> Steps { get; } = [];

    public AutomationPipelineViewModel(
AutomationViewModel owner, AutomationPipeline model, bool isManual)
      {
          _owner = owner;

          // Was never assigned, which is why every step add failed with a
          // NullReferenceException on the line that calls _service.AddStepAsync.
          _service = owner.Service;

          Model = model;
          IsManual = isManual;

        Name = model.Name ?? string.Empty;
        IconName = model.IconName ?? string.Empty;
        SelectedTrigger = owner.AvailableTriggers
            .FirstOrDefault(t => t.TypeName == model.Trigger?.GetType().Name);

        RefreshSteps();
    }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _iconName = string.Empty;

    /// <summary>The pipeline's real trigger, expressed as the matching picker option.</summary>
    [ObservableProperty]
    private TriggerOption? _selectedTrigger;

    /// <summary>Real trigger types, for this pipeline's own picker.</summary>
    public IReadOnlyList<TriggerOption> TriggerOptions => _owner.AvailableTriggers;

    /// <summary>
    /// Real step types for this pipeline. WPF filters QuickActionAutomationStep out of a
    /// quick action's catalogue, so a manual pipeline offers one list fewer.
    /// </summary>
    public IReadOnlyList<AutomationStepOption> StepOptions =>
        IsManual ? _owner.AvailableManualSteps : _owner.AvailableSteps;

    /// <summary>This pipeline's own step selection, never shared globally.</summary>
    [ObservableProperty]
    private AutomationStepOption? _selectedStepOption;

    /// <summary>WPF's header falls back to the trigger's display name when unnamed.</summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Name) ? Name : SelectedTrigger?.DisplayName ?? "Unnamed";

    /// <summary>The real trigger instance, or null for a quick action.</summary>
    public IAutomationPipelineTrigger? Trigger => Model.Trigger;

    public string TriggerDisplayName => Model.Trigger?.DisplayName ?? string.Empty;

    /// <summary>WPF's step-count subtitle.</summary>
    public string StepsSubtitle => Steps.Count == 1 ? "1 step" : $"{Steps.Count} steps";

    /// <summary>Re-reads the real trigger after an in-place change.</summary>
    public void RefreshTrigger()
    {
        SelectedTrigger = _owner.AvailableTriggers
            .FirstOrDefault(t => t.TypeName == Model.Trigger?.GetType().Name);

        OnPropertyChanged(nameof(TriggerDisplayName));
        OnPropertyChanged(nameof(DisplayTitle));
    }

    /// <summary>Re-wraps the step rows around the live steps on the backend object.</summary>
    public void RefreshSteps()
    {
        Steps.Clear();

        for (var i = 0; i < Model.Steps.Count; i++)
        {
            var step = new AutomationStepViewModel(this, Model.Steps[i], i, Model.Steps.Count - 1);

            Steps.Add(step);
        }

        OnPropertyChanged(nameof(StepsSubtitle));
    }

    /// <summary>Applies a newly chosen trigger to the real backend object.</summary>
    public async Task CommitTriggerAsync()
    {
        if (SelectedTrigger is null)
        {
            return;
        }

        IAutomationPipelineTrigger trigger;

        try
        {
            trigger = await Task.Run(SelectedTrigger.Create).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _owner.ReportError($"Could not change trigger: {ex.Message}");
            return;
        }

        await _service.SetTriggerAsync(Model, trigger);

        MarkDirty();
    }

    /// <summary>Adds a real backend step of the chosen type.</summary>
    public async Task AddStepAsync(AutomationStepOption? option)
    {
        if (option is null)
        {
            return;
        }

        // Building a step resolves its feature from the global IoC container, which
        // blocks, so it happens off the UI thread. It can also throw - an unregistered
        // feature or a missing backend type - and this runs from an async void command,
        // where an escaping exception would terminate the process rather than show up
        // as a failed command. The failure is reported instead.
        IAutomationStep? step;

        try
        {
            step = await Task.Run(option.Create).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _owner.ReportError($"Could not add {option.DisplayName}: {ex}");
            return;
        }

        try
        {
            await _service.AddStepAsync(Model, step);
        }
        catch (Exception ex)
        {
            _owner.ReportError($"Could not add {option.DisplayName}: {ex}");
            return;
        }

        MarkDirty();
    }

    /// <summary>Applies a new configuration to one of this pipeline's real steps.</summary>
    public async Task ReconfigureStepAsync(AutomationStepViewModel step, object? state)
    {
        IAutomationStep? replacement;

        try
        {
            replacement = await Task.Run(() => StepConfiguration.WithState(step.Model, state))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _owner.ReportError($"Could not reconfigure {step.DisplayName}: {ex.Message}");
            return;
        }

        if (replacement is null)
        {
            return;
        }

        await _service.ReplaceStepAsync(Model, step.Model, replacement);

        MarkDirty();
    }

    private void MarkDirty() => _owner.SetDirty();

    // Parameterless async Task methods cannot become RelayCommands, so the per-item
    // buttons below call these directly rather than through a generated command. Every
    // one of those command bindings silently did nothing.
    public Task MoveUpAsync() => _owner.MovePipelineUpAsync(this);

    public Task MoveDownAsync() => _owner.MovePipelineDownAsync(this);

    public Task RemoveAsync() => _owner.RemovePipelineAsync(this);

    public Task RunNowAsync() => _owner.RunNowAsync(this);

    /// <summary>Adds the step chosen in this pipeline's own picker.</summary>
    [RelayCommand]
    /// <summary>
    /// Adds the step chosen in this pipeline's own picker.
    ///
    /// A parameterless async Task cannot become a RelayCommand, so this is invoked
    /// directly by the Add step button's click behaviour instead of a generated command -
    /// which is why the button previously did nothing at all.
    /// </summary>
    public Task AddSelectedStepAsync() => AddStepAsync(SelectedStepOption);

    internal async Task MoveStepUpAsync(AutomationStepViewModel step)
    {
        await _service.MoveStepAsync(Model, step.Model, -1);
        MarkDirty();
    }

    internal async Task MoveStepDownAsync(AutomationStepViewModel step)
    {
        await _service.MoveStepAsync(Model, step.Model, 1);
        MarkDirty();
    }

    internal async Task RemoveStepAsync(AutomationStepViewModel step)
    {
        await _service.RemoveStepAsync(Model, step.Model);
        MarkDirty();
    }

    /// <summary>Name is two-way bound, so the draft is kept in step with the field.</summary>
    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTitle));

        // Editing the field alone does not commit; the real object is updated in place
        // so Save persists exactly what is on screen.
        Model.Name = value;
        MarkDirty();
    }

    /// <summary>Applying a new trigger builds a real trigger and attaches it.</summary>
    partial void OnSelectedTriggerChanged(TriggerOption? value) => _ = CommitTriggerAsync();
}

/// <summary>A thin binding wrapper around a live backend <see cref="IAutomationStep"/>.</summary>
public partial class AutomationStepViewModel : ViewModelBase
{
    /// <summary>The real backend step. Configuration edits act on this object.</summary>
    public IAutomationStep Model { get; }

    public AutomationPipelineViewModel Owner { get; }

    public int Index { get; }

    public int LastIndex { get; }

    public AutomationStepViewModel(
        AutomationPipelineViewModel owner, IAutomationStep model, int index, int lastIndex)
    {
        Owner = owner;
        Model = model;
        Index = index;
        LastIndex = lastIndex;

        TypeName = model.GetType().Name;
        DisplayName = StepFactoryAccess.Humanize(TypeName);
        _configurationSummary = StepConfiguration.Describe(model);

        // Deliberately not here: loading the selectable values calls the step's feature,
        // which can block on WMI, and step construction itself resolves from the global
        // IoC container. Doing that on the UI thread during rendering hangs the window.
        // RefreshAsync is called by the page once it is attached.
    }

    /// <summary>
    /// Loads the legal configuration values for this step. Must be called off the UI
    /// thread's critical path, because the backend's feature calls can block.
    /// </summary>
    public async Task RefreshAsync()
    {
        try
        {
            var values = await StepConfiguration
                .GetStatesAsync(Model)
                .ConfigureAwait(false);

            States.Clear();

            var current = StepConfiguration.GetState(Model);

            foreach (var value in values)
            {
                if (value is null)
                {
                    continue;
                }

                var option = new StepStateOption(value, value.ToString() ?? string.Empty);

                States.Add(option);

                if (Equals(value, current))
                {
                    SelectedState = option;
                }
            }
        }
        catch (Exception)
        {
            States.Clear();
        }

        OnPropertyChanged(nameof(HasConfiguration));
    }

    [ObservableProperty]
    private string _typeName = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// The step's current configuration, as text, taken from the real state where the
    /// step exposes one via <c>IAutomationStep&lt;T&gt;</c>.
    /// </summary>
    [ObservableProperty]
    private string _configurationSummary = string.Empty;

    /// <summary>False when the backend reports the hardware for this step is absent.</summary>
    public bool IsSupported { get; private set; } = true;

    public Task MoveUpAsync() => Owner.MoveStepUpAsync(this);

    public Task MoveDownAsync() => Owner.MoveStepDownAsync(this);

    public Task RemoveAsync() => Owner.RemoveStepAsync(this);

    /// <summary>
    /// The values this step accepts, straight from the backend's
    /// <c>GetAllStatesAsync()</c>. Empty for steps that take no state, which is the
    /// backend's own signal rather than a guess.
    /// </summary>
    public ObservableCollection<StepStateOption> States { get; } = [];

    /// <summary>True when the backend offers a configuration to edit.</summary>
    public bool HasConfiguration => States.Count > 0;

    /// <summary>The currently applied value, as a picker option.</summary>
    [ObservableProperty]
    private StepStateOption? _selectedState;

    /// <summary>Rebuilds the step with the newly chosen state.</summary>
    public async Task CommitStateAsync()
    {
        if (SelectedState is null)
        {
            return;
        }

        await Owner.ReconfigureStepAsync(this, SelectedState.Value);
    }

    public async Task RefreshSupportAsync()
    {
        try
        {
            IsSupported = await Model.IsSupportedAsync().ConfigureAwait(false);
            OnPropertyChanged(nameof(IsSupported));
        }
        catch (Exception)
        {
            // A step that cannot even report support is not offered as usable.
            IsSupported = false;
            OnPropertyChanged(nameof(IsSupported));
        }
    }
}

/// <summary>One selectable value of a real step's configuration.</summary>
public sealed record StepStateOption(object Value, string DisplayName);

/// <summary>Exposes the factory's humaniser to the view models.</summary>
internal static class StepFactoryAccess
{
    public static string Humanize(string typeName) => StepFactory.Humanize(typeName);
}
