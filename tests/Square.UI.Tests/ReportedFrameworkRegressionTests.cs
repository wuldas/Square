using Square.Backends;
using Square.Controls;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Graphics;
using Square.Html;
using Square.Rendering;
using Xunit;

namespace Square.UI.Tests;

public sealed class ReportedFrameworkRegressionTests
{
    [Theory]
    [InlineData("normal", "\u3000\u3000")]
    [InlineData("nowrap", "\u3000\u3000")]
    [InlineData("normal", "\u00a0\u00a0")]
    [InlineData("nowrap", "\u00a0\u00a0")]
    public void HtmlUnicodeWhitespaceOpensLineAndKeepsText(string whiteSpace, string value)
    {
        var root = new HTMLDivElement();
        root.Style.CssText = $"display: block; font-size: 16px; line-height: 24px; white-space: {whiteSpace};";
        var text = new Square.UI.Text(value);
        var following = new HTMLDivElement();
        following.Style.CssText = "height: 10px;";
        root.ChildNodes.Add(text);
        root.Children.Add(following);

        new LayoutEngine().MeasureAndArrange(root, new Size(200, 100));

        Assert.Equal(24, following.Geometry.Y);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        var fragments = tree.CollectTextFragments(root);
        Assert.Equal(value, string.Concat(fragments.Select(fragment => fragment.Text)));
        Assert.All(fragments, fragment => Assert.True(fragment.Bounds.Width > 0));
    }

    [Fact]
    public void TextControlKeepsFullWidthIndentation()
    {
        var plain = new Square.Controls.Text("目录");
        var indented = new Square.Controls.Text("\u3000\u3000目录");
        plain.Style.Set("white-space", "normal");
        indented.Style.Set("white-space", "normal");

        var plainWidth = plain.Measure(new Size(300, 100)).Width;
        var indentWidth = new Square.Controls.Text("\u3000\u3000").Measure(new Size(300, 100)).Width;
        var fullWidth = indented.Measure(new Size(300, 100)).Width;

        Assert.True(indentWidth > 0);
        Assert.Equal(plainWidth + indentWidth, fullWidth, 2);
    }

    [Theory]
    [InlineData(false, "100%")]
    [InlineData(true, "100%")]
    [InlineData(false, "100vh")]
    [InlineData(true, "100vh")]
    public void ColumnMainFlexGrowsWithoutStretchingTopbar(bool retained, string height)
    {
        var root = new View();
        root.ClassList.Add("page");
        var topbar = new View();
        topbar.ClassList.Add("topbar");
        var main = new View();
        main.ClassList.Add("main");
        var sidebar = new View();
        sidebar.Style.CssText = "width: 180px;";
        var content = new View();
        content.Style.CssText = "flex: 1 1 0; min-width: 0;";
        main.Children.Add(sidebar);
        main.Children.Add(content);
        root.Children.Add(topbar);
        root.Children.Add(main);
        var engine = Engine($".page {{ display: flex; flex-direction: column; height: {height}; }} " +
            ".topbar { height: 48px; flex-shrink: 0; } .main { display: flex; flex: 1 1 0; min-height: 0; }");
        try
        {
            engine.ApplyStylesToTree(root);
            var layout = new LayoutEngine();
            if (retained) layout.EnableRetainedFlexLayout();
            foreach (var availableHeight in new[] { 600f, 800f })
            {
                layout.MeasureAndArrange(root, new Size(1000, availableHeight));
                Assert.Equal(new Rect(0, 0, 1000, 48), topbar.Geometry);
                Assert.Equal(new Rect(0, 48, 1000, availableHeight - 48), main.Geometry);
                Assert.Equal(180, sidebar.Geometry.Width);
                Assert.Equal(820, content.Geometry.Width);
                Assert.Null(topbar.Style.Get("flex"));
                Assert.Equal("1 1 0", main.Style.Get("flex"));
            }
        }
        finally
        {
            CssStyleReconciler.UnregisterScopesForTree(root);
        }
    }

    [Fact]
    public void ComponentStyleScopeKeepsCheckboxUserAgentAppearance()
    {
        var root = new View();
        var checkbox = new CheckBox { IsChecked = true, Geometry = new Rect(8, 8, 100, 28) };
        checkbox.ClassList.Add("choice");
        root.Children.Add(checkbox);
        var global = new CssEngine();
        var scoped = Engine(".choice { color: #112233; } .alternate { color: #334455; }");
        try
        {
            global.ApplyStylesToTree(root);
            scoped.ApplyStylesToTree(root);
            checkbox.ClassList.Add("alternate");
            CssStyleReconciler.Flush();
            Assert.Equal("auto", checkbox.Style.Get("appearance"));

            using var context = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo { CanvasSize = new Size(128, 48) });
            context.Clear(Color.White);
            var tree = new DisplayTree();
            tree.BuildFrom(root);
            tree.Render(context);
            using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
            Assert.Contains(Enumerable.Range(8, 20).SelectMany(y => Enumerable.Range(8, 20).Select(x => (x, y))),
                point => bitmap.GetPixel(point.x, point.y).SequenceEqual(new byte[] { 255, 117, 0, 255 }));
        }
        finally
        {
            CssStyleReconciler.UnregisterScopesForTree(root);
        }
    }

    private static CssEngine Engine(string css)
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
        return engine;
    }
}
