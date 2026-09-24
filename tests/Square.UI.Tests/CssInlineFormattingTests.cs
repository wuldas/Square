using Square.Controls;
using Square.Backends;
using Square.Graphics;
using Square.Rendering;
using Square.UI;
using Square.UI.Html;
using Square.UI.Svg;
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

    [Fact]
    public void NestedHtmlInlineTextAndBreakStayInDocumentOrder()
    {
        var root = InlineRoot(180);
        var paragraph = new HtmlElement("p");
        var before = new HtmlTextRun("Hello ");
        var emphasis = new HtmlElement("strong");
        var middle = new HtmlTextRun("世界");
        var after = new HtmlTextRun(" !");
        var next = new HtmlTextRun("next");
        emphasis.Children.Add(middle);
        paragraph.Children.Add(before);
        paragraph.Children.Add(emphasis);
        paragraph.Children.Add(after);
        paragraph.Children.Add(new HtmlElement("br"));
        paragraph.Children.Add(next);
        root.Children.Add(paragraph);

        CssBlockFormattingTests.Layout(root, 180, 100);

        Assert.True(before.Geometry.Width > 0);
        Assert.True(emphasis.Geometry.Width > 0, $"strong={emphasis.Geometry}, middle={middle.Geometry}, before={before.Geometry}, display={emphasis.Style.Get("display")}");
        Assert.True(middle.Geometry.X >= before.Geometry.Right);
        Assert.True(after.Geometry.X >= middle.Geometry.Right);
        Assert.True(next.Geometry.Y > middle.Geometry.Y, $"next={next.Geometry}, middle={middle.Geometry}, after={after.Geometry}");
        Assert.Contains(ElementLayoutStore.Get(middle).CssTextFragments!, fragment => fragment.Text == "世界");
    }

    [Fact]
    public void HtmlFlowArrangesAndPaintsEmbeddedSvg()
    {
        var root = InlineRoot(200);
        var article = new HtmlElement("article");
        var svg = new SVGSVGElement { ViewBox = "0 0 90 45" };
        svg.SetProperty("Width", 90);
        svg.SetProperty("Height", 45);
        var circle = new SVGCircleElement();
        circle.SetProperty("CenterX", 24);
        circle.SetProperty("CenterY", 22);
        circle.SetProperty("Radius", 14);
        circle.SetProperty("Fill", "#2b78ee");
        svg.Children.Add(circle);
        article.Children.Add(svg);
        root.Children.Add(article);

        CssBlockFormattingTests.Layout(root, 200, 80);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        using var bitmap = new Bitmap(200, 80);
        using var context = new RenderContext(bitmap, 1f);
        context.Clear(Color.White);
        tree.Render(context);

        Assert.True(svg.Geometry.Width >= 90);
        var pixel = bitmap.GetPixel((int)svg.Geometry.X + 24, (int)svg.Geometry.Y + 22);
        Assert.True(pixel[0] > pixel[2], $"svg={svg.Geometry}, blue={pixel[0]}, red={pixel[2]}, alpha={pixel[3]}");
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
