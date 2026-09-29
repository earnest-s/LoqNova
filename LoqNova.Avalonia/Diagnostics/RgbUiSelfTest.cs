using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using LoqNova.Avalonia.Services;
using LoqNova.Avalonia.ViewModels.Pages;
using LoqNova.Avalonia.Views.Controls.RGB;
using LoqNova.Avalonia.Views.Pages;

namespace LoqNova.Avalonia.Diagnostics;

/// <summary>Records every state write so the self-test can prove the click path.</summary>
internal sealed class RecordingRgbService : IRgbService
{
    public List<string> Writes { get; } = new();

    public bool IsSupported => true;
    public bool IsVantageEnabled { get; set; }
    public RgbPreset CurrentPreset { get; set; } = RgbPreset.Preset1;
    public RgbEffect CurrentEffect { get; set; } = RgbEffect.Static;
    public RgbSpeed CurrentSpeed { get; set; } = RgbSpeed.Slow;
    public RgbBrightness CurrentBrightness { get; set; } = RgbBrightness.Low;
    public RgbZoneColor Zone1Color { get; set; } = new(255, 0, 0);
    public RgbZoneColor Zone2Color { get; set; } = new(0, 255, 0);
    public RgbZoneColor Zone3Color { get; set; } = new(0, 0, 255);
    public RgbZoneColor Zone4Color { get; set; } = new(0, 255, 255);
    public bool ZonesSynchronized { get; set; }

    public event Action<RgbPreset>? PresetChanged;
    public event Action<RgbEffect>? EffectChanged;
    public event Action<RgbSpeed>? SpeedChanged;
    public event Action<RgbBrightness>? BrightnessChanged;
    public event Action<int, RgbZoneColor>? ZoneColorChanged;
    public event Action<RgbZoneColor, RgbZoneColor, RgbZoneColor, RgbZoneColor>? FrameRendered;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task SetPresetAsync(RgbPreset preset)
    {
        CurrentPreset = preset;
        Writes.Add($"SetPreset({preset})");
        PresetChanged?.Invoke(preset);
        return Task.CompletedTask;
    }

    public Task SaveStateAsync(
        RgbEffect effect, RgbSpeed speed, RgbBrightness brightness,
        RgbZoneColor zone1, RgbZoneColor zone2, RgbZoneColor zone3, RgbZoneColor zone4)
    {
        CurrentEffect = effect;
        CurrentSpeed = speed;
        CurrentBrightness = brightness;
        Zone1Color = zone1;
        Zone2Color = zone2;
        Zone3Color = zone3;
        Zone4Color = zone4;
        Writes.Add($"SaveState(effect={effect}, z1={zone1}, z2={zone2}, z3={zone3}, z4={zone4})");
        EffectChanged?.Invoke(effect);
        return Task.CompletedTask;
    }

    public Task SynchroniseZonesAsync(RgbZoneColor color) => SaveStateAsync(
        CurrentEffect, CurrentSpeed, CurrentBrightness, color, color, color, color);

    public bool SupportsSpeed(RgbEffect effect) => effect is not (RgbEffect.AudioVisualizer or RgbEffect.Strobe);
    public bool SupportsZoneColors(RgbEffect effect) => true;
    public bool IsCustomEffect(RgbEffect effect) => effect >= RgbEffect.Ambient;
    public RgbEffect GetDisplayEffect(RgbEffect effect) => effect;

    public void RaiseFrame(RgbZoneColor z1, RgbZoneColor z2, RgbZoneColor z3, RgbZoneColor z4)
        => FrameRendered?.Invoke(z1, z2, z3, z4);
}

/// <summary>
/// Temporary runtime probe. Measures the real controls instead of trusting bindings,
/// and writes the result to a file the build script reads back.
/// </summary>
internal static class RgbUiSelfTest
{
    private static readonly List<string> Log = new();

