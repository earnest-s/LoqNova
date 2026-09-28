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
    }
}
