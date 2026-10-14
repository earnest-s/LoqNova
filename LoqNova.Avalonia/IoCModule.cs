using Autofac;
using LoqNova.Avalonia.Services;
using LoqNova.Lib.Extensions;

namespace LoqNova.Avalonia;

/// <summary>
/// Supplies the Avalonia-side pieces that belong in the shared
/// <see cref="LoqNova.Lib.IoCContainer"/> rather than in the Avalonia container.
/// WPF does the same thing through its own <c>IoCModule</c>: library controllers
/// depend on <c>LoqNova.Lib.Utils.IMainThreadDispatcher</c>, which the library
/// deliberately does not register because each front end must supply it.
/// </summary>
public class IoCModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.Register<MainThreadDispatcher>();
        builder.RegisterType<ScreenCapture>()
               .As<LoqNova.Lib.Controllers.SpectrumKeyboardBacklightController.IScreenCapture>()
               .SingleInstance();

        // The command-line IPC server is registered in the shared container, exactly
        // as WPF registers its own in LoqNova.WPF/IoCModule.cs. It depends on library
        // singletons (the automation processor and the two keyboard controllers), so
        // it belongs here rather than in the Avalonia-only container.
        builder.Register<CLI.IpcServer>();
    }
}
