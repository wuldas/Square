using Square.Backends.Vulkan;
using Square.Graphics;
using Square.Platform;
#if PLATFORM_WIN32
using Square.Platform.Win32;
#elif PLATFORM_X11
using Square.Platform.X11;
#endif
using Xunit;

namespace Square.Backends.Conformance.Tests;

public sealed class RealVulkanGpuConformanceTests
{
    [Fact]
    [Trait("Category", "RealVulkanGpu")]
    public void WindowedContextPresentsAndReadsBackPixels()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_VULKAN_GPU_CONFORMANCE"),
                "1",
                StringComparison.Ordinal))
            return;

        var platformFactory = CreatePlatformFactory();

        Environment.SetEnvironmentVariable("SQUARE_VULKAN_READBACK", "1");
        Environment.SetEnvironmentVariable("SQUARE_VULKAN_MSAA", "1");
        RenderBackendRegistry.Register(new VulkanBackendFactory());

        using var host = platformFactory.CreateHost(new PlatformHostCreateInfo
        {
            Title = "Square Vulkan conformance",
            Width = 64,
            Height = 48,
            RenderBackend = "Vulkan"
        });
        host.Show();

        using var context = host.CreateRenderContext();
        Assert.False(context.SupportsPartialRendering);
        var source = Assert.IsAssignableFrom<IRenderBitmapSource>(context);
        Assert.True(source.IsCaptureAvailable);

        context.Clear(Color.White);
        context.FillRect(
            new Rect(
                context.CanvasSize.Width / 4,
                context.CanvasSize.Height / 4,
                context.CanvasSize.Width / 2,
                context.CanvasSize.Height / 2),
            Brush.FromColor(Color.Red));
        context.Present();
        host.ShowAfterFirstFrame();

        using var bitmap = source.CaptureBitmap();
        AssertPixel(bitmap, 1, 1, 255, 255, 255, 255);
        AssertPixel(bitmap, bitmap.Width / 2, bitmap.Height / 2, 255, 0, 0, 255);
    }

    private static void AssertPixel(Bitmap bitmap, int x, int y, byte r, byte g, byte b, byte a)
    {
        var pixel = bitmap.GetPixel(x, y);
        Assert.Equal(b, pixel[0]);
        Assert.Equal(g, pixel[1]);
        Assert.Equal(r, pixel[2]);
        Assert.Equal(a, pixel[3]);
    }

    private static IPlatformFactory CreatePlatformFactory()
    {
#if PLATFORM_WIN32
        return new Win32PlatformFactory();
#elif PLATFORM_X11
        return new X11PlatformFactory();
#else
        throw new PlatformNotSupportedException("Real Vulkan conformance requires Win32 or X11.");
#endif
    }
}
