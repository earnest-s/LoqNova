using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Localization;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Extensions;
using LoqNova.Lib.PackageDownloader;
using LoqNova.Lib.Settings;
using LoqNova.Lib.System;
using LoqNova.Lib.Utils;

namespace LoqNova.Avalonia.ViewModels.Pages;

/// <summary>
/// Port of <c>LoqNova.WPF.Pages.PackagesPage</c>. All package retrieval and
/// file downloads go through the existing <see cref="PackageDownloaderFactory"/>
/// / <see cref="IPackageDownloader"/> in LoqNova.Lib. No local HTTP client,
/// no API reimplementation, no synthetic package data.
/// </summary>
public partial class PackagesViewModel : ViewModelBase
{
    private readonly PackageDownloaderFactory _packageDownloaderFactory;
    private readonly PackageDownloaderSettings _packageDownloaderSettings;
    private readonly INotificationService _notificationService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IMainThreadDispatcher _dispatcher;

    private IPackageDownloader? _packageDownloader;
    private CancellationTokenSource? _getPackagesTokenSource;
    private CancellationTokenSource? _filterDebounceTokenSource;

    /// <summary>Raw result of the last successful retrieval, before filtering.</summary>
    private List<Package>? _packages;

    [ObservableProperty]
    private string _machineType = string.Empty;

    [ObservableProperty]
    private OS _operatingSystem = OS.Windows11;

    [ObservableProperty]
    private bool _isVantageSource = true;

    [ObservableProperty]
    private bool _isPCSupportSource;

    [ObservableProperty]
    private bool _onlyShowUpdates;

    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>0 = Title, 1 = Category, 2 = ReleaseDate (descending).</summary>
    [ObservableProperty]
    private int _sortIndex = 2;

    [ObservableProperty]
    private string _downloadPath = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private double _progress;

    /// <summary>Mirrors the WPF loader: indeterminate while the value is negative.</summary>
    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private bool _isInitialized;

    /// <summary>True once a retrieval has completed, so the empty state can say
    /// "no matching downloads" instead of pretending packages were loaded.</summary>
    [ObservableProperty]
    private bool _hasLoadedPackages;

    public ObservableCollection<OS> OperatingSystems { get; } = new(Enum.GetValues<OS>());

    public ObservableCollection<string> SortOptions { get; } = new()
    {
        "Name", "Category", "Release date"
    };

    /// <summary>Filtered/sorted view actually rendered. Never contains mock data.</summary>
    public ObservableCollection<PackageViewModel> Packages { get; } = new();

    public bool IsCancelVisible => IsLoading;

    public bool HasHiddenPackages => _packageDownloaderSettings.Store.HiddenPackages.Count != 0;

    public bool IsOnlyShowUpdatesVisible => IsVantageSource;

    public PackagesViewModel(
        PackageDownloaderFactory packageDownloaderFactory,
        PackageDownloaderSettings packageDownloaderSettings,
        INotificationService notificationService,
        IFileDialogService fileDialogService,
        IMainThreadDispatcher dispatcher)
    {
        _packageDownloaderFactory = packageDownloaderFactory;
        _packageDownloaderSettings = packageDownloaderSettings;
        _notificationService = notificationService;
        _fileDialogService = fileDialogService;
        _dispatcher = dispatcher;
    }

    /// <summary>Mirrors WPF <c>PackagesPage_Initialized</c>.</summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (IsInitialized)
            return;

        try
        {
            var machineInformation = await Compatibility.GetMachineInformationAsync().ConfigureAwait(true);
            MachineType = machineInformation.MachineType;
        }
        catch (Exception ex)
        {
            Trace("Failed to retrieve machine information.", ex);
            await _notificationService.ShowAsync(new NotificationMessage(
                NotificationType.Error, "Packages", "Could not read machine information.")).ConfigureAwait(true);
        }

        OperatingSystem = OSExtensions.GetCurrent();

        var downloadsFolder = KnownFolders.GetPath(KnownFolder.Downloads);
        DownloadPath = Directory.Exists(_packageDownloaderSettings.Store.DownloadPath)
            ? _packageDownloaderSettings.Store.DownloadPath
            : downloadsFolder;

