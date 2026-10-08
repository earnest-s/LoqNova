using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Macro;

namespace LoqNova.Avalonia.ViewModels.Pages;

public partial class MacroViewModel : ViewModelBase, INavigationAware
{
    private readonly IMacroService _macroService;
    
    [ObservableProperty]
    private bool _isEnabled = true;
    
    [ObservableProperty]
    private int _selectedKeyNumber = 1;
    
    [ObservableProperty]
    private bool _isRecording = false;
    
    public ObservableCollection<MacroKeyViewModel> MacroKeys { get; } = new();
    
    public MacroViewModel(IMacroService macroService)
    {
        _macroService = macroService;
        
        SubscribeToEvents();
        LoadMacroKeys();

        // WPF's number pad initializes the last button it finds, which is slot 0,
        // then calls Reload for it. Opening on slot 0 keeps the editor visible
        // instead of leaving the page with no selection at all.
        _selectedKeyNumber = 0;
        _macroService.SelectedKeyNumber = 0;
        SelectKey("0");
    }
    
    /// <summary>
    /// Seeds the slots from the backend before the page is shown. The service ignores
    /// repeat calls, so returning to the page does not reload the controller.
    /// </summary>
    async Task INavigationAware.OnNavigatedToAsync()
    {
        await _macroService.InitializeAsync();
        LoadMacroKeys();
        SelectKey(SelectedKeyNumber.ToString());
    }

    private void SubscribeToEvents()
    {
        _macroService.MacroKeyChanged += key => 
        {
            var existing = MacroKeys.FirstOrDefault(k => k.KeyNumber == key.KeyNumber);
            if (existing != null)
            {
                existing.UpdateFromModel(key);
            }
            else
            {
                existing = new MacroKeyViewModel(key);
                MacroKeys.Add(existing);
            }

            // The backend loads slots lazily, so the default slot can arrive after
            // construction. Keep the editor bound to whatever the user picked.
            if (existing.KeyNumber == SelectedKeyNumber)
            {
                SelectedKeyViewModel = existing;
            }

            SyncCursorPosition(key);
        };
        
        _macroService.RecordingStateChanged += recording => IsRecording = recording;
    }
    
    private void LoadMacroKeys()
    {
        MacroKeys.Clear();
        foreach (var key in _macroService.MacroKeys)
        {
            var view = new MacroKeyViewModel(key);
            MacroKeys.Add(view);
            SyncCursorPosition(key);
        }
    }

    /// <summary>
    /// Remembers where the pointer was last seen so an appended mouse event lands
    /// under the cursor instead of at the origin.
    /// </summary>
    private void SyncCursorPosition(MacroKey key)
    {
        foreach (var evt in key.Events.Reverse())
        {
            if (evt.Source != MacroSource.Mouse)
            {
                continue;
            }

            var view = MacroKeys.FirstOrDefault(k => k.KeyNumber == key.KeyNumber);
            view?.SetCursorPosition(evt.Point.X, evt.Point.Y);
            return;
        }
    }

    /// <summary>
    /// Selects a number-pad slot and loads its real sequence from the backend. WPF
    /// does this in NumberPadButton_Click -> Reload, which builds
    /// <c>MacroIdentifier(MacroSource.Keyboard, key)</c> and reads GetSequences().
    /// <para>
    /// The parameter is a string because Avalonia XAML compiles
    /// <c>CommandParameter="1"</c> to a string. Declaring this as int made every
    /// number-pad button throw ArgumentException the moment the view's bindings
    /// activated, which aborted the navigation and left the page blank.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void SelectKey(string keyNumber)
    {
        if (!int.TryParse(keyNumber, out var slot))
        {
            return;
        }

        SelectedKeyNumber = slot;

        _macroService.LoadSlot(slot);

        var model = _macroService.MacroKeys.FirstOrDefault(k => k.KeyNumber == slot);

        if (model is not null)
        {
            var view = MacroKeys.FirstOrDefault(k => k.KeyNumber == slot);

            if (view is not null)
            {
                view.UpdateFromModel(model);
                SelectedKeyViewModel = view;
            }
        }
    }

