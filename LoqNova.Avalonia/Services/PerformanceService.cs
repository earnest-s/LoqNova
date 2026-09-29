using System;
using System.Linq;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Features;
using LoqNova.Lib.Listeners;
using Microsoft.Extensions.Logging;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Thin adapter over the existing <see cref="PowerModeFeature"/>. All reads and
/// writes go through that feature, so availability checks, the AC-adapter guard
/// and the God Mode application behave exactly as they do in WPF. No power mode
/// state is invented here.
/// </summary>
public class PerformanceService : IPerformanceService
{
    private static readonly PowerModeState[] NoStates = [];

    private readonly IMainThreadDispatcher _dispatcher;
    private readonly ILogger<PerformanceService> _logger;

    private PowerModeFeature? _powerModeFeature;
    private PowerModeState[] _availableStates = NoStates;

    public PowerModeState? CurrentMode { get; private set; }

    public bool IsSupported { get; private set; }

    public bool IsGodModeSupported => _availableStates.Contains(PowerModeState.GodMode);

    public bool IsGodModeEnabled => IsSupported && CurrentMode == PowerModeState.GodMode;

    public PowerModeState[] AvailableStates => (PowerModeState[])_availableStates.Clone();

    public event Action<PowerModeState>? ModeChanged;

    public PerformanceService(
        IMainThreadDispatcher dispatcher,
        ILogger<PerformanceService> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        await LibContainer.Initialization.ConfigureAwait(false);

        // Resolved only once the shared container is up, so constructing this
        // service never races container initialisation.
        _powerModeFeature = LoqNova.Lib.IoCContainer.Resolve<PowerModeFeature>();

        // The library's own listener is the source of truth for hardware-initiated
        // mode changes: it watches the WMI LenovoGameZoneSmartFanModeEvent, which is
        // what the Fn+Q key raises through the EC. Subscribing to it is how WPF stays
        // in step, so no polling is used here.
        var listener = LoqNova.Lib.IoCContainer.Resolve<PowerModeListener>();
        listener.Changed += OnHardwareModeChanged;

        IsSupported = await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false);

        if (!IsSupported)
        {
            _availableStates = NoStates;
            _logger.LogInformation("Power mode feature is not supported on this machine.");
            return;
        }

        _availableStates = await _powerModeFeature.GetAllStatesAsync().ConfigureAwait(false);

        _logger.LogInformation(
            "Power mode feature initialized. Available: {Modes}",
            string.Join(", ", _availableStates));

        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>True once the machine has been queried successfully.</summary>
    private bool IsReady => _powerModeFeature is not null && IsSupported;

    /// <summary>
    /// Raised by the library listener when the hardware changes the mode on its own
    /// (Fn+Q) or when the firmware switches modes because of an AC change. The event
    /// already carries the new state, so it is republished rather than re-read.
    /// </summary>
    private void OnHardwareModeChanged(object? sender, PowerModeListener.ChangedEventArgs e)
        => _dispatcher.Post(() => SetMode(e.State));

    public async Task RefreshAsync()
    {
        if (_powerModeFeature is null || !IsSupported)
            return;

        var mode = await _powerModeFeature.GetStateAsync().ConfigureAwait(false);
        await PublishAsync(mode).ConfigureAwait(false);
    }

    public async Task SetModeAsync(PowerModeState mode)
    {
        if (!IsReady)
        {
            _logger.LogWarning("Ignoring request for {Mode}: power mode is not available.", mode);
            return;
        }

        if (_availableStates.Length > 0 && !_availableStates.Contains(mode))
        {
            _logger.LogWarning("Ignoring request for {Mode}: not reported as available.", mode);
            return;
        }

        try
        {
            await _powerModeFeature!.SetStateAsync(mode).ConfigureAwait(false);

            if (mode == PowerModeState.GodMode)
                await _powerModeFeature.EnsureGodModeStateIsAppliedAsync().ConfigureAwait(false);

            // Read back rather than assuming the write landed.
            var applied = await _powerModeFeature.GetStateAsync().ConfigureAwait(false);
            await PublishAsync(applied).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set power mode to {Mode}.", mode);

            // Surface the real machine state again so the UI cannot drift.
            await RefreshAsync().ConfigureAwait(false);
        }
    }

    public async Task ApplyGodModeSettingsAsync()
    {
        if (_powerModeFeature is null || !IsGodModeEnabled)
        {
            _logger.LogInformation("Machine is not in God Mode; nothing to apply.");
            return;
        }

        try
        {
            await _powerModeFeature.EnsureGodModeStateIsAppliedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply God Mode settings.");
        }
    }

    private async Task PublishAsync(PowerModeState mode)
    {
        if (_dispatcher.CheckAccess())
        {
            SetMode(mode);
            return;
        }

        await _dispatcher.InvokeAsync(() => SetMode(mode)).ConfigureAwait(false);
    }

    private void SetMode(PowerModeState mode)
    {
        if (CurrentMode == mode)
            return;

        CurrentMode = mode;
        _logger.LogInformation("Power mode is now {Mode}.", mode);
        ModeChanged?.Invoke(mode);
    }
}
