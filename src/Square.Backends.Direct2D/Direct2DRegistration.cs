using Square.Graphics;

namespace Square.Backends.Direct2D;

/// <summary>Direct2D 后端显式注册入口。</summary>
public static class Direct2DRegistration
{
    /// <summary>注册 Direct2D 后端。</summary>
    public static void Register()
        => RenderBackendRegistry.Register(new Direct2DBackendFactory());
}
