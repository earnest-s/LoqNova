using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class TrayService : ITrayService
{
    private Window? _mainWindow;
    
    public async Task InitializeAsync(Window mainWindow)
    {
        _mainWindow = mainWindow;
        
        if (OperatingSystem.IsWindows())
        {
            // Windows tray implementation would go here
        }
    }
    
    public Task UpdateTooltipAsync(string tooltip) => Task.CompletedTask;
    
    public Task ShutdownAsync()
    {
        return Task.CompletedTask;
    }
}