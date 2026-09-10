using System;
using System.Threading.Tasks;

namespace LoqNova.Avalonia.Services;

public interface IMainThreadDispatcher
{
    void Post(Action action);
    Task<T> InvokeAsync<T>(Func<T> func);
    Task InvokeAsync(Func<Task> func);
    bool CheckAccess();
}