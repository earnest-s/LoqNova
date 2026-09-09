using Autofac;
using LoqNova.Avalonia.Platform;
using LoqNova.Lib.Extensions;
using LoqNova.Lib.Utils;

namespace LoqNova.Avalonia.Startup;

public class IoCModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<MainThreadDispatcher>()
            .As<IMainThreadDispatcher>()
            .InstancePerLifetimeScope();
    }
}
