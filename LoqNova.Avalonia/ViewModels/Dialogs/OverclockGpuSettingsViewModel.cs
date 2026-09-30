using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<OverclockGpuSettingsViewModel> _logger;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _coreClockOffset;

    [ObservableProperty]
    private int _memoryClockOffset;

    /// <summary>True when the machine reports GPU overclock support.</summary>
    public bool IsSupported { get; private set; }

    /// <summary>Why support could not be determined, shown instead of a silent failure.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// WPF's hard-coded core ceiling. Read directly rather than through the view model,
    /// because the getter is a plain constant and safe to bind.
    /// </summary>
    public int MaxCoreDelta => GPUOverclockController.GetMaxCoreDeltaMhz();

    /// <summary>
    /// The memory ceiling depends on the installed memory vendor, so it comes from the
    /// backend. It is cached during initialisation rather than read from a property
    /// getter: the getter initialises NVAPI, and running that on the UI thread while
    /// Avalonia is binding blocks the dispatcher.
    /// </summary>
    public int MaxMemoryDelta { get; private set; } = 1500;

    public OverclockGpuSettingsViewModel(IMainThreadDispatcher dispatcher, IDialogService dialogs, ILogger<OverclockGpuSettingsViewModel> logger)
        : base(dialogs)
    {
        _dispatcher = dispatcher;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await LibContainer.Initialization.ConfigureAwait(false);

            var controller = await Task.Run(
                () => LoqNova.Lib.IoCContainer.Resolve<GPUOverclockController>()).ConfigureAwait(false);

            // Support detection initialises and unloads NVAPI and queries WMI, so it is
            // kept off the UI thread.
            IsSupported = await Task.Run(() => controller.IsSupportedAsync()).ConfigureAwait(false);

            if (!IsSupported)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    ErrorMessage =
                        "The backend reports no discrete GPU overclock support on this machine. "
                        + "The stored values cannot be written to the GPU.";
                }).ConfigureAwait(false);
                return;
            }

            var maxMemory = await Task.Run(
                () => GPUOverclockController.GetMaxMemoryDeltaMhz()).ConfigureAwait(false);

            var (enabled, info) = controller.GetState();

            await _dispatcher.InvokeAsync(() =>
            {
                MaxMemoryDelta = maxMemory;
                LoadState(enabled, info);
                ErrorMessage = null;
            }).ConfigureAwait(false);

            controller.Changed += (_, _) => _dispatcher.Post(() => LoadState(controller));
        }
        catch (Exception ex)
        {
            // A failure here used to look identical to "unsupported", which sent the
            // user looking for a hardware problem that was not there.
            _logger.LogError(ex, "GPU overclock settings could not be initialised.");
            await _dispatcher.InvokeAsync(() =>
            {
                IsSupported = false;
                ErrorMessage = ex.Message;
            }).ConfigureAwait(false);
        }
    }

    private void LoadState(GPUOverclockController controller)
    {
        var (enabled, info) = controller.GetState();
        LoadState(enabled, info);
    }

    private void LoadState(bool enabled, GPUOverclockInfo info)
    {
        IsEnabled = enabled;
        CoreClockOffset = info.CoreDeltaMhz;
        MemoryClockOffset = info.MemoryDeltaMhz;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!IsSupported)
            return;

        try
        {
            await LibContainer.Initialization.ConfigureAwait(false);

            var controller = await Task.Run(
                () => LoqNova.Lib.IoCContainer.Resolve<GPUOverclockController>()).ConfigureAwait(false);

            // WPF's sliders are ranged from zero to the reported maximum, so negative
            // offsets are not selectable there and are rejected here too.
            var core = Math.Clamp(CoreClockOffset, 0, MaxCoreDelta);
            var memory = Math.Clamp(MemoryClockOffset, 0, MaxMemoryDelta);

            controller.SaveState(IsEnabled, new GPUOverclockInfo(core, memory));

            // ApplyStateAsync swallows Vantage and Legion Zone rejections, so the
            // authoritative state is re-read and any refusal is reported.
            await controller.ApplyStateAsync().ConfigureAwait(false);

            await _dispatcher.InvokeAsync(() =>
            {
                LoadState(controller);
                ErrorMessage = null;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPU overclock settings could not be applied.");
            await _dispatcher.InvokeAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        CoreClockOffset = 0;
        MemoryClockOffset = 0;
    }

}
