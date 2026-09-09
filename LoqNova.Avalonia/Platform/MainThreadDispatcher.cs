using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using LoqNova.Lib.Utils;

namespace LoqNova.Avalonia.Platform;

/// <summary>
/// Avalonia implementation of the backend <see cref="IMainThreadDispatcher"/>,
/// dispatching onto the Avalonia UI thread. Replaces the WPF MainThreadDispatcher.
/// </summary>
public class MainThreadDispatcher : IMainThreadDispatcher
{
    public void Dispatch(Action callback) => Dispatcher.UIThread.Post(callback);

    public Task DispatchAsync(Func<Task> callback) => Dispatcher.UIThread.InvokeAsync(callback);
}
