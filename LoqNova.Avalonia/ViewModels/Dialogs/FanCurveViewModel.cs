using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LoqNova.Lib;

namespace LoqNova.Avalonia.ViewModels.Dialogs;

/// <summary>
/// One temperature point of a fan curve, reproducing WPF's <c>FanCurveControl</c>
/// slider: the position selects a step from that point's <c>FanSpeeds</c> array, and
/// the chosen step is the RPM the fan runs at that temperature.
/// </summary>
public partial class FanCurvePointViewModel : ViewModelBase
{
    /// <summary>Speed step the user has selected, indexing into <c>FanSpeeds</c>.</summary>
    [ObservableProperty]
    private int _speedIndex;

    /// <summary>Number of selectable steps, i.e. the length of <c>FanSpeeds</c>.</summary>
    public int StepCount { get; init; }

    /// <summary>Highest valid index, for use as a slider maximum.</summary>
    public int MaxSpeedIndex => Math.Max(0, StepCount - 1);

    /// <summary>
    /// The RPM values this point can actually be set to, in order. The table only
    /// stores an index into this ladder, so these are the real stops the slider has and
    /// no amount of UI work invents finer ones.
    /// </summary>
    public IReadOnlyList<ushort> Stops => Speeds;

    public bool HasStops => Speeds.Length > 0;

    /// <summary>Temperature this point applies at, taken from <c>Temps</c>.</summary>
    public string TemperatureLabel { get; init; } = string.Empty;

    /// <summary>Human-readable identity of the fan/sensor pair this curve drives.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Index of this point within the fan table.</summary>
    public int Index { get; init; }

    /// <summary>Whether this point drives the GPU fan, rather than the CPU fan.</summary>
    public bool IsGpuFan { get; init; }

    /// <summary>
    /// Live reading from the fan this point drives, in RPM. The curve itself only holds
    /// a target, so without this the number next to the slider looks like the current
    /// speed when it is not.
    /// </summary>
    [ObservableProperty]
    private int _liveFanSpeed = -1;

    /// <summary>The live reading, or a dash while the sensor has no value yet.</summary>
    public string LiveSpeedLabel => LiveFanSpeed < 0 ? "—" : $"{LiveFanSpeed} RPM";

    /// <summary>The configured target, kept for the tooltip.</summary>
    public string SpeedLabel => Speeds.Length == 0
        ? "—"
        : StepCount > 0 && SpeedIndex >= 0 && SpeedIndex < Speeds.Length
            ? $"{Speeds[SpeedIndex]} RPM"
            : "0 RPM";

    /// <summary>The steps this point can take, from the backend's fan table.</summary>
    public ushort[] Speeds { get; init; } = [];
}

/// <summary>
/// The fan curve section of Custom Mode settings, reproducing WPF's
/// <c>FanCurveControl</c>: a slider per temperature point and a curve drawn through
/// them. Values are held locally and written back into the preset on save, which is
/// how WPF persists them too — the controller interface has no separate setter.
/// </summary>
public partial class FanCurveViewModel : ViewModelBase
{
    public ObservableCollection<FanCurvePointViewModel> Points { get; } = new();

    private ISensorsService? _sensors;

    /// <summary>
    /// Subscribes to the live sensor feed so each point can show the speed its fan is
    /// actually running at. The previous handler is always dropped first, so repeated
    /// <see cref="Adopt"/> calls cannot stack subscriptions on the shared service.
    /// </summary>
    public void Attach(ISensorsService sensors)
    {
        if (ReferenceEquals(_sensors, sensors))
        {
            RefreshLiveSpeeds();
            return;
        }

        Detach();
        _sensors = sensors;
        _sensors.Updated += RefreshLiveSpeeds;
        RefreshLiveSpeeds();
    }

    public void Detach()
    {
        if (_sensors is not null)
        {
            _sensors.Updated -= RefreshLiveSpeeds;
            _sensors = null;
        }
    }

    private void RefreshLiveSpeeds()
    {
        var sensors = _sensors;
        if (sensors is null)
        {
            return;
        }

        var cpu = sensors.CpuFanSpeed;
        var gpu = sensors.GpuFanSpeed;

        foreach (var point in Points)
        {
            point.LiveFanSpeed = (point.IsGpuFan ? gpu : cpu) is var rpm and >= 0 ? rpm : -1;
        }
    }

    /// <summary>False when the backend reports no fan table, so nothing is shown.</summary>
    [ObservableProperty]
    private bool _isSupported;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Populates the curve from the preset's fan table, as WPF's
    /// <c>SetFanTableInfo</c> does.</summary>
    public void Adopt(FanTableInfo? fanTableInfo)
    {
        Points.Clear();

        if (fanTableInfo is not { } info || info.Data is not { Length: > 0 } data)
        {
            IsSupported = false;
            OnPropertyChanged(nameof(IsSupported));
            return;
        }

        // The table carries the selected step per point, one entry per temperature
        // slot. WPF seeds each slider from this: slider.Value = tableValues[i], and
        // the RPM shown is FanSpeeds[that value]. Reading a fixed 0 here is what made
        // every point report the bottom of the ladder (1400 RPM on this machine).
        var tableValues = info.Table.GetTable();

        // One row per fan the backend actually reports. The table has ten slots
        // (FSS0..FSS9), but the extra ones carry no fan, so rendering them only
        // produced empty rows for hardware that does not exist.
        for (var i = 0; i < data.Length; i++)
        {
            var entry = data[i];
            var speeds = entry.FanSpeeds ?? [];
            var selected = i < tableValues.Length ? tableValues[i] : 0;
            var max = Math.Max(0, speeds.Length - 1);

            if (selected > max)
            {
                selected = 0;
            }

            var label = entry.Temps is { Length: > 0 } temps && i < temps.Length
                ? $"{temps[i]}°C"
                : $"{i + 1}";

            Points.Add(new FanCurvePointViewModel
            {
                Index = i,
                IsGpuFan = entry.Type.ToString().StartsWith("GPU", StringComparison.Ordinal),
                Speeds = speeds,
                StepCount = speeds.Length,
                TemperatureLabel = label,
                Description = $"{entry.Type} fan {entry.FanId} / sensor {entry.SensorId}",
                SpeedIndex = selected
            });
        }

        IsSupported = Points.Count > 0;
        OnPropertyChanged(nameof(IsSupported));
    }

    /// <summary>
    /// Rebuilds the edited fan table for the preset. WPF does the equivalent in
    /// <c>GetFanTableInfo</c>: the slider values become the new <c>FanTable</c> while
    /// the per-fan speed ladders in <c>Data</c> are passed through untouched.
    /// </summary>
    public FanTableInfo? BuildFanTableInfo(FanTableInfo? original)
    {
        if (original is not { } source || source.Data is not { Length: > 0 } data
            || Points.Count != data.Length)
        {
            return original;
        }

        // The table is wider than the data set, so start from what the machine has and
        // only overwrite the slots this dialog actually edits. Slots with no reported
        // ladder keep WPF's 0-10 range and write straight back.
        var tableValues = source.Table.GetTable().ToArray();

        for (var i = 0; i < Points.Count && i < tableValues.Length; i++)
        {
            var stepCount = data[i].FanSpeeds?.Length ?? 0;
            var index = Points[i].SpeedIndex;

            tableValues[i] = stepCount > 0 && index >= 0 && index < stepCount
                ? (ushort)index
                : tableValues[i];
        }

        return new FanTableInfo(data, new FanTable(tableValues));
    }
}