        IsInitialized = true;
    }

    /// <summary>Mirrors WPF <c>DownloadPackagesButton_Click</c>.</summary>
    [RelayCommand]
    private async Task DownloadPackagesAsync()
    {
        if (IsLoading)
            return;

        var errorOccurred = false;

        IsLoading = true;
        IsIndeterminate = true;
        Progress = 0;
        HasLoadedPackages = false;

        Packages.Clear();
        _packages = null;

        // WPF resets the filter and forces the default sort before each fetch.
        FilterText = string.Empty;
        SortIndex = 2;

        var machineType = MachineType.Trim();

        if (string.IsNullOrWhiteSpace(machineType) || machineType.Length != 4)
        {
            await _notificationService.ShowAsync(new NotificationMessage(
                NotificationType.Error,
                "Download failed",
                "Machine type must be exactly 4 characters, for example 82JQ.")).ConfigureAwait(true);

            IsLoading = false;
            return;
        }

        if (_getPackagesTokenSource is not null)
            await _getPackagesTokenSource.CancelAsync().ConfigureAwait(true);

        _getPackagesTokenSource = new CancellationTokenSource();
        var token = _getPackagesTokenSource.Token;

        try
        {
            var source = IsVantageSource
                ? PackageDownloaderFactory.Type.Vantage
                : PackageDownloaderFactory.Type.PCSupport;

            // Only the Vantage catalog reports IsUpdate; the WPF page hides and
            // clears the option for the other source.
            if (source == PackageDownloaderFactory.Type.Vantage)
            {
                OnlyShowUpdates = _packageDownloaderSettings.Store.OnlyShowUpdates;
            }
            else
            {
                OnlyShowUpdates = false;
            }
            OnPropertyChanged(nameof(IsOnlyShowUpdatesVisible));

            _packageDownloader = _packageDownloaderFactory.GetInstance(source);

            var progress = new Progress<float>(value =>
            {
                // value < 0 means indeterminate, matching the WPF loader.
                _dispatcher.Post(() =>
                {
                    IsIndeterminate = value < 0;
                    Progress = value;
                });
            });

            var packages = await _packageDownloader
                .GetPackagesAsync(machineType, OperatingSystem, progress, token)
                .ConfigureAwait(true);

            _packages = packages;
            HasLoadedPackages = true;

            Reload();
        }
        catch (UpdateCatalogNotFoundException ex)
        {
            Trace("Update catalog not found.", ex);

            await _notificationService.ShowAsync(new NotificationMessage(
                NotificationType.Info,
                "Update catalog not found",
                "No update catalog exists for this machine type and operating system.")).ConfigureAwait(true);

            errorOccurred = true;
        }
        catch (OperationCanceledException)
        {
            errorOccurred = true;
        }
        catch (HttpRequestException ex)
        {
            Trace("Error occurred when downloading packages.", ex);

            var message = ex.StatusCode switch
            {
                null => "Check your internet connection and try again.",
                _ => $"The package service returned {(int)ex.StatusCode} ({ex.StatusCode})."
            };

            await _notificationService.ShowAsync(new NotificationMessage(
                NotificationType.Error, "Download error", message)).ConfigureAwait(true);

            errorOccurred = true;
        }
        catch (Exception ex)
        {
            Trace("Error occurred when downloading packages.", ex);

            await _notificationService.ShowAsync(new NotificationMessage(
                NotificationType.Error, "Download error", ex.Message)).ConfigureAwait(true);

            errorOccurred = true;
        }
        finally
        {
            IsLoading = false;
            Progress = 0;
            IsIndeterminate = true;

            if (errorOccurred)
            {
                Packages.Clear();
                _packages = null;
            }
            else
            {
                OnPropertyChanged(nameof(HasHiddenPackages));
            }
        }
    }

    [RelayCommand]
    private void CancelDownloadPackages() => _getPackagesTokenSource?.Cancel();

    /// <summary>Mirrors WPF <c>FilterTextBox_TextChanged</c> including the 500ms debounce.</summary>
    partial void OnFilterTextChanged(string value) => ScheduleFilterRefresh();

    private void ScheduleFilterRefresh()
    {
        if (_packages is null)
            return;

        var source = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _filterDebounceTokenSource, source);

        previous?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, source.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _dispatcher.Post(Reload);
        });
    }

    /// <summary>Mirrors WPF <c>OnlyShowUpdatesCheckBox_OnChecked</c>.</summary>
    partial void OnOnlyShowUpdatesChanged(bool value)
    {
        if (_packages is null)
            return;

        _packageDownloaderSettings.Store.OnlyShowUpdates = value;
        _packageDownloaderSettings.SynchronizeStore();

        Reload();
    }

    /// <summary>Mirrors WPF <c>SortingComboBox_SelectionChanged</c>.</summary>
    partial void OnSortIndexChanged(int value)
    {
        if (_packages is null)
            return;

        Reload();
    }

    /// <summary>Mirrors WPF <c>DownloadToText_OnTextChanged</c>.</summary>
    partial void OnDownloadPathChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!Directory.Exists(value))
            return;

        _packageDownloaderSettings.Store.DownloadPath = value;
        _packageDownloaderSettings.SynchronizeStore();
    }

    [RelayCommand]
    private async Task BrowseDownloadPathAsync()
    {
        var selectedPath = await _fileDialogService
            .ShowFolderBrowserDialogAsync("Select download location", DownloadPath)
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        DownloadPath = selectedPath;

        _packageDownloaderSettings.Store.DownloadPath = selectedPath;
        _packageDownloaderSettings.SynchronizeStore();
    }

    [RelayCommand]
    private void OpenDownloadLocation()
    {
        var location = GetDownloadLocation();

        if (!Directory.Exists(location))
            return;

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer",
                ArgumentList = { location },
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Trace("Failed to open download location.", ex);
        }
    }

    [RelayCommand]
    private void HidePackage(PackageViewModel package)
    {
        if (package is null)
            return;

        _packageDownloaderSettings.Store.HiddenPackages.Add(package.Id);
        _packageDownloaderSettings.SynchronizeStore();

        Reload();
    }

    [RelayCommand]
    private void HideAllPackages()
    {
        if (_packages is null)
            return;

        foreach (var package in _packages)
            _packageDownloaderSettings.Store.HiddenPackages.Add(package.Id);

        _packageDownloaderSettings.SynchronizeStore();

        Reload();
    }

    [RelayCommand]
    private void ShowHiddenPackages()
    {
        _packageDownloaderSettings.Store.HiddenPackages.Clear();
        _packageDownloaderSettings.SynchronizeStore();

        Reload();
    }

    /// <summary>Mirrors WPF <c>GetDownloadLocation</c>: falls back to Downloads when missing.</summary>
    private string GetDownloadLocation()
    {
        var location = DownloadPath.Trim();

        if (Directory.Exists(location))
            return location;

        var downloads = KnownFolders.GetPath(KnownFolder.Downloads);

        DownloadPath = downloads;
        _packageDownloaderSettings.Store.DownloadPath = downloads;
        _packageDownloaderSettings.SynchronizeStore();

        return downloads;
    }

    /// <summary>Mirrors WPF <c>Reload</c>.</summary>
    private void Reload()
    {
        Packages.Clear();

        if (_packageDownloader is null || _packages is null || _packages.Count == 0)
        {
            OnPropertyChanged(nameof(HasHiddenPackages));
            return;
        }

        var packages = SortAndFilter(_packages);
        var location = GetDownloadLocation();

        foreach (var package in packages)
        {
            Packages.Add(new PackageViewModel(
                _packageDownloader, package, location, _notificationService, _dispatcher));
        }

        OnPropertyChanged(nameof(HasHiddenPackages));
    }

    /// <summary>
    /// Direct port of WPF <c>SortAndFilter</c>:
    /// sort by Title / Category / ReleaseDate descending, exclude hidden ids,
    /// optionally require IsUpdate, then match the package Index.
    /// </summary>
    private List<Package> SortAndFilter(List<Package> packages)
    {
        var result = SortIndex switch
        {
            0 => packages.OrderBy(p => p.Title),
            1 => packages.OrderBy(p => p.Category),
            2 => packages.OrderByDescending(p => p.ReleaseDate),
            _ => packages.AsEnumerable()
        };

        result = result.Where(p => !_packageDownloaderSettings.Store.HiddenPackages.Contains(p.Id));

        if (OnlyShowUpdates)
            result = result.Where(p => p.IsUpdate);

        if (!string.IsNullOrWhiteSpace(FilterText))
            result = result.Where(p => p.Index.Contains(FilterText, StringComparison.InvariantCultureIgnoreCase));

        return result.ToList();
    }

    private static void Trace(string message, Exception ex)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"{message} {ex}");
    }
}

