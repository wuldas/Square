using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.UI.Tests;

public class CssBlockFormattingTests
{
    [Fact]
    public void ContentBoxWidthAndAutoMarginsCenterBlock()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var child = new View();
        child.Style.Set("display", "block");
        child.Style.Set("width", "100px");
        child.Style.Set("height", "20px");
        child.Style.Set("padding", "10px");
        child.Style.Set("margin-left", "auto");
        child.Style.Set("margin-right", "auto");
        root.Children.Add(child);

        Layout(root, 300, 100);

        Assert.Equal(new Rect(90, 0, 120, 40), child.Geometry);
    }

    [Fact]
    public void InspectionBoxModelUsesAsymmetricCssEdges()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var child = new View();
        child.Style.Set("display", "block");
        child.Style.Set("width", "80px");
        child.Style.Set("height", "40px");
        child.Style.Set("padding", "2px 4px 6px 8px");
        child.Style.Set("border-width", "1px 2px 3px 4px");
        child.Style.Set("margin", "5px 7px 9px 11px");
        root.Children.Add(child);

        var layout = LayoutAndReturn(root, 200, 100);
        var box = layout.GetInspectionBoxModel(child);

        Assert.Equal(new Rect(23, 8, 80, 40), box.Content);
        Assert.Equal(new Rect(15, 6, 92, 48), box.Padding);
        Assert.Equal(new Rect(11, 5, 98, 52), box.Border);
        Assert.Equal(new Rect(0, 0, 116, 66), box.Margin);
    }

    [Fact]
    public void AutoWidthFillsContainingBlockMarginBox()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var child = new View();
        child.Style.Set("display", "block");
        child.Style.Set("height", "10px");
        child.Style.Set("margin", "0 15px");
        child.Style.Set("padding", "0 10px");
        root.Children.Add(child);

        Layout(root, 200, 50);

        Assert.Equal(new Rect(15, 0, 170, 10), child.Geometry);
    }

    [Fact]
    public void AdjacentVerticalMarginsCollapse()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var first = Block(20);
        first.Style.Set("margin-bottom", "30px");
        var second = Block(20);
        second.Style.Set("margin-top", "20px");
        root.Children.Add(first);
        root.Children.Add(second);

        Layout(root, 100, 100);

        Assert.Equal(0, first.Geometry.Y);
        Assert.Equal(50, second.Geometry.Y);
    }

    [Fact]
    public void ClearancePreventsMarginCollapseWithPreviousBlock()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var floated = Block(20);
        floated.Style.Set("float", "left");
        var first = Block(10);
        first.Style.Set("margin-bottom", "30px");
        var cleared = Block(10);
        cleared.Style.Set("clear", "both");
        cleared.Style.Set("margin-top", "20px");
        root.Children.Add(floated);
        root.Children.Add(first);
        root.Children.Add(cleared);

        Layout(root, 100, 100);

        Assert.Equal(40, cleared.Geometry.Y);
    }

    [Fact]
    public void MinHeightPreventsLastChildMarginFromCollapsingThroughContainer()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var container = Block(10);
        container.Style.Set("min-height", "30px");
        var child = Block(10);
        child.Style.Set("margin-bottom", "20px");
        container.Children.Add(child);
        root.Children.Add(container);

        Layout(root, 100, 100);

        Assert.Equal(30, container.Geometry.Height);
        Assert.Equal(100, root.Geometry.Height);
    }

    [Fact]
    public void UnspecifiedTreeKeepsLegacyYogaBehavior()
    {
        var root = new View();
        var child = new CssMeasuredBox(25, 12);
        root.Children.Add(child);

        Layout(root, 100, 50);

        Assert.Equal(100, child.Geometry.Width);
        Assert.Equal(12, child.Geometry.Height);
    }

    private static View Block(float height)
    {
        var result = new View();
        result.Style.Set("display", "block");
        result.Style.Set("height", $"{height}px");
        return result;
    }

    internal static void Layout(View root, float width, float height)
    {
        var engine = new LayoutEngine();
        engine.Measure(root, new Size(width, height));
        engine.Arrange(root, new Rect(0, 0, width, height));
    }

    private static LayoutEngine LayoutAndReturn(View root, float width, float height)
    {
        var engine = new LayoutEngine();
        engine.MeasureAndArrange(root, new Size(width, height));
        return engine;
    }
}

internal sealed class CssMeasuredBox : UIElement
{
    private readonly Size _size;

    public CssMeasuredBox(float width, float height) => _size = new Size(width, height);

    public override Size Measure(Size availableSize) => _size;
}
