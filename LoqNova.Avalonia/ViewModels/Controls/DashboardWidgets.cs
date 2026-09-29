using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Controllers;
using LoqNova.Lib.Listeners;

namespace LoqNova.Avalonia.ViewModels.Controls;

/// <summary>
/// Display configuration options (resolution / refresh rate) are library
/// <c>IFeature&lt;T&gt;</c> states that implement <c>IDisplayName</c>. WPF keeps
/// them in step with the display by also listening to
/// <c>DisplayConfigurationListener.Changed</c>, so a resolution or mode change made
/// outside the app is picked up.
/// </summary>
public sealed class ResolutionWidgetViewModel
    : FeatureChoiceWidgetViewModel<LoqNova.Lib.Resolution>
{
    public ResolutionWidgetViewModel(IMainThreadDispatcher dispatcher)
        : base(dispatcher, "Resolution", "ScaleFill64")
    {
    }

    protected override void SubscribeExtraSignals()
    {
        var listener = LoqNova.Lib.IoCContainer.Resolve<DisplayConfigurationListener>();
        listener.Changed += OnDisplayChanged;
    }

    private void OnDisplayChanged(object? sender, DisplayConfigurationListener.ChangedEventArgs e)
        => Dispatcher.Post(async () => await RefreshAsync().ConfigureAwait(false));
}

public sealed class RefreshRateWidgetViewModel
    : FeatureChoiceWidgetViewModel<LoqNova.Lib.RefreshRate>
{
    public RefreshRateWidgetViewModel(IMainThreadDispatcher dispatcher)
        : base(dispatcher, "Refresh Rate", "DesktopPulse64")
    {
    }

    protected override void SubscribeExtraSignals()
    {
        var listener = LoqNova.Lib.IoCContainer.Resolve<DisplayConfigurationListener>();
        listener.Changed += OnDisplayChanged;
    }

    private void OnDisplayChanged(object? sender, DisplayConfigurationListener.ChangedEventArgs e)
        => Dispatcher.Post(async () => await RefreshAsync().ConfigureAwait(false));
}

/// <summary>
/// Discrete GPU status, reproducing WPF's <c>DiscreteGPUControl</c>: a state
/// indicator driven by <c>GPUController</c>, kept in step through
/// <c>NativeWindowsMessageListener</c> (display device arrival) and the
/// controller's own <c>Refreshed</c> event, plus the deactivate / kill-processes /
/// restart actions that control exposes.
/// </summary>
public sealed partial class DiscreteGpuWidgetViewModel : FeatureWidgetViewModel
{
    private GPUController? _controller;
    private NativeWindowsMessageListener? _listener;

    public DiscreteGpuWidgetViewModel(IMainThreadDispatcher dispatcher) : base(dispatcher)
    {
        Title = "Discrete GPU";
        Icon = "DeveloperBoard64";
        IsStatus = true;
        StatusText = "--";

        PrimaryActionText = "Deactivate";
        PrimaryActionCommand = new AsyncRelayCommand(DeactivateAsync);
        SecondaryActionText = "Close GPU apps";
        SecondaryActionCommand = new AsyncRelayCommand(KillProcessesAsync);
    }

    [ObservableProperty]
    private bool _isActive;

    public override async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        _controller = LoqNova.Lib.IoCContainer.Resolve<GPUController>();
        _listener = LoqNova.Lib.IoCContainer.Resolve<NativeWindowsMessageListener>();

        _controller.Refreshed += OnControllerRefreshed;
        _listener.Changed += OnNativeMessage;

