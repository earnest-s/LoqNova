using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using LoqNova.Avalonia.Services;
using LibDispatcher = LoqNova.Lib.Utils.IMainThreadDispatcher;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Marshals work onto the Avalonia UI thread. Implements both the Avalonia-side
/// contract and <see cref="LibDispatcher"/>, because LoqNova.Lib controllers
/// (for example <c>WindowsPowerModeController</c> and the native message
/// listeners) require the library contract to be present in the container.
/// </summary>
public class MainThreadDispatcher : IMainThreadDispatcher, LibDispatcher
{
    public void Dispatch(Action callback) => Dispatcher.UIThread.Post(callback);

    public Task DispatchAsync(Func<Task> callback) => InvokeAsync(callback);

    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }
    
    public Task InvokeAsync(Action action)
    {
        var tcs = new TaskCompletionSource<object?>();
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                action();
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
    
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var result = func();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
    
    public Task InvokeAsync(Func<Task> func)
    {
        var tcs = new TaskCompletionSource<object?>();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await func();
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
    
    public bool CheckAccess()
    {
        return Dispatcher.UIThread.CheckAccess();
    }
}