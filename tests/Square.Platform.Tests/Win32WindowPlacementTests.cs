using Square.Hosting;
using Square.Platform.Win32;
using Xunit;

namespace Square.Platform.Tests;

public sealed class Win32WindowPlacementTests
{
    [Fact]
    public void CreationSizeChangedExposesNativeWindowHandle()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var host = new Win32Host(new PlatformHostCreateInfo
        {
            Title = "Square creation lifecycle test",
            Width = 320,
            Height = 240,
            TitleStyle = TitleStyle.Custom,
            BorderStyle = BorderStyle.Resizable
        });
        var handlesDuringCreation = new List<IntPtr>();
        host.SizeChanged += _ => handlesDuringCreation.Add(host.Handle);

        host.Show();

        Assert.NotEmpty(handlesDuringCreation);
        Assert.NotEqual(IntPtr.Zero, host.Handle);
        Assert.All(handlesDuringCreation, handle => Assert.Equal(host.Handle, handle));
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
