using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Extensions;
using LoqNova.Lib.Macro;

namespace LoqNova.Avalonia.ViewModels.Pages;

/// <summary>
/// The Macro page. This reproduces the behaviour of WPF's <c>MacroPage</c> and its
/// <c>MacroSequenceControl</c> child:
/// <list type="bullet">
/// <item>an Enable toggle bound to <c>MacroController.IsEnabled</c>;</item>
/// <item>a number pad whose buttons are the virtual-key codes 0x67-0x60, selecting the
/// sequence bound to <c>MacroIdentifier(MacroSource.Keyboard, vk)</c>;</item>
/// <item>Repeat / Ignore delays / Interrupt options that save the moment they change;</item>
/// <item>a recording-options selector read when Record is pressed;</item>
/// <item>read-only event cards, where consecutive Move events share one card;</item>
/// <item>a Clear button that only exists while the sequence has events.</item>
/// </list>
/// There is no Save button and no Back button in WPF: every change persists immediately
/// through the controller, so neither is invented here.
/// </summary>
public partial class MacroViewModel : ViewModelBase, INavigationAware
{
    private readonly IMacroService _macroService;

    /// <summary>
    /// Mirrors WPF's <c>_isRefreshing</c>. While a sequence is being loaded into the
    /// controls, the option setters must not treat the change as a user edit and write
    /// it straight back.
    /// </summary>
    private bool _isRefreshing;

    private bool _suppressOptionSave;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private MacroPadKey? _selectedPadKey;

    [ObservableProperty]
    private bool _hasEvents;

    [ObservableProperty]
    private int _repeatCount = 1;

    [ObservableProperty]
    private bool _ignoreDelays;

    [ObservableProperty]
    private bool _interruptOnOtherKey;

    [ObservableProperty]
    private MacroRecorderSettings _recorderSettings = MacroRecorderSettings.Keyboard;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private RecordingPhase _recordingPhase = RecordingPhase.None;

    /// <summary>True during the three-second countdown shown before a recording.</summary>
    public bool IsPreparing => RecordingPhase == RecordingPhase.Preparing;

    /// <summary>True once the recorder is capturing.</summary>
    public bool IsCapturing => RecordingPhase == RecordingPhase.Recording;

    /// <summary>
    /// WPF sets <c>Mouse.OverrideCursor = Cursors.Wait</c> while recording and clears it
    /// in Save. Avalonia has no global override, so the page binds this instead.
    /// </summary>
    [ObservableProperty]
    private bool _isWaitCursor;

    partial void OnRecordingPhaseChanged(RecordingPhase value)
    {
        OnPropertyChanged(nameof(IsPreparing));
        OnPropertyChanged(nameof(IsCapturing));
    }

    /// <summary>The event cards shown for the selected key, newest last.</summary>
    public ObservableCollection<MacroEventCardViewModel> EventCards { get; } = new();

    /// <summary>
    /// The number pad, in the exact order and grid position WPF declares it:
    /// 7 8 9 / 4 5 6 / 1 2 3 / _ 0 _
    /// </summary>
    public ObservableCollection<MacroPadKey?> PadKeys { get; } = new();

    public IReadOnlyList<MacroRecorderOption> RecorderOptions { get; } =
    [
        new(MacroRecorderSettings.Keyboard, "Keyboard only"),
        new(MacroRecorderSettings.Keyboard | MacroRecorderSettings.Mouse, "Keyboard keys and mouse buttons"),
        new(MacroRecorderSettings.Keyboard | MacroRecorderSettings.Mouse | MacroRecorderSettings.Movement, "All inputs")
    ];

    /// <summary>
    /// Repeat counts from <c>MacroController.AllowedRepeatCounts</c>. WPF boxes each
    /// value for display and labels 1 as "Don't repeat"; selection is by value, which is
    /// what <c>SelectedValue</c> with a value binding reproduces.
    /// </summary>
    public ObservableCollection<MacroRepeatOption> RepeatOptions { get; } = new();

    public MacroViewModel(IMacroService macroService)
    {
        _macroService = macroService;

        BuildPad();

        _macroService.RecorderReceived += OnRecorderReceived;
        _macroService.RecorderStopped += OnRecorderStopped;
    }

