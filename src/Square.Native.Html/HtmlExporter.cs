using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Square.Controls;
using Square.Graphics;
using Square.Graphics.Codecs;
using Square.Html;
using Square.Native;
using Square.UI;
using Square.UI.Svg;
using SquareImage = Square.Controls.Image;
using SquareText = Square.Controls.Text;

namespace Square.Native.Html;

/// <summary>将已求值的 Square Element Tree 生成静态语义 HTML/CSS。</summary>
public static class HtmlExporter
{
    private const string BaselineCss = """
        html,body{margin:0;min-height:100%;}
        *,*::before,*::after{box-sizing:border-box;}
        .square-root{min-width:0;}
        .square-root img,.square-root svg{max-width:100%;}
        button,input,select,textarea{appearance:auto;}
        :where(.sq-default-button):active:not(:disabled){background-color:#dedede;}
        [data-square-unsupported="true"]{padding:.75rem;border:1px dashed #b42318;color:#b42318;background:#fff5f5;font-family:system-ui,sans-serif;}
        """;

    private const string DefaultViewport = "width=device-width,initial-scale=1";

    /// <summary>HTML 转义保留非 ASCII 字符原样输出（与桌面绘制文本一致），仅转义标记敏感字符。</summary>
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>生成元素树 HTML。调用方负责元素树的生命周期。</summary>
    public static HtmlExportResult Export(Element root, HtmlExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.BuildElementTree();
        return ExportSnapshot(NativeUiTreeBuilder.Snapshot(root), options);
    }

    /// <summary>生成文档 HTML。文档内容应已构建。</summary>
    public static HtmlExportResult Export(Document document, HtmlExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new HtmlExportOptions();
        document.DocumentElement.BuildElementTree();
        return ExportSnapshot(NativeUiTreeBuilder.Snapshot(document.DocumentElement), options, document.Title);
    }

    private static HtmlExportResult ExportSnapshot(NativeUiNode root, HtmlExportOptions? options, string? documentTitle = null)
    {
        options ??= new HtmlExportOptions();
        var diagnostics = new List<HtmlExportDiagnostic>();
        var context = new ExportContext(options, diagnostics)
        {
            DocumentHtmlRoot = options.IncludeDocument ? FindSoleHtmlRoot(root) : null
        };
        var body = new StringBuilder();
        WriteNode(body, root, context, isRoot: true, isSnapshotRoot: true);
        var css = BuildCss(options, context.Styles);

        if (!options.IncludeDocument)
            return new HtmlExportResult { Html = body.ToString(), BodyHtml = body.ToString(), Css = css, Diagnostics = diagnostics };

        var title = !string.IsNullOrWhiteSpace(options.Title) ? options.Title
            : !string.IsNullOrWhiteSpace(context.HeadTitle) ? context.HeadTitle
            : !string.IsNullOrWhiteSpace(documentTitle) ? documentTitle
            : root.Kind;
        var lang = !string.IsNullOrWhiteSpace(context.HtmlLang) ? context.HtmlLang : options.Language;

        var html = new StringBuilder(body.Length + 512);
        html.Append("<!doctype html><html lang=\"").Append(Encode(lang)).Append("\"><head>");
        html.Append("<meta charset=\"utf-8\"><meta name=\"viewport\" content=\"")
            .Append(Encode(context.ViewportContent ?? DefaultViewport)).Append("\">");
        html.Append("<title>").Append(Encode(title)).Append("</title>");
        foreach (var item in context.HeadItems) html.Append(item);
        var stylesheetLinked = false;
        if (!string.IsNullOrWhiteSpace(options.StylesheetHref))
        {
            if (TrySafeUrl(options.StylesheetHref, allowMailTo: false, allowDataImage: false, out var stylesheetHref))
            {
                html.Append("<link rel=\"stylesheet\" href=\"").Append(Encode(stylesheetHref)).Append("\">");
                stylesheetLinked = true;
            }
            else
            {
                diagnostics.Add(new HtmlExportDiagnostic(root.Kind, $"Rejected unsafe stylesheet URL '{options.StylesheetHref}'."));
            }
        }
        if (!stylesheetLinked && !string.IsNullOrWhiteSpace(css))
        {
            html.Append("<style data-square-css=\"true\">").Append(SanitizeInlineCss(css)).Append("</style>");
        }
        html.Append("</head><body");
        if (context.BodyHostNode is { } bodyHost) WriteCommonAttributes(html, bodyHost, context, isRoot: false);
        html.Append('>').Append(body).Append("</body></html>");
        return new HtmlExportResult { Html = html.ToString(), BodyHtml = body.ToString(), Css = css, Diagnostics = diagnostics };
    }

    /// <summary>
    /// 定位穿过单一子节点链（Square 文档外壳、生成的组件包装）可达的唯一 HTML <c>html</c> 根；
    /// 找不到或存在多条分支时返回 null（嵌套 html/head/body 会被压平并诊断）。
    /// </summary>
    private static HTMLElement? FindSoleHtmlRoot(NativeUiNode root)
    {
        var current = root;
        while (true)
        {
            if (current.SourceElement is HTMLElement { TagName: "html" } html) return html;
            if (current.Children.Count != 1) return null;
            current = current.Children[0];
        }
    }

