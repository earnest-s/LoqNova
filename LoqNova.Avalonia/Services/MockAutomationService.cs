using System;
using System.Collections.ObjectCollection;
using System.Linq;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class MockAutomationService : IAutomationService
{
    public bool IsEnabled { get; set; } = true;
    
    public ObservableCollection<AutomationPipeline> AutomaticPipelines { get; } = new()
    {
        new AutomationPipeline
        {
            Name = "Gaming Mode",
            Icon = "Game",
            Enabled = true,
            TriggerType = AutomationTriggerType.ProcessStarted,
            TriggerConfig = new { ProcessName = "steam.exe" },
            Steps = new ObservableCollection<AutomationStep>
            {
                new() { Name = "Performance Mode", TypeName = "PowerModeStep", Config = new { Mode = "Performance" } },
                new() { Name = "GPU Overclock", TypeName = "OverclockGpuStep", Config = new { Enabled = true } },
                new() { Name = "RGB Strobe", TypeName = "RgbEffectStep", Config = new { Effect = "Strobe" } }
            }
        },
        new AutomationPipeline
        {
            Name = "Battery Saver",
            Icon = "BatterySaver",
            Enabled = true,
            TriggerType = AutomationTriggerType.ProcessStopped,
            TriggerConfig = new { ProcessName = "steam.exe" },
            Steps = new ObservableCollection<AutomationStep>
            {
                new() { Name = "Quiet Mode", TypeName = "PowerModeStep", Config = new { Mode = "Quiet" } },
                new() { Name = "Battery Conservation", TypeName = "BatteryStep", Config = new { Mode = "Conservation" } }
            }
        }
    };
    
    public ObservableCollection<AutomationPipeline> ManualPipelines { get; } = new()
    {
        new AutomationPipeline
        {
            Name = "Quick: Toggle GPU",
            Icon = "Gpu",
            Enabled = true,
            TriggerType = AutomationTriggerType.PeriodicAction,
            Steps = new ObservableCollection<AutomationStep>
            {
                new() { Name = "Toggle dGPU", TypeName = "DeactivateGpuStep" }
            }
        },
        new AutomationPipeline
        {
            Name = "Quick: Silent Mode",
            Icon = "VolumeMute",
            Enabled = true,
            TriggerType = AutomationTriggerType.PeriodicAction,
            Steps = new ObservableCollection<AutomationStep>
            {
                new() { Name = "Fan Quiet", TypeName = "FanCurveStep", Config = new { Profile = "Quiet" } },
                new() { Name = "Keyboard Off", TypeName = "RgbEffectStep", Config = new { Effect = "Off" } }
            }
        }
    };
    
    public event Action? PipelinesChanged;
    
    public Task InitializeAsync() => Task.CompletedTask;
    
    public Task AddPipelineAsync(AutomationPipeline pipeline, bool isManual)
    {
        if (isManual)
            ManualPipelines.Add(pipeline);
        else
            AutomaticPipelines.Add(pipeline);
        PipelinesChanged?.Invoke();
        return Task.CompletedTask;
    }
    
    public Task RemovePipelineAsync(Guid id)
    {
        var auto = AutomaticPipelines.FirstOrDefault(p => p.Id == id);
        if (auto != null) AutomaticPipelines.Remove(auto);
        
        var manual = ManualPipelines.FirstOrDefault(p => p.Id == id);
        if (manual != null) ManualPipelines.Remove(manual);
        
        PipelinesChanged?.Invoke();
        return Task.CompletedTask;
    }
    
    public Task UpdatePipelineAsync(AutomationPipeline pipeline)
    {
        PipelinesChanged?.Invoke();
        return Task.CompletedTask;
    }
    
    public Task MovePipelineAsync(Guid id, int newIndex)
    {
        PipelinesChanged?.Invoke();
        return Task.CompletedTask;
    }
    
    public Task AddStepAsync(Guid pipelineId, AutomationStep step)
    {
        var pipeline = AutomaticPipelines.FirstOrDefault(p => p.Id == pipelineId) 
                      ?? ManualPipelines.FirstOrDefault(p => p.Id == pipelineId);
        if (pipeline != null)
        {
            pipeline.Steps.Add(step);
            PipelinesChanged?.Invoke();
        }
        return Task.CompletedTask;
    }
    
    public Task RemoveStepAsync(Guid pipelineId, int stepIndex)
    {
        var pipeline = AutomaticPipelines.FirstOrDefault(p => p.Id == pipelineId) 
                      ?? ManualPipelines.FirstOrDefault(p => p.Id == pipelineId);
        if (pipeline != null && stepIndex >= 0 && stepIndex < pipeline.Steps.Count)
        {
            pipeline.Steps.RemoveAt(stepIndex);
            PipelinesChanged?.Invoke();
        }
        return Task.CompletedTask;
    }
    
    public Task SaveAsync() => Task.CompletedTask;
}