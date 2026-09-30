using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Controllers;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// Discrete GPU overclock (WPF OverclockDiscreteGPUSettingsWindow equivalent).
/// Backed by <see cref="GPUOverclockController"/>, whose model is
/// <c>GPUOverclockInfo(CoreDeltaMhz, MemoryDeltaMhz)</c> plus an enabled flag.
/// Power limit, temperature limit and core voltage offset are not part of the
/// backend model, so they are not offered here.
/// </summary>
public partial class OverclockGpuSettingsViewModel : DialogViewModelBase
{
    private readonly IMainThreadDispatcher _dispatcher;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _coreClockOffset;

    [ObservableProperty]
    private int _memoryClockOffset;

    /// <summary>True when the machine reports GPU overclock support.</summary>
    public bool IsSupported { get; private set; }

    public int MaxCoreDelta => GPUOverclockController.GetMaxCoreDeltaMhz();

    public int MaxMemoryDelta => GPUOverclockController.GetMaxMemoryDeltaMhz();

    public OverclockGpuSettingsViewModel(IMainThreadDispatcher dispatcher, IDialogService dialogs)
        : base(dialogs)
    {
        _dispatcher = dispatcher;
    }

    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        var controller = await Task.Run(() => LoqNova.Lib.IoCContainer.Resolve<GPUOverclockController>()).ConfigureAwait(false);
        IsSupported = await controller.IsSupportedAsync().ConfigureAwait(false);

        if (!IsSupported)
            return;

        LoadState(controller);

        controller.Changed += (_, _) => _dispatcher.Post(() => LoadState(controller));
    }

    private void LoadState(GPUOverclockController controller)
    {
        var (enabled, info) = controller.GetState();

        IsEnabled = enabled;
        CoreClockOffset = info.CoreDeltaMhz;
        MemoryClockOffset = info.MemoryDeltaMhz;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!IsSupported)
            return;

        await LibContainer.Initialization.ConfigureAwait(false);

        var controller = await Task.Run(() => LoqNova.Lib.IoCContainer.Resolve<GPUOverclockController>()).ConfigureAwait(false);

        // WPF's sliders are ranged from zero to the reported maximum, so negative
        // offsets are not selectable there and are rejected here too.
        var core = Math.Clamp(CoreClockOffset, 0, MaxCoreDelta);
        var memory = Math.Clamp(MemoryClockOffset, 0, MaxMemoryDelta);

        controller.SaveState(IsEnabled, new GPUOverclockInfo(core, memory));
        await controller.ApplyStateAsync().ConfigureAwait(false);

        LoadState(controller);
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        CoreClockOffset = 0;
        MemoryClockOffset = 0;
    }

}
