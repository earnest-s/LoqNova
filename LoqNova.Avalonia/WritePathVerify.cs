// TEMP-WRITEPATH-VERIFY
// Exercises the AVALONIA write path only. InitializeAsync resolves the real feature
// from the shared container, so the recorder is injected afterwards and the public
// availability flag is set directly, which isolates the selection -> write path
// from container resolution. The real AlwaysOnUSBState / HDRState enums are used.
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

        // ---------- Always On USB (choice widget) ----------
        var usb = new RecordingUsbFeature();
        var vm = new LoqNova.Avalonia.ViewModels.Controls.FeatureChoiceWidgetViewModel<AlwaysOnUSBState>(
            dispatcher, "Always On USB", "Usb64");

        Inject(vm, usb);
        vm.IsAvailable = true;
        vm.Options.Add(AlwaysOnUSBState.Off);
        vm.Options.Add(AlwaysOnUSBState.OnWhenSleeping);
        vm.Options.Add(AlwaysOnUSBState.OnAlways);
        vm.SelectedState = AlwaysOnUSBState.Off;

        log.Add($"start: selected={vm.SelectedState} backend={usb.State} available={vm.IsAvailable} busy={vm.IsBusy}");

        vm.SelectedState = AlwaysOnUSBState.OnAlways;
        vm.RequestState(AlwaysOnUSBState.OnAlways);
        await Task.Delay(200);
        log.Add($"select OnAlways  -> writes=[{string.Join(",", usb.Writes)}] backend={usb.State}  EXPECT write=1");

        vm.SelectedState = AlwaysOnUSBState.OnWhenSleeping;
        vm.RequestState(AlwaysOnUSBState.OnWhenSleeping);
        await Task.Delay(200);
        log.Add($"select OnSleeping -> writes=[{string.Join(",", usb.Writes)}]  EXPECT write=2");

        var sameBefore = usb.Writes.Count;
        vm.RequestState(AlwaysOnUSBState.OnWhenSleeping);
        await Task.Delay(150);
        log.Add($"same value again  -> newWrites={usb.Writes.Count - sameBefore}  EXPECT 0");

        // backend publishes a change: must be adopted, never written back
        var publishBefore = usb.Writes.Count;
        usb.State = AlwaysOnUSBState.OnAlways;
        await vm.RefreshAsync();
        await Task.Delay(150);
        log.Add($"backend publish   -> selected={vm.SelectedState} newWrites={usb.Writes.Count - publishBefore}  EXPECT 0 writes, selected=OnAlways");

        // failed write must not leave the UI claiming the new state
        usb.ThrowOnWrite = true;
        vm.SelectedState = AlwaysOnUSBState.Off;
        vm.RequestState(AlwaysOnUSBState.Off);
        await Task.Delay(300);
        log.Add($"failed write      -> selected={vm.SelectedState} backend={usb.State} errorSet={!string.IsNullOrEmpty(vm.ErrorMessage)}  EXPECT selected=OnAlways");

        // ---------- HDR (toggle widget) ----------
        var hdr = new RecordingHdrFeature();
        var toggle = new LoqNova.Avalonia.ViewModels.Controls.FeatureToggleWidgetViewModel<HDRState>(
            dispatcher, "HDR", "Hdr64", HDRState.On, HDRState.Off);
        Inject(toggle, hdr);
        toggle.IsAvailable = true;
        toggle.IsOn = false;

        var adoptBefore = hdr.Writes.Count;
        hdr.State = HDRState.On;
        await toggle.RefreshAsync();
        await Task.Delay(150);
        log.Add($"toggle adopt      -> IsOn={toggle.IsOn} newWrites={hdr.Writes.Count - adoptBefore}  EXPECT 0 writes, IsOn=True");

        toggle.IsOn = false;
        toggle.RequestOn(false);
        await Task.Delay(250);
        log.Add($"toggle user off   -> writes=[{string.Join(",", hdr.Writes)}]  EXPECT Off");

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
