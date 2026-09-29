using System;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
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
