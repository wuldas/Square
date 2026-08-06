using Square.Controls;
using Square.Graphics;
using Xunit;

namespace Square.UI.Tests;

public class CssPositioningTests
{
    [Fact]
    public void RelativePositionOffsetsVisualBoxWithoutChangingFlow()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var relative = Block(20);
        relative.Style.Set("position", "relative");
        relative.Style.Set("left", "7px");
        relative.Style.Set("top", "9px");
        var following = Block(10);
        root.Children.Add(relative);
        root.Children.Add(following);

        CssBlockFormattingTests.Layout(root, 100, 80);

        Assert.Equal(new Rect(7, 9, 100, 20), relative.Geometry);
        Assert.Equal(new Rect(0, 20, 100, 10), following.Geometry);
    }

    [Fact]
    public void AbsoluteExplicitInsetsResolveAgainstContainingBlock()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var absolute = new View();
        absolute.Style.Set("display", "block");
        absolute.Style.Set("position", "absolute");
        absolute.Style.Set("left", "10px");
        absolute.Style.Set("right", "20px");
        absolute.Style.Set("top", "5px");
        absolute.Style.Set("bottom", "15px");
        root.Children.Add(absolute);

        CssBlockFormattingTests.Layout(root, 200, 100);

        Assert.Equal(new Rect(10, 5, 170, 80), absolute.Geometry);
    }

    [Fact]
    public void AbsoluteChildOfStaticRootUsesInitialContainingBlock()
    {
        var root = new View();
        root.Style.Set("display", "block");
        root.Style.Set("padding", "20px 30px");
        var absolute = new View();
        absolute.Style.Set("display", "block");
        absolute.Style.Set("position", "absolute");
        absolute.Style.Set("left", "10px");
        absolute.Style.Set("right", "20px");
        absolute.Style.Set("top", "5px");
        absolute.Style.Set("bottom", "15px");
        root.Children.Add(absolute);

        CssBlockFormattingTests.Layout(root, 200, 100);

        Assert.Equal(new Rect(10, 5, 170, 80), absolute.Geometry);
    }

    [Fact]
    public void AbsoluteBoxDoesNotConsumeNormalFlowSpace()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var absolute = Block(50);
        absolute.Style.Set("position", "absolute");
        absolute.Style.Set("left", "0");
        absolute.Style.Set("top", "0");
        var normal = Block(10);
        root.Children.Add(absolute);
        root.Children.Add(normal);

        CssBlockFormattingTests.Layout(root, 100, 80);

        Assert.Equal(0, normal.Geometry.Y);
    }

    [Fact]
    public void AbsolutePositionUsesNearestPositionedBlockContainer()
    {
        var root = new View();
        root.Style.Set("display", "block");
        root.Style.Set("position", "relative");
        var wrapper = Block(40);
        wrapper.Style.Set("width", "100px");
        wrapper.Style.Set("margin-left", "30px");
        wrapper.Style.Set("position", "relative");
        var absolute = Block(10);
        absolute.Style.Set("position", "absolute");
        absolute.Style.Set("left", "10px");
        absolute.Style.Set("right", "10px");
        absolute.Style.Set("top", "5px");
        wrapper.Children.Add(absolute);
        root.Children.Add(wrapper);

        CssBlockFormattingTests.Layout(root, 200, 100);

        Assert.Equal(new Rect(40, 5, 80, 10), absolute.Geometry);
    }

    [Fact]
    public void FixedInsetsResolveAgainstViewportAndDoNotConsumeFlowSpace()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var container = Block(40);
        container.Style.Set("margin-top", "30px");
        var fixedElement = new View();
        fixedElement.Style.Set("display", "block");
        fixedElement.Style.Set("position", "fixed");
        fixedElement.Style.Set("right", "10px");
        fixedElement.Style.Set("bottom", "15px");
        fixedElement.Style.Set("width", "20px");
        fixedElement.Style.Set("height", "10px");
        var following = Block(12);
        container.Children.Add(fixedElement);
        root.Children.Add(container);
        root.Children.Add(following);

        CssBlockFormattingTests.Layout(root, 200, 100);

        Assert.Equal(new Rect(170, 75, 20, 10), fixedElement.Geometry);
        Assert.Equal(70, following.Geometry.Y);
    }

    [Fact]
    public void FixedBoxDoesNotIncreaseScrollExtentOrMoveWithAncestorScroll()
    {
        var root = new View();
        root.Style.Set("display", "block");
        root.Style.Set("overflow-y", "auto");
        var normal = Block(100);
        var fixedElement = Block(400);
        fixedElement.Style.Set("position", "fixed");
        fixedElement.Style.Set("top", "10px");
        root.Children.Add(normal);
        root.Children.Add(fixedElement);

        CssBlockFormattingTests.Layout(root, 100, 50);
        var beforeScroll = fixedElement.Geometry;
        root.ScrollTop = 40;

        Assert.Equal(100, root.ScrollContentSize.Height);
        Assert.Equal(beforeScroll, fixedElement.Geometry);
        Assert.Equal(10, fixedElement.Geometry.Y);
    }

    [Fact]
    public void MaxHeightClampsNaturalAndSpecifiedBlockHeights()
    {
        var root = new View();
        root.Style.Set("display", "block");

        var natural = new View();
        natural.Style.Set("display", "block");
        natural.Style.Set("max-height", "20px");
        natural.Children.Add(Block(50));

        var specified = Block(50);
        specified.Style.Set("max-height", "20px");

        root.Children.Add(natural);
        root.Children.Add(specified);

        CssBlockFormattingTests.Layout(root, 100, 100);

        Assert.Equal(new Rect(0, 0, 100, 20), natural.Geometry);
        Assert.Equal(new Rect(0, 20, 100, 20), specified.Geometry);
    }

    private static View Block(float height)
    {
        var result = new View();
        result.Style.Set("display", "block");
        result.Style.Set("height", $"{height}px");
        return result;
    }
}
