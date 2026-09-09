using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LoqNova.Lib.Utils;

namespace LoqNova.Avalonia.Platform;

/// <summary>
/// Platform adapter that prevents Windows from power-throttling this process via
/// EcoQoS / Efficiency Mode, improves timer resolution, and sets a stable
/// above-normal process priority. Mirrors the WPF PowerOptimization behavior.
/// Call <see cref="Apply"/> once at application startup before any animation,
/// RGB, or audio threads are created.
/// </summary>
internal static class PowerOptimization
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    private const int PROCESS_INFORMATION_CLASS_POWER_THROTTLING = 9;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess,
        int ProcessInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE ProcessInformation,
        uint ProcessInformationSize);

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uMilliseconds);

    public static void Apply()
    {
        DisableEfficiencyMode();
        SetStableProcessPriority();
        ImproveTimerResolution();
    }

    private static void DisableEfficiencyMode()
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = 0,
            };

            var ok = SetProcessInformation(
                Process.GetCurrentProcess().Handle,
                PROCESS_INFORMATION_CLASS_POWER_THROTTLING,
                ref state,
                (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"DisableEfficiencyMode: SetProcessInformation returned {ok}");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"DisableEfficiencyMode failed", ex);
        }
    }

    private static void SetStableProcessPriority()
    {
        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"SetStableProcessPriority: AboveNormal applied");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"SetStableProcessPriority failed", ex);
        }
    }

    private static void ImproveTimerResolution()
    {
        try
        {
            var result = timeBeginPeriod(1);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"ImproveTimerResolution: timeBeginPeriod(1) returned {result}");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"ImproveTimerResolution failed", ex);
        }
    }
}
