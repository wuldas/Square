using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

public class LayoutRegressionTests
{
    [Theory]
    [InlineData("row", 300, 120)]
    [InlineData("row-reverse", 300, 120)]
    [InlineData("column", 400, 20)]
    public void SingleTemplateRootGrowsAsAnItemOfTheParentFlexContainer(
        string direction, float expectedWidth, float expectedHeight)
    {
        var parent = new View();
        parent.Style.CssText = $"display: flex; flex-direction: {direction}";
        var sibling = new View();
        sibling.Style.CssText = direction == "column" ? "height: 100px" : "width: 100px";
        var component = new GeneratedHost();
        var root = new View();
        root.Style.CssText = "display: flex; flex-direction: column; flex-grow: 1; min-height: 0";
        component.Children.Add(root);
        parent.Children.Add(sibling);
        parent.Children.Add(component);

        new LayoutEngine().MeasureAndArrange(parent, new Size(400, 120));

        Assert.Equal(new Size(expectedWidth, expectedHeight), root.Geometry.Size);
        Assert.Equal(root.Geometry, component.GetBoundingClientRect());
        Assert.Same(parent, component.Parent);
        Assert.Same(component, root.Parent);
    }

    [Fact]
    public void StyledComponentHostKeepsItsOwnBox()
    {
        var parent = new View();
        parent.Style.Set("display", "flex");
        var component = new GeneratedHost();
        component.Style.CssText = "width: 100px; padding: 10px";
        var root = new View();
        root.Style.CssText = "flex-grow: 1; flex-basis: 0";
        component.Children.Add(root);
        parent.Children.Add(component);

        new LayoutEngine().MeasureAndArrange(parent, new Size(400, 120));

        Assert.Equal(new Rect(0, 0, 100, 120), component.Geometry);
        Assert.Equal(new Rect(10, 10, 80, 100), root.Geometry);
    }

    [Theory]
    [InlineData("row")]
    [InlineData("row-reverse")]
    public void RowPercentageWidthResolvesAgainstAllocatedWidth(string direction)
    {
        var parent = new View();
        parent.Style.CssText = $"display: flex; flex-direction: {direction}";
        var sibling = new View();
        sibling.Style.Set("width", "100px");
        var wrapper = new View();
        wrapper.Style.CssText = "display: flex; flex-grow: 1; padding: 0 20px";
        var track = new View();
        track.Style.CssText = "box-sizing: content-box; width: 100%; padding: 0 10px";
        wrapper.Children.Add(track);
        parent.Children.Add(sibling);
        parent.Children.Add(wrapper);

        var engine = new LayoutEngine();
        engine.MeasureAndArrange(parent, new Size(400, 100));

        Assert.Equal(300, wrapper.Geometry.Width);
        Assert.Equal(280, track.GetBoundingClientRect().Width);
    }

    [Fact]
    public void RetainedLayoutRespectsComponentHostBoxChanges()
    {
        var parent = new View();
        parent.Style.CssText = "display: flex; flex-direction: column";
        var component = new GeneratedHost();
        var root = new View();
        root.Style.Set("flex", "1");
        component.Children.Add(root);
        parent.Children.Add(component);
        var engine = new LayoutEngine();
        engine.EnableRetainedFlexLayout();

        engine.MeasureAndArrange(parent, new Size(200, 120));
        Assert.Equal(new Rect(0, 0, 200, 120), root.Geometry);

        component.Style.CssText = "height: 60px; padding: 10px";
        engine.MeasureAndArrange(parent, new Size(200, 120));
        Assert.Equal(new Rect(0, 0, 200, 60), component.Geometry);
        Assert.Equal(new Rect(10, 10, 180, 40), root.Geometry);

        component.Style.Clear();
        engine.MeasureAndArrange(parent, new Size(200, 120));
        Assert.Equal(new Rect(0, 0, 200, 120), root.Geometry);
    }

    [Fact]
    public void IntrinsicMeasurementOfPercentageTrackDoesNotPropagateTheUnboundedWidth()
    {
        var parent = new View();
        parent.Style.CssText = "display: flex; overflow: auto";
        var sibling = new View();
        sibling.Style.Set("width", "100px");
        var wrapper = new View();
        wrapper.Style.CssText = "display: flex; flex-grow: 1";
        var track = new View();
        track.Style.Set("width", "100%");
        wrapper.Children.Add(track);
        parent.Children.Add(sibling);
        parent.Children.Add(wrapper);
        var engine = new LayoutEngine();
        engine.EnableRetainedFlexLayout();

        engine.MeasureAndArrange(parent, new Size(400, 100));
        Assert.Equal(300, wrapper.Geometry.Width);
        Assert.Equal(300, track.GetBoundingClientRect().Width);

        engine.MeasureAndArrange(parent, new Size(240, 100));
        Assert.Equal(140, wrapper.Geometry.Width);
        Assert.Equal(140, track.GetBoundingClientRect().Width);
    }

    private sealed class GeneratedHost : UIElement
    {
        protected override bool IsGeneratedComponent => true;
    }
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
        root.Children.Add(overlay);
        root.Children.Add(normal);

        new LayoutEngine().MeasureAndArrange(root, new Size(200, 100));

        Assert.Equal(new Rect(170, 75, 20, 10), overlay.Geometry);
        Assert.Equal(new Rect(0, 0, 200, 40), normal.Geometry);
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
