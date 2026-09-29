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
public sealed partial class OverclockGpuWidgetViewModel : FeatureWidgetViewModel
{
    private GPUOverclockController? _controller;
    private NativeWindowsMessageListener? _listener;

    public double MaxCoreOffset => GPUOverclockController.GetMaxCoreDeltaMhz();

    public OverclockGpuWidgetViewModel(IMainThreadDispatcher dispatcher) : base(dispatcher)
    {
        Title = "GPU Overclock";
        Icon = "SpeedHigh64";
        IsToggle = true;

        // WPF's OverclockDiscreteGPUSettingsWindow exposes exactly these two, and both
        // are ranged from zero: negative offsets are not selectable there.
        Settings.Add(new WidgetSettingViewModel
        {
            Label = "Core Frequency Offset",
            Unit = "MHz",
            Minimum = 0,
            Maximum = MaxCoreOffset,
            ApplyCommand = new AsyncRelayCommand(ApplyOffsetsAsync)
        });

        Settings.Add(new WidgetSettingViewModel
        {
            Label = "Memory Frequency Offset",
            Unit = "MHz",
            Minimum = 0,
            Maximum = 1500,
            ApplyCommand = new AsyncRelayCommand(ApplyOffsetsAsync)
        });
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

            // The memory ceiling depends on the installed memory vendor, so it is read
            // here rather than assumed. That call initialises NVAPI, which is why it is
            // never called from a property initialiser.
            var maxMemory = GPUOverclockController.GetMaxMemoryDeltaMhz();

            await Dispatcher.InvokeAsync(() =>
            {
                IsAvailable = true;
                ErrorMessage = null;
                SetIsOnFromBackend(enabled);

                // [0] is the core offset, [1] the memory offset, in the order added.
                if (Settings.Count >= 2)
                {
                    Settings[1].Maximum = maxMemory;
                    Settings[0].Value = info.CoreDeltaMhz;
                    Settings[1].Value = info.MemoryDeltaMhz;
                }
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

    /// <summary>Writes the toggle through the controller, then re-reads the real state.</summary>
    private async Task ApplyAsync()
    {
        try
        {
            IsBusy = true;

            // The toggle changes the enabled flag; the offsets are whatever the
            // sliders currently show, so editing a value is never lost by flipping
            // the switch.
            var (_, info) = _controller!.GetState();
            var core = Settings.Count >= 2 ? (int)Math.Round(Settings[0].Value) : info.CoreDeltaMhz;
            var memory = Settings.Count >= 2 ? (int)Math.Round(Settings[1].Value) : info.MemoryDeltaMhz;

            _controller.SaveState(IsOn, ClampOffsets(core, memory));
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

    /// <summary>Writes the edited frequency offsets through the same controller.</summary>
    private async Task ApplyOffsetsAsync()
    {
        if (!IsAvailable || !IsOn || IsBusy || _controller is null || Settings.Count < 2)
            return;

        try
        {
            IsBusy = true;

            _controller.SaveState(
                IsOn,
                ClampOffsets((int)Math.Round(Settings[0].Value), (int)Math.Round(Settings[1].Value)));
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

    /// <summary>
    /// Clamps to the ranges the WPF sliders allow: zero to the reported maximum,
    /// because negative offsets are not selectable there.
    /// </summary>
    private GPUOverclockInfo ClampOffsets(int core, int memory)
    {
        var maxMemory = Settings.Count >= 2 ? (int)Settings[1].Maximum : 1500;
        return new GPUOverclockInfo(
            Math.Clamp(core, 0, MaxCoreOffset),
            Math.Clamp(memory, 0, maxMemory));
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
