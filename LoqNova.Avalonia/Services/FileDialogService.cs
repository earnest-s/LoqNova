using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class FileDialogService : IFileDialogService
{
    private static Window? GetWindow() =>
        (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<string?> ShowFolderBrowserDialogAsync(string title, string? initialPath = null)
    {
        var window = GetWindow();
        if (window == null) return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title
        };

        var folders = await window.StorageProvider.OpenFolderPickerAsync(options);
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task<string?> ShowOpenFileDialogAsync(string title, string filter, string? initialPath = null)
    {
        var window = GetWindow();
        if (window == null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ParseFileTypes(filter)
        };

        if (!string.IsNullOrEmpty(initialPath))
        {
            try
            {
                var folder = await window.StorageProvider.TryGetFolderFromPathAsync(initialPath);
                if (folder != null)
                {
                    options.SuggestedStartLocation = folder;
                }
            }
            catch { }
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> ShowSaveFileDialogAsync(string title, string filter, string? initialPath = null, string? defaultFileName = null)
    {
        var window = GetWindow();
        if (window == null) return null;

        var options = new FilePickerSaveOptions
        {
            Title = title,
            FileTypeChoices = ParseFileTypes(filter),
            SuggestedFileName = defaultFileName ?? ""
        };

        if (!string.IsNullOrEmpty(initialPath))
        {
            try
            {
                var folder = await window.StorageProvider.TryGetFolderFromPathAsync(initialPath);
                if (folder != null)
                {
                    options.SuggestedStartLocation = folder;
                }
            }
            catch { }
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(options);
        return file?.Path.LocalPath;
    }

    private static List<FilePickerFileType> ParseFileTypes(string filter)
    {
        var fileTypes = new List<FilePickerFileType>();
        var parts = filter.Split('|');
        for (int i = 0; i < parts.Length - 1; i += 2)
        {
            var name = parts[i];
            var extensions = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.StartsWith(".") ? e[1..] : e)
                .ToArray();
            fileTypes.Add(new FilePickerFileType(name) { Patterns = extensions });
        }
        return fileTypes;
    }
}