using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LoqNova.Lib.Controllers;
using LoqNova.Lib.Features;
using LoqNova.Lib.Features.Hybrid.Notify;
using LoqNova.Lib.Messaging;
using LoqNova.Lib.Messaging.Messages;
using LoqNova.Lib.System;
using LoqNova.Lib.Utils;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LoqNova.Lib.Listeners;

public class PowerStateListener : IListener<PowerStateListener.ChangedEventArgs>
{
    public class ChangedEventArgs(PowerStateEvent powerStateEvent, bool powerAdapterStateChanged) : EventArgs
    {
        public PowerStateEvent PowerStateEvent { get; } = powerStateEvent;
        public bool PowerAdapterStateChanged { get; } = powerAdapterStateChanged;
    }

    private readonly SafeHandle _recipientHandle;
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly PDEVICE_NOTIFY_CALLBACK_ROUTINE _callback;

    private readonly PowerModeFeature _powerModeFeature;
    private readonly BatteryFeature _batteryFeature;
    private readonly DGPUNotify _dgpuNotify;
    private readonly RGBKeyboardBacklightController _rgbController;
    private readonly PowerModeListener _powerModeListener;

    private bool _started;
    private HPOWERNOTIFY _handle;
    private PowerAdapterStatus? _lastPowerAdapterState;

    // --- AC transition → resulting performance mode strobe ---
    private static readonly TimeSpan AcModeChangeSettleDelay = TimeSpan.FromMilliseconds(2500);
    private PowerModeState? _lastKnownMode;
    private EventHandler<PowerModeListener.ChangedEventArgs>? _modeChangedHandler;
    private int _acStrobeCheckActive;
    private volatile bool _acStrobeCheckPending;
    private volatile bool _modeChangeEventSeenDuringWindow;

    public event EventHandler<ChangedEventArgs>? Changed;

    public unsafe PowerStateListener(PowerModeFeature powerModeFeature, BatteryFeature batteryFeature, DGPUNotify dgpuNotify, RGBKeyboardBacklightController rgbController, PowerModeListener powerModeListener)
    {
        _powerModeFeature = powerModeFeature;
        _batteryFeature = batteryFeature;
        _dgpuNotify = dgpuNotify;
        _rgbController = rgbController;
        _powerModeListener = powerModeListener;

        _callback = Callback;
        _recipientHandle = new StructSafeHandle<DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS>(new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
        {
            Callback = _callback,
            Context = null,
        });
    }

    public async Task StartAsync()
    {
        if (_started)
            return;

        _lastPowerAdapterState = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);

        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        RegisterSuspendResumeNotification();

        // Track the last-known performance mode so AC transitions can be compared
        // against the pre-transition state. Any existing mode-change announcement
        // (Fn+Q event, dropdown, automation) keeps this up to date.
        _modeChangedHandler = (_, e) =>
        {
            _lastKnownMode = e.State;
            _modeChangeEventSeenDuringWindow = true;
        };
        _powerModeListener.Changed += _modeChangedHandler;

        _ = Task.Run(async () =>
        {
            try
            {
                if (await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false))
                    _lastKnownMode = await _powerModeFeature.GetStateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Failed to read initial power mode for AC strobe tracking.", ex);
            }
        });

        _started = true;
    }

    public Task StopAsync()
    {
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;

        if (_modeChangedHandler is not null)
            _powerModeListener.Changed -= _modeChangedHandler;

        UnRegisterSuspendResumeNotification();

        _started = false;

        return Task.CompletedTask;
    }

    private async void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Event received: {e.Mode}");

        var powerMode = e.Mode switch
        {
            PowerModes.StatusChange => PowerStateEvent.StatusChange,
            PowerModes.Resume => PowerStateEvent.Resume,
            PowerModes.Suspend => PowerStateEvent.Suspend,
            _ => PowerStateEvent.Unknown
        };

        if (powerMode is PowerStateEvent.Unknown)
            return;

        await HandleAsync(powerMode).ConfigureAwait(false);
    }

    private unsafe uint Callback(void* context, uint type, void* setting)
    {
        _ = Task.Run(() => CallbackAsync(type));
        return (uint)WIN32_ERROR.ERROR_SUCCESS;
    }

    private async Task CallbackAsync(uint type)
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        if (!mi.Properties.SupportsAlwaysOnAc.status)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Ignoring, AO AC not enabled...");

            return;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Event value: {type}");

        var powerMode = type switch
        {
            PInvoke.PBT_APMRESUMEAUTOMATIC => PowerStateEvent.Resume,
            _ => PowerStateEvent.Unknown
        };

        if (powerMode is not PowerStateEvent.Resume)
            return;

        await HandleAsync(powerMode).ConfigureAwait(false);
    }

    private async Task HandleAsync(PowerStateEvent powerStateEvent)
    {
        var powerAdapterState = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Handle {powerStateEvent}. [newState={powerAdapterState}]");

        if (powerStateEvent is PowerStateEvent.Resume)
        {
            _ = Task.Run(async () =>
            {
                if (await _batteryFeature.IsSupportedAsync().ConfigureAwait(false))
                    await _batteryFeature.EnsureCorrectBatteryModeIsSetAsync().ConfigureAwait(false);

                if (await _rgbController.IsSupportedAsync().ConfigureAwait(false))
                    await _rgbController.SetLightControlOwnerAsync(true, true).ConfigureAwait(false);

                if (await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false))
                {
                    await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync().ConfigureAwait(false);
                    await _powerModeFeature.EnsureGodModeStateIsAppliedAsync().ConfigureAwait(false);
                }

                if (await _dgpuNotify.IsSupportedAsync().ConfigureAwait(false))
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await _dgpuNotify.NotifyAsync().ConfigureAwait(false);
                }
            });
        }

        if (powerStateEvent is PowerStateEvent.StatusChange && powerAdapterState is PowerAdapterStatus.Connected)
        {
            _ = Task.Run(async () =>
            {
                if (await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false))
                    await _powerModeFeature.EnsureGodModeStateIsAppliedAsync().ConfigureAwait(false);

                if (await _dgpuNotify.IsSupportedAsync().ConfigureAwait(false))
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await _dgpuNotify.NotifyAsync().ConfigureAwait(false);
                }
            });
        }

        var powerAdapterStateChanged = powerAdapterState != _lastPowerAdapterState;
        _lastPowerAdapterState = powerAdapterState;

        if (powerStateEvent is PowerStateEvent.Suspend or PowerStateEvent.Unknown)
            return;

        if (powerAdapterStateChanged)
            Notify(powerAdapterState);

        Changed?.Invoke(this, new(powerStateEvent, powerAdapterStateChanged));
    }

    private unsafe void RegisterSuspendResumeNotification()
    {
        _handle = PInvoke.PowerRegisterSuspendResumeNotification(REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_CALLBACK, _recipientHandle, out var handle) == WIN32_ERROR.ERROR_SUCCESS
            ? new HPOWERNOTIFY(new IntPtr(handle))
            : HPOWERNOTIFY.Null;
    }

    private void UnRegisterSuspendResumeNotification()
    {
        PInvoke.PowerUnregisterSuspendResumeNotification(_handle);
        _handle = HPOWERNOTIFY.Null;
    }

    private static void Notify(PowerAdapterStatus newState)
    {
        switch (newState)
        {
            case PowerAdapterStatus.Connected:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ACAdapterConnected));
                break;
            case PowerAdapterStatus.ConnectedLowWattage:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ACAdapterConnectedLowWattage));
                break;
            case PowerAdapterStatus.Disconnected:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ACAdapterDisconnected));
                break;
        }
    }
}
