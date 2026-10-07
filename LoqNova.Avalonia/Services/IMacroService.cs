using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using LoqNova.Lib.Macro;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// One number-pad slot. This is a UI row, not a macro model: the events are the real
/// backend <see cref="MacroEvent"/> and the slot is identified by a real
/// <see cref="MacroIdentifier"/>, so the Avalonia layer never redefines the domain.
/// </summary>
public class MacroKey
{
    /// <summary>Number-pad position, 0-9.</summary>
    public int KeyNumber { get; init; }

    /// <summary>Display label for the slot.</summary>
    public string Name { get; init; } = "";

    /// <summary>The real backend identifier this row stands for.</summary>
    public MacroIdentifier Identifier { get; init; }

    /// <summary>Real backend events held by this slot.</summary>
    public ObservableCollection<MacroEvent> Events { get; set; } = [];

    /// <summary>Whether the slot currently has any events.</summary>
    public bool HasEvents => Events.Count > 0;

    /// <summary>Playback repeat count. WPF clamps this to 1-10.</summary>
    public int RepeatCount { get; set; } = 1;

    /// <summary>Whether delays are skipped during playback.</summary>
    public bool IgnoreDelays { get; set; }

    /// <summary>Whether another key press interrupts playback.</summary>
    public bool InterruptOnOtherKey { get; set; }
}

public interface IMacroService
{
    /// <summary>The real MacroController enable state.</summary>
    bool IsEnabled { get; set; }

    /// <summary>The ten number-pad slots, backed by real identifiers.</summary>
    ObservableCollection<MacroKey> MacroKeys { get; }

    /// <summary>Currently selected slot.</summary>
    int SelectedKeyNumber { get; set; }

    /// <summary>True while the backend recorder is running.</summary>
    bool IsRecording { get; }

    event Action<MacroKey>? MacroKeyChanged;

    event Action<bool>? RecordingStateChanged;

    Task InitializeAsync();

    /// <summary>Loads a slot's real sequence from the backend.</summary>
    void LoadSlot(int slot);

    /// <summary>The backend identifier for a slot.</summary>
    MacroIdentifier IdentifierFor(int slot);

    Task StartRecordingAsync(int keyNumber);

    Task StopRecordingAsync();

    Task PlayMacroAsync(int keyNumber);

    /// <summary>Writes the edited slots back through the backend.</summary>
    Task SaveAsync();
}
