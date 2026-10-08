using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using LoqNova.Avalonia.Services;
using LoqNova.Avalonia.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;

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
        _contentHost = mainWindow.FindControl<ContentControl>("ContentHost");
        
        NavDiag.Log("INIT", $"contentHostFound={_contentHost is not null}");

        if (_contentHost != null)
        {
            await NavigateToAsync(NavigationPage.Dashboard);
        }
    }
    
    public async Task NavigateToAsync(NavigationPage page)
    {
        var previous = CurrentPage;
        NavDiag.Log("NAV-REQUEST", $"from={previous} to={page} hostNull={_contentHost is null}");
        var watch = Stopwatch.StartNew();

        // DI resolution happens here, on whichever thread raised the navigation
        // command. Traced separately because a page view model constructor is the
        // one place a navigation can block for an unbounded time.
        object viewModel;
        try
        {
            var resolveWatch = Stopwatch.StartNew();
            viewModel = ResolveViewModel(page);
            resolveWatch.Stop();
            NavDiag.Log("VM-RESOLVED", $"to={page} ctor={viewModel.GetType().Name} " +
                                      $"resolveMs={resolveWatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            NavDiag.LogException($"VM-RESOLVE-THREW to={page}", ex);
            throw;
        }

        if (_contentHost != null && viewModel != null)
        {
            Control view;
            try
            {
                view = CreateViewForViewModel(viewModel);
                NavDiag.Log("VIEW-CONSTRUCTED", $"to={page} view={view.GetType().Name}");
            }
            catch (Exception ex)
            {
                NavDiag.LogException($"VIEW-CTOR-THREW to={page}", ex);
                throw;
            }

            view.DataContext = viewModel;

            // Load the page before it is shown. Assigning content first painted an
            // empty page until its asynchronous load finished, which on this theme
            // looked like the window going black. A failure here must not leave a
            // half-built page on screen, so it aborts the navigation instead.
            if (viewModel is INavigationAware aware)
            {
                var initWatch = Stopwatch.StartNew();

                try
                {
                    await aware.OnNavigatedToAsync();
                    initWatch.Stop();
                    NavDiag.Log("PAGE-INIT-COMPLETE", $"to={page} initMs={initWatch.ElapsedMilliseconds}");
                }
                catch (Exception ex)
                {
                    NavDiag.LogException($"PAGE-INIT-THREW to={page}", ex);
                    throw;
                }
            }
            else
            {
                NavDiag.Log("PAGE-INIT-SKIPPED", $"to={page} reason=not-INavigationAware");
            }

            try
            {
                _contentHost.Content = view;
                NavDiag.Log("CONTENT-ASSIGNED", $"to={page} hostChildren={System.Linq.Enumerable.Count(_contentHost.GetVisualDescendants())}");
            }
            catch (Exception ex)
            {
                NavDiag.LogException($"CONTENT-ASSIGN-THREW to={page}", ex);
                throw;
            }

            AttachLifecycleTrace(view, page);
        }

        CurrentPage = page;
        PageChanged?.Invoke(page);
        watch.Stop();
        NavDiag.Log("NAV-COMPLETE", $"to={page} totalMs={watch.ElapsedMilliseconds}");
    }

    /// <summary>
    /// TEMPORARY diagnostic hook. Traces attach/detach/load for the page view so a
    /// navigation that never completes its visual-tree lifecycle is visible.
    /// </summary>
    private static void AttachLifecycleTrace(Control view, NavigationPage page)
    {
        view.AttachedToVisualTree += (_, _) =>
            NavDiag.Log("VIEW-ATTACHED-TO-VISUAL-TREE", $"to={page} view={view.GetType().Name}");

        view.DetachedFromVisualTree += (_, _) =>
            NavDiag.Log("VIEW-DETACHED-FROM-VISUAL-TREE", $"to={page} view={view.GetType().Name}");

        view.Loaded += (_, _) =>
            NavDiag.Log("VIEW-LOADED", $"to={page} view={view.GetType().Name}");

        view.DataContextChanged += (_, _) =>
            NavDiag.Log("VIEW-DC-CHANGED", $"to={page} view={view.GetType().Name}");
    }

    private object ResolveViewModel(NavigationPage page) => page switch
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
    
    public Task NavigateToDialogAsync<TViewModel>() where TViewModel : class => Task.CompletedTask;
    
    public Task CloseDialogAsync() => Task.CompletedTask;
}