using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.UI.Tests;

public class FlexSizingTests
{
    [Fact]
    public void ExplicitColumnHeightsDoNotShrinkByDefault()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var tabList = new View();
        tabList.Style.Set("height", "42px");
        var panels = new View();
        panels.Style.Set("height", "792px");
        root.Children.Add(tabList);
        root.Children.Add(panels);

        Layout(root, new Size(400, 500));

        Assert.Equal(42, tabList.Geometry.Height);
        Assert.Equal(792, panels.Geometry.Height);
    }

    [Fact]
    public void ExplicitFlexShrinkStillAllowsFixedHeightToShrink()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var first = new View();
        first.Style.Set("height", "300px");
        first.Style.Set("flex-shrink", "1");
        var second = new View();
        second.Style.Set("height", "300px");
        second.Style.Set("flex-shrink", "1");
        root.Children.Add(first);
        root.Children.Add(second);

        Layout(root, new Size(400, 400));

        Assert.Equal(200, first.Geometry.Height);
        Assert.Equal(200, second.Geometry.Height);
    }

    [Fact]
    public void OverflowAutoContentKeepsExplicitChildHeights()
    {
        var scroller = new View();
        scroller.Style.Set("display", "flex");
        scroller.Style.Set("flex-direction", "column");
        scroller.Style.Set("overflow-y", "auto");
        var first = new View();
        first.Style.Set("height", "180px");
        var second = new View();
        second.Style.Set("height", "180px");
        scroller.Children.Add(first);
        scroller.Children.Add(second);

        Layout(scroller, new Size(400, 200));

        Assert.Equal(180, first.Geometry.Height);
        Assert.Equal(180, second.Geometry.Height);
        Assert.Equal(360, scroller.ScrollContentSize.Height);
        Assert.Equal(160, scroller.ScrollContentSize.Height - scroller.Geometry.Height);
    }

    [Fact]
    public void WrappedTextGeometryUsesTheWidthThatProducedItsHeight()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var text = new Square.Controls.Text("Investigation, tools, changes and review share one quiet desktop surface.")
        {
            FontSize = 14
        };
        text.Style.Set("line-height", "22px");
        root.Children.Add(text);

        Layout(root, new Size(220, 120));

        var measuredAtFinalWidth = text.Measure(new Size(text.Geometry.Width, 120));
        Assert.InRange(text.Geometry.Width, 0, root.Geometry.Width);
        Assert.Equal(measuredAtFinalWidth.Height, text.Geometry.Height, 3);
        Assert.True(text.Geometry.Height > 22);
    }

    [Fact]
    public void TextDirectlyInsideRowKeepsIntrinsicSingleLineWidth()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "row");
        root.Style.Set("align-items", "center");
        var text = new Square.Controls.Text("PiSquared") { FontSize = 15 };
        root.Children.Add(text);
        var unconstrained = text.Measure(new Size(float.MaxValue, float.MaxValue));

        Layout(root, new Size(40, 40));

        Assert.InRange(text.Geometry.Width - unconstrained.Width, 0, 1);
        Assert.Equal(unconstrained.Height, text.Geometry.Height, 3);
    }

    [Theory]
    [InlineData("margin-left", "-20px", 40)]
    [InlineData("margin-right", "-20px", 60)]
    public void NegativeHorizontalMarginMatchesCenteredFlexGeometry(
        string property,
        string value,
        float expectedX)
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "row");
        root.Style.Set("justify-content", "center");
        root.Style.Set("align-items", "flex-start");
        var child = new View();
        child.Style.Set("width", "100px");
        child.Style.Set("height", "20px");
        child.Style.Set(property, value);
        root.Children.Add(child);

        Layout(root, new Size(200, 80));

        Assert.Equal(new Rect(expectedX, 0, 100, 20), child.Geometry);
    }

    [Fact]
    public void SplitterNegativeHorizontalMarginsRemoveGapBetweenAdjacentPanels()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "row");
        var left = new View();
        left.Style.Set("width", "100px");
        var splitter = new Splitter();
        splitter.Style.Set("margin-left", "-3px");
        splitter.Style.Set("margin-right", "-3px");
        var right = new View();
        right.Style.Set("width", "100px");
        root.Children.Add(left);
        root.Children.Add(splitter);
        root.Children.Add(right);

        Layout(root, new Size(200, 40));

        Assert.Equal(left.Geometry.Right, right.Geometry.X);
        Assert.Equal(new Rect(97, 0, 6, 40), splitter.Geometry);
        Assert.Equal(3, left.Geometry.Right - splitter.Geometry.X);
        Assert.Equal(3, splitter.Geometry.Right - right.Geometry.X);
    }

    private static void Layout(View root, Size size)
    {
        var layout = new LayoutEngine();
        layout.Measure(root, size);
        layout.Arrange(root, new Rect(0, 0, size.Width, size.Height));
    }
}
