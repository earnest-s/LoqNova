// TEMP-WRITEPATH-VERIFY
using System;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

internal sealed class PassThroughDispatcherForVerify : IMainThreadDispatcher
{
    public void Post(Action action) => action();
    public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
    public Task InvokeAsync(Func<Task> func) => func();
    public bool CheckAccess() => true;
}
