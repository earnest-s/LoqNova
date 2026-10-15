using Avalonia;
using Avalonia.Media;
using LoqNova.Lib;

namespace LoqNova.Avalonia.Styles;

/// <summary>
/// Resolves design-token brushes from the application resource dictionary so no
/// view or view model has to hard-code a colour. The palette lives in
/// <c>AppResources.axaml</c>; this is the single place that maps semantic state
/// onto it, so the Dashboard and the tray popup cannot drift apart.
/// </summary>
public static class ThemeBrushes
{
    /// <summary>
    /// The accent for a power mode. Unknown and unsupported states use the muted
    /// text brush rather than a plausible-looking colour.
    /// </summary>
    public static IBrush PowerMode(PowerModeState? mode) => FromKey(mode switch
    {
        PowerModeState.Quiet => "QuietModeBrush",
        PowerModeState.Balance => "BalanceModeBrush",
        PowerModeState.Performance => "PerformanceModeBrush",
        PowerModeState.GodMode => "GodModeBrush",
        _ => "TextSecondaryBrush"
    });

    // Resolved on each access (not cached in a static field) so the palette is
    // never captured before the application resources exist.
    public static IBrush Success => FromKey("SuccessBrush");

    public static IBrush Warning => FromKey("WarningBrush");

    public static IBrush Danger => FromKey("DangerBrush");

    public static IBrush Secondary => FromKey("TextSecondaryBrush");

    /// <summary>Looks a brush up by resource key, falling back to grey.</summary>
    public static IBrush FromKey(string key)
    {
        // The theme variant matters: the palette is declared per variant in
        // AppResources.axaml, and a null variant skips the theme dictionaries
        // entirely, which would silently fall back to grey.
        if (global::Avalonia.Application.Current is { } app &&
            app.TryGetResource(key, app.ActualThemeVariant, out var resource) &&
            resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }
}
