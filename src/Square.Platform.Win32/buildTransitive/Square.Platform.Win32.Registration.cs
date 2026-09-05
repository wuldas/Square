using System.Runtime.CompilerServices;
using Square.Platform;
using Square.Platform.Win32;

namespace Square.Platform.Generated;

internal static class Win32PlatformPackageRegistration
{
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        if (System.OperatingSystem.IsWindows())
            PlatformRegistry.RegisterDefault(new Win32PlatformFactory());
    }
#pragma warning restore CA2255
}
