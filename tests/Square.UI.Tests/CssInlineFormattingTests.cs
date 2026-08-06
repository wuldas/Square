using Square.Controls;
using Square.Graphics;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

public class CssInlineFormattingTests
{
    [Fact]
    public void AtomicInlineElementsWrapIntoLineBoxes()
    {
        var root = InlineRoot(100);
        var first = Atomic(60, 10);
        var second = Atomic(60, 10);
        root.Children.Add(first);
        root.Children.Add(second);

        CssBlockFormattingTests.Layout(root, 100, 60);

        Assert.Equal(new Rect(0, 0, 60, 10), first.Geometry);
        Assert.Equal(new Rect(0, 10, 60, 10), second.Geometry);
    }

    [Fact]
    public void InlineBlocksWrapAsAtomicInlineLevelBoxes()
    {
        var root = InlineRoot(100);
        var first = InlineBlock(60, 10);
        var second = InlineBlock(60, 10);
        root.Children.Add(first);
        root.Children.Add(second);

        CssBlockFormattingTests.Layout(root, 100, 60);

        Assert.Equal(new Rect(0, 0, 60, 10), first.Geometry);
        Assert.Equal(new Rect(0, 10, 60, 10), second.Geometry);
    }

    [Fact]
    public void InlineBlockLaysOutChildrenInsideItsBoxModel()
    {
        var root = InlineRoot(100);
        var inlineBlock = InlineBlock(40, 20);
        inlineBlock.Style.Set("padding", "5px");
        inlineBlock.Style.Set("border", "2px");
        inlineBlock.Style.Set("margin", "3px");
        var child = new View();
        child.Style.Set("display", "block");
        child.Style.Set("height", "8px");
        inlineBlock.Children.Add(child);
        root.Children.Add(inlineBlock);

        CssBlockFormattingTests.Layout(root, 100, 80);

        Assert.Equal(new Rect(3, 3, 54, 34), inlineBlock.Geometry);
        Assert.Equal(new Rect(10, 10, 40, 8), child.Geometry);
    }

    [Fact]
    public void InlineBoxesShareABasicBaseline()
    {
        var root = InlineRoot(100);
        var shortBox = Atomic(20, 10);
        var tallBox = Atomic(20, 30);
        root.Children.Add(shortBox);
        root.Children.Add(tallBox);

        CssBlockFormattingTests.Layout(root, 100, 60);

        Assert.Equal(20, shortBox.Geometry.Y);
        Assert.Equal(0, tallBox.Geometry.Y);
        Assert.Equal(shortBox.Geometry.Bottom, tallBox.Geometry.Bottom);
    }

    [Fact]
    public void TextAlignCentersInlineLine()
    {
        var root = InlineRoot(100);
        root.Style.Set("text-align", "center");
        var child = Atomic(40, 10);
        root.Children.Add(child);

        CssBlockFormattingTests.Layout(root, 100, 40);

        Assert.Equal(30, child.Geometry.X);
    }

    [Fact]
    public void TextControlWrapsAsInlineFragments()
    {
        var root = InlineRoot(45);
        var text = new Square.Controls.Text("abcdefghij");
        text.Style.Set("display", "inline");
        root.Children.Add(text);

        CssBlockFormattingTests.Layout(root, 45, 100);

        Assert.True(text.Geometry.Height > text.FontSize);
        Assert.True(text.Geometry.Width <= 45);
    }

    [Fact]
    public void TextControlAppliesCssWhiteSpaceAndTransform()
    {
        var root = InlineRoot(200);
        var text = new Square.Controls.Text("hello\nworld")
        {
            FontSize = 10
        };
        text.Style.Set("display", "inline");
        text.Style.Set("white-space", "pre");
        text.Style.Set("text-transform", "uppercase");
        root.Children.Add(text);

        CssBlockFormattingTests.Layout(root, 200, 100);

        Assert.True(text.Geometry.Height >= text.FontSize * 2);
        var fragments = ElementLayoutStore.Get(text).CssTextFragments;
        Assert.NotNull(fragments);
        Assert.Contains(fragments!, fragment => fragment.Text == "HELLO");
        Assert.Contains(fragments!, fragment => fragment.Text == "WORLD");
    }

    private static View InlineRoot(float width)
    {
        var root = new View();
        root.Style.Set("display", "block");
        root.Style.Set("width", $"{width}px");
        return root;
    }

    private static CssMeasuredBox Atomic(float width, float height)
    {
        var result = new CssMeasuredBox(width, height);
        result.Style.Set("display", "inline");
        result.Style.Set("width", $"{width}px");
        result.Style.Set("height", $"{height}px");
        return result;
    }

    private static View InlineBlock(float width, float height)
    {
        var result = new View();
        result.Style.Set("display", "inline-block");
        result.Style.Set("width", $"{width}px");
        result.Style.Set("height", $"{height}px");
        return result;
    }
}
