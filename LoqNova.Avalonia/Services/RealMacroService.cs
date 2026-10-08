using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using LoqNova.Lib;
using LoqNova.Lib.Macro;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Adapter over the real <see cref="MacroController"/>. The slot list, sequences,
/// enable state, recording and playback all come from the backend; this type owns no
/// macro logic and no persistence of its own.
/// </summary>
public sealed class RealMacroService : IMacroService
{
    private readonly IMainThreadDispatcher _dispatcher;

    private MacroController? _controller;
    private bool _initialised;

    /// <summary>
    /// The ten number-pad slots. WPF derives each one from its button tag parsed as
    /// hex and builds <c>MacroIdentifier(MacroSource.Keyboard, key)</c>, which is the
    /// same key the backend's macro hook listens for.
    /// </summary>
    public ObservableCollection<MacroKey> MacroKeys { get; } = [];

    public int SelectedKeyNumber { get; set; }

    public bool IsRecording => _controller is not null && _isRecording;

    private bool _isRecording;

    public event Action<MacroKey>? MacroKeyChanged;

    public event Action<bool>? RecordingStateChanged;

    public RealMacroService(IMainThreadDispatcher dispatcher) => _dispatcher = dispatcher;

    public bool IsEnabled
    {
        get => _controller?.IsEnabled ?? false;
        set
        {
            if (_controller is null)
            {
                return;
            }

            // The backend persists this through MacroSettings and starts or stops the
            // hook, so the UI holds no independent copy of the state.
            _controller.SetEnabled(value);
            OnPropertyChanged(nameof(IsEnabled));
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialised)
        {
            return;
        }

        // Resolved after the readiness gate and off the UI thread: IoCContainer.Resolve
        // takes the global container lock.
        _controller = await Task.Run(() => IoCContainer.Resolve<MacroController>())
            .ConfigureAwait(false);

        var sequences = _controller.GetSequences();

        // Seed the ten slots so the number pad is bound to real identifiers rather than
        // an Avalonia-only list. The page loads each slot's sequence on selection.
        for (var slot = 0; slot <= 9; slot++)
        {
            var identifier = SlotIdentifier(slot);

            var key = new MacroKey
            {
                KeyNumber = slot,
                Name = slot.ToString(),
                Identifier = identifier
            };

            // Apply, not just the events, so RepeatCount/IgnoreDelays/InterruptOnOtherKey
            // are seeded here too and survive an edit-and-save round trip.
            Apply(key, sequences.GetValueOrDefault(identifier));

            MacroKeys.Add(key);

            // The page subscribes for slots arriving after it was constructed, which is
            // the normal case because initialization is gated behind startup readiness.
            MacroKeyChanged?.Invoke(key);
        }

        _controller.RecorderReceived += OnRecorderReceived;
        _controller.RecorderStopped += OnRecorderStopped;

        _initialised = true;
    }

    /// <summary>
    /// Maps the key names the UI offers onto Win32 virtual-key codes, which is what the
    /// backend stores in <c>MacroEvent.Key</c>. WPF converts back with
    /// <c>KeyInterop.KeyFromVirtualKey((int)macroEvent.Key)</c>, so the stored value must
    /// be the virtual key and not a name or an ad-hoc number.
    /// </summary>
    private static readonly Dictionary<string, uint> KeyCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44, ["E"] = 0x45,
        ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48, ["I"] = 0x49, ["J"] = 0x4A,
        ["K"] = 0x4B, ["L"] = 0x4C, ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F,
        ["P"] = 0x50, ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58, ["Y"] = 0x59,
        ["Z"] = 0x5A,
        ["0"] = 0x30, ["1"] = 0x31, ["2"] = 0x32, ["3"] = 0x33, ["4"] = 0x34,
        ["5"] = 0x35, ["6"] = 0x36, ["7"] = 0x37, ["8"] = 0x38, ["9"] = 0x39,
        ["Enter"] = 0x0D, ["Escape"] = 0x1B, ["Space"] = 0x20, ["Tab"] = 0x09,
        ["Backspace"] = 0x08, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27,
        ["Down"] = 0x28, ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21,
        ["PageDown"] = 0x22, ["Insert"] = 0x2D, ["Delete"] = 0x2E,
        ["LeftShift"] = 0xA0, ["RightShift"] = 0xA1, ["LeftControl"] = 0xA2,
        ["RightControl"] = 0xA3, ["LeftAlt"] = 0xA4, ["RightAlt"] = 0xA5,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73, ["F5"] = 0x74,
        ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77, ["F9"] = 0x78, ["F10"] = 0x79,
        ["F11"] = 0x7A, ["F12"] = 0x7B
    };

    /// <summary>Mouse button codes, matching the Win32 values the backend expects.</summary>
    private static readonly Dictionary<string, uint> MouseButtons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Left"] = 0x01, ["Right"] = 0x02, ["Middle"] = 0x04, ["X1"] = 0x05, ["X2"] = 0x06
    };

    /// <summary>The key names the UI may offer, derived from the real mapping.</summary>
    public static IReadOnlyList<string> AvailableKeys { get; } = [.. KeyCodes.Keys.OrderBy(k => k)];

    /// <summary>The mouse buttons the UI may offer.</summary>
    public static IReadOnlyList<string> AvailableMouseButtons { get; } = [.. MouseButtons.Keys];

