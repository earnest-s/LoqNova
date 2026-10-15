using System.Threading.Tasks;
using Avalonia.Controls;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public interface ITrayService
{
    Task InitializeAsync(Window mainWindow);
    Task UpdateTooltipAsync(string tooltip);

    /// <summary>
    /// Brings the window back from the notification area (or raises it if it is merely
    /// behind another window). Safe to call from any thread.
    /// </summary>
    void ShowMainWindow();

    Task ShutdownAsync();
}