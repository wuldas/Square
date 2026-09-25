using Square.Html;

namespace Square.Compiler.LanguageServices;

/// <summary>One URI/local decision for parser boundaries, lowering and project analysis.</summary>
internal static class TemplateTagResolver
{
    internal static TemplateElementResolution Resolve(
        TemplateCatalog catalog, TemplateResolutionContext context, string tagName,
        bool svgDescendant = false, string isName = null)
    {
        catalog ??= TemplateCatalog.BuiltIn;
        context ??= new TemplateResolutionContext(string.Empty, Array.Empty<string>());
        var resolution = !svgDescendant || tagName.IndexOf(':') >= 0
            ? catalog.ResolveComponent(tagName, context)
            : catalog.ResolveComponent(tagName,
                context.WithDefaultElementNamespace(TemplateCatalog.SvgNamespaceUri));
        if (svgDescendant && tagName.IndexOf(':') < 0 &&
            resolution.Status == TemplateElementResolutionStatus.Resolved &&
            resolution.Component.NamespaceUri != TemplateCatalog.SvgNamespaceUri)
            return TemplateElementResolution.Failed(TemplateElementResolutionStatus.UnknownElement);
        return isName != null && resolution.Status == TemplateElementResolutionStatus.Resolved &&
               resolution.Component.Kind == TemplateElementKind.Html
            ? catalog.ResolveCustomizedBuiltIn(resolution, isName)
            : resolution;
    }

    internal static bool IsHtmlVoid(string tagName, TemplateElementResolution resolution)
    {
        if (resolution.Status == TemplateElementResolutionStatus.Resolved)
            return resolution.Component.IsHtmlHost &&
                   HtmlTagCatalog.IsVoid(resolution.Component.LocalName);
        // Keep following siblings intact so the analyzer reports the actual unknown/ambiguous tag.
        var localName = tagName.Substring(tagName.LastIndexOf(':') + 1).ToLowerInvariant();
        return HtmlTagCatalog.IsVoid(localName);
    }

    internal static bool IsHtml(TemplateElementResolution resolution) =>
        resolution != null && resolution.Status == TemplateElementResolutionStatus.Resolved &&
        resolution.Component.IsHtmlHost;

    internal static bool IsSvg(TemplateElementResolution resolution) =>
        resolution != null && resolution.Status == TemplateElementResolutionStatus.Resolved &&
        resolution.Component.Kind == TemplateElementKind.Svg;
}
