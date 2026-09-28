using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Lib;
using LoqNova.Lib.Controllers;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// Discrete GPU overclock (WPF OverclockDiscreteGPUSettingsWindow equivalent).
/// Backed by <see cref="GPUOverclockController"/>, whose model is
/// <c>GPUOverclockInfo(CoreDeltaMhz, MemoryDeltaMhz)</c> plus an enabled flag.
/// Power limit, temperature limit and voltage offset are not part of the backend
/// model and are therefore not offered here.
/// </summary>
public partial class OverclockGpuSettingsViewModel : ViewModelBase
{
    private readonly IMainThreadDispatcherBridge _dispatcher;

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

    public OverclockGpuSettingsViewModel(IMainThreadDispatcherBridge dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task InitializeAsync()
    {
        await _dispatcher.ReadyAsync();

        if (!GPUOverclockController.IsResolvable)
        {
            IsSupported = false;
            return;
        }

        var controller = GPUOverclockController.Current;
        IsSupported = await controller.IsSupportedAsync().ConfigureAwait(false);

        if (!IsSupported)
            return;

        LoadState(controller);

        controller.Changed += (_, _) => _dispatcher.Post(LoadState, controller);
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

        await _dispatcher.ReadyAsync();

        var controller = GPUOverclockController.Current;

        // Clamp to the limits the backend itself reports.
        var core = CoreClockOffset < -MaxCoreDelta ? -MaxCoreDelta : CoreClockOffset > MaxCoreDelta ? MaxCoreDelta : CoreClockOffset;
        var memory = MemoryClockOffset < -MaxMemoryDelta ? -MaxMemoryDelta : MemoryClockOffset > MaxMemoryDelta ? MaxMemoryDelta : MemoryClockOffset;

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

    [RelayCommand]
    private Task CloseAsync() => Task.CompletedTask;
}
