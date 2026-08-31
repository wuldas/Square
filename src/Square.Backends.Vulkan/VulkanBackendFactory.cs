using Square.Graphics;

namespace Square.Backends.Vulkan;

/// <summary>
/// Vulkan GPU backend factory. Creates VulkanRenderContext instances.
/// </summary>
public sealed class VulkanBackendFactory : IRenderBackendFactory
{
    public string Name => "Vulkan";

    public IRenderContext CreateContext(RenderContextCreateInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.NativeTarget is null)
            throw new VulkanException("Vulkan backend requires a platform NativeTarget (Win32RenderTarget or X11VulkanRenderTarget).");

        return new VulkanRenderContext(info);
    }
}
