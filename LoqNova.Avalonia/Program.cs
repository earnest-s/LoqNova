using System;
using System.Reflection;
using Avalonia;

namespace LoqNova.Avalonia;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = AppBuilder.Configure<App>();

        if (OperatingSystem.IsWindows())
        {
            builder
                .UseWin32()
                .UseSkia();
        }

        builder.LogToTrace();

        _ = args;
        builder.StartWithClassicDesktopLifetime(args);
    }
}
