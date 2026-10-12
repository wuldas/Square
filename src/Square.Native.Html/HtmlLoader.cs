using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Square.Controls;
using Square.CSS.Engine;
using Square.Html;
using Square.UI;
using NativeElement = Square.UI.Element;

namespace Square.Native.Html;

/// <summary>将本地无脚本 HTML/CSS 文件加载为真实 Square 元素树。</summary>
public static class HtmlLoader
{
    /// <summary>加载标准 HTML；相对路径以应用程序基目录为准。</summary>
    public static HTMLElement LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var sourcePath = Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path.Replace('/', Path.DirectorySeparatorChar)));
        using var source = File.OpenRead(sourcePath);
        var parser = new HtmlParser(new HtmlParserOptions { IsScripting = false });
        using var parsed = parser.ParseDocument(source);
        ControlRegistration.RegisterDefaults();
        var root = (HTMLElement)CreateElement(parsed.DocumentElement);
        try
        {
            List<IElement>? styleSheets = null;
            ImportElement(parsed.DocumentElement, root, sourcePath, ref styleSheets);
            if (styleSheets != null)
                ApplyStyleSheets(root, styleSheets, sourcePath);
            return root;
        }
        catch
        {
            root.DiscardGeneratedSubtree();
            throw;
        }
    }

    private static NativeElement CreateElement(IElement source)
    {
        if (source.LocalName.Equals("script", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Static HTML scripts are not supported.");
        return source.NamespaceUri switch
        {
            ElementRegistry.HtmlNamespaceUri => HtmlElementFactory.Create(source.LocalName),
            ElementRegistry.SvgNamespaceUri => ElementRegistry.CreateNamespace(source.NamespaceUri, source.LocalName),
            _ => throw new NotSupportedException($"Static HTML namespace '{source.NamespaceUri}' is not supported.")
        };
    }

    private static void ImportElement(IElement source, NativeElement target, string sourcePath,
        ref List<IElement>? styleSheets, bool includeStyleSheets = true)
    {
        foreach (var attribute in source.Attributes)
        {
            if (attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Static HTML scripts are not supported.");
            var value = attribute.Value;
            if (target is HTMLElement html)
            {
                if (source.LocalName == "img" && attribute.Name == "src" && !string.IsNullOrWhiteSpace(value))
                    value = ResolveImageSource(value, sourcePath);
                html.SetAttribute(attribute.Name, value);
            }
            else if (attribute.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                target.Id = value;
            else
            {
                var property = SvgAttributeCatalog.TryGetPropertyName(attribute.Name, out var alias)
                    ? alias : attribute.Name;
                target.SetProperty(property, value);
            }
        }

        if (includeStyleSheets && target is HTMLElement && IsAuthorStyleSheet(source))
            (styleSheets ??= []).Add(source);
        // HTML template content lives in a fragment; import it inertly and validate it too.
        var children = source is IHtmlTemplateElement template ? template.Content.ChildNodes : source.ChildNodes;
        foreach (var child in children)
        {
            if (child is IElement childElement)
            {
                var nativeChild = CreateElement(childElement);
                target.ChildNodes.Add(nativeChild);
                ImportElement(childElement, nativeChild, sourcePath, ref styleSheets,
                    includeStyleSheets && source is not IHtmlTemplateElement);
            }
            else if (child is IText text)
                target.ChildNodes.Add(new Square.UI.Text(text.Data));
        }
    }

    private static bool IsAuthorStyleSheet(IElement element)
    {
        if (element.LocalName == "style")
        {
            var type = element.GetAttribute("type");
            return string.IsNullOrWhiteSpace(type) || type.Trim().Equals("text/css", StringComparison.OrdinalIgnoreCase);
        }
        if (element.LocalName != "link" || element.HasAttribute("disabled")) return false;
        var rel = element.GetAttribute("rel");
        if (rel == null) return false;
        foreach (var range in rel.AsSpan().SplitAny(" \t\r\n\f"))
            if (rel.AsSpan()[range].Equals("stylesheet", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void ApplyStyleSheets(HTMLElement root, List<IElement> styleSheets, string sourcePath)
    {
        var engine = new CssEngine();
        var loader = new DocumentStyleSheetLoader(engine);
        foreach (var element in styleSheets)
        {
            var media = element.GetAttribute("media");
            if (!string.IsNullOrWhiteSpace(media) && !media.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Conditional HTML stylesheets are not supported.");
            if (element.LocalName == "style")
                loader.LoadText(element.TextContent, sourcePath);
            else
            {
                var href = element.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href))
                    throw new InvalidOperationException("HTML stylesheet link requires a non-empty href.");
                loader.LoadFile(ResolveStyleSheetPath(href, sourcePath));
            }
        }
        root.RegisterGeneratedResource(engine.ApplyGeneratedStylesToTree(root));
    }

    private static string ResolveStyleSheetPath(string href, string sourcePath)
    {
        if (href.StartsWith("//", StringComparison.Ordinal) || href.StartsWith(@"\\", StringComparison.Ordinal))
            throw new NotSupportedException("Only local HTML stylesheet URLs are supported.");
        if (Path.IsPathRooted(href)) return Path.GetFullPath(href);
        var uri = new Uri(new Uri(sourcePath), href);
        if (!uri.IsFile || uri.IsUnc)
            throw new NotSupportedException("Only local HTML stylesheet URLs are supported.");
        return uri.LocalPath;
    }

    private static string ResolveImageSource(string value, string sourcePath)
    {
        if (value.StartsWith("//", StringComparison.Ordinal)) return value;
        if (Path.IsPathRooted(value)) return Path.GetFullPath(value);
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return uri.IsFile ? uri.LocalPath : value;
        return new Uri(new Uri(sourcePath), value).LocalPath;
    }
}
