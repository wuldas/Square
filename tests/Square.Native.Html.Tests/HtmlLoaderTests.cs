using System.Globalization;
using Square.CSS.Engine;
using Square.Html;
using Square.UI;
using Square.UI.ElementApi;
using Square.UI.Svg;
using Xunit;
using DomText = Square.UI.Text;

namespace Square.Native.Html.Tests;

/// <summary>
/// End-to-end tests for the static HTML loader public contract
/// (<see cref="Square.Native.Html.HtmlLoader.LoadFile(string)"/>): real standards markup in,
/// real native <see cref="HTMLElement"/> tree plus computed author styles out. Fixtures live in
/// <c>Fixtures/html-load</c> next to this file and are copied to the test output directory.
/// </summary>
public sealed class HtmlLoaderTests
{
    // ---------- document structure ----------

    [Fact]
    public void LoadsCaseInsensitiveUnquotedMarkupIntoNativeTree()
    {
        WithLoadedFixture(root =>
        {
            var html = Assert.IsType<HTMLHtmlElement>(root);
            Assert.Equal("html", html.LocalName);

            var head = Assert.IsType<HTMLHeadElement>(root.Children[0]);
            var body = Assert.IsType<HTMLBodyElement>(root.Children[1]);

            var meta = Assert.IsType<HTMLMetaElement>(head.Children[0]);
            Assert.Equal("utf-8", meta.GetAttribute("charset"));
            var title = Assert.IsType<HTMLTitleElement>(head.Children[1]);
            Assert.Equal("Static Shell", TextOf(title));

            var container = Assert.IsType<HTMLDivElement>(Assert.Single(body.Children));
            Assert.Equal("main", container.Id);
            Assert.True(container.ClassList.Contains("box"));
            Assert.Equal("#123456", container.Style.Get("color"));

            var paragraph = Assert.IsType<HTMLParagraphElement>(container.Children[0]);
            Assert.Equal(3, paragraph.ChildNodes.Count);
            Assert.Equal("alpha ", Assert.IsType<DomText>(paragraph.ChildNodes[0]).Data);
            var span = Assert.IsType<HTMLSpanElement>(paragraph.ChildNodes[1]);
            Assert.True(span.ClassList.Contains("mid"));
            Assert.Equal("beta", TextOf(span));
            Assert.Equal(" gamma", Assert.IsType<DomText>(paragraph.ChildNodes[2]).Data);

            var list = Assert.IsType<HTMLUListElement>(container.Children[1]);
            // 第一个 <li> 未写闭合标签，parser 必须补全成两个相邻兄弟。
            var first = Assert.IsType<HTMLLIElement>(list.Children[0]);
            var second = Assert.IsType<HTMLLIElement>(list.Children[1]);
            Assert.Equal("甲 & 乙", TextOf(first));
            Assert.Equal("第二项", TextOf(second));

            Assert.IsType<HTMLBRElement>(container.Children[2]);
            var img = Assert.IsType<HTMLImageElement>(container.Children[3]);
            Assert.Equal("dot", img.GetAttribute("alt"));

            var pre = Assert.IsType<HTMLPreElement>(container.Children[4]);
            Assert.Equal("line one\nline two    trailing", NormalizeNewlines(TextOf(pre)));

            var literal = Assert.IsType<HTMLParagraphElement>(container.Children[5]);
            Assert.Equal("{Count} items", TextOf(literal));
            Assert.Equal("{Count}", literal.GetAttribute("data-count"));
        }, "full.html");
    }

    [Fact]
    public void BodyOnlyMarkupStillGetsImplicitHtmlHeadBody()
    {
        // 相对路径按 AppContext.BaseDirectory 解析（fixture 位于测试输出目录）。
        var root = Square.Native.Html.HtmlLoader.LoadFile("Fixtures/html-load/body-only.html");
        try
        {
            Assert.IsType<HTMLHtmlElement>(root);
            Assert.IsType<HTMLHeadElement>(root.Children[0]);
            var body = Assert.IsType<HTMLBodyElement>(root.Children[1]);
            var paragraph = Assert.IsType<HTMLParagraphElement>(Assert.Single(body.Children));
            Assert.Equal("just body", TextOf(paragraph));
        }
        finally
        {
            root.DiscardGeneratedSubtree();
        }
    }

    [Fact]
    public void EmptyFileProducesQueryableEmptyDocumentShell()
    {
        WithLoadedFixture(root =>
        {
            Assert.IsType<HTMLHtmlElement>(root);
            var head = Assert.IsType<HTMLHeadElement>(root.Children[0]);
            var body = Assert.IsType<HTMLBodyElement>(root.Children[1]);
            Assert.Empty(head.ChildNodes);
            Assert.Empty(body.ChildNodes);
        }, "empty.html");
    }

