using System.Collections.ObjectCollection;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public class PackageInfo
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime Date { get; set; }
    public long SizeBytes { get; set; }
    public string Description { get; set; } = "";
    public bool IsUpdate { get; set; }
    public bool IsHidden { get; set; }
    public string DownloadUrl { get; set; } = "";
}

public enum PackageSource
{
    Vantage,
    PCSupport
}

public enum PackageSortBy
{
    Name,
    Category,
    Date
}

public interface IPackageService
{
    string MachineType { get; set; }
    string OperatingSystem { get; set; }
    PackageSource Source { get; set; }
    bool OnlyShowUpdates { get; set; }
    string FilterText { get; set; }
    PackageSortBy SortBy { get; set; }
    string DownloadPath { get; set; }
    
    ObservableCollection<PackageInfo> Packages { get; }
    bool IsDownloading { get; }
    double DownloadProgress { get; }
    string CurrentDownloadStatus { get; }
    
    event Action? PackagesChanged;
    event Action<double>? ProgressChanged;
    event Action<bool>? DownloadingStateChanged;
    
    Task InitializeAsync();
    Task SearchAsync();
    Task DownloadSelectedAsync();
    Task CancelDownloadAsync();
    Task ToggleHiddenAsync(PackageInfo package);
    Task ToggleHideAllAsync(bool hide);
    Task<string?> BrowseDownloadPathAsync();
}