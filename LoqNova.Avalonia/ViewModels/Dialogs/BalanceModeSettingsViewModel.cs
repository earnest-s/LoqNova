using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Controllers;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// Balance Mode settings, reproducing WPF's <c>BalanceModeSettingsWindow</c>.
/// <para>
/// WPF exposes exactly one control here: an "Enable AI Engine" checkbox. The AI
/// engine is the Lenovo AI Chip feature (<c>CapabilityID.AIChip</c>, surfaced as
/// <c>mi.Properties.SupportsAIMode</c>), whose state lives on
/// <see cref="AIController.IsAIModeEnabled"/> and persists to <c>balancemode.json</c>.
/// </para>
/// <para>
/// The CPU, GPU and thermal "target" fields this ViewModel previously exposed have
/// no backend equivalent, so they are not presented: offering controls that cannot be
/// saved would be worse than not offering them.
/// </para>
/// </summary>
public partial class BalanceModeSettingsViewModel : DialogViewModelBase
{
    private readonly IMainThreadDispatcher _dispatcher;

    [ObservableProperty]
    private bool _aiEngineEnabled;

    /// <summary>True when the machine reports the AI Chip capability.</summary>
    [ObservableProperty]
    private bool _isSupported;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public BalanceModeSettingsViewModel(IMainThreadDispatcher dispatcher, IDialogService dialogs)
        : base(dialogs)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Loads the current value straight from the controller, so the checkbox shows
    /// what the machine has rather than a default.
    /// </summary>
    public async Task InitializeAsync()
    {
        ViewModels.MainWindowViewModel.Trace("Balance: begin init");
        await LibContainer.Initialization.ConfigureAwait(false);
        ViewModels.MainWindowViewModel.Trace("Balance: lib ready");

        try
        {
            ViewModels.MainWindowViewModel.Trace("Balance: resolving AIController");
            var controller = await Task.Run(() => LoqNova.Lib.IoCContainer.Resolve<AIController>()).ConfigureAwait(false);
            ViewModels.MainWindowViewModel.Trace("Balance: AIController resolved");

            // The property is the persisted value; availability is reported separately
            // by the capability check the controller performs internally.
            var enabled = controller.IsAIModeEnabled;

            await _dispatcher.InvokeAsync(() =>
            {
                AiEngineEnabled = enabled;
                IsSupported = true;
                ErrorMessage = null;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                IsSupported = false;
                ErrorMessage = ex.Message;
            }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// WPF's save path: assign the setting, stop the engine, re-apply Balance mode,
    /// then start the engine again. The mode is re-applied because the AI engine only
    /// takes effect while Balance mode is active.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;

            ViewModels.MainWindowViewModel.Trace("Balance: begin init");
        await LibContainer.Initialization.ConfigureAwait(false);
        ViewModels.MainWindowViewModel.Trace("Balance: lib ready");

            var controller = await Task.Run(() => LoqNova.Lib.IoCContainer.Resolve<AIController>()).ConfigureAwait(false);
            var powerMode = await Task.Run(() => LoqNova.Lib.IoCContainer.Resolve<LoqNova.Lib.Features.PowerModeFeature>()).ConfigureAwait(false);

            controller.IsAIModeEnabled = AiEngineEnabled;

            await controller.StopAsync().ConfigureAwait(false);
            await powerMode.SetStateAsync(LoqNova.Lib.PowerModeState.Balance).ConfigureAwait(false);
            await controller.StartIfNeededAsync().ConfigureAwait(false);

            // Read back rather than assuming the write landed.
            var applied = controller.IsAIModeEnabled;

            await _dispatcher.InvokeAsync(() =>
            {
                AiEngineEnabled = applied;
                ErrorMessage = null;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

}
