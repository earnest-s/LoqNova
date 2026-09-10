using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public class MacroKey
{
    public int KeyNumber { get; set; }
    public string Name { get; set; } = "";
    public ObservableCollection<MacroEvent> Events { get; } = new();
    public bool Enabled { get; set; }
}

public abstract class MacroEvent
{
    public double DelayMs { get; set; }
}

public class MacroKeyEvent : MacroEvent
{
    public string Key { get; set; } = "";
    public bool IsPress { get; set; } = true;
}

public class MacroMouseEvent : MacroEvent
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Button { get; set; } = "Left";
    public bool IsPress { get; set; } = true;
}

public interface IMacroService
{
    bool IsEnabled { get; set; }
    ObservableCollection<MacroKey> MacroKeys { get; }
    int SelectedKeyNumber { get; set; }
    bool IsRecording { get; }
    
    event Action<MacroKey>? MacroKeyChanged;
    event Action<bool>? RecordingStateChanged;
    
    Task InitializeAsync();
    Task StartRecordingAsync(int keyNumber);
    Task StopRecordingAsync();
    Task PlayMacroAsync(int keyNumber);
    Task SaveAsync();
}