using System.Threading.Tasks;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public interface ITrayService
{
    Task InitializeAsync(Avalonia.Controls.Window mainWindow);
    Task UpdateTooltipAsync(string tooltip);
    Task ShutdownAsync();
}