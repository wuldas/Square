using Square.Graphics;
using Square.Hosting;
using Square.Controls;
using Square.Rendering;
using Square.Rendering.Tree;
using Square.Runtime;
using Square.UI.Scrolling;
using Xunit;

namespace Square.UI.Tests;

public sealed class ScrollbarGeometryTests
{
    [Fact]
    public void AppWindowExposesScrollbarDeviceProfile()
    {
        var window = new AppWindow("Scrollbar test");

        Assert.Equal(ScrollbarDeviceProfile.Auto, window.ScrollbarProfile);
        window.ScrollbarProfile = ScrollbarDeviceProfile.Mobile;
        Assert.Equal(ScrollbarDeviceProfile.Mobile, window.ScrollbarProfile);
    }

    [Fact]
    public void ChangingScrollbarProfileInvalidatesLoadedContentLayout()
    {
        var window = new AppWindow("Scrollbar profile invalidation")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Desktop
        };
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.SetScrollContentSize(new Size(100, 300));
        window.Load(scroller);
        scroller.ClearLayoutDirty();
        scroller.ClearPaintDirty();

        window.ScrollbarProfile = ScrollbarDeviceProfile.Mobile;

        Assert.True(scroller.IsLayoutDirty);
        Assert.True(scroller.NeedsPaint);
        Assert.Equal(new Rect(0, 0, 100, 100), scroller.GetScrollbarMetrics().ViewportRect);
    }

    [Fact]
    public void ChangingScrollbarProfileInvalidatesDescendantScrollerLayoutAndPaint()
    {
        var window = new AppWindow("Nested scrollbar profile invalidation")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Desktop
        };
        var root = new View();
        var wrapper = new View();
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.SetScrollContentSize(new Size(100, 300));
        wrapper.Children.Add(scroller);
        root.Children.Add(wrapper);
        window.Load(root);
        root.ClearLayoutDirty();
        root.ClearPaintDirty();
        wrapper.ClearLayoutDirty();
        wrapper.ClearPaintDirty();
        scroller.ClearLayoutDirty();
        scroller.ClearPaintDirty();

        window.ScrollbarProfile = ScrollbarDeviceProfile.Mobile;

        Assert.True(scroller.IsLayoutDirty);
        Assert.True(scroller.NeedsPaint);
        Assert.Equal(new Rect(0, 0, 100, 100), scroller.GetScrollbarMetrics().ViewportRect);
    }

    [Fact]
    public void DesktopVerticalScrollbarReservesGutterAndPositionsThumb()
    {
        var metrics = ScrollbarGeometry.Calculate(
            new Rect(0, 0, 100, 100),
            new Size(100, 400),
            new Point(0, 100),
            verticalEnabled: true);

        Assert.False(metrics.IsOverlay);
        Assert.True(metrics.HasVertical);
        Assert.False(metrics.HasHorizontal);
        Assert.Equal(15, metrics.ScrollbarThickness);
        Assert.Equal(new Rect(85, 0, 15, 100), metrics.VerticalGutter);
        Assert.Equal(17, metrics.VerticalThumb.Height);
        Assert.Equal(9, metrics.VerticalThumb.Width);
        Assert.InRange(metrics.VerticalThumb.Y, 33, 35);
        Assert.Equal(metrics.ViewportRect, new Rect(0, 0, 85, 100));
    }

    [Fact]
    public void DesktopBothAxesReserveCornerAndThinUsesTwoThirdsWidth()
    {
        var metrics = ScrollbarGeometry.Calculate(
            new Rect(0, 0, 100, 100),
            new Size(300, 300),
            default,
            verticalEnabled: true,
            horizontalEnabled: true,
            width: ScrollbarWidthMode.Thin);

        Assert.True(metrics.HasVertical);
        Assert.True(metrics.HasHorizontal);
        Assert.Equal(10, metrics.ScrollbarThickness);
        Assert.Equal(new Rect(90, 90, 10, 10), metrics.Corner);
        Assert.Equal(new Rect(0, 90, 90, 10), metrics.HorizontalGutter);
        Assert.Equal(new Rect(90, 0, 10, 90), metrics.VerticalGutter);
        Assert.Equal(6, metrics.VerticalThumb.Width);
        Assert.Equal(6, metrics.HorizontalThumb.Height);
    }

    [Fact]
    public void EmptyScrollbarPartsDoNotHitTestOrigin()
    {
        var metrics = ScrollbarGeometry.Calculate(
            new Rect(0, 0, 100, 100),
            new Size(100, 100),
            default,
            verticalEnabled: true,
            horizontalEnabled: true);

        Assert.False(metrics.HasVertical);
        Assert.False(metrics.HasHorizontal);
        Assert.Equal(ScrollbarPart.None, metrics.HitTest(Point.Zero));
    }

    [Fact]
    public void MobileScrollbarIsAnOverlayThumbAndDoesNotHitTest()
    {
        var metrics = ScrollbarGeometry.Calculate(
            new Rect(10, 20, 100, 100),
            new Size(100, 400),
            new Point(0, 100),
            verticalEnabled: true,
            profile: ScrollbarDeviceProfile.Mobile);

        Assert.True(metrics.IsOverlay);
        Assert.True(metrics.HasVertical);
        Assert.Equal(new Rect(10, 20, 100, 100), metrics.ViewportRect);
        Assert.True(metrics.VerticalTrack.IsEmpty);
        Assert.Equal(4, metrics.VerticalThumb.Width);
        Assert.Equal(ScrollbarPart.None, metrics.HitTest(metrics.VerticalThumb.Center));
    }

    [Fact]
    public void MobileStableGutterDoesNotCreateThumbWithoutOverflow()
    {
        var metrics = ScrollbarGeometry.Calculate(
            new Rect(0, 0, 100, 100),
            new Size(100, 100),
            default,
            verticalEnabled: true,
            profile: ScrollbarDeviceProfile.Mobile,
            gutter: ScrollbarGutterMode.Stable);

        Assert.False(metrics.HasVertical);
        Assert.Equal(100, metrics.ViewportRect.Width);
    }

    [Fact]
    public void DesktopStableBothEdgesReservesLeadingAndTrailingGutters()
    {
        var metrics = ScrollbarGeometry.Calculate(
            new Rect(0, 0, 100, 100),
            new Size(100, 100),
            default,
            verticalEnabled: true,
            profile: ScrollbarDeviceProfile.Desktop,
            gutter: ScrollbarGutterMode.StableBothEdges);

        Assert.False(metrics.HasVertical);
        Assert.Equal(new Rect(15, 0, 70, 100), metrics.ViewportRect);
        Assert.True(metrics.VerticalGutter.IsEmpty);
    }

    [Fact]
    public void DesktopStableBothEdgesKeepsBothAxisBarsInTrailingGutters()
    {
        var metrics = ScrollbarGeometry.Calculate(
            new Rect(0, 0, 100, 100),
            new Size(300, 300),
            default,
            verticalEnabled: true,
            horizontalEnabled: true,
            profile: ScrollbarDeviceProfile.Desktop,
            gutter: ScrollbarGutterMode.StableBothEdges);

        Assert.Equal(new Rect(15, 15, 70, 70), metrics.ViewportRect);
        Assert.Equal(new Rect(85, 15, 15, 70), metrics.VerticalGutter);
        Assert.Equal(new Rect(15, 85, 70, 15), metrics.HorizontalGutter);
        Assert.Equal(new Rect(85, 85, 15, 15), metrics.Corner);
    }

    [Fact]
    public void ScrollContainerUsesScrollbarViewportForDesktopMaxOffset()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow", "scroll");
        scroller.SetScrollContentSize(new Size(300, 300));

        var metrics = scroller.GetScrollbarMetrics();

        Assert.Equal(new Rect(0, 0, 85, 85), metrics.ViewportRect);
        Assert.Equal(215, metrics.MaxScrollX);
        Assert.Equal(215, metrics.MaxScrollY);
    }

    [Fact]
    public void AutoOverflowDoesNotShowScrollbarWithoutOverflow()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow-y", "auto");
        scroller.SetScrollContentSize(new Size(100, 100));

        Assert.False(scroller.GetScrollbarMetrics().HasVertical);
    }

    [Theory]
    [InlineData(100, 200)]
    [InlineData(200, 100)]
    public void CrossingAutoOverflowThresholdInvalidatesLayoutAndPaint(float initialHeight, float nextHeight)
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow-y", "auto");
        scroller.SetScrollContentSize(new Size(100, initialHeight));
        scroller.ClearLayoutDirty();
        scroller.ClearPaintDirty();

        scroller.SetScrollContentSize(new Size(100, nextHeight));

        Assert.True(scroller.IsLayoutDirty);
        Assert.True(scroller.NeedsPaint);
    }

    [Fact]
    public void DesktopGutterReducesStretchedChildViewport()
    {
        var scroller = new View();
        scroller.Style.Set("overflow-y", "auto");
        var content = new View();
        content.Style.Set("height", "200px");
        scroller.Children.Add(content);

        new LayoutEngine().MeasureAndArrange(scroller, new Size(100, 100));

        var metrics = scroller.GetScrollbarMetrics();
        Assert.True(metrics.HasVertical);
        Assert.Equal(85, metrics.ViewportRect.Width);
        Assert.Equal(85, content.Geometry.Width);
    }

    [Fact]
    public void DesktopVerticalGutterCanInduceHorizontalScrollbar()
    {
        var scroller = new View();
        scroller.Style.Set("overflow", "auto");
        var content = new View();
        content.Style.Set("width", "90px");
        content.Style.Set("height", "200px");
        scroller.Children.Add(content);

        new LayoutEngine().MeasureAndArrange(scroller, new Size(100, 100));

        var metrics = scroller.GetScrollbarMetrics();
        Assert.True(metrics.HasVertical);
        Assert.True(metrics.HasHorizontal);
        Assert.Equal(85, metrics.ViewportRect.Width);
        Assert.Equal(85, metrics.ViewportRect.Height);
        Assert.False(metrics.Corner.IsEmpty);
    }

    [Fact]
    public void DesktopScrollbarSupportsThumbDragTrackPagingAndButtons()
    {
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.SetScrollContentSize(new Size(100, 400));
        var metrics = scroller.GetScrollbarMetrics();

        var thumbPoint = new Point(
            metrics.VerticalThumb.X + metrics.VerticalThumb.Width / 2,
            metrics.VerticalThumb.Y + metrics.VerticalThumb.Height / 2);
        Assert.Equal(ScrollbarPart.VerticalThumb, scroller.StartScrollbarInteraction(thumbPoint));
        scroller.ContinueScrollbarInteraction(new Point(thumbPoint.X, metrics.VerticalTrack.Bottom));
        scroller.EndScrollbarInteraction();
        Assert.InRange(scroller.ScrollTop, 295, 300);

        scroller.ScrollToTop();
        metrics = scroller.GetScrollbarMetrics();
        var trackPoint = new Point(metrics.VerticalTrack.X + 1, metrics.VerticalTrack.Bottom - 1);
        Assert.Equal(ScrollbarPart.VerticalTrack, scroller.StartScrollbarInteraction(trackPoint));
        Assert.Equal(metrics.ViewportRect.Height, scroller.ScrollTop);

        scroller.ScrollToTop();
        metrics = scroller.GetScrollbarMetrics();
        var buttonPoint = new Point(
            metrics.VerticalForwardButton.X + metrics.VerticalForwardButton.Width / 2,
            metrics.VerticalForwardButton.Y + metrics.VerticalForwardButton.Height / 2);
        Assert.Equal(ScrollbarPart.VerticalForwardButton, scroller.StartScrollbarInteraction(buttonPoint));
        Assert.Equal(40, scroller.ScrollTop);
    }

    [Fact]
    public void DisplayTreeHitTestsScrollbarChromeBeforeDocumentContent()
    {
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.SetScrollContentSize(new Size(100, 400));
        var tree = new DisplayTree();
        tree.Synchronize(scroller);
        var metrics = scroller.GetScrollbarMetrics();
        var point = new Point(metrics.VerticalThumb.X + 2, metrics.VerticalThumb.Y + 2);

        Assert.Same(scroller, tree.HitTestScrollbar(point));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisplayTreeReachesNestedScrollbarGutterInNormalAndFixedLayers(bool fixedLayer)
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 200) };
        var container = new View { Geometry = new Rect(10, 10, 120, 120) };
        if (fixedLayer) container.Style.Set("position", "fixed");
        var scroller = new ScrollViewer { Geometry = new Rect(10, 10, 100, 100) };
        scroller.SetScrollContentSize(new Size(85, 300));
        container.Children.Add(scroller);
        root.Children.Add(container);
        var tree = new DisplayTree();
        tree.Synchronize(root);

        Assert.Same(scroller, tree.HitTestScrollbar(scroller.GetScrollbarMetrics().VerticalThumb.Center));
    }

    [Fact]
    public void FixedRootScrollbarIsHitInItsOwnGutter()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 200) };
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("position", "fixed");
        scroller.SetScrollContentSize(new Size(85, 300));
        root.Children.Add(scroller);
        var tree = new DisplayTree();
        tree.Synchronize(root);

        Assert.Same(scroller, tree.HitTestScrollbar(scroller.GetScrollbarMetrics().VerticalThumb.Center));
    }

    [Fact]
    public void DisplayTreeDoesNotHitTestHiddenScrollbar()
    {
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("visibility", "hidden");
        scroller.SetScrollContentSize(new Size(100, 400));
        var tree = new DisplayTree();
        tree.Synchronize(scroller);
        var metrics = scroller.GetScrollbarMetrics();

        Assert.Null(tree.HitTestScrollbar(metrics.VerticalThumb.Center));
    }

    [Fact]
    public void VisibleScrollbarDescendantUnderHiddenAncestorIsHit()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 200) };
        root.Style.Set("visibility", "hidden");
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("visibility", "visible");
        scroller.SetScrollContentSize(new Size(85, 300));
        root.Children.Add(scroller);
        var tree = new DisplayTree();
        tree.Synchronize(root);

        Assert.Same(scroller, tree.HitTestScrollbar(scroller.GetScrollbarMetrics().VerticalThumb.Center));
    }

    [Theory]
    [InlineData("inline")]
    [InlineData("table-cell")]
    public void UnsupportedFormattingBoxesDoNotExposeScrollbarChrome(string display)
    {
        var element = new View { Geometry = new Rect(0, 0, 100, 100) };
        element.Style.Set("display", display);
        element.Style.Set("overflow-y", "auto");
        element.SetScrollContentSize(new Size(85, 300));

        Assert.False(element.GetScrollbarMetrics().HasVertical);
        Assert.False(element.IsScrollContainer());
    }

    [Fact]
    public void TrackRepeatStopsWhenThumbReachesPressedPoint()
    {
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.SetScrollContentSize(new Size(85, 400));
        var metrics = scroller.GetScrollbarMetrics();
        var point = new Point(metrics.VerticalTrack.Center.X, 60);

        Assert.Equal(ScrollbarPart.VerticalTrack, scroller.StartScrollbarInteraction(point));
        Assert.True(scroller.RepeatScrollbarInteraction());
        var stoppedAt = scroller.ScrollTop;

        Assert.False(scroller.RepeatScrollbarInteraction());
        Assert.Equal(stoppedAt, scroller.ScrollTop);
    }

    [Fact]
    public void NoneWidthHidesChromeWithoutReservingGutter()
    {
        var metrics = ScrollbarGeometry.Calculate(
            new Rect(0, 0, 100, 100),
            new Size(100, 300),
            default,
            verticalEnabled: true,
            width: ScrollbarWidthMode.None);

        Assert.False(metrics.HasVertical);
        Assert.Equal(new Rect(0, 0, 100, 100), metrics.ViewportRect);
        Assert.Equal(200, metrics.MaxScrollY);
    }

    [Fact]
    public void ScrollbarCornerIsNotInteractive()
    {
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow", "auto");
        scroller.SetScrollContentSize(new Size(200, 200));
        var metrics = scroller.GetScrollbarMetrics();
        var point = new Point(metrics.Corner.X + 1, metrics.Corner.Y + 1);

        Assert.Equal(ScrollbarPart.Corner, metrics.HitTest(point));
        Assert.Equal(ScrollbarPart.None, scroller.StartScrollbarInteraction(point));
    }

    [Fact]
    public void EndingScrollbarInteractionInvalidatesPressedChrome()
    {
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.SetScrollContentSize(new Size(100, 400));
        var metrics = scroller.GetScrollbarMetrics();
        var point = new Point(metrics.VerticalThumb.X + 2, metrics.VerticalThumb.Y + 2);
        scroller.ClearPaintDirty();

        Assert.Equal(ScrollbarPart.VerticalThumb, scroller.StartScrollbarInteraction(point));
        scroller.ClearPaintDirty();
        scroller.EndScrollbarInteraction();

        Assert.True(scroller.NeedsPaint);
    }

    [Fact]
    public void DetachingScrollbarClearsInteractionState()
    {
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.SetScrollContentSize(new Size(100, 400));
        var metrics = scroller.GetScrollbarMetrics();
        var point = new Point(metrics.VerticalThumb.X + 2, metrics.VerticalThumb.Y + 2);
        ((IComponentLifecycle)scroller).OnAttached();
        Assert.Equal(ScrollbarPart.VerticalThumb, scroller.StartScrollbarInteraction(point));

        ((IComponentLifecycle)scroller).OnDetached();

        Assert.Equal(ScrollbarPart.None, scroller.ScrollbarInteractionPart);
        Assert.Equal(ScrollbarPart.None, scroller.ScrollbarHoverPart);
    }
}