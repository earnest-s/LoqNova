using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LoqNova.Avalonia.ViewModels.Pages;

namespace LoqNova.Avalonia.Views.Pages;

public partial class BatteryPage : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };

    public BatteryPage()
    {
        InitializeComponent();

        // WPF refreshes the battery on a timer that runs only while the page is visible
        // and is cancelled the moment it is hidden (BatteryPage.xaml.cs:25-49). The
        // interval there is two seconds; this is one, so the readouts track the machine
        // closely without a manual Refresh.
        _timer.Tick += OnTick;

        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnAttached(object? sender, EventArgs e)
    {
        _timer.Start();
        Refresh();
    }

    private void OnDetached(object? sender, EventArgs e) => _timer.Stop();

    private void OnTick(object? sender, EventArgs e) => Refresh();

    /// <summary>
    /// Re-reads the battery. Failures are swallowed here because a transient WMI error
    /// must not tear down a timer that would otherwise keep the page live.
    /// </summary>
    private void Refresh()
    {
        if (DataContext is not BatteryViewModel vm)
        {
            return;
        }

        _ = vm.RefreshCommand.ExecuteAsync(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}