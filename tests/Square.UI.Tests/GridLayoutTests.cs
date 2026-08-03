using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.UI.Tests;

public sealed class MeasuredBox : UIElement
{
    private readonly Size _size;

    public MeasuredBox(float width, float height)
    {
        _size = new Size(width, height);
    }

    public override Size Measure(Size availableSize) => _size;
}

public class GridLayoutTests
{
    [Fact]
    public void GridLayoutArrangesFrTracksGapAndSpans()
    {
        var root = new View();
        root.Style.Set("display", "grid");
        root.Style.Set("grid-template-columns", "1fr 2fr");
        root.Style.Set("grid-template-rows", "50px 1fr");
        root.Style.Set("gap", "10px");

        var header = new Square.Controls.Text("header");
        header.Style.Set("grid-column", "1 / span 2");
        header.Style.Set("grid-row", "1");
        var left = new Square.Controls.Text("left");
        left.Style.Set("grid-column", "1");
        left.Style.Set("grid-row", "2");
        var right = new Square.Controls.Text("right");
        right.Style.Set("grid-column", "2");
        right.Style.Set("grid-row", "2");

        root.Children.Add(header);
        root.Children.Add(left);
        root.Children.Add(right);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(310, 160));
        layout.Arrange(root, new Rect(0, 0, 310, 160));

