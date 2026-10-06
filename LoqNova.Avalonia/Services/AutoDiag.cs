using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Temporary navigation/init diagnostics for the Automation freeze investigation.
/// Writes timestamped, thread-tagged markers so the last one reached before the UI
/// stalls identifies the blocking frame.
/// </summary>
internal static class AutoDiag
{
    private static readonly object Gate = new();

    private static string Path =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LOQNova",
            "auto-diag.log");

    public static void Mark(string stage)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

                File.AppendAllText(
                    Path,
                    $"[{DateTime.Now:HH:mm:ss.fff}] [tid={Thread.CurrentThread.ManagedThreadId}] " +
                    $"[ui={DispatcherProbe.IsUiThread}] {stage}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never be the failure.
        }
    }
}

/// <summary>Reports whether the caller is on Avalonia's UI thread.</summary>
internal static class DispatcherProbe
{
    public static bool IsUiThread
    {
        get
        {
            try
            {
                return Avalonia.Threading.Dispatcher.UIThread.CheckAccess();
            }
            catch
            {
                return false;
            }
        }
    }
}