    /// <summary>
    /// The slot currently open in the editor. The page binds this name, so the editor
    /// card stays hidden unless a slot has actually been loaded.
    /// </summary>
    [ObservableProperty]
    private MacroKeyViewModel? _selectedMacroKey;

    /// <summary>Alias the page binds to.</summary>
    public MacroKeyViewModel? SelectedKeyViewModel
    {
        get => SelectedMacroKey;
        set
        {
            SelectedMacroKey = value;
        }
    }
    
    partial void OnIsEnabledChanged(bool value)
    {
        _macroService.IsEnabled = value;
    }
    
    partial void OnSelectedKeyNumberChanged(int value)
    {
        _macroService.SelectedKeyNumber = value;
    }
    
    [RelayCommand]
    private async Task StartRecordingAsync()
    {
        await _macroService.StartRecordingAsync(SelectedKeyNumber);
    }
    
    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        await _macroService.StopRecordingAsync();
    }
    
    [RelayCommand]
    private async Task PlayMacroAsync(string keyNumber)
    {
        if (int.TryParse(keyNumber, out var slot))
        {
            await _macroService.PlayMacroAsync(slot);
        }
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        // The editor mutates view models, so the sequences have to be rebuilt from
        // them. Saving straight from the service's models would write back the
        // untouched backend lists and silently discard every edit.
        var sequences = MacroKeys.Select(key => new KeyValuePair<MacroIdentifier, MacroSequence>(key.Identifier, key.ToSequence())).ToArray();

        await _macroService.SaveAsync(sequences);
    }
}


public partial class MacroKeyViewModel : ViewModelBase
{
    public int KeyNumber { get; private set; }

    public MacroIdentifier Identifier { get; private set; }

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _enabled = true;

    // Playback options are owned by the backend sequence, but the user edits them
    // here, so they are carried through to save instead of being reset.
[ObservableProperty]
    private int _repeatCount = 1;

    [ObservableProperty]
    private bool _ignoreDelays = false;

    [ObservableProperty]
    private bool _interruptOnOtherKey = false;

    // The editor picks the key/button to append before pressing Add, the same way
    // WPF asks for one before adding it to the sequence.
    public IReadOnlyList<string> AvailableKeys => RealMacroService.AvailableKeys;

    public IReadOnlyList<string> AvailableMouseButtons => RealMacroService.AvailableMouseButtons;

    [ObservableProperty]
    private string _pendingKey = "A";

    [ObservableProperty]
    private string _pendingMouseButton = "Left";

    public ObservableCollection<MacroEventViewModel> Events { get; } = new();

    public MacroKeyViewModel(MacroKey model)
    {
        Apply(model);
    }

    private void Apply(MacroKey model)
    {
        KeyNumber = model.KeyNumber;
        Identifier = model.Identifier;
        Name = model.Name;
        Enabled = model.HasEvents;
        RepeatCount = Math.Clamp(model.RepeatCount, 1, 10);
        IgnoreDelays = model.IgnoreDelays;
        InterruptOnOtherKey = model.InterruptOnOtherKey;

        Events.Clear();
        foreach (var evt in model.Events)
        {
            Events.Add(CreateEventViewModel(evt));
        }
    }

    public void UpdateFromModel(MacroKey model) => Apply(model);

    private static MacroEventViewModel CreateEventViewModel(MacroEvent model) =>
        // The backend models keyboard and mouse events as one struct,
        // distinguished by Source. There is no subclass to switch on.
        model.Source == MacroSource.Mouse
            ? new MacroMouseEventViewModel(model)
            : new MacroKeyEventViewModel(model);

    /// <summary>Rebuilds the backend sequence from the edited events.</summary>
    public MacroSequence ToSequence() => new()
    {
        RepeatCount = Math.Clamp(RepeatCount, 1, 10),
        IgnoreDelays = IgnoreDelays,
        InterruptOnOtherKey = InterruptOnOtherKey,
        Events = [.. Events.Select(e => e.ToModel())]
    };

[RelayCommand]
    private void AddKeyEvent()
    {
        Events.Add(new MacroKeyEventViewModel(new MacroEvent
        {
            Source = MacroSource.Keyboard,
            Direction = MacroDirection.Down,
            Key = RealMacroService.ResolveKeyCode(PendingKey),
            Delay = TimeSpan.Zero
        }));
    }

