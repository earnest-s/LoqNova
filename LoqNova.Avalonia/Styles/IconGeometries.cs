using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace LoqNova.Avalonia.Styles;

/// <summary>
/// The single definition of every icon path in the application.
/// Views bind either through <c>IconNameToGeometryConverter</c> (dynamic names)
/// or via <c>x:Static</c> (static chrome), so a glyph is never duplicated.
/// </summary>
public static class IconGeometries
{
    // ---- Navigation ----
    public static Geometry Home { get; } = P("M12 2.5 L22.5 11.8 L19.8 11.8 L19.8 21.5 L14 21.5 L14 14.5 L10 14.5 L10 21.5 L4.2 21.5 L4.2 11.8 L1.5 11.8 Z");
    public static Geometry Keyboard { get; } = P("M2.5 5.5 L21.5 5.5 L21.5 18.5 L2.5 18.5 Z M4.5 7.5 L4.5 16.5 L19.5 16.5 L19.5 7.5 Z M6 9 L8.6 9 L8.6 11.2 L6 11.2 Z M10 9 L12.6 9 L12.6 11.2 L10 11.2 Z M14 9 L16.6 9 L16.6 11.2 L14 11.2 Z M6 13 L8.6 13 L8.6 15.2 L6 15.2 Z M10 13 L12.6 13 L12.6 15.2 L10 15.2 Z M14 13 L18 13 L18 15.2 L14 15.2 Z");
    public static Geometry Battery { get; } = P("M15.5 4 L8.5 4 C7.4 4 6.5 4.9 6.5 6 L6.5 18 C6.5 19.1 7.4 20 8.5 20 L15.5 20 C16.6 20 17.5 19.1 17.5 18 L17.5 6 C17.5 4.9 16.6 4 15.5 4 Z M9 6 L15 6 L15 8 L9 8 Z M9 10 L15 10 L15 18 L9 18 Z");
    public static Geometry Automation { get; } = P("M12 2 C9.2 2 7 4.2 7 7 L7 8.5 L5 8.5 C4.2 8.5 3.5 9.2 3.5 10 L3.5 14 C3.5 14.8 4.2 15.5 5 15.5 L7 15.5 L7 17 C7 19.8 9.2 22 12 22 C14.8 22 17 19.8 17 17 L17 15.5 L19 15.5 C19.8 15.5 20.5 14.8 20.5 14 L20.5 10 C20.5 9.2 19.8 8.5 19 8.5 L17 8.5 L17 7 C17 4.2 14.8 2 12 2 Z M9.5 17 L14.5 17 L14.5 19 C14.5 19.8 13.8 20.5 13 20.5 L11 20.5 C10.2 20.5 9.5 19.8 9.5 19 Z");
    public static Geometry Receipt { get; } = P("M5 2 L19 2 L19 22 L16.5 20.5 L14 22 L12 20.5 L10 22 L7.5 20.5 L5 22 Z M8 6 L16 6 L16 7.6 L8 7.6 Z M8 10 L16 10 L16 11.6 L8 11.6 Z M8 14 L16 14 L16 15.6 L8 15.6 Z");
    public static Geometry Box { get; } = P("M12 2 L21.5 6.5 L21.5 17.5 L12 22 L2.5 17.5 L2.5 6.5 Z M12 4.6 L5.4 7.8 L12 11 L18.6 7.8 Z M4.6 9.4 L4.6 16.4 L10.9 19.7 L10.9 12.7 Z M19.4 9.4 L19.4 16.4 L13.1 19.7 L13.1 12.7 Z");
    public static Geometry Settings { get; } = P("M3 6 L21 6 L21 7.8 L3 7.8 Z M3 11.1 L21 11.1 L21 12.9 L3 12.9 Z M3 16.2 L21 16.2 L21 18 L3 18 Z M8 4.2 L11 4.2 L11 9.6 L8 9.6 Z M14 9.3 L17 9.3 L17 14.7 L14 14.7 Z M6 14.4 L9 14.4 L9 19.8 L6 19.8 Z");
    public static Geometry Info { get; } = P("M12 2 C6.5 2 2 6.5 2 12 C2 17.5 6.5 22 12 22 C17.5 22 22 17.5 22 12 C22 6.5 17.5 2 12 2 Z M11 10.5 L13 10.5 L13 17.5 L11 17.5 Z M11 6.5 L13 6.5 L13 8.5 L11 8.5 Z");

