using Square.Graphics;

namespace Square.Backends.Direct2D;

/// <summary>Direct2D 后端应用扩展。</summary>
public static class Direct2DApplicationExtensions
{
    /// <summary>注册并选择 Direct2D 后端。</summary>
    public static T UseDirect2DBackend<T>(this T window)
        where T : IRenderBackendApplication
    {
        ArgumentNullException.ThrowIfNull(window);
        Direct2DRegistration.Register();
        window.RenderBackend = "Direct2D";
        return window;
    }
}
