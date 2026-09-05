using Square.Graphics;

namespace Square.Backends;

/// <summary>内置后端注册入口。</summary>
public static class BackendRegistration
{
    /// <summary>注册内置 Software 默认后端。</summary>
    public static void RegisterDefaults()
    {
        RenderBackendRegistry.Register(new RenderBackendFactory());
        RenderBackendRegistry.SetDefault("Software");
    }
}
