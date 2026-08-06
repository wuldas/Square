using Square.Controls;
using Square.Graphics;
using Xunit;

namespace Square.UI.Tests;

public class CssFloatFormattingTests
{
    [Fact]
    public void DirectLeftAndRightFloatsUseAvailableEdges()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var left = Float("left", 30, 20);
        var right = Float("right", 40, 25);
        root.Children.Add(left);
        root.Children.Add(right);

        CssBlockFormattingTests.Layout(root, 120, 80);

        Assert.Equal(new Rect(0, 0, 30, 20), left.Geometry);
        Assert.Equal(new Rect(80, 0, 40, 25), right.Geometry);
    }

    [Fact]
    public void ClearBothMovesFollowingBlockBelowFloats()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var left = Float("left", 50, 30);
        var cleared = new View();
        cleared.Style.Set("display", "block");
        cleared.Style.Set("clear", "both");
        cleared.Style.Set("height", "10px");
        root.Children.Add(left);
        root.Children.Add(cleared);

        CssBlockFormattingTests.Layout(root, 120, 80);

        Assert.Equal(30, cleared.Geometry.Y);
    }

    [Fact]
    public void TextWrapsBesideThenBelowFloat()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var left = Float("left", 70, 24);
        var text = new Square.Controls.Text("abcdefghijklmno");
        text.Style.Set("display", "inline");
        root.Children.Add(left);
        root.Children.Add(text);

        CssBlockFormattingTests.Layout(root, 100, 100);

        Assert.True(text.Geometry.Height > text.FontSize);
        Assert.True(text.Geometry.Bottom > left.Geometry.Bottom);
    }

    private static CssMeasuredBox Float(string side, float width, float height)
    {
        var result = new CssMeasuredBox(width, height);
        result.Style.Set("display", "block");
        result.Style.Set("float", side);
        result.Style.Set("width", $"{width}px");
        result.Style.Set("height", $"{height}px");
        return result;
    }
}