/// <summary>Reverse lookup so an existing event can display its key name.</summary>
    public static string ResolveKeyName(uint code)
    {
        foreach (var pair in KeyCodes)
        {
            if (pair.Value == code)
            {
                return pair.Key;
            }
        }

        return string.Empty;
    }

    /// <summary>Reverse lookup for mouse button codes.</summary>
    public static string ResolveMouseButtonName(uint code)
    {
        foreach (var pair in MouseButtons)
        {
            if (pair.Value == code)
            {
                return pair.Key;
            }
        }

        return string.Empty;
    }

    /// <summary>Resolves a key name to its virtual-key code, or 0 when unknown.</summary>

    public static uint ResolveKeyCode(string? keyName) =>
        keyName is not null && KeyCodes.TryGetValue(keyName, out var code) ? code : 0;

    /// <summary>Resolves a mouse button name to its code, or 0 when unknown.</summary>
    public static uint ResolveMouseButton(string? buttonName) =>
        buttonName is not null && MouseButtons.TryGetValue(buttonName, out var code) ? code : 0;
    /// <summary>The backend identifier for a number-pad slot.</summary>
    public static MacroIdentifier SlotIdentifier(int slot) =>
        new(MacroSource.Keyboard, Convert.ToUInt64(slot.ToString(), 16));

    /// <summary>The real identifier behind one of the exposed slots.</summary>
    public MacroIdentifier IdentifierFor(int slot) =>
        MacroKeys.FirstOrDefault(k => k.KeyNumber == slot)?.Identifier ?? SlotIdentifier(slot);

    /// <summary>Reads a slot's real sequence out of the backend.</summary>
    public void LoadSlot(int slot)
    {
        SelectedKeyNumber = slot;

        var key = MacroKeys.FirstOrDefault(k => k.KeyNumber == slot);

        if (key is null || _controller is null)
        {
            return;
        }

        Apply(key, _controller.GetSequences().GetValueOrDefault(key.Identifier));
    }

    /// <summary>
    /// Writes the edited slots back through the backend, which owns persistence.
    /// </summary>
    public Task SaveAsync(IEnumerable<KeyValuePair<MacroIdentifier, MacroSequence>> sequences)
    {
        if (_controller is null)
        {
            return Task.CompletedTask;
        }

        var updated = _controller.GetSequences();

        foreach (var entry in sequences)
        {
            updated[entry.Key] = entry.Value;
        }

        _controller.SetSequences(updated);

        return Task.CompletedTask;
    }

    public Task PlayMacroAsync(int slotNumber)
    {
        // The backend owns playback through its hook and settings; nothing to run here.
        LoadSlot(slotNumber);
        return Task.CompletedTask;
    }

    public Task StartRecordingAsync(int keyNumber)
    {
        if (_controller is null)
        {
            return Task.CompletedTask;
        }

        LoadSlot(keyNumber);

        _controller.StartRecording();
        _isRecording = true;

        RecordingStateChanged?.Invoke(true);

        return Task.CompletedTask;
    }

    public Task StopRecordingAsync()
    {
        if (_controller is null)
        {
            return Task.CompletedTask;
        }

        _controller.StopRecording();
        _isRecording = false;

        RecordingStateChanged?.Invoke(false);

        return Task.CompletedTask;
    }

    private void OnRecorderReceived(object? sender, MacroController.RecorderReceivedEventArgs e)
    {
        LoadSlot(SelectedKeyNumber);

        var key = MacroKeys.FirstOrDefault(k => k.KeyNumber == SelectedKeyNumber);

        if (key is not null && !e.MacroEvent.IsUndefined())
        {
            key.Events.Add(e.MacroEvent);
        }

        MacroKeyChanged?.Invoke(key!);
    }

    private void OnRecorderStopped(object? sender, MacroController.RecorderStoppedEventArgs e)
    {
        _isRecording = false;
        RecordingStateChanged?.Invoke(false);
    }

    /// <summary>Applies a backend sequence, including the playback options WPF exposes.</summary>
    private static void Apply(MacroKey key, MacroSequence? sequence)
    {
        key.Events = ToEvents(sequence);
        key.RepeatCount = Math.Clamp(sequence?.RepeatCount ?? 1, 1, 10);
        key.IgnoreDelays = sequence?.IgnoreDelays ?? false;
        key.InterruptOnOtherKey = sequence?.InterruptOnOtherKey ?? false;
    }
    /// <summary>Projects a backend sequence onto the real event objects the UI binds to.</summary>
    private static ObservableCollection<MacroEvent> ToEvents(MacroSequence? sequence) =>
        [.. sequence?.Events ?? []];

    /// <summary>Rebuilds a backend sequence from the edited events.</summary>
    private static MacroSequence FromEvents(MacroKey key) => new()
    {
        // WPF writes the edited options back; resetting them to constants here would
        // silently discard the user's repeat/delay/interrupt settings on save.
        RepeatCount = Math.Clamp(key.RepeatCount, 1, 10),
        IgnoreDelays = key.IgnoreDelays,
        InterruptOnOtherKey = key.InterruptOnOtherKey,
        Events = [.. key.Events]
    };

    private void OnPropertyChanged(string name)
    {
        // View-model notification is raised through the dispatcher so binding updates
        // never occur on the backend's recorder thread.
        _ = _dispatcher.InvokeAsync(() => EnabledChanged?.Invoke());
    }

    private event Action? EnabledChanged;
}
