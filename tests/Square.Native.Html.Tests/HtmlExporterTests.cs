using Square.Controls;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Graphics;
using Square.Html;
using Square.Native.Html;
using Square.UI;
using Square.UI.Svg;
using Xunit;
using SquareImage = Square.Controls.Image;
using SquareText = Square.Controls.Text;

namespace Square.Native.Html.Tests;

public sealed class HtmlExporterTests
{
    [Fact]
    public void ExportMapsCoreControlsStylesAndFormState()
    {
        var root = new View { Id = "root" };
        root.ClassList.Add("page");
        root.Style.Set("display", "flex");
        root.Style.Set("gap", "12px");
        root.Children.Add(new SquareText("Hello <Square>"));
        root.Children.Add(new Button("Save") { IsDisabled = true });
        root.Children.Add(new Input { Type = "password", Value = "a&b", Placeholder = "Password" });
        root.Children.Add(new TextArea { Value = "Line <one>", Placeholder = "Notes" });
        root.Children.Add(new CheckBox { TextContent = "Remember", IsChecked = true });
        root.Children.Add(new Radio { TextContent = "Pro", GroupName = "plan", IsChecked = true });
        root.Children.Add(new Select { Value = "Pro", Options = ["Free", "Pro"] });

        var result = HtmlExporter.Export(root, new HtmlExportOptions { Title = "Export <test>" });

        Assert.Contains("<!doctype html>", result.Html);
        Assert.Contains("<title>Export &lt;test&gt;</title>", result.Html);
        Assert.Contains("id=\"root\"", result.Html);
        Assert.Contains("class=\"page square-root sq-style-", result.Html);
        Assert.Contains("display:flex;gap:12px;", result.Css);
        // author 类只保留给页面选择器；生成的声明进签名类，不再合并进 .page 规则。
        Assert.DoesNotContain(".page{", result.Css);
        Assert.Contains("<style data-square-css=\"true\">", result.Html);
        Assert.DoesNotContain("style=\"", result.Html);
        Assert.Contains("Hello &lt;Square&gt;", result.Html);
        Assert.Contains("<button disabled>Save</button>", result.Html);
        Assert.Contains("appearance:auto", result.Css);
        Assert.Contains("type=\"password\"", result.Html);
        Assert.Contains("value=\"a&amp;b\"", result.Html);
        Assert.Contains("<textarea placeholder=\"Notes\">Line &lt;one&gt;</textarea>", result.Html);
        Assert.Contains("type=\"checkbox\" checked", result.Html);
        Assert.Contains("type=\"radio\" name=\"plan\" checked", result.Html);
        Assert.Contains("<option value=\"Pro\" selected>Pro</option>", result.Html);
        Assert.DoesNotContain("<script", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ExportIsolatesComputedStylesOfElementsSharingAnAuthorClass()
    {
        var root = new View();
        root.ClassList.Add("page");
        root.Children.Add(CreateComparisonPanel("comparison-blue", "#2563eb", "2px solid #1e3a8a"));
        root.Children.Add(CreateComparisonPanel("comparison-violet", "#7c3aed", "2px solid #4c1d95"));
        root.Children.Add(CreateComparisonPanel("comparison-amber", "#d97706", "2px solid #92400e"));
        root.Children.Add(CreateComparisonPanel("comparison-green", "#16a34a", "2px solid #166534"));
        // 与蓝色面板声明集合完全一致：去重后必须复用同一个签名类。
        root.Children.Add(CreateComparisonPanel("comparison-blue", "#2563eb", "2px solid #1e3a8a"));

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            IncludeDocument = false,
            IncludeBaselineCss = false
        });

        var panels = result.Html.Split("class=\"", StringSplitOptions.None)
            .Skip(1)
            .Select(value => value.Split('"')[0])
            .Where(value => value.Contains("comparison-card", StringComparison.Ordinal))
            .Select(value =>
            {
                var parts = value.Split(' ');
                return new
                {
                    // 快照 class 有序化输出；author 类与变体类都必须原样保留。
                    Variant = parts.Single(part =>
                        part != "comparison-card" && !part.StartsWith("sq-style-", StringComparison.Ordinal)),
                    Generated = parts.Single(part => part.StartsWith("sq-style-", StringComparison.Ordinal))
                };
            })
            .ToArray();

        Assert.Equal(
        ["comparison-blue", "comparison-violet", "comparison-amber", "comparison-green", "comparison-blue"],
            panels.Select(static panel => panel.Variant).ToArray());

        // 不同求值样式的元素必须拿到不同的签名类；一致的去重到同一个。
        Assert.Equal(4, panels.Take(4).Select(static panel => panel.Generated).Distinct().Count());
        Assert.Equal(panels[0].Generated, panels[4].Generated);

        // 每个签名类的规则只携带该元素自身的背景/边框，而不是最后一个节点的紫色。
        Assert.Contains($".{panels[0].Generated}{{background:#2563eb;border:2px solid #1e3a8a;}}", result.Css);
        Assert.Contains($".{panels[1].Generated}{{background:#7c3aed;border:2px solid #4c1d95;}}", result.Css);
        Assert.Contains($".{panels[2].Generated}{{background:#d97706;border:2px solid #92400e;}}", result.Css);
        Assert.Contains($".{panels[3].Generated}{{background:#16a34a;border:2px solid #166534;}}", result.Css);
        Assert.Equal(1, CountOccurrences(result.Css, "background:#2563eb;"));
        Assert.Equal(1, CountOccurrences(result.Css, "background:#7c3aed;"));
        Assert.DoesNotContain("#7c3aed", RuleFor(result.Css, panels[0].Generated), StringComparison.Ordinal);
        Assert.DoesNotContain("#2563eb", RuleFor(result.Css, panels[1].Generated), StringComparison.Ordinal);

        // 共享 author 类绝不再获得合并后的生成声明。
        Assert.DoesNotContain(".comparison-card{", result.Css);
        Assert.DoesNotContain(".comparison-blue{", result.Css);
        Assert.DoesNotContain(".comparison-violet{", result.Css);
    }

