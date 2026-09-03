using Square.Controls;
using Square.Graphics;
using Square.Hosting;
using Square.UI.Scrolling;
using Xunit;

namespace Square.UI.Tests;

public sealed class ScrollbarMobileTests
{
    [Fact]
    public void MobileScrollbarIsOverlayAndFadesAfterScrollIdle()
    {
        var window = new AppWindow("scrollbar-test")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Mobile
        };
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        window.Load(scroller);
        scroller.SetScrollContentSize(new Size(100, 300));

        var metrics = scroller.GetScrollbarMetrics();
        Assert.True(metrics.IsOverlay);
        Assert.Equal(100, metrics.ViewportRect.Width);
        Assert.Equal(4, metrics.VerticalThumb.Width);

        scroller.ScrollTop = 50;
        Assert.Equal(1, scroller.ScrollbarOpacity);

        scroller.AdvanceScrollbarFade(0.5f);
        Assert.Equal(1, scroller.ScrollbarOpacity);
        scroller.AdvanceScrollbarFade(0.1f);
        Assert.InRange(scroller.ScrollbarOpacity, 0.49f, 0.51f);
        scroller.AdvanceScrollbarFade(0.1f);
        Assert.Equal(0, scroller.ScrollbarOpacity);
    }
}
