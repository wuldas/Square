using System.Runtime.CompilerServices;
using Square.Platform;

namespace Square.Platform.MacOS;

internal static class MacOSPlatformBootstrap
{
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register() =>
        PlatformRegistry.RegisterDefault(new MacOSPlatformFactory());
#pragma warning restore CA2255
}
