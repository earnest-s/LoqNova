using Avalonia.Media;
using LoqNova.Avalonia.Services;
using LoqNova.Avalonia.ViewModels.Pages;
using LoqNova.Lib;
using Xunit;

namespace LoqNova.Avalonia.Tests;

/// <summary>
/// Guards the deterministic inventory of the Settings page against the WPF source of
/// truth (<c>LoqNova.WPF/Pages/SettingsPage.xaml.cs</c>): the option lists are the enum
/// values in enum order, exactly as WPF builds them.
/// <para>
/// <see cref="SettingsViewModel"/>'s constructor only builds the static option lists and
/// never touches the injected services; those services are resolved later in
/// <c>RefreshAsync</c>, which needs the real container and hardware. Passing nulls here
/// keeps the test hermetic while still exercising the constructor's real code path.
/// </para>
/// </summary>
public class SettingsViewModelInventoryTests
{
    private static SettingsViewModel Create() => new(null!, null!, null!, null!, null!);

    [Fact]
    public void Temperature_offers_celsius_then_fahrenheit()
    {
        var vm = Create();

        Assert.Equal(
            new[] { TemperatureUnit.C, TemperatureUnit.F },
            vm.TemperatureUnits.Select(o => o.Value));

        Assert.All(vm.TemperatureUnits, o => Assert.False(string.IsNullOrWhiteSpace(o.Label)));
    }

    [Fact]
    public void Theme_option_value_type_is_AppTheme_and_follows_enum_order()
    {
        var vm = Create();

        // The binding is SelectedValue={Binding Theme} with SelectedValueBinding=Value,
        // so the option value must be AppTheme (not LoqNova.Lib.Theme) or the dropdown
        // would silently select nothing.
        Assert.Equal(typeof(AppTheme), typeof(ThemeOption).GetProperty(nameof(ThemeOption.Value))!.PropertyType);
        Assert.Equal(Enum.GetValues<AppTheme>(), vm.Themes.Select(o => o.Value));
        Assert.All(vm.Themes, o => Assert.False(string.IsNullOrWhiteSpace(o.Label)));
    }

    [Fact]
    public void Remaining_combo_options_follow_enum_order()
    {
        var vm = Create();

        Assert.Equal(Enum.GetValues<AccentColorSource>(), vm.AccentColorSources.Select(o => o.Value));
        Assert.Equal(Enum.GetValues<AutorunState>(), vm.AutorunStates.Select(o => o.Value));
        Assert.Equal(Enum.GetValues<PowerModeMappingMode>(), vm.PowerModeMappingOptions.Select(o => o.Value));
    }

    [Fact]
    public void Smart_fn_lock_offers_exactly_the_three_wpf_values_in_order()
    {
        var vm = Create();

        Assert.Equal(
            new[] { ModifierKey.None, ModifierKey.Alt, ModifierKey.Alt | ModifierKey.Ctrl | ModifierKey.Shift },
            vm.SmartFnLockOptions.Select(o => o.Value));

        // WPF shows the plain "Off" label for ModifierKey.None.
        Assert.Equal("Off", vm.SmartFnLockOptions[0].Label);
    }

    [Fact]
    public void Defaults_match_the_wpf_initial_state()
    {
        var vm = Create();

        Assert.False(vm.AreSmartKeyFeaturesVisible);
        Assert.False(vm.IsAccentColorPickerVisible);
        Assert.False(vm.ShowPowerModes);
        Assert.False(vm.ShowWindowsPowerPlans);
        Assert.False(vm.IsPowerModeMappingSupported);
        Assert.Equal(Color.Parse("#0078D4"), vm.SelectedAccentColor);
    }
}