    private static void WriteNode(
        StringBuilder output,
        NativeUiNode node,
        ExportContext context,
        bool isRoot = false,
        bool isSnapshotRoot = false,
        bool includeHiddenHtmlChild = false,
        Element? interactionSource = null)
    {
        var element = node.SourceElement;
        if ((!element.IsVisible && !includeHiddenHtmlChild) || element is UIHeadElement) return;

        if (element.TemplateExportNamespaceUri != null ||
            element is HTMLElement { IsDefinedCustomElement: true } ||
            element is IHtmlStaticRepresentation)
        {
            WriteStaticRepresentation(output, node, context, isRoot, element);
            return;
        }

        if (element is UIRootElement or UIBodyElement)
        {
            foreach (var child in node.Children) WriteNode(output, child, context, isRoot);
            return;
        }

        switch (element)
        {
            case HTMLElement host:
                WriteHtmlElement(output, node, host, context, isRoot, isSnapshotRoot, interactionSource);
                return;
            case Canvas:
            case Popup:
                WriteUnsupported(output, node, context, isRoot);
                return;
            case SquareText text:
                WriteContainer(output, node, "span", context, isRoot, text.TextContent, includeChildren: false);
                return;
            case Button button:
                WriteContainer(output, node, "button", context, isRoot, button.TextContent, includeChildren: true,
                    extraAttributes: button.IsDisabled ? " disabled" : "");
                return;
            case Input input:
                WriteVoid(output, node, "input", context, isRoot,
                    $" type=\"{Encode(input.Type)}\" value=\"{Encode(input.Value)}\" placeholder=\"{Encode(input.Placeholder)}\"" +
                    (input.IsDisabled ? " disabled" : ""));
                return;
            case TextArea textArea:
                WriteContainer(output, node, "textarea", context, isRoot, textArea.Value, includeChildren: false,
                    extraAttributes: $" placeholder=\"{Encode(textArea.Placeholder)}\"" + (textArea.IsDisabled ? " disabled" : ""));
                return;
            case CheckBox checkBox:
                WriteChoice(output, node, "checkbox", context, checkBox.TextContent, checkBox.IsChecked, null, checkBox.IsDisabled, isRoot);
                return;
            case Radio radio:
                WriteChoice(output, node, "radio", context, radio.TextContent, radio.IsChecked, radio.GroupName, radio.IsDisabled, isRoot);
                return;
            case Select select:
                WriteSelect(output, node, select, context, isRoot);
                return;
            case Link link:
                WriteLink(output, node, link, context, isRoot);
                return;
            case SquareImage image:
                WriteImage(output, node, image, context, isRoot);
                return;
            case List:
                WriteContainer(output, node, "ul", context, isRoot, null, includeChildren: true);
                return;
            case ListItem item:
                WriteContainer(output, node, "li", context, isRoot, item.TextContent, includeChildren: true);
                return;
            case SVGElement svg:
                WriteSvg(output, node, svg, context, isRoot);
                return;
            case ScrollViewer:
            case View:
                WriteContainer(output, node, "div", context, isRoot, null, includeChildren: true);
                return;
        }

        if (node.Children.Count > 0 && element.GetType().Assembly != typeof(Element).Assembly)
        {
            WriteContainer(output, node, "div", context, isRoot, null, includeChildren: true,
                extraAttributes: $" data-square-component=\"{Encode(element.GetType().FullName ?? element.GetType().Name)}\"");
            return;
        }

        WriteUnsupported(output, node, context, isRoot);
    }

    private static void WriteStaticRepresentation(
        StringBuilder output, NativeUiNode node, ExportContext context, bool isRoot, Element source)
    {
        if (source is not IHtmlStaticRepresentation provider)
        {
            context.Diagnostics.Add(new HtmlExportDiagnostic(node.Kind,
                "Exported package or defined custom element has no safe static HTML representation."));
            context.RepresentationFailures++;
            return;
        }
        if (!context.ActiveRepresentationTypes.Add(source.GetType()))
        {
            context.Diagnostics.Add(new HtmlExportDiagnostic(node.Kind,
                "Recursive static HTML representation was rejected."));
            context.RepresentationFailures++;
            return;
        }
        try
        {
            var representation = provider.CreateHtmlRepresentation();
            if (representation == null || ReferenceEquals(representation, source) ||
                representation.ParentNode != null || representation.OwnerDocument != null ||
                !HtmlTagCatalog.IsTag(representation.TagName) ||
                HtmlTagCatalog.IsMetadata(representation.TagName) ||
                representation.TagName is "html" or "body" or "iframe" or "object" or "embed")
            {
                context.Diagnostics.Add(new HtmlExportDiagnostic(node.Kind,
                    "Static HTML representation must return a new detached built-in HTML subtree."));
                context.RepresentationFailures++;
                return;
            }
            if (source is HTMLElement { CustomElementExtendsTag: { } extendsTag } &&
                representation.TagName != extendsTag)
            {
                context.Diagnostics.Add(new HtmlExportDiagnostic(node.Kind,
                    "Customized built-in HTML representation must retain its original built-in tag."));
                context.RepresentationFailures++;
                return;
            }
            var failures = context.RepresentationFailures;
            var safeOutput = new StringBuilder();
            WriteNode(safeOutput, NativeUiTreeBuilder.Snapshot(representation), context, isRoot,
                interactionSource: source);
            if (context.RepresentationFailures == failures) output.Append(safeOutput);
        }
        catch (Exception exception)
        {
            context.Diagnostics.Add(new HtmlExportDiagnostic(node.Kind,
                "Static HTML representation failed: " + exception.Message));
            context.RepresentationFailures++;
        }
        finally
        {
            context.ActiveRepresentationTypes.Remove(source.GetType());
        }
    }

