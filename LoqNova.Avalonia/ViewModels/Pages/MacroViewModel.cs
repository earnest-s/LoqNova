using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Macro;

namespace LoqNova.Avalonia.ViewModels.Pages;

public partial class MacroViewModel : ViewModelBase
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
                MacroKeys.Add(new MacroKeyViewModel(key));
            }
        };
        
        _macroService.RecordingStateChanged += recording => IsRecording = recording;
    }
    
    private void LoadMacroKeys()
    {
        MacroKeys.Clear();
        foreach (var key in _macroService.MacroKeys)
        {
            MacroKeys.Add(new MacroKeyViewModel(key));
        }
    }

    /// <summary>
    /// Selects a number-pad slot and loads its real sequence from the backend. WPF
    /// does this in NumberPadButton_Click -> Reload, which builds
    /// <c>MacroIdentifier(MacroSource.Keyboard, key)</c> and reads GetSequences().
    /// The page had no such command at all, so every number-pad button was inert.
    /// </summary>
    [RelayCommand]
    private void SelectKey(int keyNumber)
    {
        SelectedKeyNumber = keyNumber;

        _macroService.LoadSlot(keyNumber);

        var model = _macroService.MacroKeys.FirstOrDefault(k => k.KeyNumber == keyNumber);

        if (model is not null)
        {
            SelectedMacroKey = MacroKeys.FirstOrDefault(k => k.KeyNumber == keyNumber);
        }
    }

    /// <summary>The slot currently open in the editor.</summary>
    [ObservableProperty]
    private MacroKeyViewModel? _selectedMacroKey;
    
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
    private async Task PlayMacroAsync(int keyNumber)
    {
        await _macroService.PlayMacroAsync(keyNumber);
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        await _macroService.SaveAsync();
    }
}

public partial class MacroKeyViewModel : ViewModelBase
{
    public int KeyNumber { get; private set; }
    
    [ObservableProperty]
    private string _name = "";
    
    [ObservableProperty]
    private bool _enabled = true;
    
    public ObservableCollection<MacroEventViewModel> Events { get; } = new();
    
    public MacroKeyViewModel(MacroKey model)
    {
        KeyNumber = model.KeyNumber;
        _name = model.Name;
        _enabled = model.HasEvents;
        
        foreach (var evt in model.Events)
        {
            Events.Add(CreateEventViewModel(evt));
        }
    }
    
    private MacroEventViewModel CreateEventViewModel(MacroEvent model)
    {
        // The backend models keyboard and mouse events as one struct,
        // distinguished by Source. There is no subclass to switch on.
        return model.Source == MacroSource.Mouse
            ? new MacroMouseEventViewModel(model)
            : new MacroKeyEventViewModel(model);
    }
    
    public void UpdateFromModel(MacroKey model)
    {
        Name = model.Name;
        Enabled = model.HasEvents;
        Events.Clear();
        foreach (var evt in model.Events)
        {
            Events.Add(CreateEventViewModel(evt));
        }
    }
    
    [RelayCommand]
    private void AddKeyEvent(string key)
    {
        // Real backend event: keyboard source, Down direction, virtual-key code.
        var evt = new MacroEvent
        {
            Source = MacroSource.Keyboard,
            Direction = MacroDirection.Down,
            Key = RealMacroService.ResolveKeyCode(key),
            Delay = TimeSpan.Zero
        };
        Events.Add(new MacroKeyEventViewModel(evt));
    }
    
    [RelayCommand]
    private void AddMouseEvent(string button)
    {
        // Mouse events reuse Key for the button code, as the backend does.
        var evt = new MacroEvent
        {
            Source = MacroSource.Mouse,
            Direction = MacroDirection.Down,
            Key = RealMacroService.ResolveMouseButton(button),
            Delay = TimeSpan.Zero
        };
        Events.Add(new MacroMouseEventViewModel(evt));
    }
    
    [RelayCommand]
    private void RemoveEvent(MacroEventViewModel evt)
    {
        if (evt != null)
        {
            Events.Remove(evt);
        }
    }
}

public abstract partial class MacroEventViewModel : ViewModelBase
{
    public abstract string TypeName { get; }
    public abstract string DisplayText { get; }
    
    [ObservableProperty]
    protected double _delayMs = 0;
}

public partial class MacroKeyEventViewModel : MacroEventViewModel
{
    public override string TypeName => "Key";
    
    [ObservableProperty]
    private string _key = "";
    
    [ObservableProperty]
    private bool _isPress = true;
    
    public override string DisplayText => $"{Key} {(IsPress ? "Down" : "Up")}";
    
    public MacroKeyEventViewModel(MacroEvent model)
    {
        _key = model.Key.ToString();
        _isPress = model.Direction == MacroDirection.Down;
        _delayMs = model.Delay.TotalMilliseconds;
    }
}

public partial class MacroMouseEventViewModel : MacroEventViewModel
{
    public override string TypeName => "Mouse";
    
    [ObservableProperty]
    private int _x = 0;
    
    [ObservableProperty]
    private int _y = 0;
    
    [ObservableProperty]
    private string _button = "Left";
    
    [ObservableProperty]
    private bool _isPress = true;
    
    public override string DisplayText => $"Mouse {Button} {(IsPress ? "Down" : "Up")} at ({X}, {Y})";
    
    public MacroMouseEventViewModel(MacroEvent model)
    {
        _x = model.Point.X;
        _y = model.Point.Y;
        _button = model.Key.ToString();
        _isPress = model.Direction == MacroDirection.Down;
        _delayMs = model.Delay.TotalMilliseconds;
    }
}
