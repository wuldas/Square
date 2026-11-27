using Square.Hosting;
using Square.Platform.Win32;
using Square.Graphics;
using Xunit;

namespace Square.Platform.Tests;

public sealed class Win32WindowPlacementTests
{
    [Fact]
    public void CreationPublishesFinalClientMetricsBeforeCreatingRenderer()
    {
        if (!OperatingSystem.IsWindows()) return;

        RenderBackendRegistry.Register(new Square.Backends.RenderBackendFactory());
        using var host = new Win32Host(new PlatformHostCreateInfo
        {
            Title = "Square creation lifecycle test",
            Width = 320,
            Height = 240,
            TitleStyle = TitleStyle.Custom,
            BorderStyle = BorderStyle.Resizable
        });
        var notifications = new List<(IntPtr Handle, bool Running, Size Size, float Dpi)>();
        IRenderContext? renderer = null;
        host.SizeChanged += size =>
        {
            notifications.Add((host.Handle, host.IsRunning, size, host.DpiScale));
            renderer ??= host.CreateRenderContext();
        };

        try
        {
            host.Show();

            var creation = Assert.Single(notifications);
            Assert.NotEqual(IntPtr.Zero, creation.Handle);
            Assert.Equal(host.Handle, creation.Handle);
            Assert.True(creation.Running, "Published size before initialization completed.");
            var nativeScale = Win32Api.GetDpiForWindow(host.Handle) / 96f;
            Assert.Equal(nativeScale, creation.Dpi);
            Assert.Equal(host.ClientSize, creation.Size);

            var context = Assert.IsAssignableFrom<IRenderContext>(renderer);
            Assert.Equal(nativeScale, context.DpiScale);
            Assert.True(Win32Api.GetClientRect(host.Handle, out var client));
            using var bitmap = Assert.IsAssignableFrom<IRenderBitmapSource>(context).CaptureBitmap();
            Assert.InRange(bitmap.Width, client.Width, client.Width + 1);
            Assert.InRange(bitmap.Height, client.Height, client.Height + 1);
        }
        finally
        {
            renderer?.Dispose();
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(120, false)]
    [InlineData(-120, false)]
    [InlineData(240, false)]
    [InlineData(-240, false)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    [InlineData(30, true)]
    [InlineData(-60, true)]
    [InlineData(121, true)]
    public void WheelDeltaClassifiesHighResolutionInput(int delta, bool expectedPrecise)
    {
        Assert.Equal(expectedPrecise, Win32Host.IsPreciseWheelDelta((short)delta));
    }

    [Fact]
    public void ChildWindowIsCenteredWithinOwnerBounds()
    {
        var owner = new Win32Api.RECT { Left = 100, Top = 80, Right = 1300, Bottom = 880 };
        var child = new Win32Api.RECT { Left = 0, Top = 0, Right = 900, Bottom = 700 };

        var position = Win32Host.CalculateCenteredPosition(owner, child);

        Assert.Equal((250, 130), position);
    }

    [Fact]
    public void OversizedChildRemainsCenteredAroundOwner()
    {
        var owner = new Win32Api.RECT { Left = 200, Top = 150, Right = 800, Bottom = 550 };
        var child = new Win32Api.RECT { Left = 0, Top = 0, Right = 900, Bottom = 700 };

        var position = Win32Host.CalculateCenteredPosition(owner, child);

        Assert.Equal((50, 0), position);
    }
}
