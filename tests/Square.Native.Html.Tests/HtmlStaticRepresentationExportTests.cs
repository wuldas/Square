using Square.Controls;
using Square.Html;
using Square.Native.Html;
using Square.UI;
using Xunit;

namespace Square.Native.Html.Tests;

/// <summary>
/// Safe static representation exports: package exports, direct <c>IHtmlStaticRepresentation</c>
/// providers and defined customized built-ins must normalize through the standard exporter, never
/// leak their resolution URI, and fail with diagnostics instead of unknown placeholders.
/// </summary>
public sealed class HtmlStaticRepresentationExportTests
{
    private const string PackageNamespace = "urn:acme:export-tests:widgets";

    [Fact]
    public void ExportedPackageElementExportsSafeRepresentationWithoutLeakingItsUri()
    {
        ElementRegistry.Register(PackageNamespace, "badge", static () => new PackageBadgeElement());
        Element widget = new UIDocument().CreateComponentElement(PackageNamespace, "badge");

        // 包解析 URI 只参与模板选择：工厂创建即打标记，但绝不写入 DOM namespaceURI。
        Assert.Equal(PackageNamespace, widget.TemplateExportNamespaceUri);
        Assert.Equal("badge", widget.TemplateExportLocalName);
        Assert.Equal(ElementRegistry.HtmlNamespaceUri, widget.NamespaceURI);
        widget.AddEventListener("click", static () => { });

        var result = HtmlExporter.Export(widget, new HtmlExportOptions
        {
            IncludeDocument = false,
            EnableInteractions = true
        });

        Assert.Contains("Badge: idle", result.BodyHtml);
        Assert.Contains("<span", result.BodyHtml);
        Assert.DoesNotContain("urn:acme", result.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acme-badge", result.BodyHtml, StringComparison.Ordinal);
        // 交互元数据指向原始源元素，而不是表示节点。
        Assert.Contains($"data-square-id=\"{widget.DebugId}\"", result.BodyHtml);
        Assert.Equal(1, CountOccurrences(result.BodyHtml, "data-square-id="));
        Assert.Contains("data-square-events=\"click\"", result.BodyHtml);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void DirectStaticRepresentationExportsNormalizedHtml()
    {
        var badge = new PackageBadgeElement();
        badge.SetAttribute("status", "ready");

        var result = HtmlExporter.Export(badge, new HtmlExportOptions { IncludeDocument = false });

        Assert.Contains("Badge: ready", result.BodyHtml);
        Assert.DoesNotContain("acme-badge", result.BodyHtml, StringComparison.Ordinal);
        // 源元素自有属性不被盲目复制；表示子树只输出它自己声明的安全属性。
        Assert.DoesNotContain("status=", result.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("data-square-unsupported", result.BodyHtml, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ReservedAndUnsafeAttributesAreStrippedFromRepresentationOutput()
    {
        var badge = new SpoofingBadgeElement();
        badge.AddEventListener("click", static () => { });

        var result = HtmlExporter.Export(badge, new HtmlExportOptions
        {
            IncludeDocument = false,
            EnableInteractions = true
        });

        // 保留属性一律剥离：表示节点不能伪造框架元数据或注入主动内容。
        Assert.DoesNotContain("999999", result.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("acme-evil", result.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("onclick", result.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("srcdoc", result.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", result.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("srcset", result.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Badge", result.BodyHtml);
        // 唯一的事件标识来自原始源元素。
        Assert.Equal(1, CountOccurrences(result.BodyHtml, "data-square-id="));
        Assert.Contains($"data-square-id=\"{badge.DebugId}\"", result.BodyHtml);
        Assert.Contains("data-square-events=\"click\"", result.BodyHtml);
        Assert.Contains(result.Diagnostics, static d => d.Message.Contains("'data-square-id'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, static d => d.Message.Contains("'is'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, static d => d.Message.Contains("'onclick'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, static d => d.Message.Contains("'srcdoc'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, static d => d.Message.Contains("'href'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, static d => d.Message.Contains("'srcset'", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingStaticRepresentationIsDiagnosedWithoutPlaceholder()
    {
        var marked = new UnrepresentedPackageElement();
        marked.MarkTemplateExport(PackageNamespace, "plain");

        var result = HtmlExporter.Export(marked, new HtmlExportOptions { IncludeDocument = false });

        Assert.Equal("", result.BodyHtml);
        Assert.DoesNotContain("data-square-unsupported", result.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("urn:acme", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Diagnostics, static d =>
            d.Message.Contains("no safe static HTML representation", StringComparison.Ordinal));
    }

    [Fact]
    public void ThrowingStaticRepresentationIsDiagnosedWithoutPlaceholder()
    {
        var badge = new ExplodingBadgeElement();

        var result = HtmlExporter.Export(badge, new HtmlExportOptions { IncludeDocument = false });

        Assert.Equal("", result.BodyHtml);
        Assert.DoesNotContain("data-square-unsupported", result.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("acme-badge", result.BodyHtml, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, static d =>
            d.Message.Contains("Static HTML representation failed:", StringComparison.Ordinal) &&
            d.Message.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public void RecursiveStaticRepresentationIsRejected()
    {
        var badge = new RecursiveBadgeElement();

        var result = HtmlExporter.Export(badge, new HtmlExportOptions { IncludeDocument = false });

        Assert.Equal("", result.BodyHtml);
        Assert.DoesNotContain("data-square-unsupported", result.BodyHtml, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, static d =>
            d.Message.Contains("Recursive static HTML representation was rejected", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(RepresentationFailure.Attached)]
    [InlineData(RepresentationFailure.DocumentOwned)]
    [InlineData(RepresentationFailure.MetadataRoot)]
    [InlineData(RepresentationFailure.EmbeddedRoot)]
    public void RepresentationMustBeFreshDetachedBuiltInSubtree(RepresentationFailure failure)
    {
        HTMLElement badge = failure switch
        {
            RepresentationFailure.Attached => new AttachedRepresentationElement(),
            RepresentationFailure.DocumentOwned => new DocumentOwnedRepresentationElement(),
            RepresentationFailure.MetadataRoot => new ScriptRepresentationElement(),
            _ => new FrameRepresentationElement()
        };

        var result = HtmlExporter.Export(badge, new HtmlExportOptions { IncludeDocument = false });

        Assert.Equal("", result.BodyHtml);
        Assert.DoesNotContain("data-square-unsupported", result.BodyHtml, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, static d =>
            d.Message.Contains("must return a new detached built-in HTML subtree", StringComparison.Ordinal));
    }

    [Fact]
    public void CustomizedBuiltInExportsStaticButtonRepresentation()
    {
        var button = new AcmeButtonElement();
        HtmlCustomElementRegistry.InitializeGenerated(button, "acme-button", "button", ["status"]);
        button.SetAttribute("is", "acme-button");

        var result = HtmlExporter.Export(button, new HtmlExportOptions { IncludeDocument = false });

        // 定制内建的表示根仍是内建 button；定义名与 is 标记都不进入导出。
        Assert.Contains("<button", result.BodyHtml);
        Assert.Contains("Custom button", result.BodyHtml);
        Assert.Contains("type=\"button\"", result.BodyHtml);
        Assert.DoesNotContain("acme-button", result.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(" is=", result.BodyHtml, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void CustomizedBuiltInRepresentationMustKeepBuiltInTag()
    {
        var button = new AcmeSpanButtonElement();
        HtmlCustomElementRegistry.InitializeGenerated(button, "acme-button", "button", []);

        var result = HtmlExporter.Export(button, new HtmlExportOptions { IncludeDocument = false });

        Assert.Equal("", result.BodyHtml);
        Assert.DoesNotContain("<span", result.BodyHtml, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, static d =>
            d.Message.Contains("must retain its original built-in tag", StringComparison.Ordinal));
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length)
            count++;
        return count;
    }

    public enum RepresentationFailure
    {
        Attached,
        DocumentOwned,
        MetadataRoot,
        EmbeddedRoot
    }

    /// <summary>包导出组件：静态表示从 status 属性读取内容。</summary>
    private sealed class PackageBadgeElement : HTMLElement, IHtmlStaticRepresentation
    {
        public PackageBadgeElement() : base("acme-badge") { }

        public HTMLElement CreateHtmlRepresentation()
        {
            var span = new HTMLSpanElement();
            span.ChildNodes.Add(new Square.UI.Text("Badge: " + (GetAttribute("status") ?? "idle")));
            return span;
        }
    }

    /// <summary>试图伪造框架保留属性与主动内容的表示提供者。</summary>
    private sealed class SpoofingBadgeElement : HTMLElement, IHtmlStaticRepresentation
    {
        public SpoofingBadgeElement() : base("acme-badge") { }

        public HTMLElement CreateHtmlRepresentation()
        {
            var span = new HTMLSpanElement();
            span.SetAttribute("data-square-id", "999999");
            span.SetAttribute("data-square-events", "click");
            span.SetAttribute("is", "acme-evil");
            span.SetAttribute("onclick", "alert(1)");
            span.SetAttribute("srcdoc", "<b>x</b>");
            span.SetAttribute("href", "javascript:alert(2)");
            span.SetAttribute("srcset", "safe.png 1x, javascript:alert(3) 2x");
            span.ChildNodes.Add(new Square.UI.Text("Badge"));
            return span;
        }
    }

    /// <summary>打标记但未实现静态表示的包组件。</summary>
    private sealed class UnrepresentedPackageElement : View { }

    private sealed class ExplodingBadgeElement : HTMLElement, IHtmlStaticRepresentation
    {
        public ExplodingBadgeElement() : base("acme-badge") { }

        public HTMLElement CreateHtmlRepresentation() => throw new InvalidOperationException("boom");
    }

    private sealed class RecursiveBadgeElement : HTMLButtonElement, IHtmlStaticRepresentation
    {

        public HTMLElement CreateHtmlRepresentation() => new RecursiveBadgeElement();
    }

    private sealed class AttachedRepresentationElement : HTMLElement, IHtmlStaticRepresentation
    {
        private readonly HTMLSpanElement _holder = new();
        private readonly HTMLSpanElement _child = new();

        public AttachedRepresentationElement() : base("acme-badge") => _holder.AppendChild(_child);

        public HTMLElement CreateHtmlRepresentation() => _child;
    }

    private sealed class DocumentOwnedRepresentationElement : HTMLElement, IHtmlStaticRepresentation
    {
        public DocumentOwnedRepresentationElement() : base("acme-badge") { }

        public HTMLElement CreateHtmlRepresentation() => new UIDocument().CreateElement<HTMLSpanElement>();
    }

    private sealed class ScriptRepresentationElement : HTMLElement, IHtmlStaticRepresentation
    {
        public ScriptRepresentationElement() : base("acme-badge") { }

        public HTMLElement CreateHtmlRepresentation() => new HTMLScriptElement();
    }

    private sealed class FrameRepresentationElement : HTMLElement, IHtmlStaticRepresentation
    {
        public FrameRepresentationElement() : base("acme-badge") { }

        public HTMLElement CreateHtmlRepresentation() => new HTMLIFrameElement();
    }

    public sealed class AcmeButtonElement : HTMLButtonElement, IHtmlStaticRepresentation
    {
        public HTMLElement CreateHtmlRepresentation()
        {
            var button = new HTMLButtonElement();
            button.SetAttribute("type", "button");
            button.ChildNodes.Add(new Square.UI.Text("Custom button"));
            return button;
        }
    }

    public sealed class AcmeSpanButtonElement : HTMLButtonElement, IHtmlStaticRepresentation
    {
        public HTMLElement CreateHtmlRepresentation() => new HTMLSpanElement();
    }
}
