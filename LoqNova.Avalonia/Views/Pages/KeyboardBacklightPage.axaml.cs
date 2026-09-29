using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoqNova.Avalonia.ViewModels.Pages;
using LoqNova.Avalonia.Views.Controls.RGB;

namespace LoqNova.Avalonia.Views.Pages;

public partial class KeyboardBacklightPage : UserControl
{
    public KeyboardBacklightPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not KeyboardBacklightViewModel vm)
            return;

        // Hydrate from the authoritative backend state when the page opens, so the
        // controls never start on invented defaults.
        _ = vm.ApplyStateAsync();

        AttachZonePickers(vm);
    }

    /// <summary>
    /// Wires each zone once. The colour is edited through the built-in picker bound
    /// straight to the ViewModel, so a change takes the single state-write path; the
    /// context menu carries WPF's explicit "Synchronise zones" action.
    /// </summary>
    private void AttachZonePickers(KeyboardBacklightViewModel vm)
    {
        Wire("Zone1", 1, vm);
        Wire("Zone2", 2, vm);
        Wire("Zone3", 3, vm);
        Wire("Zone4", 4, vm);

        void Wire(string name, int zone, KeyboardBacklightViewModel target)
        {
            if (this.FindControl<ZoneColorPicker>(name) is not { } picker)
                return;

            picker.SynchroniseRequested += (_, _) => _ = target.SynchroniseZonesCommand.ExecuteAsync(zone);
        }
    }
}