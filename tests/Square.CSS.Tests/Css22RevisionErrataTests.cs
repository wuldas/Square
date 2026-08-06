using System.Numerics;
using Square.Controls;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Graphics;
using Square.Rendering;
using Square.UI;
using Xunit;
using GraphicsImage = Square.Graphics.Image;

namespace Square.CSS.Tests;

public sealed class Css22RevisionErrataTests
{
    [Fact]
    public void ParserNumbersAndEscapesRemainPortable()
    {
        var tokens = new CssTokenizer(".icon\\+active { opacity: -.5e+2; width: 1.25e-1px; content: '\\41 B'; }").Tokenize();

        Assert.Contains(tokens, token => token.Type == CssTokenType.Identifier && token.Text == "icon+active");
        Assert.Contains(tokens, token => token.Type == CssTokenType.Number && token.Text == "-.5e+2");
        Assert.Contains(tokens, token => token.Type == CssTokenType.Number && token.Text == "1.25e-1");
        Assert.Contains(tokens, token => token.Type == CssTokenType.Unit && token.Text == "px");
        Assert.Contains(tokens, token => token.Type == CssTokenType.String && token.Text == "AB");
    }

    [Theory]
    [InlineData("serif", "Times New Roman")]
    [InlineData("sans-serif", "Segoe UI")]
    [InlineData("monospace", "Consolas")]
    public void FontFamilyGenericKeywordsUsePortableFallbackMappings(string keyword, string expectedFamily)
    {
        Assert.Equal(expectedFamily, Square.Text.FontManager.Instance.ResolveFamily(keyword));
    }

    [Fact]
    public void MalformedDeclarationsRecoverAtTheNextDeclarationBoundary()
    {
        var sheet = Parse("View { color red; width: ; height: 20px; display: block; }");
        var root = new View();
        var engine = new CssEngine();
        engine.LoadStyleSheet(sheet);
        engine.ApplyStyles(root);

        Assert.Equal("20px", root.Style.Get("height"));
        Assert.Equal("block", root.Style.Get("display"));
    }

    [Fact]
    public void UnexpectedTokenMalformedDeclarationConsumesOnlyItsDeclaration()
    {
        var sheet = Parse("View { ) color:red; background:blue; }");

        var rule = Assert.Single(sheet.Rules);
        var declaration = Assert.Single(rule.Declarations);
        Assert.Equal(("background", "blue"), (declaration.Property, declaration.Value));
    }

    [Fact]
    public void UnexpectedTokenRecoveryRespectsNestedDelimitersAndStrings()
    {
        var sheet = Parse("View { ) color: fn(a; b) [c; d] { nested: \"};\"; }; background:blue; }");

        var rule = Assert.Single(sheet.Rules);
        var declaration = Assert.Single(rule.Declarations);
        Assert.Equal(("background", "blue"), (declaration.Property, declaration.Value));
    }

    [Fact]
    public void OverflowTableBehaviorExposesClippingAndScrollContainerState()
    {
        var table = new Table { Geometry = new Rect(10, 20, 120, 40) };
        table.Style.Set("overflow", "hidden");

        Assert.Equal("hidden", table.Style.Get("overflow"));

        table.Style.Set("overflow", "auto");
        table.SetScrollContentSize(new Size(240, 80));
        Assert.Equal("auto", table.Style.Get("overflow"));
        Assert.Equal(new Size(240, 80), table.ScrollContentSize);
    }

    [Fact]
    public void FixedElementsRenderInTheViewportLayerAfterNormalContent()
    {
        var root = new View();
        root.Style.Set("display", "block");
        var normal = new PaintProbe(Color.Red);
        normal.Style.Set("display", "block");
        normal.Style.Set("width", "40px");
        normal.Style.Set("height", "40px");
        var fixedElement = new PaintProbe(Color.Blue);
        fixedElement.Style.Set("display", "block");
        fixedElement.Style.Set("position", "fixed");
        fixedElement.Style.Set("width", "40px");
        fixedElement.Style.Set("height", "40px");
        fixedElement.Style.Set("z-index", "1");
        root.Children.Add(normal);
        root.Children.Add(fixedElement);

        new LayoutEngine().MeasureAndArrange(root, new Size(100, 100));
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        var context = new RecordingRenderContext();
        tree.Render(context);

        Assert.Equal([Color.Red, Color.Blue], context.Fills);
    }

    [Fact]
    public void VisibilityHiddenKeepsVisibleDescendantsWhileDisplayNoneRemovesThem()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 100) };
        var canvas = new Canvas { Geometry = new Rect(0, 0, 80, 80) };
        var child = new Canvas { Geometry = new Rect(0, 0, 40, 40) };
        var childPaints = 0;
        child.DrawContent = (_, _) => childPaints++;
        canvas.Children.Add(child);
        root.Children.Add(canvas);

        canvas.Style.Set("visibility", "hidden");
        child.Style.Set("visibility", "visible");
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        childPaints = 0;
        tree.Render(new RecordingRenderContext());
        Assert.Equal(1, childPaints);

        childPaints = 0;
        canvas.Style.Set("display", "none");
        tree.Synchronize(root);
        tree.Render(new RecordingRenderContext());
        Assert.Equal(0, childPaints);
    }

    private static Square.CSS.Ast.CssStyleSheet Parse(string css) =>
        new CssParser(new CssTokenizer(css).Tokenize()).Parse();

    private sealed class RecordingRenderContext : IRenderContext
    {
        public List<Color> Fills { get; } = [];
        public Size CanvasSize => new(100, 100);
        public float DpiScale => 1;

        public void PushTransform(Matrix3x2 matrix) { }
        public void PopTransform() { }
        public void PushClip(Rect rect) { }
        public void PushClip(Geometry geometry) { }
        public void PopClip() { }
        public void FillRect(Rect rect, Brush brush)
        {
            if (brush is SolidColorBrush solid && solid.Color.A > 0)
                Fills.Add(solid.Color);
        }
        public void DrawRect(Rect rect, Pen pen) { }
        public void FillPath(PathGeometry path, Brush brush) { }
        public void DrawPath(PathGeometry path, Pen pen) { }
        public void FillGeometry(Geometry geometry, Brush brush) { }
        public void DrawGeometry(Geometry geometry, Pen pen) { }
        public void DrawText(TextLayout text, Point origin, Brush brush) { }
        public void DrawImage(GraphicsImage image, Rect dest, Rect? source = null) { }
        public void PushLayer(Rect bounds, float opacity) { }
        public void PopLayer() { }
        public void Clear(Color color) { }
        public void Clear(Color color, Rect rect) { }
        public void Flush() { }
        public void Present() { }
        public void Present(IReadOnlyList<Rect>? dirtyRects) { }
        public void Dispose() { }
    }

    private sealed class PaintProbe(Color color) : UIElement
    {
        public override void Paint(IRenderContext context) =>
            context.FillRect(Geometry, new SolidColorBrush(color));
    }
}
