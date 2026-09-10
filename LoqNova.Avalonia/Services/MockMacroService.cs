using System;
using System.Collections.ObjectCollection;
using System.Linq;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class MockMacroService : IMacroService
{
    public bool IsEnabled { get; set; } = true;
    
    public ObservableCollection<MacroKey> MacroKeys { get; } = new()
    {
        new MacroKey
        {
            KeyNumber = 1,
            Name = "Gaming Combo",
            Enabled = true,
            Events = new ObservableCollection<MacroEvent>
            {
                new MacroKeyEvent { Key = "W", IsPress = true, DelayMs = 0 },
                new MacroKeyEvent { Key = "W", IsPress = false, DelayMs = 100 },
                new MacroKeyEvent { Key = "Shift", IsPress = true, DelayMs = 50 },
                new MacroKeyEvent { Key = "Space", IsPress = true, DelayMs = 0 },
                new MacroKeyEvent { Key = "Space", IsPress = false, DelayMs = 200 }
            }
        },
        new MacroKey
        {
            KeyNumber = 2,
            Name = "Build Macro",
            Enabled = true,
            Events = new ObservableCollection<MacroEvent>
            {
                new MacroKeyEvent { Key = "Q", IsPress = true, DelayMs = 0 },
                new MacroKeyEvent { Key = "Q", IsPress = false, DelayMs = 50 },
                new MacroKeyEvent { Key = "LeftClick", IsPress = true, DelayMs = 0 },
                new MacroKeyEvent { Key = "LeftClick", IsPress = false, DelayMs = 100 }
            }
        }
    };
    
    public int SelectedKeyNumber { get; set; } = 1;
    public bool IsRecording { get; private set; } = false;
    
    public event Action<MacroKey>? MacroKeyChanged;
    public event Action<bool>? RecordingStateChanged;
    
    public Task InitializeAsync() => Task.CompletedTask;
    
    public Task StartRecordingAsync(int keyNumber)
    {
        SelectedKeyNumber = keyNumber;
        IsRecording = true;
        RecordingStateChanged?.Invoke(true);
        
        var key = MacroKeys.FirstOrDefault(k => k.KeyNumber == keyNumber);
        if (key != null)
        {
            key.Events.Clear();
        }
        return Task.CompletedTask;
    }
    
    public Task StopRecordingAsync()
    {
        IsRecording = false;
        RecordingStateChanged?.Invoke(false);
        return Task.CompletedTask;
    }
    
    public Task PlayMacroAsync(int keyNumber)
    {
        var key = MacroKeys.FirstOrDefault(k => k.KeyNumber == keyNumber);
        if (key != null)
        {
            // Simulate playback
        }
        return Task.CompletedTask;
    }
    
    public Task SaveAsync() => Task.CompletedTask;
}