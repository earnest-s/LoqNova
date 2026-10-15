using System;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Single and double Fn+F9 handling: the Avalonia counterpart of WPF's
/// <c>SmartKeyHelper</c>.
/// </summary>
public interface ISmartKeyService
{
    /// <summary>
    /// Invoked when a press has no action configured. WPF uses this to raise the
    /// window, so the Smart Key still does something useful when unconfigured.
    /// </summary>
    Action? BringToForeground { get; set; }

    /// <summary>Starts listening. Idempotent.</summary>
    void Start();

    /// <summary>Stops listening and cancels any pending single-press timer.</summary>
    void Stop();
}
