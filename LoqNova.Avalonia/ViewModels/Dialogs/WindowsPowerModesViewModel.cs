using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LoqNova.Avalonia.Localization;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Extensions;
using LoqNova.Lib.Features;
using LoqNova.Lib.Settings;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// Windows Power Modes mapping, reproducing WPF's <c>WindowsPowerModesWindow</c>. Each
/// power mode maps to a Windows power mode; the choice is written to
/// <c>ApplicationSettings.Store.PowerModes</c> and applied immediately through
/// <see cref="PowerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync"/>, with no
/// Save button. The Custom Mode row is only shown when the firmware reports it.
/// </summary>
public partial class WindowsPowerModesViewModel : DialogViewModelBase
{
    private readonly IMainThreadDispatcher _dispatcher;

    private ApplicationSettings? _settings;
    private PowerModeFeature? _powerModeFeature;
    private bool _isRefreshing;

    public ObservableCollection<MappingRow> Rows { get; } = new();

    public string Title => Resource.WindowsPowerModesWindow_Title;

    public WindowsPowerModesViewModel(IMainThreadDispatcher dispatcher, IDialogService dialogs)
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

            var options = Enum.GetValues<WindowsPowerMode>();
            var states = await _powerModeFeature.GetAllStatesAsync().ConfigureAwait(false);
            var hasGodMode = states.Contains(PowerModeState.GodMode);

            await _dispatcher.InvokeAsync(() =>
            {
                _isRefreshing = true;
                try
                {
                    Rows.Clear();
                    Rows.Add(CreateRow(PowerModeState.Quiet, options, true));
                    Rows.Add(CreateRow(PowerModeState.Balance, options, true));
                    Rows.Add(CreateRow(PowerModeState.Performance, options, true));
                    Rows.Add(CreateRow(PowerModeState.GodMode, options, hasGodMode));
                }
                finally
                {
                    _isRefreshing = false;
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Windows Power Modes could not be loaded: {ex}");
        }
    }

    private MappingRow CreateRow(PowerModeState state, WindowsPowerMode[] options, bool isVisible)
    {
        var selection = _settings!.Store.PowerModes.GetValueOrDefault(state, WindowsPowerMode.Balanced);
        var list = new ObservableCollection<MappingOption>(
            options.Select(option => new MappingOption(option, option.GetDisplayName())));

        return new MappingRow(
            state.GetDisplayName(),
            list,
            selection,
            isVisible,
            value => _ = ApplyAsync(state, (WindowsPowerMode)value));
    }

    private async Task ApplyAsync(PowerModeState state, WindowsPowerMode powerMode)
    {
        if (_isRefreshing || _settings is null || _powerModeFeature is null)
        {
            return;
        }

        try
        {
            _settings.Store.PowerModes[state] = powerMode;
            _settings.SynchronizeStore();

            await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Windows Power Mode mapping could not be applied: {ex}");
        }
    }
}
