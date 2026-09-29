using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoqNova.Avalonia.ViewModels.Controls;

/// <summary>
/// One telemetry channel, mirroring a row of WPF's <c>SensorsControl</c>. The value
/// and its maximum both come from the library's <c>SensorData</c>; a maximum of -1
/// (or a value of -1) means the machine did not report the channel, which is shown
/// as "--" with an empty progress bar rather than as a reading.
/// </summary>
public partial class SensorMetricViewModel : ViewModelBase
{
    public string Label { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;

    /// <summary>Accent brush key from the design system (SensorCpuBrush, ...).</summary>
    public string AccentKey { get; init; } = "SensorCpuBrush";

    public string IconKey { get; init; } = "Cpu";

    private string _valueText = "--";
    public string ValueText
    {
        get => _valueText;
        private set => SetProperty(ref _valueText, value);
    }

    private double _ratio;
    public double Ratio
    {
        get => _ratio;
        private set => SetProperty(ref _ratio, value);
    }

    private bool _isReported;
    public bool IsReported
    {
        get => _isReported;
        private set => SetProperty(ref _isReported, value);
    }

    private string? _maximumText;
    public string? MaximumText
    {
        get => _maximumText;
        private set => SetProperty(ref _maximumText, value);
    }

    /// <summary>
    /// Applies one reading. Mirrors WPF's <c>UpdateValue</c>, which collapses the
    /// channel only when the maximum or the value is negative and otherwise renders
    /// the value, the bar and the maximum - so a genuine zero is shown as a reading
    /// rather than blanked.
    /// </summary>
    public void Update(int value, int maximum)
    {
        var reported = value >= 0 && maximum >= 0;

        IsReported = reported;
        ValueText = reported
            ? value.ToString("F0", CultureInfo.CurrentCulture)
            : "-";

        // WPF drives the bar from value/maximum. A zero maximum yields a degenerate
        // bar, so the ratio is pinned to 0 rather than dividing.
        Ratio = reported && maximum > 0
            ? Math.Clamp((double)value / maximum, 0d, 1d)
            : 0d;

        MaximumText = maximum >= 0
            ? maximum.ToString("F0", CultureInfo.CurrentCulture)
            : null;
    }
}

/// <summary>The metric set for one device, in WPF's display order.</summary>
public sealed class SensorMetricGroupViewModel
{
    public string Title { get; init; } = string.Empty;
    public ObservableCollection<SensorMetricViewModel> Metrics { get; } = new();
}
