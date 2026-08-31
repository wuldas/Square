using Square.Compiler.Syntax;

namespace Square.Compiler.LanguageServices;

public enum CssCompletionKind
{
    None,
    Property,
    Value,
    Selector,
    PseudoClass,
    PseudoElement,
    AtRule
}

public sealed class CssCompletionContext
{
    public CssCompletionContext(CssCompletionKind kind, string prefix, string propertyName = "")
    {
        Kind = kind;
        Prefix = prefix ?? string.Empty;
        PropertyName = propertyName ?? string.Empty;
    }

    public CssCompletionKind Kind { get; }
    public string Prefix { get; }
    public string PropertyName { get; }
}

public static class CssCompletionService
{
    private static readonly string[] Properties =
    {
        "align-content", "align-items", "align-self", "animation", "animation-delay",
        "animation-direction", "animation-duration", "animation-iteration-count", "animation-name",
        "animation-timing-function", "appearance", "background", "background-attachment",
        "background-color", "background-image", "background-position", "background-repeat", "border",
        "border-bottom", "border-bottom-color", "border-bottom-left-radius", "border-bottom-right-radius",
        "border-bottom-style", "border-bottom-width", "border-collapse", "border-color", "border-left",
        "border-left-color", "border-left-style", "border-left-width", "border-radius", "border-right",
        "border-right-color", "border-right-style", "border-right-width", "border-spacing", "border-style",
        "border-top", "border-top-color", "border-top-left-radius", "border-top-right-radius",
        "border-top-style", "border-top-width", "border-width", "bottom", "box-shadow", "box-sizing",
        "caption-side", "caret-color", "clear", "clip", "color", "column-gap", "content",
        "counter-increment", "counter-reset", "cursor", "direction", "display", "empty-cells", "flex",
        "flex-basis", "flex-direction", "flex-grow", "flex-shrink", "flex-wrap", "float", "font",
        "font-family", "font-size", "font-style", "font-variant", "font-weight", "gap", "grid",
        "grid-area", "grid-column", "grid-column-span", "grid-row", "grid-row-span", "grid-template-areas",
        "grid-template-columns", "grid-template-rows", "height", "inset", "justify-content", "left", "letter-spacing",
        "line-height", "list-style", "list-style-image", "list-style-position", "list-style-type", "margin",
        "margin-bottom", "margin-left", "margin-right", "margin-top", "max-height", "max-width", "min-height",
        "min-width", "opacity", "orphans", "outline", "outline-color", "outline-offset", "outline-style",
        "outline-width", "overflow", "overflow-x", "overflow-y", "padding", "padding-bottom", "padding-left",
        "padding-right", "padding-top", "page-break-after", "page-break-before", "page-break-inside", "position",
        "quotes", "right", "row-gap", "selection-background", "selection-background-color", "selection-color",
        "table-layout", "text-align", "text-decoration", "text-decoration-color", "text-decoration-line",
        "text-decoration-style", "text-indent", "text-transform", "top", "unicode-bidi", "user-select",
        "vertical-align", "visibility", "white-space", "widows", "width", "word-spacing", "z-index"
    };

