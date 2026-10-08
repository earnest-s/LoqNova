using System;
using System.Threading.Tasks;
using LoqNova.Lib.Macro;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Thin adapter over the real <see cref="MacroController"/>.
/// <para>
/// This interface deliberately exposes only the backend's own vocabulary.
/// <see cref="MacroEvent"/>, <see cref="MacroSequence"/>, <see cref="MacroIdentifier"/>
/// and <see cref="MacroRecorderSettings"/> are passed straight through, because WPF's
/// Macro page works in exactly those types and the Avalonia layer must not introduce a
/// parallel model. There is no second macro engine and no second persistence path:
/// every read and write goes through the controller, which owns macro.json.
/// </para>
/// </summary>
public interface IMacroService
{
    /// <summary>
    /// The real <c>MacroController.IsEnabled</c> state.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>True while the backend recorder holds its hooks.</summary>
    bool IsRecording { get; }

    /// <summary>The repeat counts WPF offers, from the real controller.</summary>
    int[] AllowedRepeatCounts { get; }

    /// <summary>Resolves the real controller once the shared container is ready.</summary>
    Task InitializeAsync();

    /// <summary>Mirrors a change onto the real controller, which persists it.</summary>
    void SetEnabled(bool enabled);

    /// <summary>Reads the real sequence for a key, or an empty one when unset.</summary>
    MacroSequence GetSequence(MacroIdentifier identifier);

    /// <summary>Writes the sequence through the controller, which cleans and persists it.</summary>
    void SetSequence(MacroIdentifier identifier, MacroSequence sequence);

    /// <summary>Starts the real recorder with the chosen capture settings.</summary>
    void StartRecording(MacroRecorderSettings settings);

    /// <summary>Stops the real recorder. ESC and session switches stop it from the backend.</summary>
    void StopRecording();

    /// <summary>Raised for every captured real event, on the UI thread.</summary>
    event Action<MacroEvent>? RecorderReceived;

    /// <summary>Raised when recording ends; the argument is the backend's Interrupted flag.</summary>
    event Action<bool>? RecorderStopped;
}