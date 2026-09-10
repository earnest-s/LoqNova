using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.ViewModels.Pages;

public partial class PackagesViewModel : ViewModelBase
{
    private readonly IPackageService _packageService;
    
    [ObservableProperty]
    private string _machineType = "82XW";
    
    [ObservableProperty]
    private string _operatingSystem = "Windows 11 64-bit";
    
    [ObservableProperty]
    private PackageSource _source = PackageSource.Vantage;
    
    [ObservableProperty]
    private bool _onlyShowUpdates = false;
    
    [ObservableProperty]
    private string _filterText = "";
    
    [ObservableProperty]
    private PackageSortBy _sortBy = PackageSortBy.Name;
    
    [ObservableProperty]
    private string _downloadPath = "";
    
    [ObservableProperty]
    private bool _isDownloading = false;
    
    [ObservableProperty]
    private double _downloadProgress = 0;
    
    [ObservableProperty]
    private string _currentDownloadStatus = "";
    
    public ObservableCollection<PackageViewModel> Packages { get; } = new();
    
    public ObservableCollection<string> OperatingSystems { get; } = new()
    {
        "Windows 11 64-bit", "Windows 10 64-bit"
    };
    
    public ObservableCollection<PackageSource> Sources { get; } = new()
    {
        PackageSource.Vantage, PackageSource.PCSupport
    };
    
    public ObservableCollection<PackageSortBy> SortOptions { get; } = new()
    {
        PackageSortBy.Name, PackageSortBy.Category, PackageSortBy.Date
    };
    
    public PackagesViewModel(IPackageService packageService)
    {
        _packageService = packageService;
        
        SubscribeToEvents();
        LoadPackages();
    }
    
    private void SubscribeToEvents()
    {
        _packageService.PackagesChanged += LoadPackages;
        _packageService.ProgressChanged += p => DownloadProgress = p;
        _packageService.DownloadingStateChanged += d => IsDownloading = d;
    }
    
    private void LoadPackages()
    {
        Packages.Clear();
        foreach (var pkg in _packageService.Packages)
        {
            Packages.Add(new PackageViewModel(pkg));
        }
    }
    
    [RelayCommand]
    private async Task SearchAsync()
    {
        await _packageService.SearchAsync();
    }
    
    [RelayCommand]
    private async Task DownloadAsync()
    {
        await _packageService.DownloadSelectedAsync();
    }
    
    [RelayCommand]
    private async Task CancelDownloadAsync()
    {
        await _packageService.CancelDownloadAsync();
    }
    
    [RelayCommand]
    private async Task ToggleHiddenAsync(PackageViewModel pkg)
    {
        if (pkg != null)
        {
            await _packageService.ToggleHiddenAsync(pkg.Model);
        }
    }
    
    [RelayCommand]
    private async Task ToggleHideAllAsync(bool hide)
    {
        await _packageService.ToggleHideAllAsync(hide);
    }
    
    [RelayCommand]
    private async Task BrowseDownloadPathAsync()
    {
        var path = await _packageService.BrowseDownloadPathAsync();
        if (!string.IsNullOrEmpty(path))
        {
            DownloadPath = path;
        }
    }
    
    partial void OnFilterTextChanged(string value)
    {
        // Filter logic would go here
    }
}

public partial class PackageViewModel : ViewModelBase
{
    public PackageInfo Model { get; }
    
    public string Name => Model.Name;
    public string Category => Model.Category;
    public string Version => Model.Version;
    public DateTime Date => Model.Date;
    public string SizeText => FormatSize(Model.SizeBytes);
    public string Description => Model.Description;
    public bool IsUpdate => Model.IsUpdate;
    public bool IsHidden => Model.IsHidden;
    
    public PackageViewModel(PackageInfo model)
    {
        Model = model;
    }
    
    private static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int i = 0;
        double size = bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:F1} {suffixes[i]}";
    }
}