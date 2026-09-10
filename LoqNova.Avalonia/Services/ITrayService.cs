using System.Threading.Tasks;
using Avalonia.Controls;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public interface ITrayService
{
    Task InitializeAsync(Window mainWindow);
    Task UpdateTooltipAsync(string tooltip);
    Task ShutdownAsync();
}