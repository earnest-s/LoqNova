using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using LoqNova.Avalonia.ViewModels;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Dialog host backed by an overlay <c>ContentControl</c> in the main window.
/// <para>
/// The view is chosen from the dialog's type, so a ViewModel and its view stay paired
/// explicitly rather than by convention.
/// </para>
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IMainThreadDispatcher _dispatcher;
    private Control? _host;

    public DialogService(IMainThreadDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Supplies the overlay that hosts dialogs. Bound after the main window exists,
    /// which keeps the service free of a window reference at construction time.
    /// </summary>
    /// <summary>
    /// Supplies the window that hosts dialogs. The overlay itself is resolved lazily
    /// when a dialog is shown rather than captured here: the XAML-generated name field
    /// is not yet populated at this point in startup, so capturing it produced a null
    /// host and the dialog was never displayed.
    /// </summary>
    public void Attach(Window window) => _window = window;

    private Window? _window;

    /// <summary>
    /// The dialog overlay inside the main window, found on first use. Looked up by name
    /// because the generated field is not reliable this early in startup.
    /// </summary>
    private Control? Host
    {
        get
        {
            if (_host is not null)
                return _host;

            _host = _window?.FindControl<ContentControl>("DialogHost");
            return _host;
        }
    }

    /// <summary>
    /// Resolves a dialog from the shared container, which is assigned once the
    /// application has built it. This is the only supported way for a presenter to
    /// obtain a dialog, so a page never has to depend on a global root being ready.
    /// </summary>
    public T Resolve<T>() where T : ViewModelBase
    {
        var services = _services ?? throw new InvalidOperationException(
            "The dialog service has no container yet. It is assigned during application startup.");
        var made = services.GetService(typeof(T));
        return (T)made
            ?? throw new InvalidOperationException($"{typeof(T).Name} is not registered.");
    }

    /// <summary>Supplies the shared container. Called once during application startup.</summary>
    public void UseContainer(IServiceProvider services) => _services = services;

    private IServiceProvider? _services;

    private Control? HostUnused => _host;


    public bool IsOpen { get; private set; }

    public ViewModelBase? Current { get; private set; }

    public event Action? Closed;

    public static void T(string s) => System.IO.File.AppendAllText(
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "viz.txt"), s + Environment.NewLine);
    public Task ShowAsync(ViewModelBase dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        var view = CreateView(dialog);
        T("ShowAsync " + dialog.GetType().Name + " view=" + (view?.GetType().Name ?? "NULL-VIEW")
            + " hostField=" + (Host?.GetType().FullName ?? "NULL-HOST"));

        if (view is null)
            return Task.CompletedTask;

        // The host lives on the UI thread; dialogs are requested from there, but the
        // dispatcher is used so a request from a background completion is still safe.
        return _dispatcher.InvokeAsync(() =>
        {
            if (Host is not ContentControl host)
                return;

            // A dialog is presented outside the page's visual tree, so it inherits no
            // DataContext. Without this the view binds against the page ViewModel and
            // every control inside it silently fails to resolve.
            view.DataContext = dialog;

            T("host=" + (Host?.GetType().Name ?? "null") + " dc=" + (view.DataContext?.GetType().Name ?? "null"));
            host.Content = view;
            host.IsVisible = true;
            Current = dialog;
            IsOpen = true;
        });
    }

    public void Close()
    {
        _dispatcher.Post(() =>
        {
            if (Host is ContentControl host)
            {
                host.Content = null;
                host.IsVisible = false;
            }

            Current = null;
            IsOpen = false;

            // The presenter owns the scrim, so it is told rather than told to ask.
            Closed?.Invoke();
        });
    }

    private static Control? CreateView(ViewModelBase dialog) => dialog switch
    {
        ViewModels.Dialogs.BalanceModeSettingsViewModel =>
            new Views.Dialogs.BalanceModeSettingsView(),
        ViewModels.Dialogs.CustomModeSettingsViewModel =>
            new Views.Dialogs.CustomModeSettingsView(),
        ViewModels.Dialogs.OverclockGpuSettingsViewModel =>
            new Views.Dialogs.OverclockGpuSettingsView(),
        _ => null
    };
}
