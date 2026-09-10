using System.Threading.Tasks;
using LoqNova.Avalonia.ViewModels.Pages;

namespace LoqNova.Avalonia.Services;

public enum NavigationPage
{
    Dashboard,
    KeyboardBacklight,
    Battery,
    Automation,
    Macro,
    Packages,
    Settings,
    About
}

public interface INavigationService
{
    NavigationPage CurrentPage { get; }
    event Action<NavigationPage> PageChanged;
    
    Task InitializeAsync(Avalonia.Controls.Window mainWindow);
    Task NavigateToAsync(NavigationPage page);
    Task NavigateToDialogAsync<TViewModel>() where TViewModel : class;
    Task CloseDialogAsync();
}