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
        var context = new ExportContext(options, diagnostics);
        var body = new StringBuilder();
        WriteNode(body, root, context, isRoot: true);
        var css = BuildCss(options, context.Styles);

        if (!options.IncludeDocument)
            return new HtmlExportResult { Html = body.ToString(), Css = css, Diagnostics = diagnostics };

        var html = new StringBuilder(body.Length + 512);
        html.Append("<!doctype html><html lang=\"").Append(Encode(options.Language)).Append("\"><head>");
        html.Append("<meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<title>").Append(Encode(string.IsNullOrWhiteSpace(options.Title) ? root.Kind : options.Title)).Append("</title>");
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
            html.Append("<style data-square-css=\"true\">").Append(css).Append("</style>");
        }
        html.Append("</head><body>").Append(body).Append("</body></html>");
        return new HtmlExportResult { Html = html.ToString(), Css = css, Diagnostics = diagnostics };
    }

    private static void WriteNode(
        StringBuilder output,
        NativeUiNode node,
        ExportContext context,
        bool isRoot = false)
    {
        var element = node.SourceElement;
        if (!element.IsVisible || element is UIHeadElement) return;

        if (element is UIRootElement or UIBodyElement)
        {
            foreach (var child in node.Children) WriteNode(output, child, context, isRoot);
            return;
        }

        switch (element)
        {
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
        WriteCommonAttributes(output, node, context, isRoot);
        output.Append("><input type=\"").Append(type).Append('"');
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

    private static void WriteCommonAttributes(StringBuilder output, NativeUiNode node, ExportContext context, bool isRoot)
    {
        var element = node.SourceElement;
        if (!string.IsNullOrWhiteSpace(element.Id))
            output.Append(" id=\"").Append(Encode(element.Id)).Append('"');

        var classes = node.Classes.ToList();
        if (isRoot) classes.Add("square-root");

        var styles = MergeStyles(node);
        if (styles.Count > 0 && context.Options.UseInlineStyles)
        {
            output.Append(" style=\"");
            foreach (var pair in styles.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                output.Append(Encode(pair.Key)).Append(':').Append(Encode(pair.Value)).Append(';');
            output.Append('"');
        }
        else if (styles.Count > 0)
            classes.Add(context.Styles.GetClass(styles));

        if (classes.Count > 0)
            output.Append(" class=\"").Append(Encode(string.Join(' ', classes.Distinct(StringComparer.Ordinal)))).Append('"');

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

    private static string BuildCss(HtmlExportOptions options, GeneratedStyleSheet styles)
    {
        var css = new StringBuilder();
        if (options.IncludeBaselineCss) css.Append(BaselineCss);
        if (!string.IsNullOrWhiteSpace(options.AdditionalCss)) css.Append(options.AdditionalCss);
        if (!options.UseInlineStyles) css.Append(styles.ToCss());
        return css.ToString();
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
    }

    private sealed class GeneratedStyleSheet
    {
        private readonly Dictionary<string, string> _classesBySignature = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<KeyValuePair<string, string>>> _rules = new(StringComparer.Ordinal);

        public string GetClass(IReadOnlyDictionary<string, string> styles)
        {
            var declarations = styles
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToArray();
            var signature = string.Join("\u001f", declarations.Select(static pair => pair.Key + "\u001e" + pair.Value));
            if (_classesBySignature.TryGetValue(signature, out var existing)) return existing;

            var baseName = "sq-style-" + StableHash(signature);
            var className = baseName;
            var suffix = 1;
            while (_rules.ContainsKey(className)) className = baseName + "-" + suffix++;
            _classesBySignature[signature] = className;
            _rules[className] = declarations;
            return className;
        }

        public string ToCss()
        {
            if (_rules.Count == 0) return "";
            var css = new StringBuilder();
            foreach (var rule in _rules.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                css.Append('.').Append(rule.Key).Append('{');
                foreach (var declaration in rule.Value)
                {
                    css.Append(EncodeCssIdentifier(declaration.Key)).Append(':')
                        .Append(EncodeCssValue(declaration.Value)).Append(';');
                }
                css.Append('}');
            }
            return css.ToString();
        }

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
