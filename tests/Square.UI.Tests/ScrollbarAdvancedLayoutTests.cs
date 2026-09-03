using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Square.UI.Scrolling;
using Xunit;

namespace Square.UI.Tests;

public sealed class ScrollbarAdvancedLayoutTests
{
    [Fact]
    public void FlexScrollerIgnoresFixedChildForScrollExtent()
    {
        var scroller = new View();
        scroller.Style.Set("display", "flex");
        scroller.Style.Set("flex-direction", "column");
        scroller.Style.Set("overflow", "auto");
        var content = new View();
        content.Style.Set("height", "80px");
        var fixedChild = new View();
        fixedChild.Style.Set("position", "fixed");
        fixedChild.Style.Set("top", "400px");
        fixedChild.Style.Set("height", "100px");
        scroller.Children.Add(content);
        scroller.Children.Add(fixedChild);

        new LayoutEngine().MeasureAndArrange(scroller, new Size(100, 100));

        var metrics = scroller.GetScrollbarMetrics();
        Assert.False(metrics.HasVertical, $"extent={metrics.MaxScrollY}");
        Assert.Equal(0, metrics.MaxScrollY);
    }


    [Fact]
    public void GridScrollContainerReservesDesktopGutter()
    {
        var scroller = new View();
        scroller.Style.Set("display", "grid");
        scroller.Style.Set("overflow", "auto");
        scroller.Style.Set("grid-template-columns", "1fr");
        scroller.Style.Set("grid-template-rows", "200px");
        var content = new View();
        content.Style.Set("height", "200px");
        scroller.Children.Add(content);

        new LayoutEngine().MeasureAndArrange(scroller, new Size(100, 100));

        var metrics = scroller.GetScrollbarMetrics();
        Assert.True(metrics.HasVertical);
        Assert.False(metrics.HasHorizontal);
        Assert.Equal(85, metrics.ViewportRect.Width);
        Assert.Equal(85, content.Geometry.Width);
        Assert.Equal(0, metrics.MaxScrollX);
        Assert.Equal(100, metrics.MaxScrollY);
    }

    [Fact]
    public void NestedFlexScrollerStabilizesVerticalGutterWithoutFalseHorizontalRange()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        var scroller = new View();
        scroller.Style.Set("display", "flex");
        scroller.Style.Set("flex-direction", "column");
        scroller.Style.Set("width", "100px");
        scroller.Style.Set("height", "100px");
        scroller.Style.Set("overflow", "auto");
        var content = new View();
        content.Style.Set("height", "200px");
        scroller.Children.Add(content);
        root.Children.Add(scroller);

        new LayoutEngine().MeasureAndArrange(root, new Size(100, 100));

