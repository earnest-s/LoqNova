using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class FileDialogService : IFileDialogService
{
    public async Task<string?> ShowFolderBrowserDialogAsync(string title, string? initialPath = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title
        };
        
        if (!string.IsNullOrEmpty(initialPath))
        {
            try { dialog.InitialDirectory = initialPath; } catch { }
        }
        
        return await dialog.ShowAsync(null);
    }
    
    public async Task<string?> ShowOpenFileDialogAsync(string title, string filter, string? initialPath = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filters = ParseFilters(filter)
        };
        
        if (!string.IsNullOrEmpty(initialPath))
        {
            try { dialog.InitialDirectory = initialPath; } catch { }
        }
        
        var result = await dialog.ShowAsync(null);
        return result?.FirstOrDefault();
    }
    
    public async Task<string?> ShowSaveFileDialogAsync(string title, string filter, string? initialPath = null, string? defaultFileName = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filters = ParseFilters(filter),
            InitialFileName = defaultFileName ?? ""
        };
        
        if (!string.IsNullOrEmpty(initialPath))
        {
            try { dialog.InitialDirectory = initialPath; } catch { }
        }
        
        return await dialog.ShowAsync(null);
    }
    
    private static List<FileDialogFilter> ParseFilters(string filter)
    {
        var filters = new List<FileDialogFilter>();
        var parts = filter.Split('|');
        for (int i = 0; i < parts.Length - 1; i += 2)
        {
            filters.Add(new FileDialogFilter
            {
                Name = parts[i],
                Extensions = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            });
        }
        return filters;
    }
}