    [Fact]
    public void RelativeImgSrcResolvesAgainstHtmlFileDirectory()
    {
        WithLoadedFixture(root =>
        {
            var body = Assert.IsType<HTMLBodyElement>(root.Children[1]);
            var img = Assert.IsType<HTMLImageElement>(Assert.Single(body.Children));
            var expected = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "html-load", "pic.svg"));
            var actual = Path.GetFullPath(img.GetAttribute("src")!);
            Assert.Equal(expected, actual);
            Assert.True(File.Exists(actual), $"resolved image path does not exist: {actual}");
        }, "img-src.html");
    }

    [Fact]
    public void SvgAttributesMapToNativePropertyNames()
    {
        WithLoadedFixture(root =>
        {
            var body = Assert.IsType<HTMLBodyElement>(root.Children[1]);
            var svg = Assert.IsType<SVGSVGElement>(Assert.Single(body.Children));
            Assert.Equal("0 0 40 40", svg.ViewBox);

            var circle = Assert.IsType<SVGCircleElement>(Assert.Single(svg.Children));
            Assert.Equal(20f, Number(circle, "CenterX"));
            Assert.Equal(20f, Number(circle, "CenterY"));
            Assert.Equal(8f, Number(circle, "Radius"));
            Assert.Equal("red", circle.GetProperty<object>("Fill")?.ToString());
        }, "svg.html");
    }

    [Fact]
    public void BracePlaceholdersInTextAndAttributesStayLiteral()
    {
        WithLoadedFixture(root =>
        {
            var body = Assert.IsType<HTMLBodyElement>(root.Children[1]);
            var paragraph = Assert.IsType<HTMLParagraphElement>(Assert.Single(body.Children));
            Assert.Equal("{Count} literal", TextOf(paragraph));
            Assert.Equal("{Count}", paragraph.GetAttribute("data-count"));
        }, "literal-braces.html");
    }

    // ---------- static-input boundaries ----------

    [Fact]
    public void ScriptElementIsRejected() => AssertScriptRejected("script-element.html");

    [Fact]
    public void SvgScriptElementIsRejected() => AssertScriptRejected("svg-script.html");

    [Fact]
    public void EventAttributeIsRejected() => AssertScriptRejected("event-attribute.html");

    [Fact]
    public void ScriptInsideTemplateContentIsRejected() => AssertScriptRejected("template-script.html");

    [Fact]
    public void UnknownTagThrowsNativeRegistryError() =>
        Assert.Throws<ArgumentException>(() => LoadFixture("unknown-tag.html"));

    [Fact]
    public void MathMlNamespaceIsRejected() =>
        Assert.Throws<NotSupportedException>(() => LoadFixture("mathml-namespace.html"));

    [Fact]
    public void MissingFileThrowsFileNotFoundException() =>
        Assert.Throws<FileNotFoundException>(
            () => Square.Native.Html.HtmlLoader.LoadFile("Fixtures/html-load/does-not-exist.html"));

    [Fact]
    public void NullPathThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => Square.Native.Html.HtmlLoader.LoadFile(null!));

    [Fact]
    public void EmptyPathThrowsArgumentException() =>
        Assert.ThrowsAny<ArgumentException>(() => Square.Native.Html.HtmlLoader.LoadFile(""));

    // ---------- stylesheets ----------

    [Fact]
    public void InlineStyleThenLinkLetsLaterSheetWin()
    {
        WithLoadedFixture(root =>
        {
            Assert.Equal("blue", StyleOf(root, ".order", "color"));
            Assert.Equal("#0f0e0d", StyleOf(root, ".extra", "color"));
        }, "order-style-then-link.html");
    }

    [Fact]
    public void LinkThenInlineStyleLetsLaterSheetWin()
    {
        WithLoadedFixture(root =>
        {
            Assert.Equal("red", StyleOf(root, ".order", "color"));
        }, "order-link-then-style.html");
    }

    [Fact]
    public void InlineImportAnchorsToHtmlFileDirectory()
    {
        // 进程 cwd 与 fixture 目录不同；import 仍必须按 HTML 文件目录解析，否则这里直接抛错。
        WithLoadedFixture(root =>
        {
            Assert.Equal("#112233", StyleOf(root, ".anchored", "color"));
        }, "import-anchor.html");
    }

    [Fact]
    public void LocalFileUriStylesheetLoads()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "square-html-loader-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cssPath = Path.Combine(directory, "uri.css");
            File.WriteAllText(cssPath, ".via-uri { color: #334455; }");
            var htmlPath = Path.Combine(directory, "via-uri.html");
            File.WriteAllText(
                htmlPath,
                "<!doctype html><head><link rel=\"stylesheet\" href=\"" +
                new Uri(cssPath).AbsoluteUri +
                "\"></head><body><p class=\"via-uri\">u</p></body>");

            var root = Square.Native.Html.HtmlLoader.LoadFile(htmlPath);
            try
            {
                Assert.Equal("#334455", StyleOf(root, ".via-uri", "color"));
            }
            finally
            {
                root.DiscardGeneratedSubtree();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ImportantDeclarationBeatsInlineStyleWhichBeatsNormalRule()
    {
        WithLoadedFixture(root =>
        {
            Assert.Equal("red", StyleOf(root, ".imp", "color"));
            Assert.Equal("blue", StyleOf(root, ".plain", "color"));
        }, "important-inline.html");
    }

    [Fact]
    public void NonCssStyleAndDisabledOrNonStylesheetLinksAreIgnored()
    {
        // disabled / preload link 指向不存在的文件：被忽略时绝不能触发文件读取错误。
        WithLoadedFixture(root =>
        {
            Assert.Equal("#0a0b0c", StyleOf(root, ".kept", "color"));
        }, "ignored-stylesheets.html");
    }

    [Fact]
    public void StylesheetLinkWithoutHrefThrows() =>
        Assert.Throws<InvalidOperationException>(() => LoadFixture("link-without-href.html"));

    [Fact]
    public void StylesheetLinkWithEmptyHrefThrows() =>
        Assert.Throws<InvalidOperationException>(() => LoadFixture("link-empty-href.html"));

    [Fact]
    public void NonLocalStylesheetUrlIsRejected() =>
        Assert.Throws<NotSupportedException>(() => LoadFixture("link-nonlocal-url.html"));

    [Fact]
    public void ConditionalStyleMediaIsRejected() =>
        Assert.Throws<NotSupportedException>(() => LoadFixture("style-media-print.html"));

    [Fact]
    public void ConditionalLinkMediaIsRejected() =>
        Assert.Throws<NotSupportedException>(() => LoadFixture("link-media-query.html"));

    [Fact]
    public void CircularImportPreservesExistingEngineError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => LoadFixture("import-circular.html"));
        Assert.Contains("Circular", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingImportPreservesExistingFileError() =>
        Assert.Throws<FileNotFoundException>(() => LoadFixture("import-missing.html"));

    [Fact]
    public void ConditionalImportPreservesExistingNotSupportedError() =>
        Assert.Throws<NotSupportedException>(() => LoadFixture("import-conditional.html"));

    // ---------- scope lifecycle ----------

    [Fact]
    public void DiscardGeneratedSubtreeReleasesTheLoaderStyleScope()
    {
        var root = LoadFixture("order-style-then-link.html");
        var order = Require(root, ".order");

        Assert.Equal("blue", order.Style.Get("color"));
        order.ClassList.Remove("order");
        CssStyleReconciler.Flush();
        Assert.NotEqual("blue", order.Style.Get("color"));

        root.DiscardGeneratedSubtree();

        // Scope released: the author rule never reapplies (a live scope would restore blue).
        order.ClassList.Add("order");
        CssStyleReconciler.Flush();
        Assert.NotEqual("blue", order.Style.Get("color"));
    }

    [Fact]
    public void FailedLoadDoesNotPoisonSubsequentLoadStyles()
    {
        Assert.Throws<InvalidOperationException>(() => LoadFixture("link-without-href.html"));
        Assert.Throws<FileNotFoundException>(() => LoadFixture("import-missing.html"));
        Assert.Throws<NotSupportedException>(() => LoadFixture("import-conditional.html"));

        WithLoadedFixture(root =>
        {
            Assert.Equal("blue", StyleOf(root, ".order", "color"));
            Assert.Equal("#0f0e0d", StyleOf(root, ".extra", "color"));
        }, "order-style-then-link.html");
    }

    // ---------- helpers ----------

    private static string FixturePath(params string[] parts) =>
        Path.GetFullPath(Path.Combine([AppContext.BaseDirectory, "Fixtures", "html-load", .. parts]));

    private static HTMLElement LoadFixture(params string[] parts) =>
        Square.Native.Html.HtmlLoader.LoadFile(FixturePath(parts));

    private static void WithLoadedFixture(Action<HTMLElement> assert, params string[] parts)
    {
        var root = LoadFixture(parts);
        try
        {
            assert(root);
        }
        finally
        {
            root.DiscardGeneratedSubtree();
        }
    }

    private static Element Require(Element root, string selector) =>
        CssSelector.QuerySelector(root, selector)
        ?? throw new InvalidOperationException($"Selector '{selector}' matched nothing.");

    private static string? StyleOf(Element root, string selector, string property) =>
        Require(root, selector).Style.Get(property);

    private static string TextOf(Element element) =>
        string.Concat(element.ChildNodes.OfType<DomText>().Select(node => node.Data));

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static float Number(Element element, string property)
    {
        var value = element.GetProperty<object>(property);
        Assert.NotNull(value);
        return Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }

    private static void AssertScriptRejected(params string[] parts)
    {
        var exception = Assert.Throws<NotSupportedException>(() => LoadFixture(parts));
        Assert.Equal("Static HTML scripts are not supported.", exception.Message);
    }
}
