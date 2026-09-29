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

    /// <summary>
    /// Routes each zone card's "use as source" gesture to the ViewModel, which
    /// decides the colour that the single "Synchronise All Zones" action applies.
    /// Attaching on every DataContextChanged would multiply the handler, so each
    /// page instance wires itself exactly once.
    /// </summary>
    private void AttachZonePickers(KeyboardBacklightViewModel vm) => AttachZonePickers();

    private bool _zonePickersAttached;

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

            picker.SourceRequested += (_, _) =>
            {
                if (DataContext is KeyboardBacklightViewModel vm)
                    vm.SelectSourceZone(zone);
            };
        }
    }
}