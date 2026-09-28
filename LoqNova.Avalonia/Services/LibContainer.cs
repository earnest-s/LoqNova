using System;
using System.Threading.Tasks;
using LoqNova.Lib;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Owns the initialisation of the shared <see cref="IoCContainer"/>.
/// <para>
/// The container must not be built while Avalonia is still initialising.
/// <c>Lib.IoCModule</c> auto-activates Windows message listeners, and
/// <c>NativeWindowsMessageListener.StartAsync</c> posts work to the UI thread and
/// waits for its message window to initialise. During
/// <c>OnFrameworkInitializationCompleted</c> the dispatcher is not pumping yet, so
/// building the container there stalls startup. WPF does not hit this because
/// <c>OnStartup</c> runs with the dispatcher already running.
/// </para>
/// <para>
/// Initialisation therefore runs on a background thread: the container graph is
/// built there, and the listener callbacks it queues are dispatched to the UI
/// thread once Avalonia starts pumping. Adapters await
/// <see cref="Initialization"/> before touching the container.
/// </para>
/// </summary>
public static class LibContainer
{
    private static readonly TaskCompletionSource<bool> Source =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the shared container can be resolved from.</summary>
    public static Task Initialization => Source.Task;

    public static void Initialize()
    {
        _ = Task.Run(() =>
        {
            try
            {
                IoCContainer.Initialize(
                    new IoCModule(),
                    new LoqNova.Lib.Macro.IoCModule(),
                    new Avalonia.IoCModule());

                Source.TrySetResult(true);
            }
            catch (Exception ex)
            {
                Source.TrySetException(ex);
            }
        });
    }
}
