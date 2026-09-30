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
    /// Overrides the slider range for points the backend gave no speed ladder for.
    /// WPF builds its sliders as 0-10 regardless (<c>GenerateSlider(i, 0, 10)</c>), so
    /// the unbacked slots keep that same range rather than collapsing to a dead zero.
    /// </summary>
    public int? MaxSpeedIndexOverride { get; init; }

    /// <summary>Effective slider maximum.</summary>
    public int EffectiveMaxSpeedIndex => MaxSpeedIndexOverride ?? MaxSpeedIndex;

    /// <summary>Temperature this point applies at, taken from <c>Temps</c>.</summary>
    public string TemperatureLabel { get; init; } = string.Empty;

    /// <summary>Human-readable identity of the fan/sensor pair this curve drives.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Index of this point within the fan table.</summary>
    public int Index { get; init; }

    /// <summary>RPM the selected step corresponds to, or 0 when nothing is selected.</summary>
    public string SpeedLabel => StepCount > 0 && SpeedIndex >= 0 && SpeedIndex < Speeds.Length
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

        for (var i = 0; i < data.Length; i++)
        {
            var entry = data[i];
            var speeds = entry.FanSpeeds ?? [];
            var selected = i < tableValues.Length ? tableValues[i] : 0;
            if (selected > Math.Max(0, speeds.Length - 1))
            {
                selected = 0;
            }

            Points.Add(new FanCurvePointViewModel
            {
                Index = i,
                Speeds = speeds,
                StepCount = speeds.Length,
                // WPF reads the temperature from the same index it uses for the speed.
                TemperatureLabel = entry.Temps is { Length: > 0 } temps && i < temps.Length
                    ? $"{temps[i]}°C"
                    : string.Empty,
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
        // only overwrite the slots this dialog actually edits.
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
