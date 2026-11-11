using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

[CollectionDefinition("VulkanOffscreenResources", DisableParallelization = true)]
public sealed class VulkanOffscreenResourcesCollection { }

[Collection("VulkanOffscreenResources")]
public sealed class VulkanOffscreenLayerResourceTests
{
    [Fact]
    public void NullDeviceIsRejectedBeforeNativeAllocation()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new VulkanOffscreenLayer(null!, Format.B8G8R8A8Unorm, 32, 24));
    }

    [Theory]
    [InlineData(0u, 24u, "width")]
    [InlineData(32u, 0u, "height")]
    public void InvalidExtentIsRejectedBeforeAccessingTheNativeDevice(uint width, uint height, string parameter)
    {
        // No constructor/native API runs: the invalid-argument path must reject first.
        var device = (VulkanDevice)RuntimeHelpers.GetUninitializedObject(typeof(VulkanDevice));
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VulkanOffscreenLayer(device, Format.B8G8R8A8Unorm, width, height));
        Assert.Equal(parameter, error.ParamName);
    }

    [Fact]
    public void UndefinedFormatIsRejectedBeforeAccessingTheNativeDevice()
    {
        var device = (VulkanDevice)RuntimeHelpers.GetUninitializedObject(typeof(VulkanDevice));
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VulkanOffscreenLayer(device, Format.Undefined, 32, 24));
        Assert.Equal("format", error.ParamName);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("4")]
    [Trait("Category", "RealVulkanGpu")]
    public void OffscreenResourcesCanBeCreatedAndDisposed(string requestedSamples)
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("SQUARE_RUN_VULKAN_OFFSCREEN_RESOURCE_TESTS") != "1") return;

        var previousSamples = Environment.GetEnvironmentVariable("SQUARE_VULKAN_MSAA");
        var window = IntPtr.Zero;
        try
        {
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_MSAA", requestedSamples);
            var instance = GetModuleHandle(null);
            // A hidden native surface is sufficient for device/queue selection; no UI is shown.
            window = CreateWindowEx(0, "STATIC", "Vulkan offscreen resource test", 0x80000000u,
                0, 0, 64, 48, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, window);
            using var device = new VulkanDevice(new Win32RenderTarget(window, instance), enableValidation: true);
            using var layer = new VulkanOffscreenLayer(device, Format.B8G8R8A8Unorm, 32, 24);
            Assert.Equal(32u, layer.Width);
            Assert.Equal(24u, layer.Height);
            Assert.True(layer.Bytes > 0);
            Assert.NotEqual(0ul, layer.Framebuffer.Handle);
            Assert.NotEqual(0ul, layer.RenderPass.Handle);
            Assert.NotEqual(0ul, layer.ImageView.Handle);
            Assert.NotEqual(0ul, layer.Sampler.Handle);
            Console.WriteLine($"Offscreen resource samples={(uint)device.ColorSampleCount}, bytes={layer.Bytes}");

            // Synchronization belongs to the caller, not the target's Dispose.
            VulkanDevice.ThrowIfFailed(device.Api.DeviceWaitIdle(device.Device), "vkDeviceWaitIdle(resource test)");
            layer.Dispose();
            layer.Dispose();
            Assert.Equal(0ul, layer.Bytes);
            Assert.Equal(0ul, layer.Framebuffer.Handle);
            Assert.Equal(0ul, layer.RenderPass.Handle);
            Assert.Equal(0ul, layer.ImageView.Handle);
            Assert.Equal(0ul, layer.Sampler.Handle);
        }
        finally
        {
            if (window != IntPtr.Zero) DestroyWindow(window);
            Environment.SetEnvironmentVariable("SQUARE_VULKAN_MSAA", previousSamples);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
}
