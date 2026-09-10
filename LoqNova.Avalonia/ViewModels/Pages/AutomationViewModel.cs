using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.ViewModels.Pages;

public partial class AutomationViewModel : ViewModelBase
{
    private readonly IAutomationService _automationService;
    
    [ObservableProperty]
    private bool _isEnabled = true;
    
    public ObservableCollection<AutomationPipelineViewModel> AutomaticPipelines { get; } = new();
    public ObservableCollection<AutomationPipelineViewModel> ManualPipelines { get; } = new();
    
    [ObservableProperty]
    private AutomationPipelineViewModel? _selectedPipeline;
    
    public ObservableCollection<string> TriggerTypes { get; } = new()
    {
        "Game Started",
        "Process Started",
        "Process Stopped",
        "Time Schedule",
        "User Inactivity",
        "WiFi Connected",
        "Device Connected",
        "GodMode Preset",
        "Periodic Action"
    };
    
    public ObservableCollection<string> StepTypes { get; } = new()
    {
        "AlwaysOnUSB", "Battery", "BatteryNightCharge", "DeactivateGPU", "Delay",
        "DisplayBrightness", "DpiScale", "FlipToStart", "FnLock", "GodModePreset",
        "HDR", "HybridMode", "InstantBoot", "Macro", "Microphone", "Notification",
        "OneLevelWhiteKB", "OverclockDiscreteGPU", "OverDrive", "PanelLogoBacklight",
        "PlaySound", "PortsBacklight", "PowerMode", "QuickAction", "RefreshRate",
        "Resolution", "RGBKeyboardBacklight", "Run", "SpectrumBrightness", "SpectrumProfile",
        "SpectrumImportProfile", "TouchpadLock", "TurnOffMonitors", "TurnOffWiFi",
        "TurnOnWiFi", "WhiteKBBacklight", "WinKey"
    };
    
    public AutomationViewModel(IAutomationService automationService)
    {
        _automationService = automationService;
        
        SubscribeToEvents();
        LoadPipelines();
    }
    
    private void SubscribeToEvents()
    {
        _automationService.PipelinesChanged += LoadPipelines;
    }
    
    private void LoadPipelines()
    {
        AutomaticPipelines.Clear();
        foreach (var p in _automationService.AutomaticPipelines)
        {
            AutomaticPipelines.Add(new AutomationPipelineViewModel(p));
        }
        
        ManualPipelines.Clear();
        foreach (var p in _automationService.ManualPipelines)
        {
            ManualPipelines.Add(new AutomationPipelineViewModel(p));
        }
    }
    
    partial void OnIsEnabledChanged(bool value)
    {
        _automationService.IsEnabled = value;
    }
    
    [RelayCommand]
    private async Task AddAutomaticPipelineAsync()
    {
        var pipeline = new AutomationPipeline
        {
            Name = "New Pipeline",
            Icon = "Rocket",
            Enabled = true
        };
        
        await _automationService.AddPipelineAsync(pipeline, false);
    }
    
    [RelayCommand]
    private async Task AddManualPipelineAsync()
    {
        var pipeline = new AutomationPipeline
        {
            Name = "New Quick Action",
            Icon = "Flash",
            Enabled = true
        };
        
        await _automationService.AddPipelineAsync(pipeline, true);
    }
    
    [RelayCommand]
    private async Task RemovePipelineAsync(AutomationPipelineViewModel pipeline)
    {
        if (pipeline != null)
        {
            await _automationService.RemovePipelineAsync(pipeline.Model.Id);
        }
    }
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        await _automationService.SaveAsync();
    }
    
    [RelayCommand]
    private async Task RevertAsync()
    {
        LoadPipelines();
    }
}

public partial class AutomationPipelineViewModel : ViewModelBase
{
    public AutomationPipeline Model { get; }
    
    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; OnPropertyChanged(); }
    }
    
    public string Icon
    {
        get => Model.Icon;
        set { Model.Icon = value; OnPropertyChanged(); }
    }
    
    public bool Enabled
    {
        get => Model.Enabled;
        set { Model.Enabled = value; OnPropertyChanged(); }
    }
    
    public ObservableCollection<AutomationStepViewModel> Steps { get; } = new();
    
    public AutomationPipelineViewModel(AutomationPipeline model)
    {
        Model = model;
        foreach (var step in model.Steps)
        {
            Steps.Add(new AutomationStepViewModel(step));
        }
    }
    
    [RelayCommand]
    private void AddStep()
    {
        var step = new AutomationStep { Name = "New Step", TypeName = "Delay", Enabled = true };
        Model.Steps.Add(step);
        Steps.Add(new AutomationStepViewModel(step));
    }
    
    [RelayCommand]
    private void RemoveStep(AutomationStepViewModel step)
    {
        if (step != null)
        {
            Model.Steps.Remove(step.Model);
            Steps.Remove(step);
        }
    }
}

public partial class AutomationStepViewModel : ViewModelBase
{
    public AutomationStep Model { get; }
    
    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; OnPropertyChanged(); }
    }
    
    public string TypeName
    {
        get => Model.TypeName;
        set { Model.TypeName = value; OnPropertyChanged(); }
    }
    
    public bool Enabled
    {
        get => Model.Enabled;
        set { Model.Enabled = value; OnPropertyChanged(); }
    }
    
    public AutomationStepViewModel(AutomationStep model)
    {
        Model = model;
    }
}