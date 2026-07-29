namespace Square.Platform.MacOS;

public sealed class MacOSPlatformFactory : IPlatformFactory
{
    public string Name => "MacOS";

    public IPlatformHost CreateHost(PlatformHostCreateInfo info)
    {
        return new MacOSHost(info);
    }
}
