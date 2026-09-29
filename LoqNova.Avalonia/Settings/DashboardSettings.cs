using LoqNova.Lib.Settings;
using Newtonsoft.Json.Linq;

namespace LoqNova.Avalonia.Settings;

/// <summary>
/// Dashboard configuration, mirroring the WPF <c>DashboardSettings</c> store so both
/// front ends read and write the same <c>dashboard.json</c>.
/// <para>
/// The store is deserialized with <c>TypeNameHandling.Auto</c>. WPF's
/// <c>DashboardGroup</c> lives in the WPF assembly, so it cannot be deserialized
/// here. <see cref="DashboardSettingsStore.Groups"/> is therefore kept as a
/// <see cref="JArray"/>: that round-trips the existing file without losing the user's
/// saved composition, and the typed group model is applied once the dashboard
/// editor is ported.
/// </para>
/// </summary>
public class DashboardSettings() : AbstractSettings<DashboardSettings.DashboardSettingsStore>("dashboard.json")
{
    public class DashboardSettingsStore
    {
        public bool ShowSensors { get; set; } = true;

        /// <summary>Sensor poll interval in seconds. WPF offers 1, 2, 3 and 5.</summary>
        public int SensorsRefreshIntervalSeconds { get; set; } = 1;

        /// <summary>
        /// WPF's group composition, preserved verbatim. Written by the WPF
        /// dashboard editor; the Avalonia editor is not implemented yet.
        /// </summary>
        public JArray? Groups { get; set; }
    }

    protected override DashboardSettingsStore Default => new();

    /// <summary>Persists the current store.</summary>
    public void Save() => SynchronizeStore();
}
