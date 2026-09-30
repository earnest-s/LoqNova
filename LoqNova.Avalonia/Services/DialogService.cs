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
    public void Attach(Control host) => _host = host;

    private Control? Host => _host;

    public bool IsOpen { get; private set; }

    public ViewModelBase? Current { get; private set; }

    public Task ShowAsync(ViewModelBase dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        var view = CreateView(dialog);
        if (view is null)
            return Task.CompletedTask;

        // The host lives on the UI thread; dialogs are requested from there, but the
        // dispatcher is used so a request from a background completion is still safe.
        return _dispatcher.InvokeAsync(() =>
        {
            if (Host is not ContentControl host)
                return;

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