    /// <summary>Loads the backend before the page is shown.</summary>
    async Task INavigationAware.OnNavigatedToAsync()
    {
        await _macroService.InitializeAsync();

        // Same values and the same "Don't repeat" label WPF builds in
        // MacroSequenceControl.Set via ComboBoxExtensions.SetItems.
        if (RepeatOptions.Count == 0)
        {
            foreach (var count in _macroService.AllowedRepeatCounts)
            {
                RepeatOptions.Add(new MacroRepeatOption(count, count == 1 ? "Don't repeat" : count.ToString()));
            }
        }

        // WPF assigns _enableMacroToggle.IsChecked directly, which does not raise Click
        // and therefore does not write back. The same guard keeps opening the page from
        // persisting the value it just read.
        _suppressOptionSave = true;

        try
        {
            IsEnabled = _macroService.IsEnabled;
        }
        finally
        {
            _suppressOptionSave = false;
        }

        // WPF selects the last button in the number pad, which is key "0" (0x60).
        var zeroKey = PadKeys.FirstOrDefault(k => k?.Label == "0");

        if (zeroKey is not null)
        {
            SelectPadKey(zeroKey);
        }
    }

    /// <summary>
    /// Builds the pad from the controller's virtual-key codes. The layout is positional:
    /// WPF uses a 3-column grid whose last child is the zero key, so the empty cells are
    /// kept as nulls instead of being compacted away.
    /// </summary>
    private void BuildPad()
    {
        var codes = RealMacroService.PadKeyCodes;

        PadKeys.Add(new MacroPadKey("7", codes[0]));
        PadKeys.Add(new MacroPadKey("8", codes[1]));
        PadKeys.Add(new MacroPadKey("9", codes[2]));
        PadKeys.Add(new MacroPadKey("4", codes[3]));
        PadKeys.Add(new MacroPadKey("5", codes[4]));
        PadKeys.Add(new MacroPadKey("6", codes[5]));
        PadKeys.Add(new MacroPadKey("1", codes[6]));
        PadKeys.Add(new MacroPadKey("2", codes[7]));
        PadKeys.Add(new MacroPadKey("3", codes[8]));
        PadKeys.Add(null);
        PadKeys.Add(new MacroPadKey("0", codes[9]));
        PadKeys.Add(null);
    }

    /// <summary>Selects a number-pad key and loads the real sequence behind it.</summary>
    [RelayCommand]
    private void SelectPadKey(MacroPadKey key)
    {
        if (key is null)
        {
            return;
        }

        foreach (var padKey in PadKeys)
        {
            if (padKey is not null)
            {
                padKey.IsSelected = padKey == key;
            }
        }

        SelectedPadKey = key;

        Reload(key.Identifier);
    }

