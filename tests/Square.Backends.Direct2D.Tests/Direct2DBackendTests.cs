using Square.Graphics;
using System.Runtime.Versioning;
using Xunit;

namespace Square.Backends.Direct2D.Tests;

[SupportedOSPlatform("windows6.1")]
public sealed class Direct2DBackendTests
{
    [Fact]
    public void ExtensionRegistersAndSelectsBackend()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var application = new TestApplication();

        var result = application.UseDirect2DBackend();

        Assert.Same(application, result);
        Assert.Equal("Direct2D", application.RenderBackend);
        Assert.IsType<Direct2DBackendFactory>(RenderBackendRegistry.Get("direct2d"));
    }

    [Fact]
    public void FactoryNameAndNullValidationAreConsistent()
    {
        var factory = new Direct2DBackendFactory();

        Assert.Equal("Direct2D", factory.Name);
        Assert.Throws<ArgumentNullException>(() => factory.CreateContext(null!));
    }

    [Fact]
    public void FactoryFailsFastWithoutWin32Target()
    {
        var factory = new Direct2DBackendFactory();

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;

        var exception = Assert.Throws<Direct2DException>(() => factory.CreateContext(
            new RenderContextCreateInfo { CanvasSize = new Size(8, 6) }));
        Assert.Contains("Win32RenderTarget", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryRejectsZeroWindowHandle()
    {
        if (!OperatingSystem.IsWindows()) return;
        var factory = new Direct2DBackendFactory();

        var exception = Assert.Throws<Direct2DException>(() => factory.CreateContext(
            new RenderContextCreateInfo
            {
                CanvasSize = new Size(8, 6),
                NativeTarget = new Win32RenderTarget(IntPtr.Zero, IntPtr.Zero)
            }));

        Assert.Contains("non-zero", exception.Message, StringComparison.Ordinal);
    }

    private sealed class TestApplication : IRenderBackendApplication
    {
        public string RenderBackend { get; set; } = "Software";
    }
}
