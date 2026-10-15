namespace LoqNova.Avalonia.Services;

/// <summary>
/// Delivers the shared library's notification messages to the shell.
///
/// <para>
/// The library publishes every user-visible event (Fn keys, power modes, AC adapter,
/// refresh rate, Smart Key, ...) to its messaging hub. The Notifications settings
/// decide which of them are shown, so something has to subscribe and apply that
/// decision; without it the settings dialog wrote to the store and no notification was
/// ever delivered.
/// </para>
/// </summary>
public interface INotificationRelay
{
    /// <summary>Starts relaying. Idempotent.</summary>
    void Start();

    /// <summary>Stops relaying and releases the subscription.</summary>
    void Stop();
}