    // ---- Sensors ----
    public static Geometry Cpu { get; } = P("M6 6 L18 6 L18 18 L6 18 Z M8 8 L8 16 L16 16 L16 8 Z M9.5 9.5 L14.5 9.5 L14.5 14.5 L9.5 14.5 Z M9 3 L11 3 L11 6 L9 6 Z M13 3 L15 3 L15 6 L13 6 Z M9 18 L11 18 L11 21 L9 21 Z M13 18 L15 18 L15 21 L13 21 Z M3 9 L6 9 L6 11 L3 11 Z M18 9 L21 9 L21 11 L18 11 Z M3 13 L6 13 L6 15 L3 15 Z M18 13 L21 13 L21 15 L18 15 Z");
    public static Geometry Gpu { get; } = P("M2 5 L22 5 L22 16 L2 16 Z M4 7 L4 14 L20 14 L20 7 Z M5.5 8.5 L12 8.5 L12 12.5 L5.5 12.5 Z M15 8.5 L18.5 8.5 L18.5 12.5 L15 12.5 Z");
    public static Geometry Thermometer { get; } = P("M12 3 C10.6 3 9.5 4.1 9.5 5.5 L9.5 13.8 C8.4 14.6 7.8 15.8 7.8 17.2 C7.8 19.5 9.7 21.4 12 21.4 C14.3 21.4 16.2 19.5 16.2 17.2 C16.2 15.8 15.6 14.6 14.5 13.8 L14.5 5.5 C14.5 4.1 13.4 3 12 3 Z M11 6 L13 6 L13 15.5 L11 15.5 Z");
    public static Geometry Fan { get; } = P("M12 9.5 C13.4 9.5 14.5 10.6 14.5 12 C14.5 13.4 13.4 14.5 12 14.5 C10.6 14.5 9.5 13.4 9.5 12 C9.5 10.6 10.6 9.5 12 9.5 Z M12 2.5 C15 3.5 16 6 15.5 8.5 L13 9 Z M21.5 12 C20.5 15 18 16 15.5 15.5 L15 13 Z M2.5 12 C3.5 15 6 16 8.5 15.5 L9 13 Z M12 21.5 C9 20.5 8 18 8.5 15.5 L11 15 Z");

