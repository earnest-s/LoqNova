using LoqNova.Lib.Settings;

namespace LoqNova.Avalonia.Settings;

/// <summary>
/// Dashboard configuration, mirroring the WPF <c>DashboardSettings</c> store so both
/// front ends read and write the same <c>dashboard.json</c>. The type name matches
/// WPF's because the store is serialized with <c>TypeNameHandling.Auto</c>, so
/// changing it would orphan existing user settings.
/// </summary>
public class DashboardSettings() : AbstractSettings<DashboardSettings.DashboardSettingsStore>("dashboard.json")
{
    public class DashboardSettingsStore
    {
        public bool ShowSensors { get; set; } = true;

        /// <summary>Sensor poll interval in seconds. WPF offers 1, 2, 3 and 5.</summary>
        public int SensorsRefreshIntervalSeconds { get; set; } = 1;

        /// <summary>
        /// Dashboard group composition, serialised as WPF's <c>DashboardGroup</c>
        /// records. Null means "use the default groups".
        /// </summary>
        public DashboardGroupRecord[]? Groups { get; set; }
    }

    protected override DashboardSettingsStore Default => new();

    /// <summary>Persists the current store.</summary>
    public void Save() => SynchronizeStore();
}
