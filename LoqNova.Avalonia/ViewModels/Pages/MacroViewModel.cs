using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

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
        _enabled = model.Enabled;
        
        foreach (var evt in model.Events)
        {
            Events.Add(new MacroEventViewModel(evt));
        }
    }
    
    public void UpdateFromModel(MacroKey model)
    {
        Name = model.Name;
        Enabled = model.Enabled;
        Events.Clear();
        foreach (var evt in model.Events)
        {
            Events.Add(new MacroEventViewModel(evt));
        }
    }
    
    [RelayCommand]
    private void AddKeyEvent(string key)
    {
        var evt = new MacroKeyEvent { Key = key, IsPress = true, DelayMs = 0 };
        Events.Add(new MacroEventViewModel(evt));
    }
    
    [RelayCommand]
    private void AddMouseEvent(string button)
    {
        var evt = new MacroMouseEvent { Button = button, IsPress = true, DelayMs = 0 };
        Events.Add(new MacroEventViewModel(evt));
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
    private double _delayMs = 0;
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
        if (model is MacroKeyEvent keyEvent)
        {
            _key = keyEvent.Key;
            _isPress = keyEvent.IsPress;
            _delayMs = keyEvent.DelayMs;
        }
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
        if (model is MacroMouseEvent mouseEvent)
        {
            _x = mouseEvent.X;
            _y = mouseEvent.Y;
            _button = mouseEvent.Button;
            _isPress = mouseEvent.IsPress;
            _delayMs = mouseEvent.DelayMs;
        }
    }
}