using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public interface IFileDialogService
{
    Task<string?> ShowFolderBrowserDialogAsync(string title, string? initialPath = null);
    Task<string?> ShowOpenFileDialogAsync(string title, string filter, string? initialPath = null);
    Task<string?> ShowSaveFileDialogAsync(string title, string filter, string? initialPath = null, string? defaultFileName = null);
}