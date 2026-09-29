// TEMP-WRITEPATH-VERIFY
// Substitutes only IFeature<AlwaysOnUSBState> so the Avalonia write path can be
// observed without elevation. The real AlwaysOnUSBState enum and the real widget
// are used. Nothing here is production code.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using LoqNova.Lib;
using LoqNova.Lib.Features;

namespace LoqNova.Avalonia;

internal sealed class RecordingUsbFeature : IFeature<AlwaysOnUSBState>
{
    public AlwaysOnUSBState State { get; set; } = AlwaysOnUSBState.Off;
    public List<AlwaysOnUSBState> Writes { get; } = new();
    public bool ThrowOnWrite { get; set; }

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task<AlwaysOnUSBState[]> GetAllStatesAsync() =>
        Task.FromResult<AlwaysOnUSBState[]>([AlwaysOnUSBState.Off, AlwaysOnUSBState.OnWhenSleeping, AlwaysOnUSBState.OnAlways]);

    public Task<AlwaysOnUSBState> GetStateAsync() => Task.FromResult(State);

    public Task SetStateAsync(AlwaysOnUSBState state)
    {
        if (ThrowOnWrite)
            throw new InvalidOperationException("simulated EC write failure");

        Writes.Add(state);
        State = state;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingHdrFeature : IFeature<HDRState>
{
    public HDRState State { get; set; } = HDRState.Off;
    public List<HDRState> Writes { get; } = new();

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);
    public Task<HDRState[]> GetAllStatesAsync() => Task.FromResult<HDRState[]>([HDRState.Off, HDRState.On]);
    public Task<HDRState> GetStateAsync() => Task.FromResult(State);
    public Task SetStateAsync(HDRState state) { Writes.Add(state); State = state; return Task.CompletedTask; }
}

internal static class WritePathVerify
{
    public static async Task<string> RunAsync()
    {
        var log = new List<string>();
        var dispatcher = new LoqNova.Avalonia.Services.PassThroughDispatcherForVerify();

        // ---- Always On USB choice widget ----
        var usb = new RecordingUsbFeature();
        var vm = new LoqNova.Avalonia.ViewModels.Controls.FeatureChoiceWidgetViewModel<AlwaysOnUSBState>(
            dispatcher, "Always On USB", "Usb64");
        Inject(vm, usb);
        await vm.InitializeAsync();
        log.Add($"init: options=[{string.Join(",", vm.Options)}] selected={vm.SelectedState} backend={usb.State} available={vm.IsAvailable}");

        vm.SelectedState = AlwaysOnUSBState.OnAlways;
        await Task.Delay(150);
        log.Add($"select OnAlways -> writes=[{string.Join(",", usb.Writes)}] backend={usb.State} (expect 1 write)");

        vm.SelectedState = AlwaysOnUSBState.OnWhenSleeping;
        await Task.Delay(150);
        log.Add($"select OnWhenSleeping -> writes=[{string.Join(",", usb.Writes)}] (expect 2 writes)");

        var before = usb.Writes.Count;
        vm.SelectedState = AlwaysOnUSBState.OnWhenSleeping;
        await Task.Delay(120);
        log.Add($"same value reselected -> newWrites={usb.Writes.Count - before} (expect 0)");

        var writesBeforePublish = usb.Writes.Count;
        usb.State = AlwaysOnUSBState.OnAlways;
        await vm.RefreshAsync();
        await Task.Delay(120);
        log.Add($"backend publish -> selected={vm.SelectedState} newWrites={usb.Writes.Count - writesBeforePublish} (expect 0)");

        usb.ThrowOnWrite = true;
        vm.SelectedState = AlwaysOnUSBState.Off;
        await Task.Delay(250);
        log.Add($"failed write -> selected={vm.SelectedState} backend={usb.State} errorSet={!string.IsNullOrEmpty(vm.ErrorMessage)} (expect selected=OnAlways, no fake success)");

        // ---- HDR toggle widget ----
        var hdr = new RecordingHdrFeature();
        var toggle = new LoqNova.Avalonia.ViewModels.Controls.FeatureToggleWidgetViewModel<HDRState>(
            dispatcher, "HDR", "Hdr64", HDRState.On, HDRState.Off);
        Inject(toggle, hdr);
        await toggle.InitializeAsync();
        var tw = hdr.Writes.Count;
        hdr.State = HDRState.On;
        await toggle.RefreshAsync();
        await Task.Delay(120);
        log.Add($"toggle adopt -> IsOn={toggle.IsOn} newWrites={hdr.Writes.Count - tw} (expect 0)");
        toggle.IsOn = false;
        await Task.Delay(200);
        log.Add($"toggle user off -> writes=[{string.Join(",", hdr.Writes)}] (expect Off)");

        return string.Join("\n", log);
    }

    private static void Inject(object vm, object feature)
    {
        var type = vm.GetType();
        while (type is not null)
        {
            var field = type.GetField("_feature", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null)
            {
                field.SetValue(vm, feature);
                return;
            }

            type = type.BaseType;
        }
    }
}
