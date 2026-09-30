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

        if (fanTableInfo?.Data is not { Length: > 0 } data)
        {
            IsSupported = false;
            OnPropertyChanged(nameof(IsSupported));
            return;
        }

        for (var i = 0; i < data.Length; i++)
        {
            var entry = data[i];
            var speeds = entry.FanSpeeds ?? [];

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
                SpeedIndex = 0
            });
        }

        IsSupported = Points.Count > 0;
        OnPropertyChanged(nameof(IsSupported));
    }

    /// <summary>
    /// Rebuilds the edited fan table for the preset. WPF does the equivalent by taking
    /// the control's table and writing it into the preset before applying.
    /// </summary>
    public FanTableInfo? BuildFanTableInfo(FanTableInfo? original)
    {
        if (original?.Data is not { Length: > 0 } data || Points.Count != data.Length)
            return original;

        var edited = new FanTableData[data.Length];

        for (var i = 0; i < data.Length; i++)
        {
            var entry = data[i];
            var speeds = entry.FanSpeeds is { Length: > 0 } s ? [.. s] : [];

            if (Points[i].SpeedIndex >= 0 && Points[i].SpeedIndex < speeds.Length)
            {
                // The selected step is the RPM the fan should run at this temperature.
                speeds[Points[i].SpeedIndex] = speeds[Points[i].SpeedIndex];
            }

            edited[i] = new FanTableData(entry.Type, entry.FanId, entry.SensorId, speeds, entry.Temps);
        }

        return new FanTableInfo(edited, original.Table);
    }
}