    private static void WriteHtmlElement(
        StringBuilder output,
        NativeUiNode node,
        HTMLElement host,
        ExportContext context,
        bool isRoot,
        bool isSnapshotRoot,
        Element? interactionSource)
    {
        var tag = host.TagName;
        if (!HtmlTagCatalog.IsTag(tag))
        {
            context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
                "Unknown HTML tags require a safe static representation; raw custom tags are not exported."));
            if (context.ActiveRepresentationTypes.Count > 0) context.RepresentationFailures++;
            return;
        }
        switch (tag)
        {
            case "html" when ReferenceEquals(host, context.DocumentHtmlRoot):
            {
                // 唯一文档根并入生成壳：lang 合并，head 元数据入 head，其余进入 body。
                var lang = host.GetAttribute("lang");
                if (!string.IsNullOrWhiteSpace(lang)) context.HtmlLang = lang;
                WriteHtmlChildren(output, node, host, context, isRoot);
                return;
            }
            case "html" when isSnapshotRoot && !context.Options.IncludeDocument:
            case "head" when isSnapshotRoot && !context.Options.IncludeDocument:
            case "body" when isSnapshotRoot && !context.Options.IncludeDocument:
                // 片段模式下文档根原样输出。
                WriteHtmlGeneric(output, node, host, context, isRoot, interactionSource);
                return;
            case "html":
                context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
                    "HTML html element is only exported as the single document root; nested document shells are flattened."));
                goto case "flatten";
            case "head":
                if (!ReferenceEquals(host.Parent, context.DocumentHtmlRoot))
                    context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
                        "HTML head element outside the document root was flattened; its metadata moved to the document head."));
                goto case "flatten";
            case "body":
                if (ReferenceEquals(host.Parent, context.DocumentHtmlRoot))
                    context.BodyHostNode ??= node;
                else
                    context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
                        "HTML body element outside the document root was flattened into the document body."));
                goto case "flatten";
            case "flatten":
                WriteHtmlChildren(output, node, host, context, isRoot);
                return;
        }

        if (HtmlTagCatalog.IsMetadata(tag))
        {
            WriteHtmlMetadata(output, node, host, context, isRoot);
            return;
        }

        switch (tag)
        {
            case "iframe" or "embed":
                WriteEmbedPlaceholder(output, node, context, isRoot, tag);
                return;
            case "object":
                WriteObjectFallback(output, node, host, context, isRoot);
                return;
        }

        WriteHtmlGeneric(output, node, host, context, isRoot, interactionSource);
    }

    /// <summary>按目的把不可见元数据并入单一 head；被禁用的主动内容只留诊断。</summary>
    private static void WriteHtmlMetadata(
        StringBuilder output,
        NativeUiNode node,
        HTMLElement host,
        ExportContext context,
        bool isRoot)
    {
        var tag = host.TagName;
        switch (tag)
        {
            case "script":
                context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
                    "HTML script elements are never exported; script content and src would enable script execution."));
                return;
            case "style":
                context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
                    "Static HTML style elements are disabled; use component CSS or HtmlExportOptions.AdditionalCss."));
                return;
            case "base":
                context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
                    "HTML base elements are disabled; the document base URL cannot be changed."));
                return;
            case "link":
                context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
                    "HTML link elements are disabled; external resource loading is not exported."));
                return;
            case "title":
            {
                var title = GetTextContent(host);
                if (HoistMetadata(context))
                {
                    context.HeadTitle ??= title;
                    return;
                }
                output.Append("<title");
                WriteHtmlAttributes(output, host, context, tag);
                output.Append('>').Append(Encode(title)).Append("</title>");
                return;
            }
            case "meta":
                if (HoistMetadata(context))
                {
                    if (RenderMeta(host, context, hoist: true) is { } item) context.HeadItems.Add(item);
                }
                else if (RenderMeta(host, context, hoist: false) is { } markup)
                {
                    output.Append(markup);
                }
                return;
            case "template":
                WriteTemplate(output, node, host, context, hoist: HoistMetadata(context));
                return;
            default:
                return;
        }
    }

    private static bool HoistMetadata(ExportContext context) =>
        context.Options.IncludeDocument && !context.SuppressHeadHoist;

    /// <summary>校验并渲染安全的 meta；http-equiv、非 UTF-8 charset 一律拒绝并诊断。</summary>
    private static string? RenderMeta(HTMLElement host, ExportContext context, bool hoist)
    {
        if (host.GetAttribute("http-equiv") is { } httpEquiv)
        {
            context.Diagnostics.Add(new HtmlExportDiagnostic("meta",
                $"Rejected meta http-equiv '{httpEquiv}'; response header injection is prohibited."));
            return null;
        }
        if (host.GetAttribute("charset") is { } charset)
        {
            if (!charset.Trim().Equals("utf-8", StringComparison.OrdinalIgnoreCase))
            {
                context.Diagnostics.Add(new HtmlExportDiagnostic("meta",
                    $"Rejected meta charset '{charset}'; documents are exported as UTF-8."));
                return null;
            }
            // 文档壳已固定声明 UTF-8；head 合并时跳过，片段模式下原样保留。
            return hoist ? null : "<meta charset=\"utf-8\">";
        }
        var name = host.GetAttribute("name");
        var itemprop = host.GetAttribute("itemprop");
        if (name == null && itemprop == null) return null;
        var content = host.GetAttribute("content");
        if (name is not null && name.Trim().Equals("viewport", StringComparison.OrdinalIgnoreCase))
        {
            // 壳层 viewport 只出现一次：首个安全 viewport 生效，其余忽略。
            if (!string.IsNullOrWhiteSpace(content) && context.ViewportContent == null) context.ViewportContent = content;
            return hoist ? null : "<meta name=\"viewport\" content=\"" + Encode(content ?? "") + "\">";
        }
        var builder = new StringBuilder("<meta");
        if (name != null) builder.Append(" name=\"").Append(Encode(name)).Append('"');
        if (itemprop != null) builder.Append(" itemprop=\"").Append(Encode(itemprop)).Append('"');
        if (content != null) builder.Append(" content=\"").Append(Encode(content)).Append('"');
        return builder.Append('>').ToString();
    }

    /// <summary>template 子树保持惰性：文档模式下并入 head，片段模式下原样输出。</summary>
    private static void WriteTemplate(
        StringBuilder output,
        NativeUiNode node,
        HTMLElement host,
        ExportContext context,
        bool hoist)
    {
        var target = hoist ? new StringBuilder() : output;
        target.Append("<template");
        WriteHtmlAttributes(target, host, context, "template");
        target.Append('>');
        var previous = context.SuppressHeadHoist;
        context.SuppressHeadHoist = true;
        try
        {
            WriteHtmlChildren(target, node, host, context, isRoot: false);
        }
        finally
        {
            context.SuppressHeadHoist = previous;
        }
        target.Append("</template>");
        if (hoist) context.HeadItems.Add(target.ToString());
    }

    private static void WriteHtmlGeneric(
        StringBuilder output,
        NativeUiNode node,
        HTMLElement host,
        ExportContext context,
        bool isRoot,
        Element? interactionSource = null)
    {
        var tag = host.TagName;
        var isVoid = HtmlTagCatalog.IsVoid(tag);
        output.Append('<').Append(tag);
        if (context.Options.EnableInteractions) output.Append(" data-square-html=\"true\"");
        WriteCommonAttributes(output, node, context, isRoot, interactionSource: interactionSource);
        WriteHtmlAttributes(output, host, context, tag);
        output.Append('>');
        if (tag == "textarea")
        {
            // 浏览器以元素内容作为 textarea 的值；当前值来自 value 属性（含交互回写），
            // 缺席时回退到按 ChildNodes 顺序的文本子节点。
            var value = host.GetAttribute("value");
            if (value != null) output.Append(Encode(value));
            else WriteHtmlChildren(output, node, host, context, isRoot);
        }
        else if (!isVoid)
        {
            WriteHtmlChildren(output, node, host, context, isRoot, includeHiddenHtmlChild: tag == "details");
        }
        if (!isVoid) output.Append("</").Append(tag).Append('>');
    }

    /// <summary>
    /// 按 DOM ChildNodes 顺序输出 HTML 混合内容：Text 节点编码为裸文本，元素按快照匹配输出。
    /// 快照只包含可见元素，文本直接取自语义子节点；details 的隐藏子元素仍需导出（由交互切换）。
    /// </summary>
    private static void WriteHtmlChildren(
        StringBuilder output,
        NativeUiNode node,
        HTMLElement host,
        ExportContext context,
        bool isRoot,
        bool includeHiddenHtmlChild = false)
    {
        var snapshots = node.Children;
        foreach (var child in host.ChildNodes)
        {
            if (child is Square.UI.Text text)
            {
                output.Append(Encode(text.Data));
                continue;
            }
            if (child is not Element element) continue;
            var snapshot = snapshots.FirstOrDefault(item => ReferenceEquals(item.SourceElement, element));
            if (snapshot == null)
            {
                if (!includeHiddenHtmlChild) continue;
                snapshot = NativeUiTreeBuilder.Snapshot(element);
            }
            WriteNode(output, snapshot, context, isRoot, includeHiddenHtmlChild: includeHiddenHtmlChild);
        }
    }

    /// <summary>
    /// 输出 HTML 元素自有属性：名字必须形如 <c>[A-Za-z][A-Za-z0-9:_-]*</c>，拒绝 <c>on*</c>/<c>srcdoc</c>，
    /// URL 属性逐项走安全校验，<c>srcset</c> 任一候选不安全则整体省略。id/class/style/title 由
    /// WriteCommonAttributes 输出，不重复。
    /// </summary>
    private static void WriteHtmlAttributes(StringBuilder output, HTMLElement host, ExportContext context, string tag)
    {
        foreach (var (name, value) in host.GetAttributes())
        {
            if (name is "id" or "class" or "style" or "title") continue;
            if (tag == "textarea" && name == "value") continue;
            if (name == "is" || name.StartsWith("data-square-", StringComparison.Ordinal))
            {
                context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
                    $"Rejected reserved HTML attribute '{name}'."));
                continue;
            }
            if (!IsValidAttributeName(name) || name is "srcdoc" || name.StartsWith("on", StringComparison.Ordinal))
            {
                context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
                    $"Rejected prohibited or malformed HTML attribute '{name}'."));
                continue;
            }
            if (IsUrlAttribute(name))
            {
                if (!TrySafeUrl(value, allowMailTo: name == "href", allowDataImage: name is "src" or "poster", out _))
                {
                    context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
                        $"Rejected unsafe URL in HTML attribute '{name}'."));
                    continue;
                }
            }
            else if (name == "srcset")
            {
                if (!TrySafeSrcSet(value))
                {
                    context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
                        "Rejected HTML attribute 'srcset' with an unsafe candidate URL."));
                    continue;
                }
            }
            output.Append(' ').Append(name);
            if (value.Length == 0 && HtmlTagCatalog.BooleanAttributes.Contains(name)) continue;
            output.Append("=\"").Append(Encode(value)).Append('"');
        }
    }

    private static bool IsUrlAttribute(string name) =>
        name is "href" or "src" or "poster" or "action" or "formaction" or "cite";

    private static bool IsValidAttributeName(string name)
    {
        if (name.Length == 0) return false;
        var first = name[0];
        if (first is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z'))) return false;
        foreach (var character in name.AsSpan(1))
        {
            if (character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or ':' or '-' or '_') continue;
            return false;
        }
        return true;
    }

    /// <summary>srcset 任一候选 URL 不安全即整体拒绝，不能只验证首个候选。</summary>
    private static bool TrySafeSrcSet(string value)
    {
        foreach (var candidate in value.Split(','))
        {
            var trimmed = candidate.Trim();
            if (trimmed.Length == 0) return false;
            var url = trimmed.Split(' ', '\t', '\n', '\r', '\f')[0];
            if (url.Length == 0 || !TrySafeUrl(url, allowMailTo: false, allowDataImage: true, out _)) return false;
        }
        return true;
    }

    private static void WriteEmbedPlaceholder(
        StringBuilder output,
        NativeUiNode node,
        ExportContext context,
        bool isRoot,
        string tag)
    {
        context.Diagnostics.Add(new HtmlExportDiagnostic(tag,
            $"Embedded {tag} content is disabled; documents and plugins are never loaded or exported."));
        output.Append("<div");
        WriteCommonAttributes(output, node, context, isRoot);
        output.Append(" data-square-kind=\"").Append(Encode(tag)).Append("\" data-square-unsupported=\"true\">");
        output.Append(Encode("Embedded content disabled"));
        output.Append("</div>");
    }

    private static void WriteObjectFallback(
        StringBuilder output,
        NativeUiNode node,
        HTMLElement host,
        ExportContext context,
        bool isRoot)
    {
        context.Diagnostics.Add(new HtmlExportDiagnostic("object",
            "Embedded object content is disabled; plugin data is never loaded or exported."));
        if (!node.Children.Any())
        {
            WriteEmbedPlaceholder(output, node, context, isRoot, "object");
            return;
        }
        // object 不加载其 URL，只输出文本 fallback 子内容。
        output.Append("<div");
        WriteCommonAttributes(output, node, context, isRoot);
        output.Append(" data-square-kind=\"object\">");
        WriteHtmlChildren(output, node, host, context, isRoot);
        output.Append("</div>");
    }

    private static string GetTextContent(Element element)
    {
        var builder = new StringBuilder();
        AppendTextContent(element, builder);
        return builder.ToString();
    }

    private static void AppendTextContent(Element element, StringBuilder builder)
    {
        if (element is SquareText text)
        {
            builder.Append(text.TextContent);
            return;
        }
        foreach (var child in element.ChildNodes)
        {
            if (child is Square.UI.Text domText) builder.Append(domText.Data);
            else if (child is Element childElement) AppendTextContent(childElement, builder);
        }
    }

    private static void WriteContainer(
        StringBuilder output,
        NativeUiNode node,
        string tag,
        ExportContext context,
        bool isRoot,
        string? text,
        bool includeChildren,
        string extraAttributes = "")
    {
        output.Append('<').Append(tag);
        WriteCommonAttributes(output, node, context, isRoot);
        output.Append(extraAttributes).Append('>');
        if (!string.IsNullOrEmpty(text)) output.Append(Encode(text));
        if (includeChildren)
            foreach (var child in node.Children) WriteNode(output, child, context);
        output.Append("</").Append(tag).Append('>');
    }

    private static void WriteVoid(StringBuilder output, NativeUiNode node, string tag, ExportContext context, bool isRoot, string extraAttributes)
    {
        output.Append('<').Append(tag);
        WriteCommonAttributes(output, node, context, isRoot);
        output.Append(extraAttributes).Append('>');
    }

    private static void WriteChoice(
        StringBuilder output,
        NativeUiNode node,
        string type,
        ExportContext context,
        string text,
        bool isChecked,
        string? name,
        bool isDisabled,
        bool isRoot)
    {
        output.Append("<label");
        WriteCommonAttributes(output, node, context, isRoot, hostChoiceInput: true);
        output.Append("><input type=\"").Append(type).Append('"');
        WriteWidgetAppearance(output, node, context);
        if (!string.IsNullOrWhiteSpace(name)) output.Append(" name=\"").Append(Encode(name)).Append('"');
        if (isChecked) output.Append(" checked");
        if (isDisabled) output.Append(" disabled");
        output.Append('>');
        if (!string.IsNullOrEmpty(text)) output.Append(Encode(text));
        output.Append("</label>");
    }

    private static void WriteSelect(StringBuilder output, NativeUiNode node, Select select, ExportContext context, bool isRoot)
    {
        output.Append("<select");
        WriteCommonAttributes(output, node, context, isRoot);
        if (select.IsDisabled) output.Append(" disabled");
        output.Append('>');
        if (select.Value.Length == 0 && select.Placeholder.Length > 0)
            output.Append("<option value=\"\" selected disabled>").Append(Encode(select.Placeholder)).Append("</option>");
        foreach (var option in select.Options)
        {
            output.Append("<option value=\"").Append(Encode(option)).Append('"');
            if (string.Equals(option, select.Value, StringComparison.Ordinal)) output.Append(" selected");
            output.Append('>').Append(Encode(option)).Append("</option>");
        }
        output.Append("</select>");
    }

    private static void WriteLink(
        StringBuilder output,
        NativeUiNode node,
        Link link,
        ExportContext context,
        bool isRoot)
    {
        output.Append("<a");
        WriteCommonAttributes(output, node, context, isRoot);
        if (TrySafeUrl(link.Href, allowMailTo: true, allowDataImage: false, out var href))
            output.Append(" href=\"").Append(Encode(href)).Append('"');
        else if (!string.IsNullOrWhiteSpace(link.Href))
            context.Diagnostics.Add(new HtmlExportDiagnostic(node.Kind, $"Rejected unsafe link URL '{link.Href}'."));
        output.Append('>').Append(Encode(link.TextContent)).Append("</a>");
    }

    private static void WriteImage(
        StringBuilder output,
        NativeUiNode node,
        SquareImage image,
        ExportContext context,
        bool isRoot)
    {
        string? source = null;
        if (image.ImageContent is Bitmap bitmap && !bitmap.IsDisposed)
        {
            using var stream = new MemoryStream();
            BitmapPngEncoder.Save(bitmap, stream);
            source = "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
        else if (TrySafeUrl(image.Source, allowMailTo: false, allowDataImage: true, out var safeSource))
        {
            source = safeSource;
        }

        if (string.IsNullOrEmpty(source))
        {
            context.Diagnostics.Add(new HtmlExportDiagnostic(node.Kind, "Image has no browser-safe source or bitmap content."));
            WriteUnsupported(output, node, context, isRoot, addDiagnostic: false);
            return;
        }

        WriteVoid(output, node, "img", context, isRoot,
            $" src=\"{Encode(source)}\" alt=\"{Encode(image.Source)}\"");
    }

    private static void WriteSvg(
        StringBuilder output,
        NativeUiNode node,
        SVGElement svg,
        ExportContext context,
        bool isRoot)
    {
        var tag = svg.TagName.ToLowerInvariant();
        output.Append('<').Append(tag);
        WriteCommonAttributes(output, node, context, isRoot);
        foreach (var (name, property) in GetSvgAttributes(svg))
        {
            var value = svg.GetProperty<object>(property)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                output.Append(' ').Append(name).Append("=\"").Append(Encode(value)).Append('"');
        }
        output.Append('>');
        foreach (var child in node.Children) WriteNode(output, child, context);
        output.Append("</").Append(tag).Append('>');
    }

    private static IEnumerable<(string Name, string Property)> GetSvgAttributes(SVGElement svg)
    {
        if (svg is SVGSVGElement)
        {
            yield return ("viewBox", "ViewBox");
            yield return ("width", "Width");
            yield return ("height", "Height");
        }
        if (svg is SVGPathElement) yield return ("d", "Data");
        if (svg is SVGRectElement)
        {
            yield return ("x", "X"); yield return ("y", "Y");
            yield return ("width", "Width"); yield return ("height", "Height");
            yield return ("rx", "RadiusX"); yield return ("ry", "RadiusY");
        }
        if (svg is SVGCircleElement)
        {
            yield return ("cx", "CenterX"); yield return ("cy", "CenterY"); yield return ("r", "Radius");
        }
        if (svg is SVGEllipseElement)
        {
            yield return ("cx", "CenterX"); yield return ("cy", "CenterY");
            yield return ("rx", "RadiusX"); yield return ("ry", "RadiusY");
        }
        if (svg is SVGLineElement)
        {
            yield return ("x1", "X1"); yield return ("y1", "Y1");
            yield return ("x2", "X2"); yield return ("y2", "Y2");
        }
        if (svg is SVGPolylineElement or SVGPolygonElement) yield return ("points", "Points");
        yield return ("fill", "Fill");
        yield return ("stroke", "Stroke");
        yield return ("stroke-width", "StrokeWidth");
        yield return ("opacity", "Opacity");
        yield return ("transform", "Transform");
    }

    private static void WriteUnsupported(
        StringBuilder output,
        NativeUiNode node,
        ExportContext context,
        bool isRoot,
        bool addDiagnostic = true)
    {
        if (addDiagnostic)
            context.Diagnostics.Add(new HtmlExportDiagnostic(node.Kind, $"No semantic HTML mapping exists for {node.SourceElement.GetType().FullName}."));
        output.Append("<div");
        WriteCommonAttributes(output, node, context, isRoot);
        output.Append(" data-square-kind=\"").Append(Encode(node.Kind)).Append("\" data-square-unsupported=\"true\">");
        output.Append(Encode(node.Kind)).Append(" is not supported by the static HTML target.");
        output.Append("</div>");
    }

    private static void WriteCommonAttributes(StringBuilder output, NativeUiNode node, ExportContext context,
        bool isRoot, bool hostChoiceInput = false, Element? interactionSource = null)
    {
        var element = node.SourceElement;
        if (context.Options.EnableInteractions)
        {
            output.Append(" data-square-id=\"").Append((interactionSource ?? element).DebugId).Append('"');
            var eventTypes = (interactionSource ?? element).RegisteredEventTypes;
            if (eventTypes.Count > 0)
                output.Append(" data-square-events=\"")
                    .Append(Encode(string.Join(' ', eventTypes.Select(static type => type.ToLowerInvariant()))))
                    .Append('"');
        }
        if (!string.IsNullOrWhiteSpace(element.Id))
            output.Append(" id=\"").Append(Encode(element.Id)).Append('"');

        var classes = node.Classes.ToList();
        if (isRoot) classes.Add("square-root");
        // Only unstyled native buttons receive Square's visible pressed-face baseline; authored
        // backgrounds and appearance:none keep full control of their active appearance.
        if ((element is Button { IsEnabled: true } or HTMLElement { TagName: "button", IsDisabled: false }) &&
            HasAutoAppearance(node.Style) &&
            !element.Style.IsAuthorSpecified("background") &&
            !element.Style.IsAuthorSpecified("background-color"))
            classes.Add("sq-default-button");

        var styles = CompactStyles(MergeStyles(node, hostChoiceInput));
        if (styles.Count > 0 && context.Options.UseInlineStyles)
        {
            output.Append(" style=\"");
            foreach (var pair in styles)
                output.Append(Encode(pair.Key)).Append(':').Append(Encode(pair.Value)).Append(';');
            output.Append('"');
        }
        else if (styles.Count > 0 && !context.Options.UseInlineStyles)
        {
            // 每个元素的求值样式挂独立的生成签名类；绝不能并入共享 author 类选择器，
            // 否则共享类（如 .comparison-card）会带上最后一个节点的声明，在浏览器里
            // 后置生成的规则会覆盖各变体类（.comparison-blue 等）的真实配色。
            classes.Add(context.Styles.GetClass(styles));
        }

        if (classes.Count > 0)
            output.Append(" class=\"").Append(Encode(string.Join(' ', classes.Distinct(StringComparer.Ordinal)))).Append('"');

        var title = element is HTMLElement html ? html.GetAttribute("title") : (element as UIElement)?.Tooltip;
        if (!string.IsNullOrWhiteSpace(title))
            output.Append(" title=\"").Append(Encode(title)).Append('"');
    }

    private static void WriteWidgetAppearance(StringBuilder output, NativeUiNode node, ExportContext context)
    {
        if (!TryGetAppearance(node, out var appearance) || appearance == "auto") return;

        var styles = CompactStyles(new Dictionary<string, string>(StringComparer.Ordinal) { ["appearance"] = appearance });
        if (styles.Count == 0) return;
        if (context.Options.UseInlineStyles)
        {
            output.Append(" style=\"");
            foreach (var pair in styles)
                output.Append(Encode(pair.Key)).Append(':').Append(Encode(pair.Value)).Append(';');
            output.Append('"');
            return;
        }

        output.Append(" class=\"").Append(Encode(context.Styles.GetClass(styles))).Append('"');
    }

    private static Dictionary<string, string> MergeStyles(NativeUiNode node, bool hostChoiceInput = false)
    {
        var element = node.SourceElement;
        var styles = new Dictionary<string, string>(node.Style, StringComparer.Ordinal);
        if (hostChoiceInput)
        {
            // 选择控件的样式挂在 label 包装上；label 不是原生控件，appearance 无意义。
            styles.Remove("appearance");
        }
        else if (IsNativeFormWidget(element) && HasAutoAppearance(styles))
        {
            // appearance:auto 的表单控件由浏览器 UA 呈现系统外观（包括 3D 边框与 :active 状态）。
            // Square UA 级默认声明（border/background/font/padding 等）若以 author 级输出，
            // 会压过浏览器 UA 的 :active 规则并冻结原生外观，因此只输出作者真实声明的属性；
            // 作者自定义 border/background/font/padding 与 appearance:none 均原样保留。
            foreach (var property in styles.Keys.ToArray())
                if (!element.Style.IsAuthorSpecified(property))
                    styles.Remove(property);
            styles.Remove("appearance");
        }
        if (element is UIElement ui)
        {
            AddPixels(styles, "width", ui.Width, float.NaN);
            AddPixels(styles, "height", ui.Height, float.NaN);
            AddPixels(styles, "min-width", ui.MinWidth, 0);
            AddPixels(styles, "min-height", ui.MinHeight, 0);
            AddPixels(styles, "max-width", ui.MaxWidth, float.PositiveInfinity);
            AddPixels(styles, "max-height", ui.MaxHeight, float.PositiveInfinity);
            AddPixels(styles, "margin-left", ui.MarginLeft, 0);
            AddPixels(styles, "margin-top", ui.MarginTop, 0);
            AddPixels(styles, "margin-right", ui.MarginRight, 0);
            AddPixels(styles, "margin-bottom", ui.MarginBottom, 0);
            AddPixels(styles, "padding-left", ui.PaddingLeft, 0);
            AddPixels(styles, "padding-top", ui.PaddingTop, 0);
            AddPixels(styles, "padding-right", ui.PaddingRight, 0);
            AddPixels(styles, "padding-bottom", ui.PaddingBottom, 0);
        }
        return styles;
    }

    /// <summary>Square 控件与 HTML 同名宿主都属于交给浏览器 UA 呈现的表单控件。</summary>
    private static bool IsNativeFormWidget(Element element) =>
        element is Button or Input or TextArea or Select ||
        element is HTMLElement { TagName: "button" or "input" or "textarea" or "select" };

    /// <summary>appearance 缺席与显式 auto 都表示控件沿用浏览器原生外观。</summary>
    private static bool HasAutoAppearance(IReadOnlyDictionary<string, string> styles) =>
        !styles.TryGetValue("appearance", out var appearance) ||
        appearance.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetAppearance(NativeUiNode node, out string appearance)
    {
        appearance = "";
        if (!node.Style.TryGetValue("appearance", out var value) || string.IsNullOrWhiteSpace(value))
            return false;
        appearance = value.Trim().ToLowerInvariant();
        return appearance is "auto" or "none";
    }

    private static void AddPixels(Dictionary<string, string> styles, string property, float value, float defaultValue)
    {
        if (styles.ContainsKey(property) || float.IsNaN(value) || float.IsInfinity(value)) return;
        if (!float.IsNaN(defaultValue) && value.Equals(defaultValue)) return;
        styles[property] = value.ToString("0.###", CultureInfo.InvariantCulture) + "px";
    }

    private static IReadOnlyList<KeyValuePair<string, string>> CompactStyles(IReadOnlyDictionary<string, string> styles)
    {
        var result = new Dictionary<string, string>(styles, StringComparer.Ordinal);
        foreach (var shorthand in ShorthandLonghands)
        {
            if (!result.TryGetValue(shorthand.Key, out var shorthandValue)) continue;
            foreach (var longhand in shorthand.Value)
            {
                if (!result.TryGetValue(longhand, out var longhandValue)) continue;
                if (string.Equals(longhandValue, shorthandValue, StringComparison.Ordinal) ||
                    IsCoveredByShorthand(shorthand.Key, shorthandValue, longhand, longhandValue))
                    result.Remove(longhand);
            }
        }

        return result.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray();
    }

    private static bool IsCoveredByShorthand(string shorthand, string shorthandValue, string longhand, string longhandValue)
    {
        if (shorthand is "padding" or "margin" && longhand.StartsWith(shorthand + "-", StringComparison.Ordinal))
            return string.Equals(longhandValue, shorthandValue, StringComparison.Ordinal);
        if (shorthand == "background" && longhand == "background-color")
            return string.Equals(longhandValue, shorthandValue, StringComparison.Ordinal);
        if (shorthand == "border" && longhand.StartsWith("border-", StringComparison.Ordinal))
            return shorthandValue.Contains(longhandValue, StringComparison.Ordinal);
        return false;
    }

    private static readonly Dictionary<string, string[]> ShorthandLonghands = new(StringComparer.Ordinal)
    {
        ["padding"] = ["padding-top", "padding-right", "padding-bottom", "padding-left"],
        ["margin"] = ["margin-top", "margin-right", "margin-bottom", "margin-left"],
        ["background"] = ["background-color"],
        ["border"] =
        [
            "border-width", "border-style", "border-color",
            "border-top", "border-right", "border-bottom", "border-left",
            "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
            "border-top-style", "border-right-style", "border-bottom-style", "border-left-style",
            "border-top-color", "border-right-color", "border-bottom-color", "border-left-color"
        ],
        ["border-radius"] =
        [
            "border-top-left-radius", "border-top-right-radius",
            "border-bottom-right-radius", "border-bottom-left-radius"
        ]
    };

    private static string BuildCss(HtmlExportOptions options, GeneratedStyleSheet styles)
    {
        var css = new StringBuilder();
        if (options.IncludeBaselineCss) css.Append(BaselineCss);
        if (!string.IsNullOrWhiteSpace(options.AdditionalCss)) css.Append(options.AdditionalCss);
        if (!options.UseInlineStyles) css.Append(styles.ToCss());
        return css.ToString();
    }

    /// <summary>
    /// 内嵌样式块中的 <c>&lt;</c> 统一转义为 CSS 等价形式，杜绝 <c>&lt;/style&gt;</c> 提前闭合注入脚本。
    /// </summary>
    private static string SanitizeInlineCss(string css) => css.Replace("<", "\\3c ", StringComparison.Ordinal);

    private static bool TrySafeUrl(string? value, bool allowMailTo, bool allowDataImage, out string safe)
    {
        safe = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        foreach (var character in text)
            if (char.IsControl(character)) return false;
        if (text.StartsWith('#') || Uri.TryCreate(text, UriKind.Relative, out _))
        {
            safe = text;
            return true;
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is "http" or "https" || allowMailTo && uri.Scheme == "mailto" ||
            allowDataImage && uri.Scheme == "data" && text.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            safe = text;
            return true;
        }
        return false;
    }

    private static string Encode(string? value) => Encoder.Encode(value ?? "");

    private sealed class ExportContext
    {
        public ExportContext(HtmlExportOptions options, List<HtmlExportDiagnostic> diagnostics)
        {
            Options = options;
            Diagnostics = diagnostics;
        }

        public HtmlExportOptions Options { get; }
        public List<HtmlExportDiagnostic> Diagnostics { get; }
        public GeneratedStyleSheet Styles { get; } = new();
        public HashSet<Type> ActiveRepresentationTypes { get; } = [];
        public int RepresentationFailures { get; set; }

        /// <summary>文档模式下穿过单一包装找到的唯一 HTML html 根。</summary>
        public HTMLElement? DocumentHtmlRoot { get; init; }
        /// <summary>文档根的 body 宿主；其公共属性并入壳层 body 标签。</summary>
        public NativeUiNode? BodyHostNode { get; set; }
        /// <summary>HTML html 根的 lang 属性，优先于 options.Language。</summary>
        public string? HtmlLang { get; set; }
        /// <summary>首个 HTML title 元素的文本（选项显式标题缺省时的次级来源）。</summary>
        public string? HeadTitle { get; set; }
        /// <summary>首个安全 viewport meta 的 content，替换壳层默认值。</summary>
        public string? ViewportContent { get; set; }
        /// <summary>按遍历顺序合并进 head 的安全元数据片段。</summary>
        public List<string> HeadItems { get; } = new();
        /// <summary>渲染惰性 template 内容时禁止元数据外提。</summary>
        public bool SuppressHeadHoist { get; set; }
    }

    private sealed class GeneratedStyleSheet
    {
        private readonly Dictionary<string, string> _classesBySignature = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<KeyValuePair<string, string>>> _rules = new(StringComparer.Ordinal);

        public string GetClass(IReadOnlyList<KeyValuePair<string, string>> styles)
        {
            var signature = string.Join("\u001f", styles.Select(static pair => pair.Key + "\u001e" + pair.Value));
            if (_classesBySignature.TryGetValue(signature, out var existing)) return existing;

            var baseName = "sq-style-" + StableHash(signature);
            var className = baseName;
            var suffix = 1;
            while (_rules.ContainsKey(SelectorFor(className))) className = baseName + "-" + suffix++;
            _classesBySignature[signature] = className;
            // 仅当完整声明集合一致时才去重到同一个签名类；选择器唯一，直接落规则。
            _rules[SelectorFor(className)] = styles;
            return className;
        }

        public string ToCss()
        {
            if (_rules.Count == 0) return "";
            var css = new StringBuilder();
            foreach (var rule in _rules.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                css.Append(rule.Key).Append('{');
                foreach (var declaration in rule.Value)
                {
                    css.Append(EncodeCssIdentifier(declaration.Key)).Append(':')
                        .Append(EncodeCssValue(declaration.Value)).Append(';');
                }
                css.Append('}');
            }
            return css.ToString();
        }

        /// <summary>生成类名在 CSS 选择器中统一编码，避免选择器注入。</summary>
        private static string SelectorFor(string className) => "." + EncodeCssIdentifier(className);

        private static string StableHash(string value)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (var character in value)
                {
                    hash ^= character;
                    hash *= 1099511628211UL;
                }
                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        private static string EncodeCssIdentifier(string value)
        {
            var result = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character) || character is '-' or '_') result.Append(character);
                else result.Append('\\').Append(((int)character).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
            }
            return result.ToString();
        }

        private static string EncodeCssValue(string value)
        {
            // Style values have already passed the CSS property validator. Keep control
            // characters out of the generated stylesheet without using fragile literals.
            var result = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (character == (char)60) result.Append("\\3c ");
                else if (character == (char)62) result.Append("\\3e ");
                else if (character == (char)13) result.Append("\\d ");
                else if (character == (char)10) result.Append("\\a ");
                else result.Append(character);
            }
            return result.ToString();
        }
    }
}
