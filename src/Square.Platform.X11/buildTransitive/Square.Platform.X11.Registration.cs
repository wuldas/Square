using System.Runtime.CompilerServices;
using Square.Platform;
using Square.Platform.X11;

namespace Square.Platform.Generated;

internal static class X11PlatformPackageRegistration
{
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        if (System.OperatingSystem.IsLinux() && !System.OperatingSystem.IsAndroid())
            PlatformRegistry.RegisterDefault(new X11PlatformFactory());
    }
#pragma warning restore CA2255
}