    public static async Task RunAsync()
    {
        var dispatcher = new ImmediateDispatcher();
        var service = new RecordingRgbService();
        var vm = new KeyboardBacklightViewModel(service, dispatcher);
        await vm.ApplyStateAsync();

        // ---- Zone palette -------------------------------------------------
        var picker = new ZoneColorPicker { ZoneNumber = 1, IsInteractive = true, Color = new RgbZoneColor(255, 0, 0) };
        picker.Measure(new Size(240, 400));
        picker.Arrange(new Rect(picker.DesiredSize));
        ForceLayout(picker);

        var items = FindAll<ItemsControl>(picker).FirstOrDefault();
        var buttons = FindAll<Button>(picker).ToList();
        var swatches = buttons.Where(b => b.Tag is RgbZoneColor).ToList();
        var labels = FindAll<TextBlock>(picker).Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

        Log.Add($"palette.itemscontrolfound={items is not null}");
        Log.Add($"palette.itemcount={items?.ItemCount}");
        Log.Add($"palette.realisedButtons={buttons.Count}");
        Log.Add($"palette.clickableSwatches={swatches.Count}");
        Log.Add($"palette.labels=[{string.Join("|", labels)}]");
        Log.Add($"palette.zoneLabelPresent={labels.Any(t => t!.Contains("ZONE", StringComparison.OrdinalIgnoreCase))}");
        Log.Add($"palette.syncActionPresent={buttons.Any(b => b.Content?.ToString()?.Contains("Synchronise", StringComparison.OrdinalIgnoreCase) == true)}");

        var preview = FindAll<Border>(picker).FirstOrDefault(b => b.Height == 64);
        Log.Add($"palette.previewBackground={((Avalonia.Media.SolidColorBrush?)preview?.Background)?.Color}");

        // Selected-state indicator for the current colour (red).
        Log.Add($"palette.selectedRingForRed={CountSelected(picker)}");

        // Click a different swatch (the green one) and see whether it reaches the VM.
        var green = swatches.FirstOrDefault(b => b.Tag is RgbZoneColor c && c.G == 255 && c.R == 0);
        if (green is not null)
        {
            green.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Yield();
        }

        Log.Add($"click.pickerColorAfter={picker.Color}");
        Log.Add($"click.vmZone1After={vm.Zone1Color}");
        Log.Add($"click.serviceWrites={service.Writes.Count}");
        Log.Add($"click.lastWrite={service.Writes.LastOrDefault()}");
        Log.Add($"click.reachedViewModel={green is not null && vm.Zone1Color.G == 255 && vm.Zone1Color.R == 0}");

        // ---- Effects traversal --------------------------------------------
        var page = new KeyboardBacklightPage { DataContext = vm };
        page.Measure(new Size(1200, 2000));
        page.Arrange(new Rect(page.DesiredSize));
        ForceLayout(page);

        var combos = FindAll<ComboBox>(page).ToList();
        var effects = combos.FirstOrDefault(c => c.ItemsSource is not null && c.ItemsSource.Cast<object>().Any(o => o is RgbEffect));
        var itemsSource = effects?.ItemsSource?.Cast<object>().ToList() ?? new List<object>();

        Log.Add($"effects.comboboxFound={effects is not null}");
        Log.Add($"effects.itemCount={itemsSource.Count}");
        Log.Add($"effects.items=[{string.Join(",", itemsSource.Cast<RgbEffect>())}]");

        // Walk the list the way keyboard navigation does: select each item in order
        // and record what the ComboBox shows after the ViewModel rehydrates.
        var traversal = new List<string>();
        if (effects is not null)
        {
            foreach (var item in itemsSource)
            {
                effects.SelectedItem = item;
                await Task.Yield();
                await Task.Yield();
                var shown = effects.SelectedItem as RgbEffect? ?? RgbEffect.Static;
                traversal.Add(shown.ToString() ?? "?");
            }
        }

        Log.Add($"effects.traversal=[{string.Join(" -> ", traversal)}]");
        Log.Add($"effects.traversalCount={traversal.Count}");
        Log.Add($"effects.traversalDistinct={traversal.Distinct().Count()}");
        Log.Add($"effects.allReachable={traversal.Distinct().Count() == itemsSource.Count && traversal.Count == itemsSource.Count}");

        await File.WriteAllLinesAsync(
            Path.Combine(Path.GetTempPath(), "rgbselftest.txt"), Log);
    }

    private static void ForceLayout(Control control)
    {
        control.Measure(new Size(1200, 3000));
        control.Arrange(new Rect(control.DesiredSize));
        Dispatcher.UIThread.RunJobs();
    }

    private static int CountSelected(Visual root) => FindAll<Border>(root)
        .Count(b => b.IsVisible && b.BorderThickness.Top == 2);

    private static IEnumerable<T> FindAll<T>(Visual root) where T : Visual
    {
        var found = new List<T>();
        void Walk(Visual v)
        {
            if (v is T t)
                found.Add(t);
            foreach (var child in v.GetVisualChildren())
                Walk(child);
        }

        Walk(root);
        return found;
    }

    private sealed class ImmediateDispatcher : IMainThreadDispatcher
    {
        public bool IsMainThread => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }
}
