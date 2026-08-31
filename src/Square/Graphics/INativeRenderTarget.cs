namespace Square.Graphics;

/// <summary>原生渲染目标种类。</summary>
public enum NativeRenderTargetKind
{
    /// <summary>Win32 Vulkan 渲染目标。</summary>
    Win32Vulkan,
    /// <summary>X11 Vulkan 渲染目标。</summary>
    X11Vulkan,
    /// <summary>通用 Win32 窗口渲染目标。</summary>
    Win32
}

/// <summary>原生窗口渲染目标，供 GPU 后端使用。</summary>
public interface INativeRenderTarget
{
    /// <summary>目标种类。</summary>
    NativeRenderTargetKind Kind { get; }
    /// <summary>窗口句柄。</summary>
    IntPtr WindowHandle { get; }
    /// <summary>显示句柄（X11）或实例句柄（Win32）。</summary>
    IntPtr DisplayHandle { get; }
    /// <summary>屏幕编号。</summary>
    int Screen { get; }
}

/// <summary>通用 Win32 窗口渲染目标。</summary>
public sealed record Win32RenderTarget(IntPtr WindowHandle, IntPtr InstanceHandle) : INativeRenderTarget
{
    /// <inheritdoc/>
    public NativeRenderTargetKind Kind => NativeRenderTargetKind.Win32;
    /// <inheritdoc/>
    public IntPtr DisplayHandle => InstanceHandle;
    /// <inheritdoc/>
    public int Screen => 0;
}

/// <summary>Win32 Vulkan 渲染目标。</summary>
public sealed record Win32VulkanRenderTarget(IntPtr WindowHandle, IntPtr InstanceHandle) : INativeRenderTarget
{
    /// <inheritdoc/>
    public NativeRenderTargetKind Kind => NativeRenderTargetKind.Win32Vulkan;
    /// <inheritdoc/>
    public IntPtr DisplayHandle => InstanceHandle;
    /// <inheritdoc/>
    public int Screen => 0;
}

/// <summary>X11 Vulkan 渲染目标。</summary>
public sealed record X11VulkanRenderTarget(IntPtr DisplayHandle, IntPtr WindowHandle, int Screen) : INativeRenderTarget
{
    /// <inheritdoc/>
    public NativeRenderTargetKind Kind => NativeRenderTargetKind.X11Vulkan;
}
