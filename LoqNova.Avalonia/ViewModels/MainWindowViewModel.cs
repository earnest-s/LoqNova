using System;
using System.Collections.ObjectModel;
using LoqNova.Avalonia.Services;
using LoqNova.Avalonia.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoqNova.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly ISettingsService _settingsService;
    private readonly IPerformanceService _performanceService;
    private readonly IRgbService _rgbService;
    private readonly IThermalService _thermalService;
    private readonly IBatteryService _batteryService;
    
    [ObservableProperty]
    private string _deviceModel = "LOQ 15IRH8";
    
    [ObservableProperty]
    private bool _isTracing = false;
    
    [ObservableProperty]
    private bool _isVantageActive = true;
    
    [ObservableProperty]
    private bool _isLegionZoneActive = true;
    
    [ObservableProperty]
    private bool _isFnKeysActive = true;
    
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new();
    
    public MainWindowViewModel(
        INavigationService navigationService,
        ISettingsService settingsService,
        IPerformanceService performanceService,
        IRgbService rgbService,
        IThermalService thermalService,
        IBatteryService batteryService)
    {
        _navigationService = navigationService;
        _settingsService = settingsService;
        _performanceService = performanceService;
        _rgbService = rgbService;
        _thermalService = thermalService;
        _batteryService = batteryService;
        
        InitializeNavigationItems();
        _navigationService.PageChanged += OnPageChanged;
    }
    
    private void InitializeNavigationItems()
    {
        NavigationItems.Add(new NavigationItemViewModel
        {
            Page = NavigationPage.Dashboard,
            Icon = "Home",
            Label = "Dashboard",
            IsVisible = true
        });
        
        NavigationItems.Add(new NavigationItemViewModel
        {
            Page = NavigationPage.KeyboardBacklight,
            Icon = "Keyboard",
            Label = "Keyboard",
            IsVisible = true
        });
        
        NavigationItems.Add(new NavigationItemViewModel
        {
            Page = NavigationPage.Battery,
            Icon = "Battery",
            Label = "Battery",
            IsVisible = true
        });
        
        NavigationItems.Add(new NavigationItemViewModel
        {
            Page = NavigationPage.Automation,
            Icon = "Rocket",
            Label = "Automation",
            IsVisible = true
        });
        
        NavigationItems.Add(new NavigationItemViewModel
        {
            Page = NavigationPage.Macro,
            Icon = "Receipt",
            Label = "Macros",
            IsVisible = true
        });
        
        NavigationItems.Add(new NavigationItemViewModel
        {
            Page = NavigationPage.Packages,
            Icon = "Box",
            Label = "Packages",
            IsVisible = true
        });
        
        NavigationItems.Add(new NavigationItemViewModel
        {
            Page = NavigationPage.Settings,
            Icon = "Settings",
            Label = "Settings",
            IsVisible = true,
            IsFooter = true
        });
        
        NavigationItems.Add(new NavigationItemViewModel
        {
            Page = NavigationPage.About,
            Icon = "Info",
            Label = "About",
            IsVisible = true,
            IsFooter = true
        });
    }
    
    private void OnPageChanged(NavigationPage page)
    {
        foreach (var item in NavigationItems)
        {
            item.IsSelected = item.Page == page;
        }
    }
    
    [RelayCommand]
    private void Navigate(NavigationPage page)
    {
        _ = _navigationService.NavigateToAsync(page);
    }
}

public partial class NavigationItemViewModel : ViewModelBase
{
    public NavigationPage Page { get; init; }
    public string Icon { get; init; } = "";
    public string Label { get; init; } = "";
    public bool IsVisible { get; init; } = true;
    public bool IsFooter { get; init; } = false;
    
    [ObservableProperty]
    private bool _isSelected = false;
}