        Assert.Equal(new Rect(0, 0, 310, 50), header.Geometry);
        Assert.Equal(new Rect(0, 60, 100, 100), left.Geometry);
        Assert.Equal(new Rect(110, 60, 200, 100), right.Geometry);
    }

    [Fact]
    public void IntrinsicWidthKeywordsUseMeasuredContentWidth()
    {
        var root = new View();
        root.Style.Set("display", "grid");
        root.Style.Set("grid-template-columns", "max-content 1fr");
        root.Style.Set("grid-template-rows", "50px");
        root.Style.Set("gap", "10px");
        var content = new MeasuredBox(75, 20);
        var fill = new MeasuredBox(10, 20);
        fill.Style.Set("grid-column", "2");

        root.Children.Add(content);
        root.Children.Add(fill);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(200, 50));
        layout.Arrange(root, new Rect(0, 0, 200, 50));

        Assert.Equal(new Rect(0, 0, 75, 50), content.Geometry);
        Assert.Equal(new Rect(85, 0, 115, 50), fill.Geometry);
    }

    [Fact]
    public void RelativeFontUnitsResolveAgainstFontSize()
    {
        var root = new View();
        root.Style.Set("font-size", "20px");
        var rem = new MeasuredBox(1, 1);
        rem.Style.Set("width", "2rem");
        rem.Style.Set("height", "1rem");
        var em = new MeasuredBox(1, 1);
        em.Style.Set("font-size", "10px");
        em.Style.Set("width", "3em");
        em.Style.Set("height", "2em");

        root.Children.Add(rem);
        root.Children.Add(em);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(200, 200));
        layout.Arrange(root, new Rect(0, 0, 200, 200));

        Assert.Equal(new Rect(0, 0, 40, 20), rem.Geometry);
        Assert.Equal(new Rect(0, 20, 30, 20), em.Geometry);
    }

    [Fact]
    public void ViewportUnitsResolveAgainstAvailableSize()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        var child = new View();
        child.Style.Set("width", "50vw");
        child.Style.Set("height", "60vh");
        root.Children.Add(child);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(800, 500));
        layout.Arrange(root, new Rect(0, 0, 800, 500));

        Assert.Equal(new Rect(0, 0, 400, 300), child.Geometry);
    }

    [Fact]
    public void NestedViewportUnitsResolveAgainstRootViewport()
    {
        var root = new View();
        root.Style.Set("width", "800px");
        root.Style.Set("height", "500px");
        var container = new View();
        container.Style.Set("width", "200px");
        container.Style.Set("height", "100px");
        var child = new View();
        child.Style.Set("width", "50vw");
        child.Style.Set("height", "50vh");
        container.Children.Add(child);
        root.Children.Add(container);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(800, 500));
        layout.Arrange(root, new Rect(0, 0, 800, 500));

        Assert.Equal(400, child.Geometry.Width);
        Assert.Equal(250, child.Geometry.Height);
    }

    [Fact]
    public void ResponsiveUnitUsesParentWidthPercentage()
    {
        var root = new View();
        var child = new View();
        child.Style.Set("width", "25rp");
        root.Children.Add(child);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(400, 100));
        layout.Arrange(root, new Rect(0, 0, 400, 100));

        Assert.Equal(100, child.Geometry.Width);
    }

    [Fact]
    public void FlexLayoutAppliesCssPaddingShorthandEdges()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        root.Style.Set("padding", "16px 0 0");
        var child = new MeasuredBox(10, 10);
        child.Style.Set("width", "10px");
        child.Style.Set("height", "10px");
        root.Children.Add(child);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(100, 100));
        layout.Arrange(root, new Rect(0, 0, 100, 100));

        Assert.Equal(new Rect(0, 16, 10, 10), child.Geometry);
    }

    [Fact]
    public void WidthDefaultsToBorderBoxWithPadding()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        var child = new View();
        child.Style.Set("width", "100px");
        child.Style.Set("padding", "10px");
        root.Children.Add(child);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(200, 100));
        layout.Arrange(root, new Rect(0, 0, 200, 100));

        Assert.Equal(100, child.Geometry.Width);
    }

    [Fact]
    public void ContentBoxAddsPaddingToSpecifiedWidth()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        var child = new View();
        child.Style.Set("box-sizing", "content-box");
        child.Style.Set("width", "100px");
        child.Style.Set("padding", "10px");
        root.Children.Add(child);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(200, 100));
        layout.Arrange(root, new Rect(0, 0, 200, 100));

        Assert.Equal(120, child.Geometry.Width);
    }

    [Fact]
    public void ExplicitZeroMinWidthAllowsLeafControlToShrink()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("width", "188px");
        var input = new Input();
        input.Style.Set("min-width", "0");
        root.Children.Add(input);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(188, 60));
        layout.Arrange(root, new Rect(0, 0, 188, 60));

        Assert.Equal(188, input.Geometry.Width);
    }

    [Fact]
    public void FlexColumnDoesNotShrinkLeafControlsBelowIntrinsicHeight()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        root.Style.Set("gap", "10px");
        var first = new Button("One");
        var second = new Button("Two");
        var third = new Button("Three");

        root.Children.Add(first);
        root.Children.Add(second);
        root.Children.Add(third);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(200, 60));
        layout.Arrange(root, new Rect(0, 0, 200, 60));

        Assert.True(first.Geometry.Height >= 36);
        Assert.True(second.Geometry.Height >= 36);
        Assert.True(third.Geometry.Height >= 36);
        Assert.True(second.Geometry.Top >= first.Geometry.Bottom + 10);
        Assert.True(third.Geometry.Top >= second.Geometry.Bottom + 10);
    }

    [Fact]
    public void FlexShorthandAcceptsUnitlessZeroBasis()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "row");

        var first = new MeasuredBox(100, 20);
        first.Style.Set("flex", "2 1 0");
        first.Style.Set("min-width", "0");
        var second = new MeasuredBox(700, 20);
        second.Style.Set("flex", "1 1 0");
        second.Style.Set("min-width", "0");
        root.Children.Add(first);
        root.Children.Add(second);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(900, 60));
        layout.Arrange(root, new Rect(0, 0, 900, 60));

        Assert.Equal(600, first.Geometry.Width);
        Assert.Equal(300, second.Geometry.Width);
    }

    [Fact]
    public void OverflowAutoTracksContentSizeAndScrollOffset()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        root.Style.Set("overflow-y", "auto");
        var first = new Button("One");
        var second = new Button("Two");
        var third = new Button("Three");

        root.Children.Add(first);
        root.Children.Add(second);
        root.Children.Add(third);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(200, 60));
        layout.Arrange(root, new Rect(0, 0, 200, 60));

        Assert.True(root.ScrollContentSize.Height > root.Geometry.Height);
        Assert.True(root.ScrollBy(0, 30));
        Assert.True(root.ScrollTop > 0);
        Assert.Same(second, root.HitTest(new Point(10, second.Geometry.Top - root.ScrollTop + 1)));
    }

    [Fact]
    public void OverflowAutoKeepsDynamicControlsMeasurableWhenContentExceedsHeight()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        root.Style.Set("overflow-y", "auto");

        for (var i = 0; i < 30; i++)
            root.Children.Add(new Button("Click " + i));

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(200, 120));
        layout.Arrange(root, new Rect(0, 0, 200, 120));

        Assert.True(root.ScrollContentSize.Height > root.Geometry.Height);
        Assert.All(root.QueryAll<Button>(), button => Assert.True(button.Geometry.Height >= 36));
    }

    [Fact]
    public void ScrollViewerTracksLayoutExtentAndMapsScrolledHitTesting()
    {
        var scroller = new ScrollViewer();
        scroller.Style.Set("display", "flex");
        scroller.Style.Set("flex-direction", "column");
        var first = new MeasuredBox(100, 40);
        var second = new MeasuredBox(100, 40);
        var third = new MeasuredBox(100, 40);
        scroller.Children.Add(first);
        scroller.Children.Add(second);
        scroller.Children.Add(third);

        var layout = new LayoutEngine();
        layout.Measure(scroller, new Size(100, 60));
        layout.Arrange(scroller, new Rect(0, 0, 100, 60));
        scroller.ScrollToBottom();

        Assert.Equal(120, scroller.ExtentHeight);
        Assert.Equal(60, scroller.ScrollableHeight);
        Assert.Equal(60, scroller.VerticalOffset);
        Assert.Same(third, scroller.HitTest(new Point(10, 30)));
    }

    [Fact]
    public void MenuBarStretchesAndMenuItemsKeepIntrinsicWidthWithSubmenus()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var bar = new MenuBar();
        var file = new MenuItem { TextContent = "File" };
        file.Children.Add(new Menu { Geometry = new Rect(0, 0, 220, 100) });
        var view = new MenuItem { TextContent = "View" };
        view.Children.Add(new Menu { Geometry = new Rect(0, 0, 220, 100) });
        bar.Children.Add(file);
        bar.Children.Add(view);
        root.Children.Add(bar);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(600, 200));
        layout.Arrange(root, new Rect(0, 0, 600, 200));

        Assert.Equal(600, bar.Geometry.Width);
        Assert.Equal(56, file.Geometry.Width);
        Assert.Equal(56, view.Geometry.Width);
        Assert.Equal(file.Geometry.Right, view.Geometry.X);
        Assert.Equal(32, bar.Geometry.Height);
    }

    [Fact]
    public void GridMinMaxAndAutoPlacementFillCellsInOrder()
    {
        var root = new View();
        root.Style.Set("display", "grid");
        root.Style.Set("grid-template-columns", "minmax(50px, 1fr) minmax(20px, 2fr)");
        root.Style.Set("grid-template-rows", "40px 40px");
        root.Style.Set("gap", "10px");
        var first = new MeasuredBox(1, 1);
        var second = new MeasuredBox(1, 1);
        var third = new MeasuredBox(1, 1);

        root.Children.Add(first);
        root.Children.Add(second);
        root.Children.Add(third);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(230, 90));
        layout.Arrange(root, new Rect(0, 0, 230, 90));

        Assert.Equal(new Rect(0, 0, 100, 40), first.Geometry);
        Assert.Equal(new Rect(110, 0, 120, 40), second.Geometry);
        Assert.Equal(new Rect(0, 50, 100, 40), third.Geometry);
    }

    [Fact]
    public void GridNumericEndLinesAndAutoPlacementAvoidExplicitCells()
    {
        var root = new View();
        root.Style.Set("display", "grid");
        root.Style.Set("grid-template-columns", "100px 100px");
        root.Style.Set("grid-template-rows", "40px 40px");
        var explicitCell = new MeasuredBox(1, 1);
        explicitCell.Style.Set("grid-column", "2 / 3");
        explicitCell.Style.Set("grid-row", "1 / 2");
        var firstAuto = new MeasuredBox(1, 1);
        var secondAuto = new MeasuredBox(1, 1);
        root.Children.Add(explicitCell);
        root.Children.Add(firstAuto);
        root.Children.Add(secondAuto);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(200, 80));
        layout.Arrange(root, new Rect(0, 0, 200, 80));

        Assert.Equal(new Rect(100, 0, 100, 40), explicitCell.Geometry);
        Assert.Equal(new Rect(0, 0, 100, 40), firstAuto.Geometry);
        Assert.Equal(new Rect(0, 40, 100, 40), secondAuto.Geometry);
    }

    [Fact]
    public void GridTemplateAreasPlaceChildrenByNamedArea()
    {
        var root = new View();
        root.Style.Set("display", "grid");
        root.Style.Set("grid-template-columns", "100px 200px");
        root.Style.Set("grid-template-rows", "40px 60px");
        root.Style.Set("grid-template-areas", "header header | nav main");
        var header = new MeasuredBox(1, 1);
        header.Style.Set("grid-area", "header");
        var main = new MeasuredBox(1, 1);
        main.Style.Set("grid-area", "main");

        root.Children.Add(header);
        root.Children.Add(main);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(300, 100));
        layout.Arrange(root, new Rect(0, 0, 300, 100));

        Assert.Equal(new Rect(0, 0, 300, 40), header.Geometry);
        Assert.Equal(new Rect(100, 40, 200, 60), main.Geometry);
    }

    [Fact]
    public void GridAcceptsChromeQuotedAreasAndIndependentGaps()
    {
        var root = new View();
        root.Style.Set("display", "grid");
        root.Style.Set("grid-template-columns", "80px 120px");
        root.Style.Set("grid-template-rows", "40px 50px");
        root.Style.Set("grid-template-areas", "\"header header\" \"nav main\"");
        root.Style.Set("row-gap", "10px");
        root.Style.Set("column-gap", "20px");
        var header = new MeasuredBox(1, 1);
        header.Style.Set("grid-area", "header");
        var nav = new MeasuredBox(1, 1);
        nav.Style.Set("grid-area", "nav");
        var main = new MeasuredBox(1, 1);
        main.Style.Set("grid-area", "main");
        root.Children.Add(header);
        root.Children.Add(nav);
        root.Children.Add(main);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(220, 100));
        layout.Arrange(root, new Rect(0, 0, 220, 100));

        Assert.Equal(new Rect(0, 0, 220, 40), header.Geometry);
        Assert.Equal(new Rect(0, 50, 80, 50), nav.Geometry);
        Assert.Equal(new Rect(100, 50, 120, 50), main.Geometry);
    }

    [Fact]
    public void GridSpanPropertiesAffectPlacement()
    {
        var root = new View();
        root.Style.Set("display", "grid");
        root.Style.Set("grid-template-columns", "100px 100px");
        root.Style.Set("grid-template-rows", "50px");
        var child = new MeasuredBox(1, 1);
        child.Style.Set("grid-column", "1");
        child.Style.Set("grid-row", "1");
        child.Style.Set("grid-column-span", "2");
        root.Children.Add(child);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(200, 50));
        layout.Arrange(root, new Rect(0, 0, 200, 50));

        Assert.Equal(new Rect(0, 0, 200, 50), child.Geometry);
    }

    [Fact]
    public void FlexShorthandDistributesRemainingSpace()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("width", "300px");
        var first = new View();
        first.Style.Set("flex", "1");
        var second = new View();
        second.Style.Set("flex", "2");
        root.Children.Add(first);
        root.Children.Add(second);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(300, 40));
        layout.Arrange(root, new Rect(0, 0, 300, 40));

        Assert.Equal(100, first.Geometry.Width);
        Assert.Equal(200, second.Geometry.Width);
    }

    [Fact]
    public void AspectRatioDerivesMissingDimension()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        var child = new View();
        child.Style.Set("width", "160px");
        child.Style.Set("aspect-ratio", "16 / 9");
        root.Children.Add(child);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(300, 300));
        layout.Arrange(root, new Rect(0, 0, 300, 300));

        Assert.Equal(160, child.Geometry.Width);
        Assert.Equal(90, child.Geometry.Height);
    }

    [Fact]
    public void InsetShorthandPositionsAbsoluteChild()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("width", "200px");
        root.Style.Set("height", "100px");
        var child = new View();
        child.Style.Set("position", "absolute");
        child.Style.Set("inset", "10px 20px 30px 40px");
        root.Children.Add(child);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(200, 100));
        layout.Arrange(root, new Rect(0, 0, 200, 100));

        Assert.Equal(new Rect(40, 10, 140, 60), child.Geometry);
    }

    [Fact]
    public void BorderWidthParticipatesInYogaLayout()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("border-width", "10px");
        var child = new View();
        child.Style.Set("width", "20px");
        child.Style.Set("height", "20px");
        root.Children.Add(child);

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(100, 100));
        layout.Arrange(root, new Rect(0, 0, 100, 100));

        Assert.Equal(new Rect(10, 10, 20, 20), child.Geometry);
    }

    [Fact]
    public void AlignContentAppliesToWrappedFlexLines()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-wrap", "wrap");
        root.Style.Set("align-content", "center");
        for (var i = 0; i < 4; i++)
        {
            var child = new View();
            child.Style.Set("width", "40px");
            child.Style.Set("height", "20px");
            root.Children.Add(child);
        }

        var layout = new LayoutEngine();
        layout.Measure(root, new Size(100, 100));
        layout.Arrange(root, new Rect(0, 0, 100, 100));

        Assert.Equal(30, root.Children[0].Geometry.Y);
        Assert.Equal(30, root.Children[1].Geometry.Y);
        Assert.Equal(50, root.Children[2].Geometry.Y);
    }
}
