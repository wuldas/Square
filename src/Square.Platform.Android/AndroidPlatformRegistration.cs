using Android.App;
using Square.Backends.AndroidCanvas;
using Square.Backends.Vulkan;
using Square.Platform;
using AndroidActivity = Android.App.Activity;

namespace Square.Platform.Android;

/// <summary>Android 平台注册和工厂入口。</summary>
public static class AndroidPlatformRegistration
{
    /// <summary>把当前 Activity 注册为默认 Android 宿主工厂。</summary>
    public static AndroidPlatformFactory Register(AndroidActivity activity)
    {
        var factory = new AndroidPlatformFactory(activity);
        AndroidCanvasRegistration.Register();
        AndroidSkiaRegistration.Register();
        VulkanRegistration.Register();
        return factory;
    }
}

/// <summary>绑定到一个 Activity 的 Android 平台工厂。</summary>
public sealed class AndroidPlatformFactory : IPlatformFactory
{
    private readonly AndroidActivity _activity;

    /// <summary>创建工厂。</summary>
    public AndroidPlatformFactory(AndroidActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _activity = activity;
    }

    /// <inheritdoc />
    public string Name => "Android";

    /// <inheritdoc />
    public IPlatformHost CreateHost(PlatformHostCreateInfo info) =>
        new AndroidPlatformHost(_activity, info);
}
