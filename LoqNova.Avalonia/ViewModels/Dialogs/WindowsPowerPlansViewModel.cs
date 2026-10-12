using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LoqNova.Avalonia.Localization;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Controllers;
using LoqNova.Lib.Extensions;
using LoqNova.Lib.Features;
using LoqNova.Lib.Settings;
using LoqNova.Lib.Utils;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// Windows Power Plans mapping, reproducing WPF's <c>WindowsPowerPlansWindow</c>. Each
/// power mode maps to a Windows power plan (or "Default"); the choice is written to
/// <c>ApplicationSettings.Store.PowerPlans</c> and applied immediately, with no Save
/// button. The Modern Standby warning is shown when the machine reports it.
/// </summary>
public partial class WindowsPowerPlansViewModel : DialogViewModelBase
{
    private static readonly WindowsPowerPlan DefaultValue =
        new(Guid.Empty, Resource.WindowsPowerPlansWindow_DefaultPowerPlan, false);

    private readonly IMainThreadDispatcher _dispatcher;

    private ApplicationSettings? _settings;
    private PowerModeFeature? _powerModeFeature;
    private WindowsPowerPlanController? _windowsPowerPlanController;
    private bool _isRefreshing;

    public ObservableCollection<MappingRow> Rows { get; } = new();

    [ObservableProperty]
    private bool _isAoAcWarningVisible;

    public string Title => Resource.WindowsPowerPlansWindow_Title;

    public string AoAcWarningTitle => Resource.PowerPlansWindow_AoAcWarning_Title;

    public string AoAcWarningMessage => Resource.PowerPlansWindow_AoAcWarning_Message;

    public WindowsPowerPlansViewModel(IMainThreadDispatcher dispatcher, IDialogService dialogs)
        : base(dialogs)
    {
        _dispatcher = dispatcher;
    }

    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        try
        {
            _settings ??= IoCContainer.Resolve<ApplicationSettings>();
            _powerModeFeature ??= IoCContainer.Resolve<PowerModeFeature>();
            _windowsPowerPlanController ??= IoCContainer.Resolve<WindowsPowerPlanController>();

            var compatibility = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            var supportsAoAc = compatibility.Properties.SupportsAlwaysOnAc.status;

            var powerPlans = _windowsPowerPlanController.GetPowerPlans()
                .OrderBy(plan => plan.Name)
                .Prepend(DefaultValue)
                .ToArray();

            var states = await _powerModeFeature.GetAllStatesAsync().ConfigureAwait(false);
            var hasGodMode = states.Contains(PowerModeState.GodMode);

            await _dispatcher.InvokeAsync(() =>
            {
                _isRefreshing = true;
                try
                {
                    IsAoAcWarningVisible = supportsAoAc;

                    Rows.Clear();
                    Rows.Add(CreateRow(PowerModeState.Quiet, powerPlans, true));
                    Rows.Add(CreateRow(PowerModeState.Balance, powerPlans, true));
                    Rows.Add(CreateRow(PowerModeState.Performance, powerPlans, true));
                    Rows.Add(CreateRow(PowerModeState.GodMode, powerPlans, hasGodMode));
                }
                finally
                {
                    _isRefreshing = false;
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Windows Power Plans could not be loaded: {ex}");
        }
    }

    private MappingRow CreateRow(PowerModeState state, WindowsPowerPlan[] powerPlans, bool isVisible)
    {
        var selection = _settings!.Store.PowerPlans.GetValueOrDefault(state);
        var list = new ObservableCollection<MappingOption>(
            powerPlans.Select(plan => new MappingOption(plan.Guid, plan.Name)));

        return new MappingRow(
            state.GetDisplayName(),
            list,
            selection,
            isVisible,
            value => _ = ApplyAsync(state, (Guid)value));
    }

    private async Task ApplyAsync(PowerModeState state, Guid powerPlanGuid)
    {
        if (_isRefreshing || _settings is null || _powerModeFeature is null)
        {
            return;
        }

        try
        {
            _settings.Store.PowerPlans[state] = powerPlanGuid;
            _settings.SynchronizeStore();

            await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Windows Power Plan mapping could not be applied: {ex}");
        }
    }
}
