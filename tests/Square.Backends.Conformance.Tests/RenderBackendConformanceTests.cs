using Square.Backends;
using Square.Backends.Skia;
using Square.Backends.Vulkan;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Conformance.Tests;

public sealed class RenderBackendConformanceTests
{
    public static TheoryData<IRenderBackendFactory, string> Factories => new()
    {
        { new RenderBackendFactory(), "Software" },
        { new SkiaBackendFactory(), "Skia" },
        { new VulkanBackendFactory(), "Vulkan" }
    };

    public static TheoryData<IRenderBackendFactory, bool> HeadlessFactories => new()
    {
        { new RenderBackendFactory(), true },
        { new SkiaBackendFactory(), true }
    };

    [Theory]
    [MemberData(nameof(Factories))]
    public void FactoryNameAndNullValidationAreConsistent(IRenderBackendFactory factory, string expectedName)
    {
        Assert.Equal(expectedName, factory.Name);
        Assert.Throws<ArgumentNullException>(() => factory.CreateContext(null!));
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void FactoryRegistersAndResolvesCaseInsensitively(IRenderBackendFactory factory, string expectedName)
    {
        RenderBackendRegistry.Register(factory);

        Assert.Same(factory, RenderBackendRegistry.Get(expectedName.ToLowerInvariant()));
        Assert.Contains(expectedName, RenderBackendRegistry.AvailableNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void VulkanFailsFastWithoutNativeTarget()
    {
        var exception = Assert.Throws<VulkanException>(() => new VulkanBackendFactory().CreateContext(
            new RenderContextCreateInfo { CanvasSize = new Size(8, 6) }));

        Assert.Contains("NativeTarget", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(HeadlessFactories))]
    public void HeadlessContextsExposeCommonCapabilities(IRenderBackendFactory factory, bool supportsPartialRendering)
    {
        using var context = factory.CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(8, 6),
            DpiScale = 2
        });

        Assert.Equal(new Size(8, 6), context.CanvasSize);
        Assert.Equal(2, context.DpiScale);
        Assert.Equal(supportsPartialRendering, context.SupportsPartialRendering);
        Assert.IsAssignableFrom<IDpiResizableRenderContext>(context);
        Assert.IsAssignableFrom<IRenderBitmapSource>(context);
    }

    [Theory]
    [MemberData(nameof(HeadlessFactories))]
    public void HeadlessContextsNormalizeInvalidDpi(IRenderBackendFactory factory, bool _)
    {
        using var context = factory.CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(8, 6),
            DpiScale = float.NaN
        });

        Assert.Equal(1, context.DpiScale);
        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        Assert.Equal(8, bitmap.Width);
        Assert.Equal(6, bitmap.Height);
    }

    [Theory]
    [MemberData(nameof(HeadlessFactories))]
    public void HeadlessContextsShareBasicPixelAndCaptureSemantics(IRenderBackendFactory factory, bool _)
    {
        using var context = factory.CreateContext(new RenderContextCreateInfo { CanvasSize = new Size(8, 6) });
        context.Clear(Color.White);
        context.PushClip(new Rect(2, 1, 4, 4));
        context.FillRect(new Rect(0, 0, 8, 6), Brush.FromColor(Color.Red));
        context.PopClip();

        var source = (IRenderBitmapSource)context;
        using var captured = source.CaptureBitmap();
        AssertPixel(captured, 3, 2, 255, 0, 0, 255);
        AssertPixel(captured, 0, 0, 255, 255, 255, 255);

        context.Clear(Color.Blue);
        AssertPixel(captured, 3, 2, 255, 0, 0, 255);
    }

    private static void AssertPixel(Bitmap bitmap, int x, int y, byte r, byte g, byte b, byte a)
    {
        var pixel = bitmap.GetPixel(x, y);
        Assert.Equal(b, pixel[0]);
        Assert.Equal(g, pixel[1]);
        Assert.Equal(r, pixel[2]);
        Assert.Equal(a, pixel[3]);
    }
}
