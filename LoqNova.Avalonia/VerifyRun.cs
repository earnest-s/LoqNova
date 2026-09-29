// TEMP-VERIFY
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LoqNova.Lib;
using LoqNova.Lib.Features;

namespace LoqNova.Avalonia;

internal static class VerifyRun
{
    public static async Task<string> RunAsync()
    {
        var log = new System.Collections.Generic.List<string>();

        // ---- Sensor display semantics (WPF UpdateValue parity) ----
        var dvm = Container.Resolve<LoqNova.Avalonia.ViewModels.Pages.DashboardViewModel>();
        await dvm.ResumeSensorsAsync();
        await Task.Delay(500);

        void Set(int cpuU, int cpuUMax, int cpuC, int cpuCMax, int cpuT, int cpuTMax, int cpuF, int cpuFMax,
                 int gpuU, int gpuUMax, int gpuC, int gpuCMax, int gpuM, int gpuMMax, int gpuT, int gpuTMax, int gpuF, int gpuFMax)
        {
            SetProp(dvm, nameof(dvm.CpuUtilization), cpuU); SetProp(dvm, nameof(dvm.CpuMaxUtilization), cpuUMax);
            SetProp(dvm, nameof(dvm.CpuCoreClock), cpuC); SetProp(dvm, nameof(dvm.CpuMaxCoreClock), cpuCMax);
            SetProp(dvm, nameof(dvm.CpuTemperature), cpuT); SetProp(dvm, nameof(dvm.CpuMaxTemperature), cpuTMax);
            SetProp(dvm, nameof(dvm.CpuFanSpeed), cpuF); SetProp(dvm, nameof(dvm.CpuMaxFanSpeed), cpuFMax);
            SetProp(dvm, nameof(dvm.GpuUtilization), gpuU); SetProp(dvm, nameof(dvm.GpuMaxUtilization), gpuUMax);
            SetProp(dvm, nameof(dvm.GpuCoreClock), gpuC); SetProp(dvm, nameof(dvm.GpuMaxCoreClock), gpuCMax);
            SetProp(dvm, nameof(dvm.GpuMemoryClock), gpuM); SetProp(dvm, nameof(dvm.GpuMaxMemoryClock), gpuMMax);
            SetProp(dvm, nameof(dvm.GpuTemperature), gpuT); SetProp(dvm, nameof(dvm.GpuMaxTemperature), gpuTMax);
            SetProp(dvm, nameof(dvm.GpuFanSpeed), gpuF); SetProp(dvm, nameof(dvm.GpuMaxFanSpeed), gpuFMax);
        }

        var publish = typeof(LoqNova.Avalonia.ViewModels.Pages.DashboardViewModel)
            .GetMethod("PublishMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Set(11, 100, 3800, 5000, 54, 100, 2400, 6000, 0, 100, 210, 2100, 405, 8100, 49, 100, 0, 0);
        publish.Invoke(dvm, null);
        log.Add($"populated: cpuUtil='{dvm.CpuUtilizationText}' cpuClock='{dvm.CpuCoreClockText}' (max '{dvm.CpuCoreClockMaxText}') cpuTemp='{dvm.CpuTemperatureText}' cpuFan='{dvm.CpuFanSpeedText}' (max '{dvm.CpuFanSpeedMaxText}')");
        log.Add($"          gpuUtil='{dvm.GpuUtilizationText}' gpuClock='{dvm.GpuCoreClockText}' gpuMem='{dvm.GpuMemoryClockText}' gpuTemp='{dvm.GpuTemperatureText}' gpuFan='{dvm.GpuFanSpeedText}' (max '{dvm.GpuFanSpeedMaxText ?? "-"}')");
        log.Add($"          ratios cpuClock={dvm.CpuCoreClockRatio:0.00} gpuClock={dvm.GpuCoreClockRatio:0.00} gpuMem={dvm.GpuMemoryClockRatio:0.00}");

        Set(-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1);
        publish.Invoke(dvm, null);
        log.Add($"unreported: cpuUtil='{dvm.CpuUtilizationText}' cpuClock='{dvm.CpuCoreClockText}' gpuClock='{dvm.GpuCoreClockText}' gpuFan='{dvm.GpuFanSpeedText}'  (WPF shows \"-\")");
        log.Add($"            ratios cpuClock={dvm.CpuCoreClockRatio:0.00} gpuMem={dvm.GpuMemoryClockRatio:0.00}  (must be 0, no NaN)");

        // zero GPU fan reported must be shown as a real value, not blanked
        Set(0,100,1000,5000,40,100,0,6000,0,100,210,2100,405,8100,40,100,0,0);
        publish.Invoke(dvm, null);
        log.Add($"zero fan  : cpuFan='{dvm.CpuFanSpeedText}' gpuFan='{dvm.GpuFanSpeedText}'  (0 must be displayed as a reading)");

        // ---- Power mode combo hydration ----
        var perf = Container.Resolve<IPerformanceService>();
        log.Add($"power: supported={perf.IsSupported} current={perf.CurrentMode?.ToString() ?? "null"} items=[{string.Join(",", dvm.PowerModeItems)}]");
        log.Add($"power: current listed in combo items = {dvm.PowerModeItems.Contains(dvm.CurrentPowerMode!.Value)} (prevents '--' in the combo)");
        var conv = Container.Resolve<LoqNova.Avalonia.Converters.PowerModeDisplayNameConverter>();
        log.Add($"mapping: GodMode -> '{conv.Convert(PowerModeState.GodMode, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture)}'");
        log.Add($"mapping: null   -> '{conv.Convert(null, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture)}'");
        log.Add($"mapping: Quiet  -> '{conv.Convert(PowerModeState.Quiet, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture)}'");

        return string.Join("\n", log);
    }

    private static void SetProp(object target, string name, object value)
    {
        var p = target.GetType().GetProperty(name)!;
        p.SetValue(target, value);
    }
}