    /// <summary>
    /// Reproduces WPF's <c>Reload</c> plus <c>MacroSequenceControl.Set</c>: stop any
    /// recording, then push the stored sequence into the option controls and rebuild the
    /// event cards.
    /// </summary>
    private void Reload(MacroIdentifier identifier)
    {
        _isRefreshing = true;
        _suppressOptionSave = true;

        try
        {
            _macroService.StopRecording();

            var sequence = _macroService.GetSequence(identifier);
            var events = sequence.Events ?? [];

            HasEvents = events.Length > 0;

            RepeatCount = Math.Clamp(sequence.RepeatCount, 1, 10);
            IgnoreDelays = sequence.IgnoreDelays;
            InterruptOnOtherKey = sequence.InterruptOnOtherKey;

            // WPF re-runs SetItems for the recording options on every Set, with
            // Keyboard as the default, so switching key or saving resets the selector.
            RecorderSettings = MacroRecorderSettings.Keyboard;

            EventCards.Clear();

            foreach (var macroEvent in events)
            {
                AddEventCard(macroEvent);
            }

            // WPF's Set ends with Mouse.OverrideCursor = null and Record re-enabled.
            IsWaitCursor = false;
        }
        finally
        {
            _suppressOptionSave = false;
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// Builds a display row from a raw event. Consecutive movements merge into one row
    /// and a key release that closes an open press merges into that press row, so a
    /// normal key press is shown once with its hold time. Rows keep every raw event they
    /// represent, so saving still writes the sequence back unchanged.
    /// </summary>
    private void AddEventCard(MacroEvent macroEvent)
    {
        if (macroEvent.Direction == MacroDirection.Move)
        {
            if (EventCards.LastOrDefault() is { Kind: MacroRowKind.Movement } movement)
            {
                movement.Add(macroEvent);
                return;
            }

            EventCards.Add(new MacroEventCardViewModel(MacroRowKind.Movement, macroEvent));
            return;
        }

        if (macroEvent.Direction == MacroDirection.Up &&
            EventCards.LastOrDefault() is { Kind: MacroRowKind.KeyPress } press &&
            press.Closes(macroEvent))
        {
            press.Add(macroEvent);
            return;
        }

        var kind = macroEvent.Direction == MacroDirection.Down
            ? MacroRowKind.KeyPress
            : MacroRowKind.Standalone;

        EventCards.Add(new MacroEventCardViewModel(kind, macroEvent));
    }

    /// <summary>
    /// Writes the current controls back to the backend, then reloads so the UI reflects
    /// whatever the controller's CleanUp decided to keep.
    /// </summary>
    private void Save()
    {
        if (_isRefreshing || SelectedPadKey is null)
        {
            return;
        }

        _macroService.SetSequence(SelectedPadKey.Identifier, new MacroSequence
        {
            RepeatCount = Math.Clamp(RepeatCount, 1, 10),
            IgnoreDelays = IgnoreDelays,
            InterruptOnOtherKey = InterruptOnOtherKey,
            Events = [.. EventCards.SelectMany(card => card.Events)]
        });

        Reload(SelectedPadKey.Identifier);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressOptionSave)
        {
            return;
        }

        _macroService.SetEnabled(value);
    }

    partial void OnRepeatCountChanged(int value)
    {
        if (!_suppressOptionSave)
        {
            Save();
        }
    }

    partial void OnIgnoreDelaysChanged(bool value)
    {
        if (!_suppressOptionSave)
        {
            Save();
        }
    }

    partial void OnInterruptOnOtherKeyChanged(bool value)
    {
        if (!_suppressOptionSave)
        {
            Save();
        }
    }

    /// <summary>
    /// Empties the event cards and then saves, which is what WPF's Clear does: the
    /// panel is cleared first and Save then writes an event-less sequence, so the
    /// backend's ClearEmptySequences removes the key entirely.
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        EventCards.Clear();
        HasEvents = false;

        Save();
    }

    /// <summary>
    /// Starts a recording. WPF clears the event list first, so recording replaces the
    /// previous sequence, and shows a three-second countdown when movement will be
    /// captured.
    /// </summary>
    [RelayCommand]
    private async Task RecordAsync()
    {
        // WPF reads the combo when Record is pressed; the combo itself has no handler.
        var settings = RecorderSettings;

        EventCards.Clear();

        // WPF collapses the Clear button for the duration of the recording.
        HasEvents = false;

        IsWaitCursor = true;
        IsRecording = true;

        if (settings.HasFlag(MacroRecorderSettings.Mouse) && settings.HasFlag(MacroRecorderSettings.Movement))
        {
            RecordingPhase = RecordingPhase.Preparing;

            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        RecordingPhase = RecordingPhase.Recording;

        _macroService.StartRecording(settings);
    }

    /// <summary>Stops recording early; the events captured so far are kept.</summary>
    [RelayCommand]
    private void StopRecording()
    {
        _macroService.StopRecording();
    }

    private void OnRecorderReceived(MacroEvent macroEvent) => AddEventCard(macroEvent);

    /// <summary>
    /// The recorder stopped on its own, either because ESC was pressed or because the
    /// session was interrupted. WPF discards a partial recording when the backend reports
    /// Interrupted, and keeps it otherwise.
    /// </summary>
    private void OnRecorderStopped(bool interrupted)
    {
        IsRecording = false;
        RecordingPhase = RecordingPhase.None;

        if (interrupted)
        {
            EventCards.Clear();
        }

        Save();
    }
}

public enum RecordingPhase
{
    None,
    Preparing,
    Recording
}

/// <summary>One number-pad key: a label and the virtual-key code WPF stores in its Tag.</summary>
public partial class MacroPadKey : ObservableObject
{
    public MacroPadKey(string label, ulong virtualKey)
    {
        Label = label;
        VirtualKey = virtualKey;
    }

    public string Label { get; }

    public ulong VirtualKey { get; }

