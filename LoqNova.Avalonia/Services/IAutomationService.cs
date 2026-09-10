using System;
using System.Collections.ObjectCollection;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public enum AutomationTriggerType
{
    GameAuto,
    ProcessStarted,
    ProcessStopped,
    Time,
    UserInactivity,
    WiFiConnected,
    DeviceConnected,
    GodModePreset,
    PeriodicAction
}

public class AutomationPipeline
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public AutomationTriggerType TriggerType { get; set; }
    public object? TriggerConfig { get; set; }
    public ObservableCollection<AutomationStep> Steps { get; } = new();
}

public class AutomationStep
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public object? Config { get; set; }
    public bool Enabled { get; set; } = true;
}

public interface IAutomationService
{
    bool IsEnabled { get; set; }
    ObservableCollection<AutomationPipeline> AutomaticPipelines { get; }
    ObservableCollection<AutomationPipeline> ManualPipelines { get; }
    
    event Action? PipelinesChanged;
    
    Task InitializeAsync();
    Task AddPipelineAsync(AutomationPipeline pipeline, bool isManual);
    Task RemovePipelineAsync(Guid id);
    Task UpdatePipelineAsync(AutomationPipeline pipeline);
    Task MovePipelineAsync(Guid id, int newIndex);
    Task AddStepAsync(Guid pipelineId, AutomationStep step);
    Task RemoveStepAsync(Guid pipelineId, int stepIndex);
    Task SaveAsync();
}