using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Xunit;

namespace Square.UI.Tests;

public class LayoutRegressionTests
{
    [Fact]
    public void EmptyAutoHeightChildrenDoNotConsumeFlexSpace()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        for (var i = 0; i < 3; i++)
        {
            var empty = new View();
            empty.Style.Set("width", "100%");
            root.Children.Add(empty);
        }
        var content = new View();
        content.Style.Set("flex-grow", "1");
        content.Style.Set("height", "0px");
        root.Children.Add(content);

        new LayoutEngine().MeasureAndArrange(root, new Size(400, 300));

        Assert.All(root.Children.Take(3), child => Assert.Equal(0, child.Geometry.Height));
        Assert.Equal(300, content.Geometry.Height);
    }

    [Fact]
    public void FixedElementInsideFlexContainerResolvesAgainstTheViewport()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var normal = new View();
        normal.Style.Set("height", "40px");
        var overlay = new View();
        overlay.Style.Set("position", "fixed");
        overlay.Style.Set("right", "10px");
        overlay.Style.Set("bottom", "15px");
        overlay.Style.Set("width", "20px");
        overlay.Style.Set("height", "10px");
        root.Children.Add(normal);
        root.Children.Add(overlay);

        new LayoutEngine().MeasureAndArrange(root, new Size(200, 100));

        Assert.Equal(new Rect(170, 75, 20, 10), overlay.Geometry);
        Assert.Equal(40, normal.Geometry.Height);
    }

    [Fact]
    public void FixedInsetShorthandFillsTheViewportAndIsMarkedAsFixedRoot()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var overlay = new View();
        overlay.Style.Set("position", "fixed");
        overlay.Style.Set("inset", "5px");
        root.Children.Add(overlay);

        new LayoutEngine().MeasureAndArrange(root, new Size(300, 200));

        Assert.Equal(new Rect(5, 5, 290, 190), overlay.Geometry);
    }
}
