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

        }

        var sequences = _controller.GetSequences();

        // Seed the ten slots so the number pad is bound to real identifiers rather than
        // an Avalonia-only list. The page loads each slot's sequence on selection.
        for (var slot = 0; slot <= 9; slot++)
        {
            var identifier = SlotIdentifier(slot);

            MacroKeys.Add(new MacroKey
            {
                KeyNumber = slot,
                Name = slot.ToString(),
                Identifier = identifier,
                Events = ToEvents(sequences.GetValueOrDefault(identifier))
            });
        }

        _controller.RecorderReceived += OnRecorderReceived;
        _controller.RecorderStopped += OnRecorderStopped;

        _initialised = true;
    }

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

        key.Events = ToEvents(_controller.GetSequences().GetValueOrDefault(key.Identifier));
    }

    /// <summary>
    /// Writes the edited slots back through the backend, which owns persistence.
    /// </summary>
    public Task SaveAsync()
    {
        if (_controller is null)
        {
            return Task.CompletedTask;
        }

        var sequences = _controller.GetSequences();

        foreach (var key in MacroKeys)
        {
            sequences[key.Identifier] = FromEvents(key);
        }

        _controller.SetSequences(sequences);

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

    /// <summary>Projects a backend sequence onto the real event objects the UI binds to.</summary>
    private static ObservableCollection<MacroEvent> ToEvents(MacroSequence? sequence) =>
        [.. sequence?.Events ?? []];

    /// <summary>Rebuilds a backend sequence from the edited events.</summary>
    private static MacroSequence FromEvents(MacroKey key) => new()
    {
        RepeatCount = 1,
        IgnoreDelays = false,
        InterruptOnOtherKey = false,
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
