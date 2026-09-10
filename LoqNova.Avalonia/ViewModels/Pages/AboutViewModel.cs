using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.ViewModels.Pages;

public partial class AboutViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    
    [ObservableProperty]
    private string _appName = "LOQ Nova";
    
    [ObservableProperty]
    private string _version = "3.1.0";
    
    [ObservableProperty]
    private string _buildDate = "2024-12-15";
    
    [ObservableProperty]
    private string _githubUrl = "https://github.com/earnest/LOQ-Nova";
    
    [ObservableProperty]
    private string _licenseText = "MIT License\n\nCopyright (c) 2024 Earnest S\n\nPermission is hereby granted...";
    
    [ObservableProperty]
    private string _openSourceLicenses = "";
    
    [ObservableProperty]
    private string _donationUrl = "https://paypal.me/earnest";
    
    public ObservableCollection<CreditItem> Credits { get; } = new()
    {
        new CreditItem { Name = "Avalonia UI", Url = "https://avaloniaui.net/" },
        new CreditItem { Name = "CommunityToolkit.Mvvm", Url = "https://github.com/CommunityToolkit/dotnet" },
        new CreditItem { Name = "Autofac", Url = "https://autofac.org/" },
        new CreditItem { Name = "Newtonsoft.Json", Url = "https://www.newtonsoft.com/json" },
        new CreditItem { Name = "NAudio", Url = "https://naudio.net/" },
        new CreditItem { Name = "NvAPIWrapper.Net", Url = "https://github.com/mxgmn/NvAPIWrapper.Net" },
        new CreditItem { Name = "SkiaSharp", Url = "https://github.com/mono/SkiaSharp" },
        new CreditItem { Name = "Humanizer", Url = "https://github.com/Humanizr/Humanizer" },
        new CreditItem { Name = "PubSub", Url = "https://github.com/kekyo/PubSub" }
    };
    
    public AboutViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        
        // Get actual version from assembly
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informationalVersion))
        {
            Version = informationalVersion.Split('+')[0];
        }
        
        LoadOpenSourceLicenses();
    }
    
    private void LoadOpenSourceLicenses()
    {
        // In a real implementation, this would load from embedded resources
        OpenSourceLicenses = "Avalonia UI - MIT License\nCommunityToolkit.Mvvm - MIT License\nAutofac - MIT License\nNewtonsoft.Json - MIT License\nNAudio - MIT License\nNvAPIWrapper.Net - MIT License\nSkiaSharp - MIT License\nHumanizer - MIT License\nPubSub - MIT License";
    }
    
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        // Trigger update check
    }
    
    [RelayCommand]
    private async Task OpenGitHubAsync()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = GithubUrl,
                UseShellExecute = true
            });
        }
        catch { }
    }
    
    [RelayCommand]
    private async Task OpenDonationAsync()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DonationUrl,
                UseShellExecute = true
            });
        }
        catch { }
    }
}

public class CreditItem
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}