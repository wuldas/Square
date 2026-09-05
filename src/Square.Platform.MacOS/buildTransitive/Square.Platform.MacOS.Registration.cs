using System.Runtime.CompilerServices;
using Square.Platform;
using Square.Platform.MacOS;

namespace Square.Platform.Generated;

internal static class MacOSPlatformPackageRegistration
{
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        if (System.OperatingSystem.IsMacOS())
            PlatformRegistry.RegisterDefault(new MacOSPlatformFactory());
    }
#pragma warning restore CA2255
}
