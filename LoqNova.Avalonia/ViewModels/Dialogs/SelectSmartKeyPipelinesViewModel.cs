using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Localization;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Automation;
using LoqNova.Lib.Automation.Pipeline;
using LoqNova.Lib.Settings;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// Smart Key Quick Action selector, reproducing WPF's
/// <c>SelectSmartKeyPipelinesWindow</c>. It lists the same trigger-less pipelines the
/// automation engine owns and writes the same single/double-press store fields.
/// <para>
/// WPF passes <c>isDoublePress</c> through the constructor. A dialog is constructed by
/// the container here, so the caller sets <see cref="IsDoublePress"/> before presenting
/// it; it selects which pair of settings the dialog reads and writes.
/// </para>
/// </summary>
public partial class SelectSmartKeyPipelinesViewModel : DialogViewModelBase
{
    private readonly IMainThreadDispatcher _dispatcher;

    private AutomationProcessor? _automationProcessor;
    private ApplicationSettings? _settings;

    /// <summary>Selects the double-press slot instead of the single-press slot.</summary>
    public bool IsDoublePress { get; set; }

    [ObservableProperty]
    private ObservableCollection<SmartKeyPipelineItem> _pipelines = new();

    [ObservableProperty]
    private bool _showThisApp;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasPipelines;

    public string EmptyMessage => Resource.SelectSmartKeyPipelinesWindow_List_Empty;

    public string ListDescription => Resource.SelectSmartKeyPipelinesWindow_List_Description;

    public string ShowThisAppTitle => Resource.SelectSmartKeyPipelinesWindow_ShowThisApp;

    public string Title => IsDoublePress
        ? Resource.SettingsPage_SmartKeyDoublePressAction_Title
        : Resource.SettingsPage_SmartKeySinglePressAction_Title;

    public SelectSmartKeyPipelinesViewModel(IMainThreadDispatcher dispatcher, IDialogService dialogs)
        : base(dialogs)
    {
        _dispatcher = dispatcher;
    }

    private Guid? SettingsStoreGuid
    {
        get => IsDoublePress ? _settings!.Store.SmartKeyDoublePressActionId : _settings!.Store.SmartKeySinglePressActionId;
        set
        {
            if (IsDoublePress)
                _settings!.Store.SmartKeyDoublePressActionId = value;
            else
                _settings!.Store.SmartKeySinglePressActionId = value;
        }
    }

    private System.Collections.Generic.List<Guid> SettingsStoreList =>
        IsDoublePress ? _settings!.Store.SmartKeyDoublePressActionList : _settings!.Store.SmartKeySinglePressActionList;

    /// <summary>The list is only selectable while "Show this app" is off, as in WPF.</summary>
    public bool IsListEnabled => !ShowThisApp;

    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        try
        {
            _automationProcessor ??= LoqNova.Lib.IoCContainer.Resolve<AutomationProcessor>();
            _settings ??= LoqNova.Lib.IoCContainer.Resolve<ApplicationSettings>();

            await _dispatcher.InvokeAsync(() => IsBusy = true).ConfigureAwait(false);

            var allPipelines = await _automationProcessor.GetPipelinesAsync().ConfigureAwait(false);
            var pipelines = allPipelines.Where(p => p.Trigger is null).OrderBy(p => p.Name).ToArray();

            var selectedGuid = SettingsStoreGuid;
            var selectedList = SettingsStoreList;

            await _dispatcher.InvokeAsync(() =>
            {
                ShowThisApp = selectedGuid is null;

                Pipelines.Clear();
                foreach (var pipeline in pipelines)
                {
                    Pipelines.Add(new SmartKeyPipelineItem(pipeline.Id, pipeline.Name ?? string.Empty)
                    {
                        IsChecked = selectedList.Exists(x => x == pipeline.Id) || pipeline.Id == selectedGuid
                    });
                }

                HasPipelines = Pipelines.Count > 0;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Smart Key pipelines could not be loaded: {ex}");
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_settings is null)
        {
            return;
        }

        var selectedPipelines = Pipelines.Where(item => item.IsChecked).Select(item => item.Id).ToArray();

        SettingsStoreList.Clear();

        if (ShowThisApp)
        {
            SettingsStoreGuid = null;
        }
        else
        {
            SettingsStoreList.AddRange(selectedPipelines);
            SettingsStoreGuid = SettingsStoreList.FirstOrDefault();
        }

        _settings.SynchronizeStore();

        // WPF closes the window once the store is written; the command runs on the UI
        // thread, so the host can be cleared inline.
        CloseCommand.Execute(null);
        await Task.CompletedTask;
    }

    partial void OnShowThisAppChanged(bool value) => OnPropertyChanged(nameof(IsListEnabled));
}

/// <summary>A pipeline row the user can check, mirroring WPF's inline <c>ListItem</c>.</summary>
public partial class SmartKeyPipelineItem(Guid id, string name) : ViewModelBase
{
    public Guid Id { get; } = id;

    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isChecked;
}
