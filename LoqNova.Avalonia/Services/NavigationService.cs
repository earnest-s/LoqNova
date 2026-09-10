using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using LoqNova.Avalonia.Services;
using LoqNova.Avalonia.ViewModels.Pages;

namespace LoqNova.Avalonia.Services;

public class NavigationService : INavigationService
{
    public NavigationPage CurrentPage { get; private set; } = NavigationPage.Dashboard;
    public event Action<NavigationPage>? PageChanged;
    
    private Window? _mainWindow;
    private ContentControl? _contentHost;
    private readonly IServiceProvider _serviceProvider;
    
    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public async Task InitializeAsync(Window mainWindow)
    {
        _mainWindow = mainWindow;
        // Find the content host in the main window
        _contentHost = mainWindow.FindControl<ContentControl>("ContentHost");
        
        if (_contentHost != null)
        {
            await NavigateToAsync(NavigationPage.Dashboard);
        }
        await Task.CompletedTask;
    }
    
    public async Task NavigateToAsync(NavigationPage page)
    {
        CurrentPage = page;
        
        var viewModel = page switch
        {
            NavigationPage.Dashboard => _serviceProvider.GetRequiredService<DashboardViewModel>(),
            NavigationPage.KeyboardBacklight => _serviceProvider.GetRequiredService<KeyboardBacklightViewModel>(),
            NavigationPage.Battery => _serviceProvider.GetRequiredService<BatteryViewModel>(),
            NavigationPage.Automation => _serviceProvider.GetRequiredService<AutomationViewModel>(),
            NavigationPage.Macro => _serviceProvider.GetRequiredService<MacroViewModel>(),
            NavigationPage.Packages => _serviceProvider.GetRequiredService<PackagesViewModel>(),
            NavigationPage.Settings => _serviceProvider.GetRequiredService<SettingsViewModel>(),
            NavigationPage.About => _serviceProvider.GetRequiredService<AboutViewModel>(),
            _ => _serviceProvider.GetRequiredService<DashboardViewModel>()
        };
        
        if (_contentHost != null && viewModel != null)
        {
            var view = CreateViewForViewModel(viewModel);
            view.DataContext = viewModel;
            _contentHost.Content = view;
        }
        
        PageChanged?.Invoke(page);
        await Task.CompletedTask;
    }
    
    private Control CreateViewForViewModel(object viewModel)
    {
        return viewModel switch
        {
            DashboardViewModel => new Views.Pages.DashboardPage(),
            KeyboardBacklightViewModel => new Views.Pages.KeyboardBacklightPage(),
            BatteryViewModel => new Views.Pages.BatteryPage(),
            AutomationViewModel => new Views.Pages.AutomationPage(),
            MacroViewModel => new Views.Pages.MacroPage(),
            PackagesViewModel => new Views.Pages.PackagesPage(),
            SettingsViewModel => new Views.Pages.SettingsPage(),
            AboutViewModel => new Views.Pages.AboutPage(),
            _ => new Views.Pages.DashboardPage()
        };
    }
    
    public Task NavigateToDialogAsync<TViewModel>() where TViewModel : class
    {
        // Dialog navigation would be implemented here
        // For now, just return completed
        return Task.CompletedTask;
    }
    
    public Task CloseDialogAsync()
    {
        return Task.CompletedTask;
    }
}