        var metrics = scroller.GetScrollbarMetrics();
        Assert.True(metrics.HasVertical);
        Assert.False(metrics.HasHorizontal);
        Assert.Equal(new Rect(0, 0, 85, 100), metrics.ViewportRect);
        Assert.Equal(new Rect(0, 0, 85, 200), content.Geometry);
        Assert.Equal(new Size(85, 200), scroller.ScrollContentSize);
        Assert.Equal(0, metrics.MaxScrollX);
        Assert.Equal(100, metrics.MaxScrollY);
    }

    [Fact]
    public void PopupFlexScrollerStabilizesVerticalGutterWithoutFalseHorizontalRange()
    {
        var root = new View();
        var popup = new Popup();
        popup.Style.Set("width", "100px");
        popup.Style.Set("height", "100px");
        var scroller = new View();
        scroller.Style.Set("display", "flex");
        scroller.Style.Set("flex-direction", "column");
        scroller.Style.Set("width", "100px");
        scroller.Style.Set("height", "100px");
        scroller.Style.Set("overflow", "auto");
        var content = new View();
        content.Style.Set("height", "200px");
        scroller.Children.Add(content);
        popup.Children.Add(scroller);
        root.Children.Add(popup);

        new LayoutEngine().MeasureAndArrange(root, new Size(100, 100));

        var metrics = scroller.GetScrollbarMetrics();
        Assert.True(metrics.HasVertical);
        Assert.False(metrics.HasHorizontal);
        Assert.Equal(new Rect(0, 0, 85, 100), metrics.ViewportRect);
        Assert.Equal(new Rect(0, 0, 85, 200), content.Geometry);
        Assert.Equal(0, metrics.MaxScrollX);
    }

    [Fact]
    public void ContentBoxFlexScrollerGutterDoesNotExpandOuterWidth()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        var scroller = new View();
        scroller.Style.Set("display", "flex");
        scroller.Style.Set("flex-direction", "column");
        scroller.Style.Set("box-sizing", "content-box");
        scroller.Style.Set("width", "100px");
        scroller.Style.Set("height", "100px");
        scroller.Style.Set("overflow-y", "auto");
        scroller.Style.Set("scrollbar-gutter", "stable");
        var content = new View();
        content.Style.Set("height", "200px");
        scroller.Children.Add(content);
        root.Children.Add(scroller);

        new LayoutEngine().MeasureAndArrange(root, new Size(100, 100));

        Assert.Equal(100, scroller.Geometry.Width);
        Assert.Equal(85, scroller.GetScrollbarMetrics().ViewportRect.Width);
        Assert.Equal(85, content.Geometry.Width);
    }

    [Fact]
    public void GridScrollerNestedInFlexUpdatesExtentGeometryAndOffsets()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        var grid = new View();
        grid.Style.Set("display", "grid");
        grid.Style.Set("grid-template-columns", "1fr");
        grid.Style.Set("grid-template-rows", "200px");
        grid.Style.Set("width", "100px");
        grid.Style.Set("height", "100px");
        grid.Style.Set("overflow", "auto");
        var content = new View();
        grid.Children.Add(content);
        root.Children.Add(grid);

        new LayoutEngine().MeasureAndArrange(root, new Size(100, 100));
        grid.ScrollTop = 50;

        var metrics = grid.GetScrollbarMetrics();
        Assert.Equal(new Size(85, 200), grid.ScrollContentSize);
        Assert.Equal(new Rect(0, 0, 85, 100), metrics.ViewportRect);
        Assert.Equal(new Rect(0, 0, 85, 200), content.Geometry);
        Assert.Equal(50, grid.ScrollTop);
        Assert.Equal(0, grid.ScrollLeft);
        Assert.True(metrics.HasVertical);
        Assert.False(metrics.HasHorizontal);
        Assert.Equal(0, metrics.MaxScrollX);
        Assert.Equal(100, metrics.MaxScrollY);
    }

    [Fact]
    public void TableScrollContainerReservesDesktopGutter()
    {
        var table = new Table();
        table.Style.Set("overflow-y", "auto");
        table.Style.Set("width", "100px");
        table.Style.Set("height", "100px");
        var group = new TableRowGroup();
        for (var i = 0; i < 3; i++)
        {
            var row = new TableRow();
            var cell = new TableCell();
            cell.Style.Set("height", "40px");
            row.Children.Add(cell);
            group.Children.Add(row);
        }
        table.Children.Add(group);

        new LayoutEngine().MeasureAndArrange(table, new Size(100, 100));

        var metrics = table.GetScrollbarMetrics();
        Assert.True(metrics.HasVertical);
        Assert.Equal(85, metrics.ViewportRect.Width);
        Assert.True(metrics.MaxScrollY > 0);
    }

    [Fact]
    public void TableScrollerNestedInFlexUpdatesExtentGeometryAndOffsets()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        var table = new Table();
        table.Style.Set("width", "100px");
        table.Style.Set("height", "100px");
        table.Style.Set("overflow", "auto");
        table.Style.Set("border-collapse", "collapse");
        TableCell? firstCell = null;
        for (var i = 0; i < 2; i++)
        {
            var row = new TableRow();
            var cell = new TableCell();
            cell.Style.Set("height", "60px");
            firstCell ??= cell;
            row.Children.Add(cell);
            table.Children.Add(row);
        }
        root.Children.Add(table);

        new LayoutEngine().MeasureAndArrange(root, new Size(100, 100));
        table.ScrollTop = 10;

        var metrics = table.GetScrollbarMetrics();
        Assert.Equal(new Size(85, 120), table.ScrollContentSize);
        Assert.Equal(new Rect(0, 0, 85, 100), metrics.ViewportRect);
        Assert.Equal(new Rect(0, 0, 85, 60), firstCell!.Geometry);
        Assert.Equal(10, table.ScrollTop);
        Assert.Equal(0, table.ScrollLeft);
        Assert.True(metrics.HasVertical);
        Assert.False(metrics.HasHorizontal);
        Assert.Equal(0, metrics.MaxScrollX);
        Assert.Equal(20, metrics.MaxScrollY);
    }

    [Fact]
    public void ExplicitWidthTableKeepsGridInsideStableGutterViewport()
    {
        var table = new Table();
        table.Style.Set("overflow-y", "auto");
        table.Style.Set("scrollbar-gutter", "stable");
        table.Style.Set("width", "100px");
        table.Style.Set("height", "100px");
        var row = new TableRow();
        var cell = new TableCell();
        cell.Style.Set("height", "20px");
        row.Children.Add(cell);
        table.Children.Add(row);

        new LayoutEngine().MeasureAndArrange(table, new Size(100, 100));

        var viewport = table.GetScrollbarMetrics().ViewportRect;
        Assert.Equal(85, viewport.Width);
        Assert.True(cell.Geometry.Right <= viewport.Right);
    }

    [Fact]
    public void TableOverflowInducedByStableGuttersMapsScrollOffset()
    {
        var table = new Table { Geometry = new Rect(0, 0, 100, 100) };
        table.Style.Set("overflow", "auto");
        table.Style.Set("scrollbar-gutter", "stable");
        table.SetScrollContentSize(new Size(90, 100));

        var metrics = table.GetScrollbarMetrics();
        table.ScrollLeft = metrics.MaxScrollX;

        Assert.True(metrics.HasHorizontal);
        Assert.True(table.IsScrollContainer());
        Assert.True(table.ScrollLeft > 0);
        Assert.True(table.MapsScrollOffsetForChildren());
    }

    [Fact]
    public void BlockScrollerNestedInBlockUpdatesExtentGeometryAndOffsets()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var scroller = new View();
        scroller.Style.Set("display", "block");
        scroller.Style.Set("width", "100px");
        scroller.Style.Set("height", "100px");
        scroller.Style.Set("overflow", "auto");
        var content = new View();
        content.Style.Set("display", "block");
        content.Style.Set("height", "200px");
        scroller.Children.Add(content);
        root.Children.Add(scroller);

        new LayoutEngine().MeasureAndArrange(root, new Size(100, 100));
        scroller.ScrollTop = 50;

        var metrics = scroller.GetScrollbarMetrics();
        Assert.Equal(new Size(85, 200), scroller.ScrollContentSize);
        Assert.Equal(new Rect(0, 0, 85, 100), metrics.ViewportRect);
        Assert.Equal(new Rect(0, 0, 85, 200), content.Geometry);
        Assert.Equal(50, scroller.ScrollTop);
        Assert.Equal(0, scroller.ScrollLeft);
        Assert.True(metrics.HasVertical);
        Assert.False(metrics.HasHorizontal);
        Assert.Equal(0, metrics.MaxScrollX);
        Assert.Equal(100, metrics.MaxScrollY);
    }

    [Fact]
    public void StableScrollbarGutterReservesSpaceWithoutOverflow()
    {
        var scroller = new View { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow-y", "auto");
        scroller.Style.Set("scrollbar-gutter", "stable");
        scroller.SetScrollContentSize(new Size(100, 100));

        var metrics = scroller.GetScrollbarMetrics();

        Assert.False(metrics.HasVertical);
        Assert.Equal(85, metrics.ViewportRect.Width);
        Assert.Equal(0, metrics.MaxScrollY);
    }

    [Fact]
    public void StableBothEdgesMovesGridContentIntoReservedViewport()
    {
        var scroller = new View();
        scroller.Style.Set("display", "grid");
        scroller.Style.Set("grid-template-columns", "1fr");
        scroller.Style.Set("grid-template-rows", "100px");
        scroller.Style.Set("overflow-y", "auto");
        scroller.Style.Set("scrollbar-gutter", "stable both-edges");
        var content = new View();
        content.Style.Set("height", "100px");
        scroller.Children.Add(content);

        new LayoutEngine().MeasureAndArrange(scroller, new Size(100, 100));

        Assert.Equal(new Rect(15, 0, 70, 100), content.Geometry);
    }

    [Fact]
    public void StableBothEdgesMovesNormalFlowContentIntoReservedViewport()
    {
        var scroller = new View();
        scroller.Style.Set("display", "block");
        scroller.Style.Set("width", "100px");
        scroller.Style.Set("height", "100px");
        scroller.Style.Set("overflow-y", "auto");
        scroller.Style.Set("scrollbar-gutter", "stable both-edges");
        var content = new View();
        content.Style.Set("height", "100px");
        scroller.Children.Add(content);

        new LayoutEngine().MeasureAndArrange(scroller, new Size(100, 100));

        Assert.Equal(new Rect(15, 0, 70, 100), content.Geometry);
    }

    [Fact]
    public void StableBothEdgesMovesFlexContentIntoReservedViewport()
    {
        var scroller = new View();
        scroller.Style.Set("display", "flex");
        scroller.Style.Set("flex-direction", "column");
        scroller.Style.Set("width", "100px");
        scroller.Style.Set("height", "100px");
        scroller.Style.Set("overflow-y", "auto");
        scroller.Style.Set("scrollbar-gutter", "stable both-edges");
        var content = new View();
        content.Style.Set("height", "100px");
        scroller.Children.Add(content);

        new LayoutEngine().MeasureAndArrange(scroller, new Size(100, 100));

        Assert.Equal(new Rect(15, 0, 70, 100), content.Geometry);
    }

    [Fact]
    public void StableBothEdgesDoesNotCreateScrollableRangeForFittingContent()
    {
        var scroller = new View();
        scroller.Style.Set("display", "grid");
        scroller.Style.Set("grid-template-columns", "1fr");
        scroller.Style.Set("grid-template-rows", "1fr");
        scroller.Style.Set("overflow", "auto");
        scroller.Style.Set("scrollbar-gutter", "stable both-edges");
        scroller.Children.Add(new View());

        new LayoutEngine().MeasureAndArrange(scroller, new Size(100, 100));

        var metrics = scroller.GetScrollbarMetrics();
        Assert.False(metrics.HasVertical);
        Assert.False(metrics.HasHorizontal);
        Assert.Equal(new Rect(15, 15, 70, 70), metrics.ViewportRect);
        Assert.Equal(0, metrics.MaxScrollX);
        Assert.Equal(0, metrics.MaxScrollY);
    }
}
