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

        AttachZonePickers();
    }

    private bool _zonePickersAttached;

    /// <summary>
    /// Routes each zone's "Synchronise zones" action to the ViewModel. WPF exposes
    /// this per zone and applies the clicked zone's colour to all four in one state
    /// write. Handlers are attached once per page instance, so navigating back and
    /// forth cannot multiply them.
    /// </summary>
    private void AttachZonePickers()
    {
        if (_zonePickersAttached)
            return;

        _zonePickersAttached = true;
        Wire("Zone1", 1);
        Wire("Zone2", 2);
        Wire("Zone3", 3);
        Wire("Zone4", 4);

        void Wire(string name, int zone)
        {
            if (this.FindControl<ZoneColorPicker>(name) is not { } picker)
                return;

            picker.SynchroniseRequested += (_, _) =>
            {
                if (DataContext is KeyboardBacklightViewModel vm)
                    _ = vm.SynchroniseZonesCommand.ExecuteAsync(zone);
            };
        }
    }
}