    [Fact]
    public void ExportInlineStylesKeepPerElementColorsForSharedAuthorClass()
    {
        var root = new View();
        root.ClassList.Add("page");
        root.Children.Add(CreateComparisonPanel("comparison-blue", "#2563eb", "2px solid #1e3a8a"));
        root.Children.Add(CreateComparisonPanel("comparison-violet", "#7c3aed", "2px solid #4c1d95"));

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            IncludeDocument = false,
            IncludeBaselineCss = false,
            UseInlineStyles = true
        });

        Assert.DoesNotContain("sq-style-", result.Html);
        Assert.DoesNotContain("style data-square-css", result.Html);
        Assert.Contains("style=\"background:#2563eb;border:2px solid #1e3a8a;\"", result.Html);
        Assert.Contains("style=\"background:#7c3aed;border:2px solid #4c1d95;\"", result.Html);
    }

    [Fact]
    public void ExportedOpacityAnimationRunsWithScopedKeyframesAndInlineStyles()
    {
        var root = new HTMLDivElement();
        var badge = new HTMLSpanElement();
        badge.ClassList.Add("fade");
        badge.ChildNodes.Add(new Square.UI.Text("CSS fade-in"));
        root.Children.Add(badge);
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer("""
            @keyframes fade {
                from { opacity: 0.25; background-image: url(javascript:alert(1)); }
                to { opacity: 1; }
            }
            .fade { animation-name: fade; animation-duration: 2s; animation-iteration-count: 1; }
            """).Tokenize()).Parse());
        using var scope = engine.ApplyGeneratedStylesToTree(root);

        var exported = HtmlExporter.Export(root, new HtmlExportOptions { IncludeBaselineCss = false });
        var match = System.Text.RegularExpressions.Regex.Match(exported.Css,
            @"@keyframes (?<name>[A-Za-z_][\w-]*)\{0%\{opacity:0\.25;\}100%\{opacity:1;\}\}");
        Assert.True(match.Success, "the browser needs the active opacity keyframes, not only animation-name");
        var name = match.Groups["name"].Value;
        Assert.Contains($"animation-name:{name};", exported.Css);
        Assert.Contains($"@keyframes {name}", exported.Html);
        Assert.Contains("CSS fade-in", exported.BodyHtml);
        Assert.DoesNotContain("javascript:", exported.Css, StringComparison.OrdinalIgnoreCase);

        var inline = HtmlExporter.Export(root, new HtmlExportOptions
        {
            IncludeBaselineCss = false,
            UseInlineStyles = true
        });
        Assert.Contains($"animation-name:{name};", inline.BodyHtml);
        Assert.Contains($"@keyframes {name}", inline.Css);
        Assert.DoesNotContain("sq-style-", inline.BodyHtml);
    }

    [Fact]
    public void SameNamedComponentAnimationsRetainIndependentOpacityTracks()
    {
        static CssEngine CreateEngine(string from)
        {
            var engine = new CssEngine();
            var css = $"@keyframes pulse {{ from {{ opacity: {from}; }} to {{ opacity: 1; }} }} " +
                ".fade { animation-name: pulse; animation-duration: 1s; }";
            engine.LoadStyleSheet(new CssParser(new CssTokenizer(css).Tokenize()).Parse());
            return engine;
        }

        var root = new View();
        var first = new HTMLSpanElement { Id = "first" };
        var second = new HTMLSpanElement { Id = "second" };
        first.ClassList.Add("fade");
        second.ClassList.Add("fade");
        first.ChildNodes.Add(new Square.UI.Text("First"));
        second.ChildNodes.Add(new Square.UI.Text("Second"));
        root.Children.Add(first);
        root.Children.Add(second);
        using var firstScope = CreateEngine("0.25").ApplyGeneratedStylesToTree(first);
        using var secondScope = CreateEngine("0.6").ApplyGeneratedStylesToTree(second);

        var exported = HtmlExporter.Export(root, new HtmlExportOptions
        {
            IncludeDocument = false,
            IncludeBaselineCss = false,
            UseInlineStyles = true
        });
        var rules = System.Text.RegularExpressions.Regex.Matches(exported.Css,
            @"@keyframes (?<name>[A-Za-z_][\w-]*)\{0%\{opacity:(?<from>0\.\d+);\}100%\{opacity:1;\}\}");
        Assert.Equal(2, rules.Count);
        var firstName = rules.Single(rule => rule.Groups["from"].Value == "0.25").Groups["name"].Value;
        var secondName = rules.Single(rule => rule.Groups["from"].Value == "0.6").Groups["name"].Value;
        Assert.NotEqual(firstName, secondName);
        var firstStyle = System.Text.RegularExpressions.Regex.Match(exported.BodyHtml,
            @"id=""first"" style=""(?<style>[^""]+)""").Groups["style"].Value;
        var secondStyle = System.Text.RegularExpressions.Regex.Match(exported.BodyHtml,
            @"id=""second"" style=""(?<style>[^""]+)""").Groups["style"].Value;
        Assert.Contains($"animation-name:{firstName};", firstStyle);
        Assert.Contains($"animation-name:{secondName};", secondStyle);
    }

    private static View CreateComparisonPanel(string variant, string background, string border)
    {
        var panel = new View();
        panel.ClassList.Add("comparison-card");
        panel.ClassList.Add(variant);
        panel.Style.Set("background", background);
        panel.Style.Set("border", border);
        return panel;
    }

    /// <summary>按生成类名取出完整规则体，验证元素到声明的实际映射而非字符串存在。</summary>
    private static string RuleFor(string css, string generatedClass)
    {
        var start = css.IndexOf("." + generatedClass + "{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing CSS rule for {generatedClass}.");
        return css[start..(css.IndexOf('}', start) + 1)];
    }

    [Fact]
    public void ExportAttachesGeneratedClassToElementsWithoutAuthorClass()
    {
        var root = new View();
        var unique = new View();
        unique.Style.Set("padding", "8px");
        root.Children.Add(unique);

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            IncludeDocument = false,
            IncludeBaselineCss = false
        });

        Assert.Contains("class=\"sq-style-", result.Html);
        Assert.Contains("padding:8px;", result.Css);
        Assert.DoesNotContain("padding-left", result.Css);
    }

    [Fact]
    public void ExportDeduplicatesComputedStylesIntoHeadStylesheet()
    {
        var root = new View();
        var first = new View();
        var second = new View();
        first.Style.Set("padding", "8px");
        second.Style.Set("padding", "8px");
        root.Children.Add(first);
        root.Children.Add(second);

        var result = HtmlExporter.Export(root, new HtmlExportOptions { IncludeDocument = false });

        Assert.DoesNotContain("style=\"", result.Html);
        var classes = result.Html.Split("class=\"", StringSplitOptions.None)
            .Skip(1)
            .Select(value => value.Split('"')[0])
            .Where(value => value.Contains("sq-style-", StringComparison.Ordinal))
            .ToArray();
        Assert.True(classes.Length >= 2);
        Assert.Equal(classes[0], classes[1]);
        Assert.Equal(1, CountOccurrences(result.Css, "padding:8px;"));
    }

    [Fact]
    public void ExportCanOptIntoLegacyInlineStyles()
    {
        var root = new View();
        root.Style.Set("display", "grid");

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            IncludeDocument = false,
            UseInlineStyles = true,
            IncludeBaselineCss = false
        });

        Assert.Contains("style=\"display:grid;\"", result.Html);
        Assert.DoesNotContain("style data-square-css", result.Html);
        Assert.DoesNotContain("display:grid;", result.Css);
    }

    [Fact]
    public void ExportCanReferenceGeneratedExternalStylesheet()
    {
        var root = new View();
        root.Style.Set("display", "flex");

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            StylesheetHref = "/assets/square.generated.css"
        });

        Assert.Contains("<link rel=\"stylesheet\" href=\"/assets/square.generated.css\">", result.Html);
        Assert.DoesNotContain("<style data-square-css=\"true\">", result.Html);
        Assert.Contains("display:flex;", result.Css);
    }

    [Fact]
    public void ExportRejectsUnsafeExternalStylesheetAndKeepsInlineFallback()
    {
        var root = new View();
        root.Style.Set("display", "grid");

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            StylesheetHref = "javascript:alert(1)"
        });

        Assert.Contains("<style data-square-css=\"true\">", result.Html);
        Assert.DoesNotContain("javascript:", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("display:grid;", result.Css);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("stylesheet URL", StringComparison.Ordinal));
    }

    [Fact]
    public void ExportRejectsUnsafeLinkAndMarksUnsupportedControls()
    {
        var root = new View();
        root.Children.Add(new Link("Bad", "javascript:alert(1)"));
        root.Children.Add(new Canvas());

        var result = HtmlExporter.Export(root, new HtmlExportOptions { IncludeDocument = false });

        Assert.Contains("<a>Bad</a>", result.Html);
        Assert.DoesNotContain("javascript:", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-square-kind=\"Canvas\" data-square-unsupported=\"true\"", result.Html);
        Assert.Equal(2, result.Diagnostics.Count);
    }

    [Fact]
    public void ExportSerializesInlineSvgAndBitmapImage()
    {
        var root = new View();
        var svg = new SVGSVGElement { ViewBox = "0 0 24 24" };
        var circle = new SVGCircleElement();
        circle.SetProperty("CenterX", 12);
        circle.SetProperty("CenterY", 12);
        circle.SetProperty("Radius", 10);
        circle.SetProperty("Fill", "#ff0000");
        svg.Children.Add(circle);
        root.Children.Add(svg);

        var bitmap = new Bitmap(1, 1);
        bitmap.SetPixels([0, 0, 255, 255]);
        root.Children.Add(new SquareImage { ImageContent = bitmap });

        var result = HtmlExporter.Export(root, new HtmlExportOptions { IncludeDocument = false });

        Assert.Contains("<svg viewBox=\"0 0 24 24\">", result.Html);
        Assert.Contains("<circle cx=\"12\" cy=\"12\" r=\"10\" fill=\"#ff0000\"></circle>", result.Html);
        Assert.Contains("src=\"data:image/png;base64,", result.Html);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ExportBuildsGeneratedStyleLikeComponentsOnlyOnce()
    {
        var component = new TestComponent();

        var first = HtmlExporter.Export(component, new HtmlExportOptions { IncludeDocument = false });
        var second = HtmlExporter.Export(component, new HtmlExportOptions { IncludeDocument = false });

        Assert.Equal(1, component.BuildCount);
        Assert.Equal(first.Html, second.Html);
        Assert.Contains("data-square-component=", first.Html);
    }

    [Fact]
    public void ExportAppliesAppearanceToNativeFormWidgets()
    {
        var root = new View();
        var autoButton = new Button("Save");
        var noneButton = new Button("Plain");
        noneButton.Style.Set("appearance", "none");
        noneButton.Style.Set("background", "#175cd3");
        var checkBox = new CheckBox { TextContent = "Remember" };
        var noneCheck = new CheckBox { TextContent = "Custom" };
        noneCheck.Style.Set("appearance", "none");
        root.Children.Add(autoButton);
        root.Children.Add(noneButton);
        root.Children.Add(checkBox);
        root.Children.Add(noneCheck);

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            IncludeDocument = false
        });

        Assert.Contains("button,input,select,textarea{appearance:auto;}", result.Css);
        Assert.DoesNotContain("label{appearance:auto;}", result.Css);
        Assert.DoesNotContain(".sq-style-", result.Html.Split("<button", 2)[1].Split("</button>", 2)[0]);
        Assert.Contains("appearance:none", result.Css);
        Assert.Contains("background:#175cd3", result.Css);
        Assert.Contains("<label", result.Html);
        Assert.Contains("<input type=\"checkbox\"", result.Html);
        Assert.Contains("type=\"checkbox\" class=\"sq-style-", result.Html);
        Assert.DoesNotContain("label{appearance:none;}", result.Css);
        Assert.DoesNotContain("input{appearance:none;}", result.Css);
    }

    [Fact]
    public void ExportLeavesDefaultFormWidgetChromeToBrowserUserAgent()
    {
        // Square UA 级默认（2px outset/inset 边框、ButtonFace/Field 背景、13.3333px 字体）
        // 不得固化为 author 级 CSS：否则覆盖浏览器原生外观和 :active 边框变化。
        // 默认按钮只保留可见按下反馈标记，不携带 author 级控件 chrome。
        var root = new View();
        root.Children.Add(new Button("Save"));
        root.Children.Add(new Input { Placeholder = "Name" });
        var htmlButton = new HTMLButtonElement();
        htmlButton.ChildNodes.Add(new Square.UI.Text("Native"));
        root.Children.Add(htmlButton);
        root.Children.Add(new HTMLInputElement { Type = "text", Value = "Editable text" });
        root.Children.Add(new HTMLTextAreaElement());
        root.Children.Add(new HTMLSelectElement());
        new CssEngine().ApplyStylesToTree(root);

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            IncludeDocument = false,
            IncludeBaselineCss = false
        });

        Assert.Contains("sq-default-button", result.Html);
        Assert.Contains(">Save</button>", result.Html);
        Assert.Contains(">Native</button>", result.Html);
        Assert.DoesNotContain("sq-style-", result.Html, StringComparison.Ordinal);
        foreach (var token in new[] { "outset", "inset", "buttonborder", "buttonface", "fieldtext", "#767676", "13.3333" })
            Assert.DoesNotContain(token, result.Css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border", result.Css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("appearance", result.Css, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ExportKeepsAuthorOverridesOnFormWidgets()
    {
        // 作者真实声明（border/background/font/padding）必须原样保留，且不受 UA 剥离影响。
        var root = new View();
        var button = new Button("Save");
        button.Style.Set("border", "3px dashed rebeccapurple");
        button.Style.Set("background-color", "#175cd3");
        var htmlInput = new HTMLInputElement { Type = "text", Value = "Ada" };
        htmlInput.Style.CssText = "border: 1px solid #0ea5e9; padding: 4px;";
        root.Children.Add(button);
        root.Children.Add(htmlInput);
        new CssEngine().ApplyStylesToTree(root);

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            IncludeDocument = false,
            IncludeBaselineCss = false
        });

        Assert.Contains("border:3px dashed rebeccapurple", result.Css);
        Assert.Contains("background-color:#175cd3", result.Css);
        // HTML 宿主上的作者声明同样保留；Square UA 默认（2px outset 等）仍被剥离。
        Assert.Contains("border:1px solid #0ea5e9", result.Css);
        Assert.Contains("padding:4px", result.Css);
        Assert.DoesNotContain("outset", result.Css, StringComparison.Ordinal);
        Assert.DoesNotContain("buttonface", result.Css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sq-default-button", result.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("2px", result.Css, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ExportDoesNotFreezeBrowserActiveStateWithGeneratedBorderStyles()
    {
        // Square UA 的 :active inset 是 UA 级状态样式；导出不得把按下态的 border-style
        // 固化为 author 声明，浏览器的 :active 反馈必须保持由浏览器 UA 接管。
        var root = new View();
        var idle = new Button("Idle");
        var pressed = new Button("Pressed");
        pressed.SetState(ElementState.Active, true);
        root.Children.Add(idle);
        root.Children.Add(pressed);
        new CssEngine().ApplyStylesToTree(root);

        var result = HtmlExporter.Export(root, new HtmlExportOptions
        {
            IncludeDocument = false,
            IncludeBaselineCss = false
        });

        Assert.DoesNotContain("border-style", result.Css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inset", result.Css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outset", result.Css, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ExportCanIncludeInteractionMetadataAndBodyFragment()
    {
        var button = new Button("Save") { Id = "save" };
        button.AddEventListener("click", () => { });

        var result = HtmlExporter.Export(button, new HtmlExportOptions
        {
            EnableInteractions = true
        });

        Assert.Contains($"data-square-id=\"{button.DebugId}\"", result.Html);
        Assert.Contains("data-square-events=\"click\"", result.Html);
        Assert.Contains("id=\"save\"", result.BodyHtml);
        Assert.DoesNotContain("<!doctype html>", result.BodyHtml);
        Assert.DoesNotContain("<script", result.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportKeepsHtmlMixedTextVoidAndSvgOrder()
    {
        var article = new HTMLArticleElement();
        var paragraph = new HTMLParagraphElement();
        paragraph.ChildNodes.Add(new Square.UI.Text("Hello "));
        var strong = new HTMLStrongElement();
        strong.ChildNodes.Add(new Square.UI.Text("世界"));
        paragraph.AppendChild(strong);
        paragraph.ChildNodes.Add(new Square.UI.Text(" !"));
        paragraph.AppendChild(new HTMLBRElement());
        paragraph.ChildNodes.Add(new Square.UI.Text("next"));
        article.AppendChild(paragraph);

        // HTML 混合文本是真正的 DOM Text 节点：按原序进入 childNodes，children 只含元素。
        Assert.Equal(
        [
            Node.NodeType.Text,
            Node.NodeType.Element,
            Node.NodeType.Text,
            Node.NodeType.Element,
            Node.NodeType.Text
        ], paragraph.ChildNodes.Select(static node => node.NodeTypeValue).ToArray());
        Assert.Equal(2, paragraph.Children.Count);
        Assert.DoesNotContain(paragraph.ChildNodes, static node => node is Square.Controls.Text);

        var checkbox = new HTMLInputElement { Type = "checkbox", Checked = true };
        article.AppendChild(checkbox);
        var svg = new SVGSVGElement();
        var circle = new SVGCircleElement();
        circle.SetProperty("Radius", 8);
        svg.Children.Add(circle);
        article.AppendChild(svg);

        var result = HtmlExporter.Export(article, new HtmlExportOptions { IncludeDocument = false });

        Assert.Contains("Hello ", result.BodyHtml);
        Assert.Contains("<strong", result.BodyHtml);
        Assert.Contains("世界</strong> !<br", result.BodyHtml);
        Assert.Contains("next", result.BodyHtml);
        Assert.Contains("<input", result.BodyHtml);
        Assert.Contains(" checked", result.BodyHtml);
        Assert.DoesNotContain("</br>", result.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("</input>", result.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("<svg", result.BodyHtml);
        Assert.Contains("<circle", result.BodyHtml);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ExportReflectsTypedFormStateOnHtmlHosts()
    {
        var root = new View();
        var form = new HTMLFormElement();
        form.AppendChild(new HTMLInputElement { Type = "text", Name = "name", Value = "Ada", Placeholder = "Name" });
        form.AppendChild(new HTMLTextAreaElement { Value = "Line <one>" });
        var select = new HTMLSelectElement { Value = "B" };
        var first = new HTMLOptionElement { Value = "A" };
        first.ChildNodes.Add(new Square.UI.Text("A"));
        var second = new HTMLOptionElement { Value = "B", Selected = true };
        second.ChildNodes.Add(new Square.UI.Text("B"));
        select.AppendChild(first);
        select.AppendChild(second);
        form.AppendChild(select);
        form.AppendChild(new HTMLInputElement { Type = "checkbox", Checked = true });
        form.AppendChild(new HTMLDetailsElement { Open = true });
        root.Children.Add(form);

        var result = HtmlExporter.Export(root, new HtmlExportOptions { IncludeDocument = false });

        Assert.Contains("value=\"Ada\"", result.BodyHtml);
        Assert.Matches(@"<textarea\b[^>]*>Line &lt;one&gt;</textarea>", result.BodyHtml);
        Assert.Matches(@"<option\b(?=[^>]*\bvalue=""B"")(?=[^>]*\bselected(?:\s|>|=))[^>]*>", result.BodyHtml);
        Assert.Matches(@"<input\b(?=[^>]*\btype=""checkbox"")(?=[^>]*\bchecked(?:\s|>|=))[^>]*>", result.BodyHtml);
        Assert.Matches(@"<details\b[^>]*\bopen(?:\s|>|=)", result.BodyHtml);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ExportRejectsActiveHtmlPayloadAndUnsafeUrls()
    {
        var root = new HTMLArticleElement();
        var link = new HTMLAnchorElement();
        link.SetAttribute("href", "javascript:alert(1)");
        link.SetAttribute("onclick", "alert(2)");
        link.SetAttribute("data-state", "a&b");
        link.SetAttribute("title", "<safe>");
        link.Tooltip = "Square fallback";
        link.ChildNodes.Add(new Square.UI.Text("<click>"));
        root.AppendChild(link);
        var image = new HTMLImageElement();
        image.SetAttribute("srcset", "safe.png 1x, javascript:alert(3) 2x");
        root.AppendChild(image);
        var script = new HTMLScriptElement();
        script.ChildNodes.Add(new Square.UI.Text("alert(4)"));
        root.AppendChild(script);
        var iframe = new HTMLIFrameElement();
        iframe.SetAttribute("src", "https://example.test/");
        root.AppendChild(iframe);

        var result = HtmlExporter.Export(root, new HtmlExportOptions { IncludeDocument = false });

        Assert.Contains("data-state=\"a&amp;b\"", result.BodyHtml);
        Assert.Contains("&lt;click&gt;", result.BodyHtml);
        Assert.Contains("title=\"&lt;safe&gt;\"", result.BodyHtml);
        Assert.DoesNotContain("Square fallback", result.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", result.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", result.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("srcset", result.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", result.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", result.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Theory]
    [InlineData("java\nscript:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("javascript:alert(1)")]
    public void HtmlHrefRejectsBrowserNormalizedScriptSchemes(string href)
    {
        var link = new HTMLAnchorElement();
        link.SetAttribute("href", href);
        link.ChildNodes.Add(new Square.UI.Text("safe text"));

        var result = HtmlExporter.Export(link, new HtmlExportOptions { IncludeDocument = false });

        Assert.StartsWith("<a", result.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(" href=", result.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void ClosedDetailsRetainContentForBrowserDisclosure()
    {
        var details = new HTMLDetailsElement();
        var summary = new HTMLSummaryElement();
        summary.ChildNodes.Add(new Square.UI.Text("More"));
        var content = new HTMLParagraphElement();
        content.ChildNodes.Add(new Square.UI.Text("Hidden detail"));
        details.AppendChild(summary);
        details.AppendChild(content);

        var result = HtmlExporter.Export(details, new HtmlExportOptions { IncludeDocument = false });

        Assert.Contains("<details", result.BodyHtml);
        Assert.Contains("<summary", result.BodyHtml);
        Assert.Contains("More", result.BodyHtml);
        // 关闭的 details 内容仍进入导出 DOM：浏览器负责切换显示。
        Assert.Contains("Hidden detail", result.BodyHtml);
        Assert.DoesNotContain(" open", result.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlDocumentRootMergesHeadAndBodyIntoOneShell()
    {
        var html = new HTMLHtmlElement();
        html.SetAttribute("lang", "zh");
        var head = new HTMLHeadElement();
        var title = new HTMLTitleElement();
        title.ChildNodes.Add(new Square.UI.Text("文档标题"));
        head.AppendChild(title);
        html.AppendChild(head);
        var body = new HTMLBodyElement();
        var paragraph = new HTMLParagraphElement();
        paragraph.ChildNodes.Add(new Square.UI.Text("正文"));
        body.AppendChild(paragraph);
        html.AppendChild(body);

        var result = HtmlExporter.Export(html);

        Assert.Equal(1, CountOccurrences(result.Html, "<!doctype html>"));
        Assert.Equal(1, CountOccurrences(result.Html, "<html"));
        Assert.Equal(1, CountOccurrences(result.Html, "<head>"));
        Assert.Equal(1, CountOccurrences(result.Html, "<body"));
        Assert.Contains("<title>文档标题</title>", result.Html);
        Assert.Contains("正文", result.BodyHtml);
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length)
            count++;
        return count;
    }

    private sealed class TestComponent : UIElement
    {
        private bool _built;
        public int BuildCount { get; private set; }

        public override void BuildElementTree()
        {
            if (_built) return;
            _built = true;
            BuildCount++;
            Children.Add(new SquareText("Generated"));
            Style.Set("display", "flex");
        }
    }
}