    // ---- Feature widgets ----
    public static Geometry Bolt { get; } = P("M13.5 2 L5 13.5 L10.5 13.5 L9 22 L18.5 10.5 L13 10.5 Z");
    public static Geometry Key { get; } = P("M14.5 3 C17 3 19 5 19 7.5 C19 10 17 12 14.5 12 C13.9 12 13.4 11.9 12.9 11.7 L12 12.6 L12 14.5 L10.5 14.5 L10.5 16 L9 16 L9 12.4 L4.5 8 C5.8 4.8 9.8 3 14.5 3 Z M14.5 5.5 C13.4 5.5 12.5 6.4 12.5 7.5 C12.5 8.6 13.4 9.5 14.5 9.5 C15.6 9.5 16.5 8.6 16.5 7.5 C16.5 6.4 15.6 5.5 14.5 5.5 Z");
    public static Geometry Window { get; } = P("M3 4 L9 4 L9 10 L3 10 Z M11 4 L21 4 L21 10 L11 10 Z M3 12 L9 12 L9 20 L3 20 Z M11 12 L21 12 L21 20 L11 20 Z");
    public static Geometry Mic { get; } = P("M12 2 C10.3 2 9 3.3 9 5 L9 11 C9 12.7 10.3 14 12 14 C13.7 14 15 12.7 15 11 L15 5 C15 3.3 13.7 2 12 2 Z M5.5 10.5 L7.5 10.5 L7.5 12 C7.5 14.2 9.6 16 12 16 C14.4 16 16.5 14.2 16.5 12 L16.5 10.5 L18.5 10.5 L18.5 12 C18.5 15.1 16.3 17.6 13.4 18.2 L13.4 21 L15.5 21 L15.5 22.5 L8.5 22.5 L8.5 21 L10.6 21 L10.6 18.2 C7.7 17.6 5.5 15.1 5.5 12 Z");
    public static Geometry Touchpad { get; } = P("M4 4 L20 4 L20 20 L4 20 Z M6 6 L6 14 L18 14 L18 6 Z M8.5 16 L15.5 16 L15.5 18 L8.5 18 Z");
    public static Geometry Display { get; } = P("M2.5 4 L21.5 4 L21.5 16 L2.5 16 Z M4.5 6 L4.5 14 L19.5 14 L19.5 6 Z M9 18 L15 18 L15 19.5 L9 19.5 Z M7.5 20.5 L16.5 20.5 L16.5 22 L7.5 22 Z");
    public static Geometry DisplayOff { get; } = P("M2.5 4 L21.5 4 L21.5 16 L2.5 16 Z M4.5 6 L4.5 14 L19.5 14 L19.5 6 Z M9 18 L15 18 L15 19.5 L9 19.5 Z M3.5 3.5 L20.5 20.5 L19 21.6 L2 4.6 Z");
    public static Geometry Flash { get; } = P("M13 2 L5 13 L10.5 13 L9 22 L18 10 L12.5 10 Z");
    public static Geometry Badge { get; } = P("M12 2 L14.2 5 L17.8 4.4 L18.4 8 L21 10 L18.4 12.6 L18.4 16.2 L14.2 16.8 L12 20 L9.8 16.8 L5.6 16.2 L5.6 12.6 L3 10 L5.6 7.4 L5.6 4.4 L9.8 5 Z M12 8 C10.3 8 9 9.3 9 11 C9 12.7 10.3 14 12 14 C13.7 14 15 12.7 15 11 C15 9.3 13.7 8 12 8 Z");
    public static Geometry UsbC { get; } = P("M7 3 L17 3 L17 21 L7 21 Z M9 5 L9 19 L15 19 L15 5 Z M11 8 L13 8 L13 16 L11 16 Z");
    public static Geometry SpeedHigh { get; } = P("M3 18 L8 18 L8 20 L3 20 Z M9.5 13 L12.5 13 L12.5 20 L9.5 20 Z M14 8 L17 8 L17 20 L14 20 Z M18.5 3 L21 3 L21 20 L18.5 20 Z");

    /// <summary>Name based lookup used by <c>IconNameToGeometryConverter</c>.</summary>
    public static IReadOnlyDictionary<string, Geometry> ByName { get; } =
        new Dictionary<string, Geometry>(StringComparer.OrdinalIgnoreCase)
        {
            ["Home"] = Home,
            ["Keyboard"] = Keyboard,
            ["Battery"] = Battery,
            ["Rocket"] = Bolt,
            ["Receipt"] = Receipt,
            ["Box"] = Box,
            ["Settings"] = Settings,
            ["Info"] = Info,
            ["Cpu"] = Cpu,
            ["Gpu"] = Gpu,
            ["Thermometer"] = Thermometer,
            ["Fan"] = Fan,
            ["Bolt"] = Bolt,
            ["Key"] = Key,
            ["Window"] = Window,
            ["Mic"] = Mic,
            ["Touchpad"] = Touchpad,
            ["Display"] = Display,
            ["DisplayOff"] = DisplayOff,
            ["Flash"] = Flash,
            ["Badge"] = Badge,
            ["UsbC"] = UsbC,
            ["SpeedHigh"] = SpeedHigh
        };

    private static Geometry P(string data) => StreamGeometry.Parse(data);
}
