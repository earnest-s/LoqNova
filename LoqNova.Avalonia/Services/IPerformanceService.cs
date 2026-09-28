using System;
using System.Threading.Tasks;
using LoqNova.Lib;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Power mode access. Deliberately exposes no synthetic tuning values
/// (CPU/GPU power or thermal limits): those live in
/// <c>GodModeSettings.GodModeSettingsStore.Preset</c> and are applied through
/// <c>IGodModeController</c>, so they are not part of the power mode surface.
/// </summary>
public interface IPerformanceService
{
    /// <summary>Last state read from the machine.</summary>
    PowerModeState CurrentMode { get; }

    /// <summary>True when the machine exposes the Lenovo Smart Fan mode WMI feature.</summary>
    bool IsSupported { get; }

    /// <summary>True when the machine reports God Mode as an available power mode.</summary>
    bool IsGodModeSupported { get; }

    /// <summary>True when the machine is currently in God Mode.</summary>
    bool IsGodModeEnabled { get; }

    /// <summary>Power modes the machine actually reports, in canonical order.</summary>
    PowerModeState[] AvailableStates { get; }

    event Action<PowerModeState>? ModeChanged;

    Task InitializeAsync();

    /// <summary>Re-reads the power mode from the machine and republishes it.</summary>
    Task RefreshAsync();

    /// <summary>Applies <paramref name="mode"/> to the machine and republishes the resulting state.</summary>
    Task SetModeAsync(PowerModeState mode);

    /// <summary>
    /// Re-applies the current God Mode parameters to the machine, mirroring
    /// <c>PowerModeFeature.EnsureGodModeStateIsAppliedAsync</c>. Does nothing when
    /// the machine is not in God Mode.
    /// </summary>
    Task ApplyGodModeSettingsAsync();
}