    /// <summary>The real identifier the backend keys this macro by.</summary>
    public MacroIdentifier Identifier => new(MacroSource.Keyboard, VirtualKey);

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>An entry in the recording-options selector.</summary>
public sealed record MacroRecorderOption(MacroRecorderSettings Value, string Label);

/// <summary>
/// A repeat count with its display text. WPF boxes each allowed count through
/// ComboBoxExtensions.SetItems and shows 1 as "Don't repeat", selecting by value.
/// </summary>
public sealed record MacroRepeatOption(int Value, string Label);

/// <summary>
/// A read-only event card, matching WPF's <c>AbstractMacroEventControl</c>. The cards are
/// never edited in WPF; the events they hold are collected again on save.
/// </summary>
public sealed partial class MacroEventCardViewModel : ViewModelBase
{
    private readonly List<MacroEvent> _events = [];

    public MacroEventCardViewModel(MacroEvent macroEvent, bool isMovement)
    {
        IsMovement = isMovement;
        _events.Add(macroEvent);
        Refresh();
    }

    /// <summary>True for a card that aggregates consecutive mouse-movement events.</summary>
    public bool IsMovement { get; }

    /// <summary>The real events behind this card, in capture order.</summary>
    public IReadOnlyList<MacroEvent> Events => _events;

    /// <summary>Summed delay across the events, which is what a movement card shows.</summary>
    public TimeSpan TotalDelay => _events.Aggregate(TimeSpan.Zero, (total, e) => total + e.Delay);

    public string Title { get; private set; } = "";

    public string Subtitle { get; private set; } = "";

    /// <summary>Direction glyph, chosen as WPF chooses its card icon.</summary>
    public string Icon { get; private set; } = "";

    /// <summary>Folds another movement event into this card.</summary>
    public void Add(MacroEvent macroEvent)
    {
        _events.Add(macroEvent);
        Refresh();
    }

    private void Refresh()
    {
        var macroEvent = _events[^1];

        Icon = macroEvent.Direction switch
        {
            MacroDirection.Up => "ArrowUp",
            MacroDirection.Down => "ArrowDown",
            MacroDirection.Wheel => "Rotate",
            MacroDirection.HorizontalWheel => "Rotate",
            MacroDirection.Move => "Move",
            _ => "None"
        };

        // The same discrimination WPF performs in AbstractMacroEventControl.Set.
        Title = (macroEvent.Source, macroEvent.Direction, macroEvent.Key) switch
        {
            (MacroSource.Keyboard, _, _) => RealMacroService.KeyName(macroEvent.Key),
            (MacroSource.Mouse, MacroDirection.Move, _) => "MOVE",
            (MacroSource.Mouse, MacroDirection.Wheel, >= 0x80000000) => "WHEEL DOWN",
            (MacroSource.Mouse, MacroDirection.Wheel, _) => "WHEEL UP",
            (MacroSource.Mouse, MacroDirection.HorizontalWheel, >= 0x80000000) => "WHEEL LEFT",
            (MacroSource.Mouse, MacroDirection.HorizontalWheel, _) => "WHEEL RIGHT",
            (MacroSource.Mouse, _, >= 0xFF) => "XBUTTON" + (macroEvent.Key >> 16),
            (MacroSource.Mouse, _, 1) => "LBUTTON",
            (MacroSource.Mouse, _, 2) => "RBUTTON",
            (MacroSource.Mouse, _, 3) => "MBUTTON",
            (MacroSource.Mouse, _, _) => "BUTTON" + macroEvent.Key,
            _ => string.Empty
        };

        Subtitle = $"{macroEvent.Source.GetDisplayName()} • {FormatDelay(TotalDelay)}";

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(TotalDelay));
    }

    /// <summary>Formats a delay the way Humanizer's ms Humanize does, which WPF uses.</summary>
    private static string FormatDelay(TimeSpan delay)
    {
        var ms = (int)delay.TotalMilliseconds;

        return ms switch
        {
            0 => "0 ms",
            1 => "1 millisecond",
            < 1000 => $"{ms} milliseconds",
            _ => FormatSeconds(delay)
        };
    }

    private static string FormatSeconds(TimeSpan delay)
    {
        var seconds = delay.TotalSeconds;

        return seconds < 2 ? "1 second" : $"{seconds:0.##} seconds";
    }
}