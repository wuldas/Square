using Square.Graphics;
using Square.Hosting;
using Square.Platform.MacOS;
using Xunit;

namespace Square.Platform.Tests;

public sealed class MacOSHostPolicyTests
{
    public static TheoryData<bool, bool, nuint, AppWindowState> WindowStates => new()
    {
        { false, false, 0, AppWindowState.Normal },
        { false, true, 0, AppWindowState.Maximized },
        { false, false, 1 << 14, AppWindowState.Maximized },
        { true, true, 1 << 14, AppWindowState.Minimized }
    };

    [Theory]
    [MemberData(nameof(WindowStates))]
    public void ResolveStateUsesCocoaWindowFlags(
        bool miniaturized,
        bool zoomed,
        nuint styleMask,
        AppWindowState expected)
    {
        Assert.Equal(expected, MacOSHostPolicy.ResolveState(miniaturized, zoomed, styleMask));
    }

    [Fact]
    public void TextInputRectConvertsTopLeftCoordinatesToCocoaCoordinates()
    {
        var result = MacOSHostPolicy.ToCocoaTextInputRect(
            new Rect(25, 20, 2, 20),
            new Size(800, 600));

        Assert.Equal(new Rect(25, 560, 2, 20), result);
    }

    [Fact]
    public void TextInputRectKeepsANonEmptyNativeInputClient()
    {
        var result = MacOSHostPolicy.ToCocoaTextInputRect(Rect.Empty, new Size(800, 600));

        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
    }
}
