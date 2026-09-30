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

    [ObservableProperty]
    private bool _isAutomationEnabled;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True only while the draft differs from the persisted state.</summary>
    [ObservableProperty]
    private bool _isDirty;

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

        _automationService.DraftChanged += Rebuild;
        _automationService.EnabledChanged += OnEnabledChanged;

        // A valid trigger is preselected so Add Automatic can never produce a pipeline
        // whose trigger is null while still being listed as automatic.
        SelectedTriggerOption = AvailableTriggers.FirstOrDefault();

        Rebuild();
        IsAutomationEnabled = _automationService.IsEnabled;
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
        var trigger = SelectedTriggerOption?.Create() ?? AvailableTriggers[0].Create();

        await _automationService.AddPipelineAsync("New Pipeline", trigger).ConfigureAwait(true);

        IsDirty = true;
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
    private async Task RemovePipelineAsync(AutomationPipelineViewModel? pipeline)
    {
        if (pipeline is null)
        {
            return;
        }

        await _automationService.RemovePipelineAsync(pipeline.Model).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task MovePipelineUpAsync(AutomationPipelineViewModel? pipeline)
    {
        if (pipeline is null)
        {
            return;
        }

        await _automationService.MovePipelineAsync(pipeline.Model, -1).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task MovePipelineDownAsync(AutomationPipelineViewModel? pipeline)
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
    private async Task SetIconAsync(AutomationPipelineViewModel? pipeline, string? iconName)
    {
        if (pipeline is null)
        {
            return;
        }

        await _automationService.SetIconAsync(pipeline.Model, iconName).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task SetTriggerAsync(AutomationPipelineViewModel? pipeline, TriggerOption? option)
    {
        if (pipeline is null || option is null)
        {
            return;
        }

        await _automationService.SetTriggerAsync(pipeline.Model, option.Create()).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task AddStepAsync(AutomationPipelineViewModel? pipeline, AutomationStepOption? option)
    {
        if (pipeline is null || option is null)
        {
            return;
        }

        await _automationService.AddStepAsync(pipeline.Model, option.Create()).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task RemoveStepAsync(AutomationStepViewModel? step)
    {
        if (step?.Owner is not { } pipeline)
        {
            return;
        }

        await _automationService.RemoveStepAsync(pipeline.Model, step.Model).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task MoveStepUpAsync(AutomationStepViewModel? step)
    {
        if (step?.Owner is not { } pipeline)
        {
            return;
        }

        await _automationService.MoveStepAsync(pipeline.Model, step.Model, -1).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task MoveStepDownAsync(AutomationStepViewModel? step)
    {
        if (step?.Owner is not { } pipeline)
        {
            return;
        }

        await _automationService.MoveStepAsync(pipeline.Model, step.Model, 1).ConfigureAwait(true);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task RunNowAsync(AutomationPipelineViewModel? pipeline)
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

    /// <summary>The real backend object. Never a copy; edits mutate the draft.</summary>
    public AutomationPipeline Model { get; }

    public Guid Id => Model.Id;

    public bool IsManual { get; }

    public ObservableCollection<AutomationStepViewModel> Steps { get; } = [];

    public AutomationPipelineViewModel(
        AutomationViewModel owner, AutomationPipeline model, bool isManual)
    {
        _owner = owner;
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

    /// <summary>WPF's header falls back to the trigger's display name when unnamed.</summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Name) ? Name : SelectedTrigger?.DisplayName ?? "Unnamed";

    /// <summary>The real trigger instance, or null for a quick action.</summary>
    public IAutomationPipelineTrigger? Trigger => Model.Trigger;

    public string TriggerDisplayName => Model.Trigger?.DisplayName ?? string.Empty;

    /// <summary>WPF's step-count subtitle.</summary>
    public string StepsSubtitle => Steps.Count == 1 ? "1 step" : $"{Steps.Count} steps";

    /// <summary>Re-wraps the step rows around the live steps on the backend object.</summary>
    public void RefreshSteps()
    {
        Steps.Clear();

        for (var i = 0; i < Model.Steps.Count; i++)
        {
            Steps.Add(new AutomationStepViewModel(this, Model.Steps[i], i, Model.Steps.Count - 1));
        }

        OnPropertyChanged(nameof(StepsSubtitle));
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));

    /// <summary>Keeps the backend object's name in step with the edited field.</summary>
    public async Task CommitNameAsync()
    {
        Model.Name = Name;
        await _owner.RenamePipelineCommand.ExecuteAsync(this);
    }

    /// <summary>Applies a newly chosen trigger to the real backend object.</summary>
    public async Task CommitTriggerAsync()
    {
        if (SelectedTrigger is null)
        {
            return;
        }

        await _owner.SetTriggerCommand.ExecuteAsync(this);
    }

    /// <summary>Adds a real backend step of the chosen type.</summary>
    public Task AddStepAsync(AutomationStepOption? option) =>
        _owner.AddStepCommand.ExecuteAsync((this, option));

    public Task AddManualStepAsync(AutomationStepOption? option) =>
        _owner.AddStepCommand.ExecuteAsync((this, option));
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
    }

    [ObservableProperty]
    private string _typeName = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// The step's current configuration, as text, taken from the real state where the
    /// step exposes one via <c>IAutomationStep&lt;T&gt;</c>.
    /// </summary>
    public string ConfigurationSummary => StepConfiguration.Describe(Model);

    /// <summary>False when the backend reports the hardware for this step is absent.</summary>
    public bool IsSupported { get; private set; } = true;

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

/// <summary>Exposes the factory's humaniser to the view models.</summary>
internal static class StepFactoryAccess
{
    public static string Humanize(string typeName) => StepFactory.Humanize(typeName);
}
