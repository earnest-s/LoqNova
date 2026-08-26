using System;
using System.Threading.Tasks;
using LoqNova.Lib.Controllers;
using LoqNova.Lib.System.Management;
using LoqNova.Lib.Utils;

namespace LoqNova.Lib.Listeners;

public class ThermalModeListener(
    WindowsPowerModeController windowsPowerModeController,
    WindowsPowerPlanController windowsPowerPlanController,
    Lazy<PowerModeFeature> powerModeFeature)
    : AbstractWMIListener<ThermalModeListener.ChangedEventArgs, ThermalModeState, int>(WMI.LenovoGameZoneThermalModeEvent.Listen)
{
    public class ChangedEventArgs(ThermalModeState state) : EventArgs
    {
        public ThermalModeState State { get; } = state;
    }

    private readonly ThreadSafeCounter _suppressCounter = new();

    protected override ThermalModeState GetValue(int value)
    {
        var state = (ThermalModeState)value;

        if (!Enum.IsDefined(state))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Unknown value received: {value}");

            state = ThermalModeState.Unknown;
        }

        return state;
    }

    protected override ChangedEventArgs GetEventArgs(ThermalModeState value) => new(value);

    protected override async Task OnChangedAsync(ThermalModeState state)
    {
        if (!_suppressCounter.Decrement())
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Suppressed.");
            return;
        }

        if (state == ThermalModeState.Unknown)
            return;

        var powerModeState = state switch
        {
            ThermalModeState.Quiet => PowerModeState.Quiet,
            ThermalModeState.Balance => PowerModeState.Balance,
            ThermalModeState.Performance => PowerModeState.Performance,
            ThermalModeState.GodMode => PowerModeState.GodMode,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };

        await windowsPowerModeController.SetPowerModeAsync(powerModeState).ConfigureAwait(false);
        await windowsPowerPlanController.SetPowerPlanAsync(powerModeState).ConfigureAwait(false);

        // Firmware-initiated performance mode transition (e.g. AC adapter driven).
        // Announce the resulting mode so the existing performance-mode strobe plays
        // in that mode's color. AnnounceModeChangeAsync deduplicates against an
        // actually-executed strobe for the same mode (e.g. Fn+Q echo), and the
        // SuppressNext() guard above already filters programmatic internal changes.
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[DIAG] Thermal event → resulting PowerModeState. [state={powerModeState}]");

        await powerModeFeature.Value.AnnounceModeChangeAsync(powerModeState).ConfigureAwait(false);
    }

    public void SuppressNext()
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Suppressing next...");

        _suppressCounter.Increment();
    }
}
