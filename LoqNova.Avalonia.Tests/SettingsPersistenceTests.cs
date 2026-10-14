using System.Reflection;
using LoqNova.Avalonia.ViewModels.Pages;
using LoqNova.Lib;
using LoqNova.Lib.Settings;
using Xunit;

namespace LoqNova.Avalonia.Tests;

/// <summary>
/// Proves the Settings page's write path is real: a control change made through the view
/// model must mutate the authoritative <see cref="ApplicationSettings"/> store, hit disk,
/// and come back unchanged on the next load (the restart case).
/// <para>
/// The store is the same shared singleton the rest of the app resolves from
/// <c>LoqNova.Lib.IoCContainer</c> (registered <c>SingleInstance</c> by
/// <c>ContainerBuilderExtensions.Register&lt;T&gt;</c>). These tests cannot build that whole
/// container because <c>Lib.IoCModule</c> auto-activates Windows message listeners that
/// need a real desktop, so the singleton is injected directly and its file path is
/// redirected to a temp directory so the developer's own settings.json is never touched.
/// </para>
/// <para>
/// The store defaults matter: <c>MinimizeToTray</c> defaults to <c>true</c>, so the tests
/// move every value to a non-default to prove the change really happened.
/// </para>
/// </summary>
public class SettingsPersistenceTests
{
    private static ApplicationSettings NewStoreAt(string path)
    {
        var settings = new ApplicationSettings();

        // AbstractSettings<T> computes its path from Folders.AppData in the constructor.
        // Repoint it at a temp file so SynchronizeStore()/LoadStore() never touch the
        // real %LocalAppData%\LOQNova\settings.json.
        var pathField = settings.GetType().BaseType!
            .GetField("_settingsStorePath", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_settingsStorePath field not found on AbstractSettings<T>.");
        pathField.SetValue(settings, path);

        return settings;
    }

    private static string NewTempPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loqnova-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    private static (SettingsViewModel Vm, ApplicationSettings Settings, string Path) Create()
    {
        var path = NewTempPath();
        var settings = NewStoreAt(path);

        // The constructor only builds the static option lists; the services are resolved
        // in RefreshAsync. Injecting the store is all the persistence handlers need.
        var vm = new SettingsViewModel(null!, null!, null!, null!, null!);
        var field = typeof(SettingsViewModel)
            .GetField("_applicationSettings", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_applicationSettings field not found on SettingsViewModel.");
        field.SetValue(vm, settings);

        // Mirror what RefreshAsync does on navigation: push the store values into the bound
        // properties while the refresh guard is set. Without this the view model would still
        // be at its C# field defaults (e.g. MinimizeToTray == false while the store says
        // true), and a first "edit" to a value equal to the field default would be a no-op.
        var refreshing = typeof(SettingsViewModel)
            .GetField("_isRefreshing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        refreshing.SetValue(vm, true);
        vm.TemperatureUnit = settings.Store.TemperatureUnit;
        vm.MinimizeToTray = settings.Store.MinimizeToTray;
        vm.MinimizeOnClose = settings.Store.MinimizeOnClose;
        vm.SyncBrightnessToAllPowerPlans = settings.Store.SynchronizeBrightnessToAllPowerPlans;
        vm.SmartFnLockFlags = settings.Store.SmartFnLockFlags;
        vm.IsResetBatterySinceEnabled = settings.Store.ResetBatteryOnSinceTimerOnReboot;
        vm.PowerModeMappingMode = settings.Store.PowerModeMappingMode;
        refreshing.SetValue(vm, false);

        return (vm, settings, path);
    }

    [Fact]
    public void Dropdown_and_toggle_changes_reach_the_store_write_to_disk_and_survive_a_reload()
    {
        var (vm, settings, path) = Create();

        // Every one of these is a control that WPF persists immediately (no Save button).
        // Values are deliberately moved off their defaults.
        vm.TemperatureUnit = TemperatureUnit.F;                                  // default C
        vm.MinimizeToTray = false;                                               // default true
        vm.MinimizeOnClose = true;                                               // default false
        vm.SyncBrightnessToAllPowerPlans = true;                                 // default false
        vm.SmartFnLockFlags = ModifierKey.Alt;                                   // default None
        vm.IsResetBatterySinceEnabled = true;                                    // default false
        vm.PowerModeMappingMode = PowerModeMappingMode.WindowsPowerPlan;         // default WindowsPowerMode

        // 1. The authoritative in-memory store reflects the change.
        Assert.Equal(TemperatureUnit.F, settings.Store.TemperatureUnit);
        Assert.False(settings.Store.MinimizeToTray);
        Assert.True(settings.Store.MinimizeOnClose);
        Assert.True(settings.Store.SynchronizeBrightnessToAllPowerPlans);
        Assert.Equal(ModifierKey.Alt, settings.Store.SmartFnLockFlags);
        Assert.True(settings.Store.ResetBatteryOnSinceTimerOnReboot);
        Assert.Equal(PowerModeMappingMode.WindowsPowerPlan, settings.Store.PowerModeMappingMode);

        // 2. The change was written to disk (not just held in memory).
        Assert.True(File.Exists(path), $"Expected settings to be persisted to {path}.");
        var json = File.ReadAllText(path);
        Assert.Contains("\"F\"", json);
        Assert.Contains("\"Alt\"", json);
        Assert.Contains("\"MinimizeToTray\": false", json);
        Assert.Contains("\"MinimizeOnClose\": true", json);

        // 3. A fresh load from the same file (the restart case) restores them.
        var reloaded = NewStoreAt(path);
        Assert.Equal(TemperatureUnit.F, reloaded.Store.TemperatureUnit);
        Assert.False(reloaded.Store.MinimizeToTray);
        Assert.True(reloaded.Store.MinimizeOnClose);
        Assert.True(reloaded.Store.SynchronizeBrightnessToAllPowerPlans);
        Assert.Equal(ModifierKey.Alt, reloaded.Store.SmartFnLockFlags);
        Assert.True(reloaded.Store.ResetBatteryOnSinceTimerOnReboot);
        Assert.Equal(PowerModeMappingMode.WindowsPowerPlan, reloaded.Store.PowerModeMappingMode);
    }

    [Fact]
    public void Toggling_a_switch_both_ways_both_persist()
    {
        var (vm, settings, path) = Create();

        // default true -> false
        vm.MinimizeToTray = false;
        Assert.False(settings.Store.MinimizeToTray);

        // and back again
        vm.MinimizeToTray = true;
        Assert.True(settings.Store.MinimizeToTray);

        var reloaded = NewStoreAt(path);
        Assert.True(reloaded.Store.MinimizeToTray);
    }

    [Fact]
    public void Edits_made_while_the_page_is_refreshing_are_ignored()
    {
        // RefreshAsync sets _isRefreshing = true while it pushes backend values into the
        // bound properties. Those programmatic assignments must not write back to disk.
        var (vm, settings, _) = Create();

        var unitBefore = settings.Store.TemperatureUnit;   // C
        var trayBefore = settings.Store.MinimizeToTray;    // true

        typeof(SettingsViewModel)
            .GetField("_isRefreshing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, true);

        vm.TemperatureUnit = unitBefore == TemperatureUnit.C ? TemperatureUnit.F : TemperatureUnit.C;
        vm.MinimizeToTray = !trayBefore;

        Assert.Equal(unitBefore, settings.Store.TemperatureUnit);
        Assert.Equal(trayBefore, settings.Store.MinimizeToTray);
    }
}
