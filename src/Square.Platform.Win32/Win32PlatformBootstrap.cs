using System.Runtime.CompilerServices;
using Square.Platform;

namespace Square.Platform.Win32;

internal static class Win32PlatformBootstrap
{
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        if (OperatingSystem.IsWindows())
            PlatformRegistry.RegisterDefault(new Win32PlatformFactory());
    }
#pragma warning restore CA2255
}
