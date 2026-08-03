using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Square.Controls;
using Square.Graphics;
using Square.Graphics.Codecs;
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
        [data-square-unsupported="true"]{padding:.75rem;border:1px dashed #b42318;color:#b42318;background:#fff5f5;font-family:system-ui,sans-serif;}
        """;

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
        if (string.IsNullOrWhiteSpace(options.Title)) options.Title = document.Title;
        document.DocumentElement.BuildElementTree();
        return ExportSnapshot(NativeUiTreeBuilder.Snapshot(document.DocumentElement), options);
    }

    private static HtmlExportResult ExportSnapshot(NativeUiNode root, HtmlExportOptions? options)
    {
        options ??= new HtmlExportOptions();
        var diagnostics = new List<HtmlExportDiagnostic>();
        var body = new StringBuilder();
        WriteNode(body, root, diagnostics, isRoot: true);

        if (!options.IncludeDocument)
            return new HtmlExportResult { Html = body.ToString(), Diagnostics = diagnostics };

        var html = new StringBuilder(body.Length + 512);
        html.Append("<!doctype html><html lang=\"").Append(Encode(options.Language)).Append("\"><head>");
        html.Append("<meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<title>").Append(Encode(string.IsNullOrWhiteSpace(options.Title) ? root.Kind : options.Title)).Append("</title>");
        if (options.IncludeBaselineCss || !string.IsNullOrWhiteSpace(options.AdditionalCss))
        {
            html.Append("<style>");
            if (options.IncludeBaselineCss) html.Append(BaselineCss);
            if (!string.IsNullOrWhiteSpace(options.AdditionalCss)) html.Append(options.AdditionalCss);
            html.Append("</style>");
        }
        html.Append("</head><body>").Append(body).Append("</body></html>");
        return new HtmlExportResult { Html = html.ToString(), Diagnostics = diagnostics };
    }

    private static void WriteNode(
        StringBuilder output,
        NativeUiNode node,
        List<HtmlExportDiagnostic> diagnostics,
        bool isRoot = false)
    {
        var element = node.SourceElement;
        if (!element.IsVisible || element is UIHeadElement) return;

        if (element is UIRootElement or UIBodyElement)
        {
            foreach (var child in node.Children) WriteNode(output, child, diagnostics, isRoot);
            return;
        }

        switch (element)
        {
            case Canvas:
            case Popup:
                WriteUnsupported(output, node, diagnostics, isRoot);
                return;
            case SquareText text:
                WriteContainer(output, node, "span", diagnostics, isRoot, text.TextContent, includeChildren: false);
                return;
            case Button button:
                WriteContainer(output, node, "button", diagnostics, isRoot, button.TextContent, includeChildren: true,
                    extraAttributes: button.IsDisabled ? " disabled" : "");
                return;
            case Input input:
                WriteVoid(output, node, "input", isRoot,
                    $" type=\"{Encode(input.Type)}\" value=\"{Encode(input.Value)}\" placeholder=\"{Encode(input.Placeholder)}\"" +
                    (input.IsDisabled ? " disabled" : ""));
                return;
            case TextArea textArea:
                WriteContainer(output, node, "textarea", diagnostics, isRoot, textArea.Value, includeChildren: false,
                    extraAttributes: $" placeholder=\"{Encode(textArea.Placeholder)}\"" + (textArea.IsDisabled ? " disabled" : ""));
                return;
            case CheckBox checkBox:
                WriteChoice(output, node, "checkbox", checkBox.TextContent, checkBox.IsChecked, null, checkBox.IsDisabled, isRoot);
                return;
            case Radio radio:
                WriteChoice(output, node, "radio", radio.TextContent, radio.IsChecked, radio.GroupName, radio.IsDisabled, isRoot);
                return;
            case Select select:
                WriteSelect(output, node, select, isRoot);
                return;
            case Link link:
                WriteLink(output, node, link, diagnostics, isRoot);
                return;
            case SquareImage image:
                WriteImage(output, node, image, diagnostics, isRoot);
                return;
            case List:
                WriteContainer(output, node, "ul", diagnostics, isRoot, null, includeChildren: true);
                return;
            case ListItem item:
                WriteContainer(output, node, "li", diagnostics, isRoot, item.TextContent, includeChildren: true);
                return;
            case SVGElement svg:
                WriteSvg(output, node, svg, diagnostics, isRoot);
                return;
            case ScrollViewer:
            case View:
                WriteContainer(output, node, "div", diagnostics, isRoot, null, includeChildren: true);
                return;
        }

        if (node.Children.Count > 0 && element.GetType().Assembly != typeof(Element).Assembly)
        {
            WriteContainer(output, node, "div", diagnostics, isRoot, null, includeChildren: true,
                extraAttributes: $" data-square-component=\"{Encode(element.GetType().FullName ?? element.GetType().Name)}\"");
            return;
        }

        WriteUnsupported(output, node, diagnostics, isRoot);
    }

    private static void WriteContainer(
        StringBuilder output,
        NativeUiNode node,
        string tag,
        List<HtmlExportDiagnostic> diagnostics,
        bool isRoot,
        string? text,
        bool includeChildren,
        string extraAttributes = "")
    {
        output.Append('<').Append(tag);
        WriteCommonAttributes(output, node, isRoot);
        output.Append(extraAttributes).Append('>');
        if (!string.IsNullOrEmpty(text)) output.Append(Encode(text));
        if (includeChildren)
            foreach (var child in node.Children) WriteNode(output, child, diagnostics);
        output.Append("</").Append(tag).Append('>');
    }

    private static void WriteVoid(StringBuilder output, NativeUiNode node, string tag, bool isRoot, string extraAttributes)
    {
        output.Append('<').Append(tag);
        WriteCommonAttributes(output, node, isRoot);
        output.Append(extraAttributes).Append('>');
    }

    private static void WriteChoice(
        StringBuilder output,
        NativeUiNode node,
        string type,
        string text,
        bool isChecked,
        string? name,
        bool isDisabled,
        bool isRoot)
    {
        output.Append("<label");
        WriteCommonAttributes(output, node, isRoot);
        output.Append("><input type=\"").Append(type).Append('"');
        if (!string.IsNullOrWhiteSpace(name)) output.Append(" name=\"").Append(Encode(name)).Append('"');
        if (isChecked) output.Append(" checked");
        if (isDisabled) output.Append(" disabled");
        output.Append('>');
        if (!string.IsNullOrEmpty(text)) output.Append(Encode(text));
        output.Append("</label>");
    }

    private static void WriteSelect(StringBuilder output, NativeUiNode node, Select select, bool isRoot)
    {
        output.Append("<select");
        WriteCommonAttributes(output, node, isRoot);
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
        List<HtmlExportDiagnostic> diagnostics,
        bool isRoot)
    {
        output.Append("<a");
        WriteCommonAttributes(output, node, isRoot);
        if (TrySafeUrl(link.Href, allowMailTo: true, allowDataImage: false, out var href))
            output.Append(" href=\"").Append(Encode(href)).Append('"');
        else if (!string.IsNullOrWhiteSpace(link.Href))
            diagnostics.Add(new HtmlExportDiagnostic(node.Kind, $"Rejected unsafe link URL '{link.Href}'."));
        output.Append('>').Append(Encode(link.TextContent)).Append("</a>");
    }

    private static void WriteImage(
        StringBuilder output,
        NativeUiNode node,
        SquareImage image,
        List<HtmlExportDiagnostic> diagnostics,
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
            diagnostics.Add(new HtmlExportDiagnostic(node.Kind, "Image has no browser-safe source or bitmap content."));
            WriteUnsupported(output, node, diagnostics, isRoot, addDiagnostic: false);
            return;
        }

        WriteVoid(output, node, "img", isRoot,
            $" src=\"{Encode(source)}\" alt=\"{Encode(image.Source)}\"");
    }

    private static void WriteSvg(
        StringBuilder output,
        NativeUiNode node,
        SVGElement svg,
        List<HtmlExportDiagnostic> diagnostics,
        bool isRoot)
    {
        var tag = svg.TagName.ToLowerInvariant();
        output.Append('<').Append(tag);
        WriteCommonAttributes(output, node, isRoot);
        foreach (var (name, property) in GetSvgAttributes(svg))
        {
            var value = svg.GetProperty<object>(property)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                output.Append(' ').Append(name).Append("=\"").Append(Encode(value)).Append('"');
        }
        output.Append('>');
        foreach (var child in node.Children) WriteNode(output, child, diagnostics);
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
        List<HtmlExportDiagnostic> diagnostics,
        bool isRoot,
        bool addDiagnostic = true)
    {
        if (addDiagnostic)
            diagnostics.Add(new HtmlExportDiagnostic(node.Kind, $"No semantic HTML mapping exists for {node.SourceElement.GetType().FullName}."));
        output.Append("<div");
        WriteCommonAttributes(output, node, isRoot);
        output.Append(" data-square-kind=\"").Append(Encode(node.Kind)).Append("\" data-square-unsupported=\"true\">");
        output.Append(Encode(node.Kind)).Append(" is not supported by the static HTML target.");
        output.Append("</div>");
    }

    private static void WriteCommonAttributes(StringBuilder output, NativeUiNode node, bool isRoot)
    {
        var element = node.SourceElement;
        if (!string.IsNullOrWhiteSpace(element.Id))
            output.Append(" id=\"").Append(Encode(element.Id)).Append('"');

        var classes = node.Classes.ToList();
        if (isRoot) classes.Add("square-root");
        if (classes.Count > 0)
            output.Append(" class=\"").Append(Encode(string.Join(' ', classes.Distinct(StringComparer.Ordinal)))).Append('"');

        var styles = MergeStyles(node);
        if (styles.Count > 0)
        {
            output.Append(" style=\"");
            foreach (var pair in styles.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                output.Append(Encode(pair.Key)).Append(':').Append(Encode(pair.Value)).Append(';');
            output.Append('"');
        }

        if (element is UIElement ui && !string.IsNullOrWhiteSpace(ui.Tooltip))
            output.Append(" title=\"").Append(Encode(ui.Tooltip)).Append('"');
    }

    private static Dictionary<string, string> MergeStyles(NativeUiNode node)
    {
        var styles = new Dictionary<string, string>(node.Style, StringComparer.Ordinal);
        if (node.SourceElement is UIElement ui)
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

    private static void AddPixels(Dictionary<string, string> styles, string property, float value, float defaultValue)
    {
        if (styles.ContainsKey(property) || float.IsNaN(value) || float.IsInfinity(value)) return;
        if (!float.IsNaN(defaultValue) && value.Equals(defaultValue)) return;
        styles[property] = value.ToString("0.###", CultureInfo.InvariantCulture) + "px";
    }

    private static bool TrySafeUrl(string? value, bool allowMailTo, bool allowDataImage, out string safe)
    {
        safe = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
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

    private static string Encode(string? value) => HtmlEncoder.Default.Encode(value ?? "");
}