    [RelayCommand]
    private void AddMouseEvent()
    {
        // Mouse events reuse Key for the button code, as the backend does.
        Events.Add(new MacroMouseEventViewModel(new MacroEvent
        {
            Source = MacroSource.Mouse,
            Direction = MacroDirection.Down,
            Key = RealMacroService.ResolveMouseButton(PendingMouseButton),
            Point = new Point(CursorX, CursorY),
            Delay = TimeSpan.Zero
        }));
    }

    /// <summary>
    /// Last cursor position observed by the recorder, so an appended mouse event
    /// lands where the pointer actually is instead of at the origin.
    /// </summary>
    public int CursorX { get; private set; }

    public int CursorY { get; private set; }

    public void SetCursorPosition(int x, int y)
    {
        CursorX = x;
        CursorY = y;
    }

    [RelayCommand]
    private void RemoveEvent(MacroEventViewModel evt)
    {
        if (evt is not null)
        {
            Events.Remove(evt);
        }
    }

    [RelayCommand]
    private void ClearEvents() => Events.Clear();
}

public abstract partial class MacroEventViewModel : ViewModelBase
{
    public abstract string TypeName { get; }

    public abstract string DisplayText { get; }

    /// <summary>
    /// MacroEvent is an immutable readonly struct, so each save rebuilds the backend
    /// event from the current view state rather than mutating a cached instance.
    /// </summary>
    public abstract MacroEvent ToModel();

    protected void ApplyDelay(double milliseconds) =>
        DelayMs = milliseconds;

    [ObservableProperty]
    protected double _delayMs = 0;
}

public partial class MacroKeyEventViewModel : MacroEventViewModel
{
    public override string TypeName => "Key";

    public IReadOnlyList<string> AvailableKeys => RealMacroService.AvailableKeys;

    public override string DisplayText => $"{Key} {(IsPress ? "Down" : "Up")}";

    public MacroKeyEventViewModel(MacroEvent model)
    {
        _key = RealMacroService.ResolveKeyName(model.Key);
        _isPress = model.Direction == MacroDirection.Down;
        _delayMs = model.Delay.TotalMilliseconds;
    }

    [ObservableProperty]
    private string _key = "";

    [ObservableProperty]
    private bool _isPress = true;

    public override MacroEvent ToModel() => new()
    {
        Source = MacroSource.Keyboard,
        Direction = IsPress ? MacroDirection.Down : MacroDirection.Up,
        Key = RealMacroService.ResolveKeyCode(Key),
        Delay = TimeSpan.FromMilliseconds(DelayMs)
    };
}
public partial class MacroMouseEventViewModel : MacroEventViewModel
{
    public override string TypeName => "Mouse";

    public IReadOnlyList<string> AvailableMouseButtons => RealMacroService.AvailableMouseButtons;

    public override string DisplayText => $"Mouse {Button} {(IsPress ? "Down" : "Up")} at ({X}, {Y})";

    public MacroMouseEventViewModel(MacroEvent model)
    {
        _x = model.Point.X;
        _y = model.Point.Y;
        _button = RealMacroService.ResolveMouseButtonName(model.Key);
        _isPress = model.Direction == MacroDirection.Down;
        _delayMs = model.Delay.TotalMilliseconds;
    }

    [ObservableProperty]
    private int _x = 0;

    [ObservableProperty]
    private int _y = 0;

    [ObservableProperty]
    private string _button = "Left";

    [ObservableProperty]
    private bool _isPress = true;

    public override MacroEvent ToModel() => new()
    {
        Source = MacroSource.Mouse,
        Direction = IsPress ? MacroDirection.Down : MacroDirection.Up,
        Key = RealMacroService.ResolveMouseButton(Button),
        Point = new Point(X, Y),
        Delay = TimeSpan.FromMilliseconds(DelayMs)
    };
}