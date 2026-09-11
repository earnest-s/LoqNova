using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

public partial class MacroRecordingViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _keyNumber = 1;
    
    [ObservableProperty]
    private bool _isRecording = true;
    
    [ObservableProperty]
    private TimeSpan _recordingTime = TimeSpan.Zero;
    
    [ObservableProperty]
    private int _eventCount = 0;
    
    [ObservableProperty]
    private string _lastKey = "";
    
    private System.Timers.Timer? _timer;
    
    public MacroRecordingViewModel()
    {
        _timer = new System.Timers.Timer(100);
        _timer.Elapsed += (_, _) => 
        {
            RecordingTime = RecordingTime.Add(TimeSpan.FromMilliseconds(100));
        };
        _timer.Start();
    }
    
    public void AddEvent(string key)
    {
        LastKey = key;
        EventCount++;
    }
    
    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        IsRecording = false;
        _timer?.Stop();
    }
    
    [RelayCommand]
    private async Task CloseAsync()
    {
        _timer?.Stop();
    }
}