    private static readonly IReadOnlyDictionary<string, string[]> PropertyValues =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["align-content"] = ["stretch", "flex-start", "center", "flex-end", "space-between", "space-around"],
            ["align-items"] = ["stretch", "flex-start", "center", "flex-end"],
            ["align-self"] = ["auto", "stretch", "flex-start", "center", "flex-end"],
            ["animation-direction"] = ["normal", "reverse", "alternate", "alternate-reverse"],
            ["animation-iteration-count"] = ["1", "infinite"],
            ["animation-timing-function"] = ["linear", "ease", "ease-in", "ease-out", "ease-in-out", "step-start", "step-end", "cubic-bezier()", "steps()"],
            ["appearance"] = ["none", "auto"],
            ["background"] = ["transparent", "none", "currentcolor"],
            ["background-attachment"] = ["scroll", "fixed"],
            ["background-color"] = ["transparent", "currentcolor"],
            ["background-image"] = ["none", "url()"],
            ["background-position"] = ["left", "center", "right", "top", "bottom", "0% 0%"],
            ["background-repeat"] = ["repeat", "repeat-x", "repeat-y", "no-repeat"],
            ["border-collapse"] = ["collapse", "separate"],
            ["border-style"] = BorderStyles(),
            ["border-top-style"] = BorderStyles(),
            ["border-right-style"] = BorderStyles(),
            ["border-bottom-style"] = BorderStyles(),
            ["border-left-style"] = BorderStyles(),
            ["box-shadow"] = ["none", "0 4px 8px 2px rgba(0, 0, 0, 0.48)"],
            ["box-sizing"] = ["content-box", "border-box"],
            ["caption-side"] = ["top", "bottom"],
            ["clear"] = ["none", "left", "right", "both"],
            ["color"] = ["currentcolor", "transparent"],
            ["cursor"] = ["auto", "default", "pointer", "text"],
            ["direction"] = ["ltr", "rtl"],
            ["display"] = ["none", "inline", "block", "inline-block", "list-item", "flex", "grid", "table", "inline-table", "table-row-group", "table-header-group", "table-footer-group", "table-row", "table-column-group", "table-column", "table-cell", "table-caption"],
            ["empty-cells"] = ["show", "hide"],
            ["flex-basis"] = ["auto", "0", "100%"],
            ["flex-direction"] = ["row", "row-reverse", "column", "column-reverse"],
            ["flex-grow"] = ["0", "1"],
            ["flex-shrink"] = ["0", "1"],
            ["flex-wrap"] = ["nowrap", "wrap", "wrap-reverse"],
            ["float"] = ["none", "left", "right"],
            ["font-family"] = ["sans-serif", "serif", "monospace"],
            ["font-size"] = ["small", "medium", "large", "12px", "1rem"],
            ["font-style"] = ["normal", "italic", "oblique"],
            ["font-variant"] = ["normal", "small-caps"],
            ["font-weight"] = ["normal", "bold", "100", "200", "300", "400", "500", "600", "700", "800", "900"],
            ["grid-template-columns"] = ["none", "1fr", "repeat()", "minmax()"],
            ["grid-template-rows"] = ["none", "auto", "1fr", "repeat()", "minmax()"],
            ["height"] = DimensionValues(),
            ["justify-content"] = ["flex-start", "center", "flex-end", "space-between", "space-around"],
            ["left"] = ["auto", "0", "100%"],
            ["line-height"] = ["normal", "1", "1.5", "100%"],
            ["list-style-image"] = ["none", "url()"],
            ["list-style-position"] = ["inside", "outside"],
            ["list-style-type"] = ["none", "disc", "circle", "square", "decimal", "lower-roman", "upper-roman"],
            ["max-height"] = ["none", "0", "100%"],
            ["max-width"] = ["none", "0", "100%"],
            ["min-height"] = ["0", "100%"],
            ["min-width"] = ["0", "100%"],
            ["opacity"] = ["0", "0.5", "1"],
            ["outline-style"] = BorderStyles(),
            ["overflow"] = OverflowValues(),
            ["overflow-x"] = OverflowValues(),
            ["overflow-y"] = OverflowValues(),
            ["page-break-after"] = ["auto", "always", "avoid", "left", "right"],
            ["page-break-before"] = ["auto", "always", "avoid", "left", "right"],
            ["page-break-inside"] = ["auto", "avoid"],
            ["position"] = ["static", "relative", "absolute", "fixed"],
            ["right"] = ["auto", "0", "100%"],
            ["table-layout"] = ["auto", "fixed"],
            ["text-align"] = ["left", "right", "center", "justify", "start", "end"],
            ["text-decoration"] = ["none", "underline", "overline", "line-through"],
            ["text-decoration-line"] = ["none", "underline", "overline", "line-through"],
            ["text-transform"] = ["none", "capitalize", "uppercase", "lowercase"],
            ["top"] = ["auto", "0", "100%"],
            ["unicode-bidi"] = ["normal", "embed", "bidi-override"],
            ["user-select"] = ["auto", "none", "text"],
            ["vertical-align"] = ["baseline", "sub", "super", "top", "text-top", "middle", "bottom", "text-bottom"],
            ["visibility"] = ["visible", "hidden", "collapse"],
            ["white-space"] = ["normal", "pre", "nowrap", "pre-wrap", "pre-line"],
            ["width"] = DimensionValues(),
            ["z-index"] = ["auto", "0", "1"]
        };

    private static readonly string[] PseudoClasses =
    {
        "hover", "focus", "focus-visible", "active", "disabled", "checked", "open", "empty",
        "first-child", "last-child", "only-child", "root", "nth-child()", "not()"
    };

    private static readonly string[] PseudoElements = { "before", "after", "marker", "selection" };
    private static readonly string[] AtRules = { "@import", "@media", "@keyframes", "@theme" };
    private static readonly string[] GlobalValues = { "inherit", "initial", "unset", "var()" };

    public static CssCompletionContext GetContext(string text, int offset, string sourcePath)
    {
        text ??= string.Empty;
        offset = Math.Min(Math.Max(offset, 0), text.Length);
        var document = SquareDocumentService.ParseSyntaxTree(text, sourcePath ?? string.Empty).ParsedSqxDocument;
        var style = document?.Syntax?.Style;
        if (style?.Css == null || offset < style.ContentRange.Offset || offset > style.ContentRange.End)
            return new CssCompletionContext(CssCompletionKind.None, string.Empty);

        var declaration = EnumerateDeclarations(style.Css)
            .Where(item => offset >= item.FullRange.Offset && offset <= item.FullRange.End)
            .OrderBy(item => item.FullRange.Length)
            .FirstOrDefault();
        if (declaration != null)
        {
            if (offset <= declaration.PropertyRange.End)
                return new CssCompletionContext(
                    CssCompletionKind.Property,
                    GetIdentifierPrefix(text, offset, allowCustomProperty: true));
            if (IsInsideString(text, declaration.ValueRange.Offset, offset))
                return new CssCompletionContext(CssCompletionKind.None, string.Empty);
            return new CssCompletionContext(
                CssCompletionKind.Value,
                GetValuePrefix(text, declaration.ValueRange.Offset, offset),
                declaration.Property);
        }

        var block = FindInnermostBlock(style.Css, offset);
        if (block != null)
        {
            var segmentStart = FindDeclarationSegmentStart(text, block.Value.Offset + 1, offset);
            var colon = FindTopLevelColon(text, segmentStart, offset);
            if (colon >= 0)
            {
                if (IsInsideString(text, colon + 1, offset))
                    return new CssCompletionContext(CssCompletionKind.None, string.Empty);
                return new CssCompletionContext(
                    CssCompletionKind.Value,
                    GetValuePrefix(text, colon + 1, offset),
                    text.Substring(segmentStart, colon - segmentStart).Trim());
            }
            return new CssCompletionContext(
                CssCompletionKind.Property,
                GetIdentifierPrefix(text, offset, allowCustomProperty: true));
        }

        var selector = EnumerateRules(style.Css)
            .SelectMany(rule => rule.Selectors)
            .Where(item => offset >= item.Range.Offset && offset <= item.Range.End)
            .OrderBy(item => item.Range.Length)
            .FirstOrDefault();
        var selectorStart = selector?.Range.Offset ?? FindSelectorStart(text, style.ContentRange.Offset, offset);
        var selectorPrefix = GetSelectorPrefix(text, selectorStart, offset, out var selectorKind);
        return new CssCompletionContext(selectorKind, selectorPrefix);
    }

    public static IReadOnlyList<TemplateCompletionItem> GetItems(
        CssCompletionContext context,
        string text,
        string sourcePath)
    {
        switch (context.Kind)
        {
            case CssCompletionKind.Property:
                return GetPropertyItems(context.Prefix, text);
            case CssCompletionKind.Value:
                return GetValueItems(context.PropertyName, context.Prefix, text);
            case CssCompletionKind.PseudoClass:
                return Filter(PseudoClasses, context.Prefix, "CSS pseudo-class", 14);
            case CssCompletionKind.PseudoElement:
                return Filter(PseudoElements, context.Prefix, "CSS pseudo-element", 14);
            case CssCompletionKind.AtRule:
                return Filter(AtRules, context.Prefix, "CSS at-rule", 14);
            case CssCompletionKind.Selector:
                return GetSelectorItems(context.Prefix, text, sourcePath);
            default:
                return Array.Empty<TemplateCompletionItem>();
        }
    }

    private static IReadOnlyList<TemplateCompletionItem> GetPropertyItems(string prefix, string text)
    {
        var names = Properties.Concat(ExtractCustomProperties(text));
        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name)
            .Select(name => new TemplateCompletionItem(name, 10, "Square CSS property", name))
            .ToArray();
    }

    private static IReadOnlyList<TemplateCompletionItem> GetValueItems(
        string propertyName,
        string prefix,
        string text)
    {
        var values = new List<string>(GlobalValues);
        if (PropertyValues.TryGetValue(propertyName, out var propertyValues)) values.AddRange(propertyValues);
        if (IsColorProperty(propertyName))
            values.AddRange(new[] { "transparent", "currentcolor", "#000000", "rgb()", "rgba()" });
        values.AddRange(ExtractCustomProperties(text).Select(name => "var(" + name + ")"));
        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(value => new TemplateCompletionItem(value, 12, "CSS value for " + propertyName, value))
            .ToArray();
    }

    private static IReadOnlyList<TemplateCompletionItem> GetSelectorItems(
        string prefix,
        string text,
        string sourcePath)
    {
        var tags = TemplateCatalog.BuiltIn.Components.Select(component => component.TagName);
        var classes = ExtractTemplateClasses(text, sourcePath).Select(name => "." + name);
        var ids = ExtractTemplateIds(text, sourcePath).Select(name => "#" + name);
        return tags.Concat(classes).Concat(ids).Concat(new[] { "*" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(value => new TemplateCompletionItem(value, 7, "Square CSS selector", value))
            .ToArray();
    }

    private static IReadOnlyList<TemplateCompletionItem> Filter(
        IEnumerable<string> values,
        string prefix,
        string detail,
        int kind) => values
        .Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .Select(value => new TemplateCompletionItem(value, kind, detail, value))
        .ToArray();

    private static IEnumerable<CssRuleSyntax> EnumerateRules(CssStyleSheetSyntax style)
    {
        foreach (var rule in style.Rules) yield return rule;
        foreach (var atRule in style.AtRules)
            foreach (var rule in EnumerateRules(atRule)) yield return rule;
    }

    private static IEnumerable<CssRuleSyntax> EnumerateRules(CssAtRuleSyntax atRule)
    {
        foreach (var rule in atRule.Rules) yield return rule;
        foreach (var child in atRule.AtRules)
            foreach (var rule in EnumerateRules(child)) yield return rule;
    }

    private static IEnumerable<CssDeclarationSyntax> EnumerateDeclarations(CssStyleSheetSyntax style)
    {
        foreach (var declaration in style.Rules.SelectMany(rule => rule.Declarations)) yield return declaration;
        foreach (var atRule in style.AtRules)
            foreach (var declaration in EnumerateDeclarations(atRule)) yield return declaration;
    }

    private static IEnumerable<CssDeclarationSyntax> EnumerateDeclarations(CssAtRuleSyntax atRule)
    {
        foreach (var declaration in atRule.Declarations) yield return declaration;
        foreach (var declaration in atRule.Rules.SelectMany(rule => rule.Declarations)) yield return declaration;
        foreach (var child in atRule.AtRules)
            foreach (var declaration in EnumerateDeclarations(child)) yield return declaration;
    }

    private static SquareSourceRange? FindInnermostBlock(CssStyleSheetSyntax style, int offset)
    {
        var blocks = EnumerateRules(style).Select(rule => rule.BlockRange)
            .Concat(EnumerateAtRules(style)
                .Where(rule => rule.Declarations.Count > 0)
                .Select(rule => rule.BlockRange))
            .Where(range => range.Length > 0 && offset >= range.Offset && offset <= range.End)
            .OrderBy(range => range.Length)
            .ToArray();
        return blocks.Length == 0 ? null : blocks[0];
    }

    private static IEnumerable<CssAtRuleSyntax> EnumerateAtRules(CssStyleSheetSyntax style)
    {
        foreach (var atRule in style.AtRules)
        {
            yield return atRule;
            foreach (var child in EnumerateAtRules(atRule)) yield return child;
        }
    }

    private static IEnumerable<CssAtRuleSyntax> EnumerateAtRules(CssAtRuleSyntax atRule)
    {
        foreach (var child in atRule.AtRules)
        {
            yield return child;
            foreach (var nested in EnumerateAtRules(child)) yield return nested;
        }
    }

    private static int FindDeclarationSegmentStart(string text, int blockStart, int offset)
    {
        var start = offset;
        var depth = 0;
        while (start > blockStart)
        {
            var value = text[start - 1];
            if (value == ')') depth++;
            else if (value == '(' && depth > 0) depth--;
            else if (depth == 0 && value is ';' or '{') break;
            start--;
        }
        while (start < offset && char.IsWhiteSpace(text[start])) start++;
        return start;
    }

    private static int FindTopLevelColon(string text, int start, int end)
    {
        var depth = 0;
        for (var index = start; index < end; index++)
        {
            if (text[index] == '(') depth++;
            else if (text[index] == ')') depth = Math.Max(0, depth - 1);
            else if (text[index] == ':' && depth == 0) return index;
        }
        return -1;
    }

    private static bool IsInsideString(string text, int start, int offset)
    {
        var quote = '\0';
        for (var index = Math.Max(0, start); index < offset && index < text.Length; index++)
        {
            var value = text[index];
            if (quote != '\0')
            {
                if (value == '\\' && index + 1 < offset) index++;
                else if (value == quote) quote = '\0';
            }
            else if (value is '\'' or '"') quote = value;
        }
        return quote != '\0';
    }

    private static int FindSelectorStart(string text, int styleStart, int offset)
    {
        var start = offset;
        while (start > styleStart && text[start - 1] is not ('}' or ';')) start--;
        while (start < offset && char.IsWhiteSpace(text[start])) start++;
        return start;
    }

    private static string GetSelectorPrefix(
        string text,
        int selectorStart,
        int offset,
        out CssCompletionKind kind)
    {
        var start = offset;
        while (start > selectorStart && IsSelectorIdentifierPart(text[start - 1])) start--;
        if (start > selectorStart && text[start - 1] == ':')
        {
            if (start - 2 >= selectorStart && text[start - 2] == ':')
            {
                kind = CssCompletionKind.PseudoElement;
                return text.Substring(start, offset - start);
            }
            kind = CssCompletionKind.PseudoClass;
            return text.Substring(start, offset - start);
        }
        if (start > selectorStart && text[start - 1] == '@')
        {
            kind = CssCompletionKind.AtRule;
            return text.Substring(start - 1, offset - start + 1);
        }
        start = offset;
        while (start > selectorStart && !char.IsWhiteSpace(text[start - 1]) && text[start - 1] is not ('>' or '+' or '~' or ',')) start--;
        kind = CssCompletionKind.Selector;
        return text.Substring(start, offset - start);
    }

    private static string GetIdentifierPrefix(string text, int offset, bool allowCustomProperty)
    {
        var start = offset;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '-' or '_' ||
                            allowCustomProperty && text[start - 1] == '-')) start--;
        return text.Substring(start, offset - start);
    }

    private static string GetValuePrefix(string text, int valueStart, int offset)
    {
        var start = offset;
        while (start > valueStart && !char.IsWhiteSpace(text[start - 1]) && text[start - 1] is not (';' or ',')) start--;
        return text.Substring(start, offset - start);
    }

    private static IEnumerable<string> ExtractCustomProperties(string text)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + 2 < text.Length; index++)
        {
            if (text[index] != '-' || text[index + 1] != '-') continue;
            var end = index + 2;
            while (end < text.Length && IsSelectorIdentifierPart(text[end])) end++;
            if (end > index + 2) names.Add(text.Substring(index, end - index));
            index = end;
        }
        return names;
    }

    private static IEnumerable<string> ExtractTemplateClasses(string text, string sourcePath) =>
        EnumerateTemplateAttributes(text, sourcePath, "class")
            .SelectMany(value => value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<string> ExtractTemplateIds(string text, string sourcePath) =>
        EnumerateTemplateAttributes(text, sourcePath, "id");

    private static IEnumerable<string> EnumerateTemplateAttributes(
        string text,
        string sourcePath,
        string attributeName)
    {
        var document = SquareDocumentService.ParseSyntaxTree(text, sourcePath ?? string.Empty).ParsedSqxDocument;
        var template = document?.Syntax?.Template;
        if (template?.SqxSyntax != null)
            return EnumerateSqxAttributes(template.SqxSyntax.Roots, attributeName);
        if (template?.SqvSyntax != null)
            return EnumerateSqvAttributes(template.SqvSyntax.Roots, attributeName);
        return Array.Empty<string>();
    }

    private static IEnumerable<string> EnumerateSqxAttributes(
        IEnumerable<SqxSyntaxNode> nodes,
        string attributeName)
    {
        foreach (var element in nodes.OfType<SqxElementSyntax>())
        {
            foreach (var attribute in element.Attributes.Where(attribute =>
                         attribute.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase) &&
                         !attribute.IsExpression && !string.IsNullOrWhiteSpace(attribute.Value)))
                yield return attribute.Value;
            foreach (var value in EnumerateSqxAttributes(element.Children, attributeName)) yield return value;
        }
    }

    private static IEnumerable<string> EnumerateSqvAttributes(
        IEnumerable<SqvSyntaxNode> nodes,
        string attributeName)
    {
        foreach (var element in nodes.OfType<SqvElementSyntax>())
        {
            foreach (var attribute in element.Attributes.Where(attribute =>
                         attribute.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase) &&
                         string.IsNullOrEmpty(attribute.DirectiveName) && !string.IsNullOrWhiteSpace(attribute.Value)))
                yield return attribute.Value;
            foreach (var value in EnumerateSqvAttributes(element.Children, attributeName)) yield return value;
        }
    }

    private static bool IsColorProperty(string propertyName) =>
        propertyName.EndsWith("color", StringComparison.OrdinalIgnoreCase) ||
        propertyName is "background" or "border" or "outline" or "box-shadow";

    private static bool IsSelectorIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value is '-' or '_';

    private static string[] BorderStyles() =>
        ["none", "hidden", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset", "outset"];

    private static string[] DimensionValues() => ["auto", "0", "100%", "100px", "1rem", "100vw", "100vh"];

    private static string[] OverflowValues() => ["visible", "hidden", "scroll", "auto", "clip"];
}
