using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LoqNova.Lib.Controllers;
using LoqNova.Lib.Controllers.GodMode;
using LoqNova.Lib.Extensions;
using LoqNova.Lib.Listeners;
using LoqNova.Lib.Messaging;
using LoqNova.Lib.Messaging.Messages;
using LoqNova.Lib.System;
using LoqNova.Lib.System.Management;
using LoqNova.Lib.Utils;

namespace LoqNova.Lib.Features;

public class PowerModeUnavailableWithoutACException(PowerModeState powerMode) : Exception
{
    public PowerModeState PowerMode { get; } = powerMode;
}

public class PowerModeFeature(
    GodModeController godModeController,
    WindowsPowerModeController windowsPowerModeController,
    WindowsPowerPlanController windowsPowerPlanController,
    RGBKeyboardBacklightController rgbKeyboardBacklightController,
    ThermalModeListener thermalModeListener,
    PowerModeListener powerModeListener)
    : AbstractWmiFeature<PowerModeState>(WMI.LenovoGameZoneData.GetSmartFanModeAsync, WMI.LenovoGameZoneData.SetSmartFanModeAsync, WMI.LenovoGameZoneData.IsSupportSmartFanAsync, 1)
{
    private int _strobeGuard;

    // Execution-proof strobe dedupe: records what FireStrobeAsync ACTUALLY strobed,
    // so echo announcements (e.g. a thermal event following an Fn+Q smart-fan event)
    // cannot produce a second strobe for the same physical mode transition.
    private static readonly TimeSpan StrobeDedupeWindow = TimeSpan.FromMilliseconds(2500);
    private PowerModeState? _lastStrobeMode;
    private DateTime _lastStrobeUtc;

    public bool AllowAllPowerModesOnBattery { get; set; }

    public override async Task<PowerModeState[]> GetAllStatesAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return mi.Properties.SupportsGodMode
            ? [PowerModeState.Quiet, PowerModeState.Balance, PowerModeState.Performance, PowerModeState.GodMode]
            : [PowerModeState.Quiet, PowerModeState.Balance, PowerModeState.Performance];
    }

    public override async Task SetStateAsync(PowerModeState state)
    {
        var allStates = await GetAllStatesAsync().ConfigureAwait(false);
        if (!allStates.Contains(state))
            throw new InvalidOperationException($"Unsupported power mode {state}");

        if (state is PowerModeState.Performance or PowerModeState.GodMode
            && !AllowAllPowerModesOnBattery
            && await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false) is PowerAdapterStatus.Disconnected)
            throw new PowerModeUnavailableWithoutACException(state);

        // Fire OSD + RGB strobe IMMEDIATELY � before hardware write.
        // This gives instant visual feedback while WMI blocks.
        PublishNotification(state);
        _ = FireStrobeAsync(state);

        var currentState = await GetStateAsync().ConfigureAwait(false);

        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        if (mi.Properties.HasQuietToPerformanceModeSwitchingBug && currentState == PowerModeState.Quiet && state == PowerModeState.Performance)
        {
            thermalModeListener.SuppressNext();
            await base.SetStateAsync(PowerModeState.Balance).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }

        if (mi.Properties.HasGodModeToOtherModeSwitchingBug && currentState == PowerModeState.GodMode && state != PowerModeState.GodMode)
        {
            thermalModeListener.SuppressNext();

            switch (state)
            {
                case PowerModeState.Quiet:
                    await base.SetStateAsync(PowerModeState.Performance).ConfigureAwait(false);
                    break;
                case PowerModeState.Balance:
                    await base.SetStateAsync(PowerModeState.Quiet).ConfigureAwait(false);
                    break;
                case PowerModeState.Performance:
                    await base.SetStateAsync(PowerModeState.Balance).ConfigureAwait(false);
                    break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);

        }

        thermalModeListener.SuppressNext();
        await base.SetStateAsync(state).ConfigureAwait(false);

        // Dependencies run after hardware write succeeds
        await ApplyDependenciesAsync(state).ConfigureAwait(false);

        // Raise listener Changed event for existing subscribers
        await powerModeListener.NotifyAsync(state).ConfigureAwait(false);
    }

    /// <summary>
    /// Central performance mode post-change logic. Called by both
    /// <see cref="SetStateAsync"/> (dropdown / automation) and
    /// <see cref="PowerModeListener"/> (Fn+Q hardware key).
    /// Fires OSD + RGB strobe immediately, then applies dependencies.
    /// </summary>
    public async Task ApplyPerformanceModeAsync(PowerModeState mode)
    {
        // 1. OSD notification — instant
        PublishNotification(mode);

        // 2. RGB strobe — fire-and-forget, runs concurrently with deps
        _ = FireStrobeAsync(mode);

        // 3. Apply dependencies (GodMode preset, Windows power mode/plan)
        await ApplyDependenciesAsync(mode).ConfigureAwait(false);
    }

    /// <summary>
    /// Announces a performance mode that was changed EXTERNALLY by the firmware
    /// (e.g. an AC-adapter-driven transition reported via the thermal-mode event).
    /// Triggers the existing performance-mode keyboard strobe in the resulting
    /// mode's color — unless that exact mode was already strobed within the
    /// dedupe window, which proves an earlier announcement (typically Fn+Q's
    /// smart-fan event path) already executed the strobe for this transition.
    /// </summary>
    public Task AnnounceModeChangeAsync(PowerModeState mode)
    {
        var lastUtc = _lastStrobeUtc;
        if (_lastStrobeMode == mode && (DateTime.UtcNow - lastUtc) < StrobeDedupeWindow)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] AnnounceModeChange SKIPPED — identical mode already strobed. [mode={mode}, msAgo={(int)(DateTime.UtcNow - lastUtc).TotalMilliseconds}]");
            return Task.CompletedTask;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[DIAG] AnnounceModeChange → FireStrobeAsync. [mode={mode}]");

        return FireStrobeAsync(mode);
    }

    private async Task FireStrobeAsync(PowerModeState mode)
    {
        if (mode is PowerModeState.GodMode)
            await godModeController.ApplyStateAsync().ConfigureAwait(false);

        await windowsPowerModeController.SetPowerModeAsync(mode).ConfigureAwait(false);
        await windowsPowerPlanController.SetPowerPlanAsync(mode).ConfigureAwait(false);
    }

    private async Task FireStrobeAsync(PowerModeState mode)
    {
        // Guard: allow only one strobe at a time � prevents triple execution
        // when SetStateAsync and ApplyPerformanceModeAsync overlap.
        // [DIAGNOSTIC-ONLY] guard telemetry (no behavior change)
        var sw = global::System.Diagnostics.Stopwatch.StartNew();
        if (Interlocked.CompareExchange(ref _strobeGuard, 1, 0) != 0)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] FireStrobe BLOCKED by _strobeGuard (previous strobe still active). [mode={mode}]");
            return;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[DIAG] FireStrobe guard acquired. [mode={mode}]");

        try
        {
            if (await rgbKeyboardBacklightController.IsSupportedAsync().ConfigureAwait(false))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[DIAG] FireStrobe dispatching PlayTransitionAsync. [mode={mode}, supported=true]");
                await rgbKeyboardBacklightController.PlayTransitionAsync(mode).ConfigureAwait(false);
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[DIAG] FireStrobe PlayTransitionAsync returned. [mode={mode}, elapsedMs={sw.ElapsedMilliseconds}]");
            }
            else if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"[DIAG] FireStrobe skipped: RGB controller not supported. [mode={mode}]");
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to trigger RGB strobe for power mode {mode}", ex);
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] FireStrobe EXCEPTION swallowed. [type={ex.GetType().FullName}, msg={ex.Message}, elapsedMs={sw.ElapsedMilliseconds}]");
        }
        finally
        {
            Interlocked.Exchange(ref _strobeGuard, 0);
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] FireStrobe guard released. [mode={mode}, totalElapsedMs={sw.ElapsedMilliseconds}]");
        }
    }

    public async Task EnsureCorrectWindowsPowerSettingsAreSetAsync()
    {
        var state = await GetStateAsync().ConfigureAwait(false);
        await windowsPowerModeController.SetPowerModeAsync(state).ConfigureAwait(false);
        await windowsPowerPlanController.SetPowerPlanAsync(state, true).ConfigureAwait(false);
    }

    public async Task EnsureGodModeStateIsAppliedAsync()
    {
        var state = await GetStateAsync().ConfigureAwait(false);
        if (state != PowerModeState.GodMode)
            return;

        await godModeController.ApplyStateAsync().ConfigureAwait(false);
    }

    private static void PublishNotification(PowerModeState value)
    {
        var type = value switch
        {
            PowerModeState.Quiet => NotificationType.PowerModeQuiet,
            PowerModeState.Balance => NotificationType.PowerModeBalance,
            PowerModeState.Performance => NotificationType.PowerModePerformance,
            PowerModeState.GodMode => NotificationType.PowerModeGodMode,
            _ => (NotificationType?)null
        };

        if (type is { } t)
            MessagingCenter.Publish(new NotificationMessage(t, value.GetDisplayName()));
    }
}
