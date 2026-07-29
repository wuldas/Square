using Square.Graphics;
namespace Square.Platform;

/// <summary>平台工厂注册表。</summary>
public static class PlatformRegistry
{
    private static IPlatformFactory? _factory;
    private static IPlatformFactory? _defaultFactory;

    /// <summary>注册平台工厂。</summary>
    public static void Register(IPlatformFactory factory) => _factory = factory;

    /// <summary>注册默认平台工厂（仅首次生效）。</summary>
    public static void RegisterDefault(IPlatformFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Interlocked.CompareExchange(ref _defaultFactory, factory, null);
    }

    /// <summary>尝试获取平台工厂。</summary>
    public static bool TryGet(out IPlatformFactory? factory)
    {
        factory = _factory ?? _defaultFactory;
        return factory is not null;
    }

    /// <summary>获取平台工厂。</summary>
    /// <exception cref="InvalidOperationException">未注册任何平台工厂。</exception>
    public static IPlatformFactory Get() =>
        _factory ?? _defaultFactory ?? throw new InvalidOperationException(
            "No platform factory registered. Reference Square.Platform.Win32, Square.Platform.X11, or Square.Platform.MacOS.");
}
