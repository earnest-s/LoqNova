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
    /// <summary>
    /// Whether the machine supports discrete GPU overclocking.
    ///
    /// This must be observable. As a plain auto-property the setter raised no change
    /// notification, so the sliders and Apply stayed greyed out for the whole life of the
    /// dialog even after support had been confirmed - the dialog simply looked inert.
    ///
    /// It also starts enabled so the dialog is usable the instant it opens, as WPF's is.
    /// The backend still validates on apply, and a failed support check corrects this to
    /// false and explains why.
    /// </summary>
    [ObservableProperty]
    private bool _isSupported = true;

    /// <summary>Why support could not be determined, shown instead of a silent failure.</summary>
    [ObservableProperty]
    private string? _errorMessage;

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
    [ObservableProperty]
    private int _maxMemoryDelta = 1500;

    public OverclockGpuSettingsViewModel(IMainThreadDispatcher dispatcher, IDialogService dialogs, ILogger<OverclockGpuSettingsViewModel> logger)
        : base(dialogs)
    {
        _dispatcher = dispatcher;

        _logger = logger;
    }

    /// <summary>
    /// True while the backend is being queried, so nothing is claimed yet. This has to
    /// be an observable property: as a plain auto-property the setter never raised
    /// change notification, so the "Reading..." panel stayed on screen permanently even
    /// after initialisation had finished and the real values had loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading = true;

    private static TimedResult<bool>? _cachedSupport;
    private static int? _cachedMaxMemory;
    private GPUOverclockController? _controller;

    /// <summary>Result of a bounded call, so a hang is reportable rather than silent.</summary>
    private readonly record struct TimedResult<T>(T? Value, bool TimedOut);

    /// <summary>
    /// Runs a backend call off the UI thread and gives up after <paramref name="limit"/>.
    /// The task is not cancelled, because NVAPI and WMI calls here have no cancellation
    /// token, but the caller is released either way.
    /// </summary>
    private static async Task<TimedResult<T>> WithTimeout<T>(
        Func<Task<T>> call, TimeSpan limit)
    {
        var work = Task.Run(call);

        var finished = await Task.WhenAny(work, Task.Delay(limit)).ConfigureAwait(false);

        if (finished == work)
        {
            try
            {
                return new TimedResult<T>(await work.ConfigureAwait(false), false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"GPU overclock backend call failed: {ex.Message}", ex);
            }
        }

        return new TimedResult<T>(default, true);
    }

    public async Task InitializeAsync()
    {
        try
        {
            await LibContainer.Initialization.ConfigureAwait(false);

            var controller = await Task.Run(
                () => LoqNova.Lib.IoCContainer.Resolve<GPUOverclockController>()).ConfigureAwait(false);

            _controller = controller;

            // WPF's dialog is usable the moment it opens, because it paints from the saved
            // state first and only then talks to the hardware. GetState is a json read, so
            // the sliders show their real values straight away instead of the dialog sitting
            // on "Reading..." while NVAPI initialises and unloads.
            var (enabled, info) = controller.GetState();

            await _dispatcher.InvokeAsync(() =>
            {
                LoadState(enabled, info);
                IsLoading = false;
            }).ConfigureAwait(false);

            // Support detection and the memory ceiling both initialise NVAPI and query
            // WMI, so they are slow. They are cached across openings because neither
            // changes while the app is running, and repeating them per dialog is what made
            // this feel slower than WPF.
            var support = _cachedSupport ?? await WithTimeout(
                () => controller.IsSupportedAsync(), TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            var supported = !support.TimedOut && support.Value == true;

            var maxMemory = _cachedMaxMemory ?? await Task.Run(
                () => GPUOverclockController.GetMaxMemoryDeltaMhz()).ConfigureAwait(false);

            if (supported)
            {
                _cachedSupport = support;
                _cachedMaxMemory = maxMemory;

                await _dispatcher.InvokeAsync(() =>
                {
                    IsSupported = true;
                    MaxMemoryDelta = maxMemory;
                    ErrorMessage = null;
                }).ConfigureAwait(false);

                controller.Changed += OnControllerChanged;                return;
            }

            // Report what each half of the check returned. The backend combines an NVAPI
            // GPU check with a Lenovo WMI capability check and reports only the result, so
            // a generic "unsupported" left it impossible to tell a driver problem from a
            // firmware capability flag.
            //
            // A hung check is reported as-is: waiting again on the raw WMI call doubled
            // the wait for no gain, since the same subsystem is what hung first.
            TimedResult<int> wmi;
            if (support.TimedOut)
            {
                wmi = new TimedResult<int>(default, true);
            }
            else
            {
                wmi = await WithTimeout(
                    () => LoqNova.Lib.System.Management.WMI.LenovoGameZoneData
                        .IsSupportGpuOCAsync(), TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }

            var message = support.TimedOut
                ? "The discrete GPU support check did not respond within 5 seconds, so the "
                  + "backend's NVAPI and Lenovo WMI calls are not returning on this machine. "
                  + "The values shown are the last saved state."
                : wmi.TimedOut
                    ? "The backend reports no discrete GPU overclock support, and the direct "
                      + "Lenovo WMI query also did not respond within 5 seconds."
                    : "The backend reports no discrete GPU overclock support. "
                      + $"Lenovo IsSupportGpuOC value: {wmi.Value} (must be greater than 0). "
                      + "A value of 0 is the firmware's own capability flag, not a driver fault.";

            await _dispatcher.InvokeAsync(() =>
            {
                IsSupported = false;
                ErrorMessage = message;
            }).ConfigureAwait(false);
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
                IsLoading = false;
            }).ConfigureAwait(false);
        }
    }

    private void OnControllerChanged(object? sender, EventArgs e) => _dispatcher.Post(() =>
    {
        if (_controller is { } controller)
        {
            LoadState(controller);
        }
    });

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

            _controller = controller;

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
