using Square.Graphics;
using System.Runtime.Versioning;

namespace Square.Backends.Direct2D;

/// <summary>Direct2D HWND 渲染后端工厂。</summary>
public sealed class Direct2DBackendFactory : IRenderBackendFactory
{
    /// <inheritdoc/>
    public string Name => "Direct2D";

    /// <inheritdoc/>
    [SupportedOSPlatform("windows6.1")]
    public IRenderContext CreateContext(RenderContextCreateInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            throw new PlatformNotSupportedException("Direct2D backend requires Windows 7 or later.");
        if (info.NativeTarget is not Win32RenderTarget target)
            throw new Direct2DException("Direct2D backend requires a Win32RenderTarget NativeTarget.");
        if (target.WindowHandle == IntPtr.Zero)
            throw new Direct2DException("Direct2D backend requires a non-zero Win32 window handle.");

        return new Direct2DRenderContext(info, target);
    }
}
