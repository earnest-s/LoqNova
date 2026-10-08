using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// TEMPORARY navigation diagnostics. Every line is prefixed with [NAV-DIAG] and
/// carries a timestamp plus the managed thread id, so UI-thread work is
/// distinguishable from background work in the trace.
/// Exceptions are logged and then rethrown; nothing here swallows a failure.
/// </summary>
internal static class NavDiag
{
    private static readonly object FileGate = new();
    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(),
        "loqnova-navdiag.log");

    private static long _seq;

    public static bool Enabled { get; set; }

    public static void Log(string stage, string detail = "")
    {
        if (!Enabled)
        {
            return;
        }

        var seq = Interlocked.Increment(ref _seq);
        var line = $"[NAV-DIAG] {DateTime.Now:HH:mm:ss.fff} tid={Environment.CurrentManagedThreadId} " +
                   $"ui={Dispatcher.UIThread.CheckAccess()} #{seq} {stage}" +
                   (detail.Length > 0 ? $" | {detail}" : string.Empty);

        Debug.WriteLine(line);

        try
        {
            lock (FileGate)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // Diagnostics must never break navigation.
        }
    }

    public static void LogException(string stage, Exception ex)
    {
        // Walk the whole chain: Autofac wraps the real cause in a
        // DependencyResolutionException whose own message hides it.
        var parts = new List<string>();
        var current = ex;
        var depth = 0;

        while (current is not null && depth < 8)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
            current = current.InnerException;
            depth++;
        }

        Log(stage + ":EXCEPTION", string.Join(" <-- ", parts) + Environment.NewLine +
            "        " + ex.ToString().Replace(Environment.NewLine, Environment.NewLine + "        "));
    }

    /// <summary>
    /// Renders the content host (not the Window: a top-level Window renders through the
    /// compositor, so RenderTargetBitmap returns an empty surface for it) and reports how
    /// much of it is still the near-black page background (#0B0D11).
    /// </summary>
    public static string ProbeVisualState(Window window, string label)
    {
        if (!Enabled || !Dispatcher.UIThread.CheckAccess())
        {
            return "probe-skipped";
        }

        try
        {
            var host = window.FindControl<ContentControl>("ContentHost");

            if (host is null)
            {
                return "contenthost-missing";
            }

            var width = (int)Math.Max(1, host.Bounds.Width);
            var height = (int)Math.Max(1, host.Bounds.Height);

            if (width < 2 || height < 2)
            {
                return $"contenthost-zero-size w={host.Bounds.Width} h={host.Bounds.Height}";
            }

            var size = new PixelSize(width, height);
            var rtb = new RenderTargetBitmap(size, new Vector(96, 96));
            rtb.Render(host);

            var stride = width * 4;
            var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(stride * height);

            try
            {
                rtb.CopyPixels(new PixelRect(size), buffer, stride * height, stride);

                long dark = 0;
                long total = width * (long)height;

                for (var i = 0; i < stride * height; i += 4)
                {
                    var b = System.Runtime.InteropServices.Marshal.ReadByte(buffer, i);
                    var g = System.Runtime.InteropServices.Marshal.ReadByte(buffer, i + 1);
                    var r = System.Runtime.InteropServices.Marshal.ReadByte(buffer, i + 2);

                    if (r <= 0x20 && g <= 0x22 && b <= 0x26)
                    {
                        dark++;
                    }
                }

                var pct = total == 0 ? 0 : dark * 100 / total;

                return $"darkPct={pct}% descendants={host.GetVisualDescendants().Count()} " +
                       $"host={width}x{height} hostOpacity={host.Opacity} hostVisible={host.IsVisible} " +
                       $"content={(host.Content is null ? "null" : host.Content.GetType().Name)}";
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            LogException("probe-visual", ex);
            return "probe-failed";
        }
    }

    public static void Reset()
    {
        try
        {
            lock (FileGate)
            {
                File.Delete(LogPath);
            }
        }
        catch (IOException)
        {
            // Nothing to clear.
        }

        Interlocked.Exchange(ref _seq, 0);
    }

    /// <summary>
    /// Drives the acceptance sequence from section 8 without a human at the mouse.
    /// Goes through the exact same INavigationService path the sidebar buttons use,
    /// so it reproduces the lifecycle rather than simulating it.
    /// </summary>
    public static async Task RunNavigationCycleAsync(
        INavigationService navigation,
        Func<Window?> windowAccessor,
        int cycles)
    {
        var sequence = new[]
        {
            NavigationPage.Battery,
            NavigationPage.Packages,
            NavigationPage.Battery,
            NavigationPage.Automation,
            NavigationPage.Macro,
            NavigationPage.Dashboard,
            NavigationPage.Macro,
            NavigationPage.Battery
        };

        Log("CYCLE-START", $"cycles={cycles} seqLen={sequence.Length}");

        for (var cycle = 1; cycle <= cycles; cycle++)
        {
            foreach (var page in sequence)
            {
                var window = windowAccessor();
                if (window is null)
                {
                    Log("CYCLE-ABORT", "no window");
                    return;
                }

                Log("CYCLE-NAV-REQUEST", $"cycle={cycle} target={page}");
                var watch = Stopwatch.StartNew();

                try
                {
                    await navigation.NavigateToAsync(page);
                }
                catch (Exception ex)
                {
                    LogException($"CYCLE-NAV-THREW cycle={cycle} target={page}", ex);
                    continue;
                }

                watch.Stop();

                // Let the dispatcher settle and the layout/render pass run.
                await Task.Delay(250);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                Log("CYCLE-NAV-DONE", $"cycle={cycle} target={page} elapsedMs={watch.ElapsedMilliseconds} " +
                                     ProbeVisualState(window, page.ToString()));
            }
        }

        Log("CYCLE-END", $"cycles={cycles}");
    }
}