/// <summary>
/// Avalonia equivalent of <c>LoqNova.WPF.Controls.Packages.PackageControl</c>.
/// Wraps a real <see cref="Package"/> and owns its own download state.
/// </summary>
public partial class PackageViewModel : ViewModelBase
{
    private readonly IPackageDownloader _packageDownloader;
    private readonly string _location;
    private readonly INotificationService _notificationService;
    private readonly IMainThreadDispatcher _dispatcher;

    private CancellationTokenSource? _downloadPackageTokenSource;

    public Package Package { get; }

    public string Id => Package.Id;

    public string Title => Package.Title;

    public string Description => Package.Description;

    public string Version => Package.Version;

    public string Category => Package.Category;

    public string FileName => Package.FileName;

    public string FileSize => Package.FileSize;

    public DateTime ReleaseDate => Package.ReleaseDate;

    public bool IsUpdate => Package.IsUpdate;

    public string? ReadmeUrl => Package.Readme;

    public bool HasReadme => !string.IsNullOrWhiteSpace(Package.Readme);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Package.Description);

    /// <summary>WPF PackageControl flags releases older than a year.</summary>
    public bool IsOld => Package.ReleaseDate < DateTime.UtcNow.AddYears(-1);

    public string ReleaseDateText => Package.ReleaseDate.ToString("d");

    public string DetailText => $"Version {Package.Version}   |   {Package.FileSize}   |   {Package.FileName}";

    public string RebootText => Package.Reboot switch
    {
        RebootType.Delayed => "Reboot recommended",
        RebootType.Requested => "Reboot recommended",
        RebootType.Forced => "Reboot required",
        RebootType.ForcedPowerOff => "Shutdown required",
        _ => string.Empty
    };

    public bool HasRebootWarning =>
        Package.Reboot is RebootType.Delayed or RebootType.Requested
            or RebootType.Forced or RebootType.ForcedPowerOff;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isDownloadComplete;

    [ObservableProperty]
    private double _progress;

    /// <summary>PackageControl treats a non-positive value as indeterminate.</summary>
    [ObservableProperty]
    private bool _isIndeterminate = true;

    public PackageViewModel(
        IPackageDownloader packageDownloader,
        Package package,
        string location,
        INotificationService notificationService,
        IMainThreadDispatcher dispatcher)
    {
        _packageDownloader = packageDownloader;
        _location = location;
        _notificationService = notificationService;
        _dispatcher = dispatcher;

        Package = package;
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsDownloading)
            return;

        IsDownloading = true;
        IsDownloadComplete = false;
        IsIndeterminate = true;
        Progress = 0;

        var result = false;

        try
        {
            if (_downloadPackageTokenSource is not null)
                await _downloadPackageTokenSource.CancelAsync().ConfigureAwait(true);

            _downloadPackageTokenSource = new CancellationTokenSource();
            var token = _downloadPackageTokenSource.Token;

            var progress = new Progress<float>(value =>
            {
                _dispatcher.Post(() =>
                {
                    IsIndeterminate = !(value > 0);
                    Progress = value * 100;
                });
            });

            await _packageDownloader
                .DownloadPackageFileAsync(Package, _location, progress, token)
                .ConfigureAwait(true);

            result = true;
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user: not an error.
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            PackagesViewModelTrace("Not found 404.", ex);

            await _notificationService.ShowAsync(new NotificationMessage(
                NotificationType.Error,
                "Download failed",
                $"The package file could not be found on the server. ({FileName})")).ConfigureAwait(true);
        }
        catch (HttpRequestException ex)
        {
            PackagesViewModelTrace("Error occurred when downloading package file.", ex);

            await _notificationService.ShowAsync(new NotificationMessage(
                NotificationType.Error,
                "Download failed",
                $"A network error occurred while downloading {FileName}.")).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            PackagesViewModelTrace("Error occurred when downloading package file.", ex);

            await _notificationService.ShowAsync(new NotificationMessage(
                NotificationType.Error, "Download failed", ex.Message)).ConfigureAwait(true);
        }
        finally
        {
            _dispatcher.Post(() =>
            {
                IsDownloading = false;
                Progress = 0;
                IsIndeterminate = true;
                IsDownloadComplete = result;
            });
        }

        if (result)
        {
            await _notificationService.ShowAsync(new NotificationMessage(
                NotificationType.Success, "Download complete", FileName)).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void CancelDownload() => _downloadPackageTokenSource?.Cancel();

    [RelayCommand]
    private void OpenReadme()
    {
        if (string.IsNullOrWhiteSpace(ReadmeUrl))
            return;

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ReadmeUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            PackagesViewModelTrace("Failed to open readme.", ex);
        }
    }

    private static void PackagesViewModelTrace(string message, Exception ex)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"{message} {ex}");
    }
}
