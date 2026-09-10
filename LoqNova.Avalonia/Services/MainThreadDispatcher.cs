using System;
using System.Threading.Tasks;
using Avalonia;
using LoqNova.Avalonia.Services;

namespace LoqNova.Avalonia.Services;

public class MainThreadDispatcher : IMainThreadDispatcher
{
    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
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