using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class MockPackageService : IPackageService
{
    public string MachineType { get; set; } = "82XW";
    public string OperatingSystem { get; set; } = "Windows 11 64-bit";
    public PackageSource Source { get; set; } = PackageSource.Vantage;
    public bool OnlyShowUpdates { get; set; } = false;
    public string FilterText { get; set; } = "";
    public PackageSortBy SortBy { get; set; } = PackageSortBy.Name;
    public string DownloadPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads\\LOQNova";
    
    public ObservableCollection<PackageInfo> Packages { get; } = new()
    {
        new PackageInfo { Name = "Lenovo System Interface Foundation", Category = "System", Version = "1.1.29.0", Date = new DateTime(2024, 12, 15), SizeBytes = 45_000_000, Description = "Provides system interface foundation for Lenovo devices", IsUpdate = true, DownloadUrl = "https://download.lenovo.com/..." },
        new PackageInfo { Name = "NVIDIA Graphics Driver", Category = "Graphics", Version = "566.36", Date = new DateTime(2024, 11, 20), SizeBytes = 650_000_000, Description = "NVIDIA GeForce Game Ready Driver", IsUpdate = true, DownloadUrl = "https://download.nvidia.com/..." },
        new PackageInfo { Name = "Realtek Audio Driver", Category = "Audio", Version = "6.0.9740.1", Date = new DateTime(2024, 10, 10), SizeBytes = 180_000_000, Description = "Realtek High Definition Audio Driver", IsUpdate = false, DownloadUrl = "https://download.lenovo.com/..." },
        new PackageInfo { Name = "Intel Bluetooth Driver", Category = "Bluetooth", Version = "23.40.0", Date = new DateTime(2024, 9, 5), SizeBytes = 35_000_000, Description = "Intel Wireless Bluetooth Driver", IsUpdate = false, DownloadUrl = "https://download.lenovo.com/..." },
        new PackageInfo { Name = "Intel WiFi Driver", Category = "Network", Version = "23.70.0", Date = new DateTime(2024, 11, 1), SizeBytes = 420_000_000, Description = "Intel Wi-Fi 6E AX211 Driver", IsUpdate = true, DownloadUrl = "https://download.nvidia.com/..." },
        new PackageInfo { Name = "Lenovo Vantage Service", Category = "System", Version = "4.12.108", Date = new DateTime(2024, 12, 1), SizeBytes = 120_000_000, Description = "Lenovo Vantage device management service", IsUpdate = false, DownloadUrl = "https://download.lenovo.com/..." },
        new PackageInfo { Name = "BIOS Update - 2.14", Category = "BIOS", Version = "2.14", Date = new DateTime(2024, 11, 28), SizeBytes = 28_000_000, Description = "System BIOS update with security improvements", IsUpdate = true, DownloadUrl = "https://download.lenovo.com/..." },
        new PackageInfo { Name = "EC Firmware Update - 1.08", Category = "Firmware", Version = "1.08", Date = new DateTime(2024, 10, 20), SizeBytes = 15_000_000, Description = "Embedded Controller firmware update", IsUpdate = true, DownloadUrl = "https://download.lenovo.com/..." }
    };
    
    public bool IsDownloading { get; private set; } = false;
    public double DownloadProgress { get; private set; } = 0;
    public string CurrentDownloadStatus { get; private set; } = "";
    
    public event Action? PackagesChanged;
    public event Action<double>? ProgressChanged;
    public event Action<bool>? DownloadingStateChanged;
    
    private readonly Random _random = new();
    
    public Task InitializeAsync() => Task.CompletedTask;
    
    public Task SearchAsync() => Task.Delay(1000);
    
    public async Task DownloadSelectedAsync()
    {
        var selected = Packages.Where(p => !p.IsHidden).ToList();
        if (!selected.Any()) return;
        
        IsDownloading = true;
        DownloadingStateChanged?.Invoke(true);
        DownloadProgress = 0;
        ProgressChanged?.Invoke(0);
        
        foreach (var pkg in selected)
        {
            CurrentDownloadStatus = $"Downloading {pkg.Name}...";
            for (int i = 0; i <= 100; i += 5)
            {
                DownloadProgress = i;
                ProgressChanged?.Invoke(i);
                await Task.Delay(_random.Next(50, 150));
            }
        }
        
        CurrentDownloadStatus = "All downloads complete";
        DownloadProgress = 100;
        ProgressChanged?.Invoke(100);
        
        await Task.Delay(1000);
        IsDownloading = false;
        DownloadingStateChanged?.Invoke(false);
    }
    
    public Task CancelDownloadAsync()
    {
        IsDownloading = false;
        DownloadingStateChanged?.Invoke(false);
        return Task.CompletedTask;
    }
    
    public Task ToggleHiddenAsync(PackageInfo package)
    {
        package.IsHidden = !package.IsHidden;
        PackagesChanged?.Invoke();
        return Task.CompletedTask;
    }
    
    public Task ToggleHideAllAsync(bool hide)
    {
        foreach (var pkg in Packages) pkg.IsHidden = hide;
        PackagesChanged?.Invoke();
        return Task.CompletedTask;
    }
    
    public async Task<string?> BrowseDownloadPathAsync()
    {
        await Task.Delay(100);
        return DownloadPath;
    }
}