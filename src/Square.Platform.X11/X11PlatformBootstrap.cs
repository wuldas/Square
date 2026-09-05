using System.Runtime.CompilerServices;
using Square.Platform;

namespace Square.Platform.X11;

internal static class X11PlatformBootstrap
{
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        if (OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid())
            PlatformRegistry.RegisterDefault(new X11PlatformFactory());
    }
#pragma warning restore CA2255
}
