using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LoqNova.Avalonia.Startup;

namespace LoqNova.Avalonia;

public partial class App : Application
{
    private const string MUTEX_NAME = "LOQNova_Avalonia_Mutex_6efcc882-924c-4cbc-8fec-f45c25696f98";
    private const string EVENT_NAME = "LOQNova_Avalonia_Event_6efcc882-924c-4cbc-8fec-f45c25696f98";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _singleInstanceWaitHandle;
    private AppStartup? _startup;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize();
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        var args = GetStartupArgs();

        _singleInstanceMutex = new Mutex(true, MUTEX_NAME, out var isOwned);
        _singleInstanceWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, EVENT_NAME);

        if (!isOwned)
        {
            // Another LoqNova Avalonia instance is already running; bring it to front.
            _singleInstanceWaitHandle.Set();
            ShutdownApplication(0);
            return;
        }

        _startup = new AppStartup(args);

        try
        {
            if (await _startup.InitializeBackendAsync())
            {
                var window = _startup.CreateMainWindow();
                window.Show();
                WatchForSecondaryInstances();
            }
            else
            {
                ShutdownApplication(0);
            }
        }
        catch (Exception ex)
        {
            _startup.ReportStartupFailure(ex);
            ShutdownApplication(100);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private string[] GetStartupArgs()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            return lifetime.Args ?? [];

        return [];
    }

    private void WatchForSecondaryInstances()
    {
        new Thread(() =>
        {
            while (_singleInstanceWaitHandle.WaitOne())
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
                    {
                        window.Activate();
                        if (window.WindowState == WindowState.Minimized)
                            window.WindowState = WindowState.Normal;
                    }
                    else
                    {
                        if (_startup is not null)
                            await _startup.ShutdownAsync();
                    }
                });
            }
        })
        {
            IsBackground = true
        }.Start();
    }

    public async Task ShutdownAsync()
    {
        if (_startup is not null)
            await _startup.ShutdownAsync();
        else
            ShutdownApplication(0);
    }

    private void ShutdownApplication(int exitCode)
    {
        Environment.ExitCode = exitCode;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            lifetime.Shutdown();
        else
            Dispatcher.UIThread.InvokeShutdown();
    }
}