        await RefreshAsync().ConfigureAwait(false);
    }

    private void OnControllerRefreshed(object? sender, GPUStatus status)
        => Dispatcher.Post(async () =>
        {
            IsAvailable = true;
            ApplyState(status.State);
        });

    private void OnNativeMessage(object? sender, NativeWindowsMessageListener.ChangedEventArgs e)
    {
        if (e.Message is not (NativeWindowsMessage.OnDisplayDeviceArrival or NativeWindowsMessage.MonitorDisconnected))
            return;

        Dispatcher.Post(async () => await RefreshAsync().ConfigureAwait(false));
    }

    public async Task RefreshAsync()
    {
        if (_controller is null)
            return;

        try
        {
            if (!_controller.IsSupported())
            {
                IsAvailable = false;
                return;
            }

            var state = await _controller.GetLastKnownStateAsync().ConfigureAwait(false);

            await Dispatcher.InvokeAsync(() =>
            {
                IsAvailable = true;
                ErrorMessage = null;
                ApplyState(state);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
        }
    }

    private void ApplyState(GPUState state)
    {
        IsActive = state == GPUState.Active;
        StatusText = state.ToString();
    }

    /// <summary>WPF's Deactivate action.</summary>
    private async Task DeactivateAsync()
    {
        if (_controller is null)
            return;

        try
        {
            IsBusy = true;
            await _controller.RestartGPUAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>WPF's context-menu "kill apps" action.</summary>
    private async Task KillProcessesAsync()
    {
        if (_controller is null)
            return;

        try
        {
            IsBusy = true;
            await _controller.KillGPUProcessesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Discrete GPU overclock, reproducing WPF's <c>OverclockDiscreteGPUControl</c>:
/// an enable toggle backed by <c>GPUOverclockController</c>, refreshed on display
/// device arrival and on the controller's own change event.
/// </summary>
public sealed class OverclockGpuWidgetViewModel : FeatureWidgetViewModel
{
    private GPUOverclockController? _controller;
    private NativeWindowsMessageListener? _listener;

    /// <summary>
    /// Core frequency offset in MHz, as WPF's <c>OverclockDiscreteGPUSettingsWindow</c>
    /// exposes it. The range starts at zero because that is the range the WPF slider
    /// uses; negative offsets are not selectable there.
    /// </summary>
    [ObservableProperty]
    private double _coreOffsetMhz;

    /// <summary>Memory frequency offset in MHz, same range rules as the core offset.</summary>
    [ObservableProperty]
    private double _memoryOffsetMhz;

    /// <summary>The offsets only apply while overclocking is switched on.</summary>
    public bool IsOffsetEditorEnabled => IsAvailable && IsOn;

    public double MaxCoreOffset => GPUOverclockController.GetMaxCoreDeltaMhz();

    /// <summary>
    /// The memory ceiling depends on the installed memory vendor, so it is read from
    /// the controller rather than assumed. That call initialises NVAPI, so it is read
    /// once during refresh and never from a property initialiser.
    /// </summary>
    public double MaxMemoryOffset { get; private set; } = 1500;

    public OverclockGpuWidgetViewModel(IMainThreadDispatcher dispatcher) : base(dispatcher)
    {
        Title = "GPU Overclock";
        Icon = "SpeedHigh64";
        IsToggle = true;
    }



    public override async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        _controller = LoqNova.Lib.IoCContainer.Resolve<GPUOverclockController>();
        _listener = LoqNova.Lib.IoCContainer.Resolve<NativeWindowsMessageListener>();

        _controller.Changed += OnControllerChanged;
        _listener.Changed += OnNativeMessage;

        await RefreshAsync().ConfigureAwait(false);
    }

    private void OnControllerChanged(object? sender, EventArgs e)
        => Dispatcher.Post(async () => await RefreshAsync().ConfigureAwait(false));

    private void OnNativeMessage(object? sender, NativeWindowsMessageListener.ChangedEventArgs e)
    {
        if (e.Message != NativeWindowsMessage.OnDisplayDeviceArrival)
            return;

        Dispatcher.Post(async () => await RefreshAsync().ConfigureAwait(false));
    }

    public async Task RefreshAsync()
    {
        if (_controller is null)
            return;

        try
        {
            if (!await _controller.IsSupportedAsync().ConfigureAwait(false))
            {
                IsAvailable = false;
                return;
            }

            var (enabled, info) = _controller.GetState();

            // Read the vendor-dependent memory ceiling off the UI thread: it
            // initialises NVAPI to work out which memory the machine has.
            var maxMemory = GPUOverclockController.GetMaxMemoryDeltaMhz();

            await Dispatcher.InvokeAsync(() =>
            {
                IsAvailable = true;
                ErrorMessage = null;
                SetIsOnFromBackend(enabled);
                MaxMemoryOffset = maxMemory;
                CoreOffsetMhz = info.CoreDeltaMhz;
                MemoryOffsetMhz = info.MemoryDeltaMhz;
                OnPropertyChanged(nameof(MaxCoreOffset));
                OnPropertyChanged(nameof(MaxMemoryOffset));
                OnPropertyChanged(nameof(IsOffsetEditorEnabled));
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
        }
    }

    /// <summary>Writes the toggle through the controller, then re-reads the real state.</summary>
    protected override void OnIsOnRequested(bool value)
    {
        if (!IsAvailable || IsBusy || _controller is null)
            return;

        _ = ApplyAsync();
    }

    private async Task ApplyAsync()
    {
        try
        {
            IsBusy = true;

            var (_, info) = _controller!.GetState();
            _controller.SaveState(IsOn, info);
            await _controller.ApplyStateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// WPF's <c>TurnOffMonitorsControl</c>: a single action that turns the monitors off
/// through the native message listener.
/// </summary>
public sealed class TurnOffMonitorsWidgetViewModel : FeatureWidgetViewModel
{
    private NativeWindowsMessageListener? _listener;

    public TurnOffMonitorsWidgetViewModel(IMainThreadDispatcher dispatcher) : base(dispatcher)
    {
        Title = "Turn Off Monitors";
        Icon = "DisplayOff64";
        IsAction = true;
        PrimaryActionText = "Turn Off";
        PrimaryActionCommand = new AsyncRelayCommand(TurnOffAsync);
    }

    public override async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        _listener = LoqNova.Lib.IoCContainer.Resolve<NativeWindowsMessageListener>();

        // The action is always available: the listener reports monitor state rather
        // than feature support, and the button disables itself while in flight.
        IsAvailable = true;
    }

    private async Task TurnOffAsync()
    {
        if (_listener is null)
            return;

        try
        {
            IsBusy = true;
            await _listener.TurnOffMonitorAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
