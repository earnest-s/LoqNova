using System;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LoqNova.Avalonia.ViewModels.Pages;

namespace LoqNova.Avalonia.Views.Pages;

public partial class DashboardPage : UserControl
{
    public DashboardPage()
    {
        InitializeComponent();

        // Mirrors WPF's SensorsControl: the sensor refresh loop only runs while the
        // page is actually visible, and is stopped when the page is hidden.
        this.GetObservable(Visual.IsVisibleProperty)
            .Subscribe(_ => ApplyVisibility());

        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e) => ApplyVisibility();

    /// <summary>
    /// Forwards a user's power-mode choice. The combo is bound OneWay, so this is the
    /// only path that issues a write, and it never compares against the already
    /// updated property: the chosen item is sent straight to the service, which
    /// writes to the backend and then re-reads the real state.
    /// </summary>
    private void OnPowerModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: LoqNova.Avalonia.ViewModels.Pages.DashboardViewModel vm })
            return;

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is LoqNova.Lib.PowerModeState requested)
        {
            _ = vm.RequestPowerModeAsync(requested);
        }
    }


    /// <summary>
    /// Forwards a user's combo selection to the widget. The binding is OneWay, so
    /// the widget stays the single source of truth for what is displayed and this
    /// is the only path that issues a backend write.
    /// </summary>
    private void OnWidgetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: LoqNova.Avalonia.ViewModels.Controls.FeatureWidgetViewModel widget }
            && e.AddedItems.Count > 0
            && e.AddedItems[0] is { } selected)
        {
            widget.RequestStateFrom(selected);
        }
    }

    /// <summary>
    /// Forwards a user's toggle. <c>Click</c> is used rather than a property-changed
    /// callback because it unambiguously means the user acted, which is what the
    /// widget needs in order not to write back a value it just read from the
    /// hardware.
    /// </summary>
    private void OnWidgetToggleClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { IsChecked: { } checkedValue, DataContext: LoqNova.Avalonia.ViewModels.Controls.FeatureWidgetViewModel widget })
        {
            widget.RequestOn(checkedValue);
        }
    }

    private void ApplyVisibility()
    {
        if (DataContext is not DashboardViewModel vm)
            return;

        if (IsVisible)
            _ = vm.ResumeSensorsAsync();
        else
            vm.PauseSensors();
    }
}
