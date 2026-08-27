using System.Text.RegularExpressions;
using Square.Compiler.Syntax;

namespace Square.Compiler.LanguageServices;

public enum TemplateCompletionKind
{
    None,
    Tag,
    Attribute,
    Event,
    Directive,
    CssClass
}

public sealed class TemplateCompletionContext
{
    public TemplateCompletionContext(
        TemplateCompletionKind kind,
        string prefix,
        string tagName,
        bool isSqv)
    {
        Kind = kind;
        Prefix = prefix ?? string.Empty;
        TagName = tagName ?? string.Empty;
        IsSqv = isSqv;
    }

    public TemplateCompletionKind Kind { get; }
    public string Prefix { get; }
    public string TagName { get; }
    public bool IsSqv { get; }
}

public sealed class TemplateCompletionItem
{
    public TemplateCompletionItem(string label, int kind, string detail, string insertText)
    {
        Label = label;
        Kind = kind;
        Detail = detail;
        InsertText = insertText;
    }

    public string Label { get; }
    public int Kind { get; }
    public string Detail { get; }
    public string InsertText { get; }
}

public static class TemplateCompletionService
{
    private static readonly string[] VueDirectives =
    {
        "v-if", "v-else-if", "v-else", "v-for", "v-show", "v-text",
        "v-model", "v-bind", "v-on", "v-slot"
    };

    public static TemplateCompletionContext GetContext(string text, int offset, string sourcePath)
    {
        text ??= string.Empty;
        offset = Math.Min(Math.Max(offset, 0), text.Length);
        var isSqv = sourcePath != null && sourcePath.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase);
        var result = SquareDocumentService.ParseSyntaxTree(text, sourcePath ?? string.Empty);
        var document = result.ParsedSqxDocument;
        var template = document?.Syntax?.Template;
        if (template != null &&
            (offset < template.ContentRange.Offset || offset > template.ContentRange.End))
            return new TemplateCompletionContext(TemplateCompletionKind.None, string.Empty, string.Empty, isSqv);
        if (template != null)
        {
            var element = template.SqxSyntax != null
                ? FindSqxElement(template.SqxSyntax.Roots, offset)
                : template.SqvSyntax != null
                    ? FindSqvElement(template.SqvSyntax.Roots, offset)
                    : null;
            if (element != null)
            {
                var headerEnd = FindHeaderEnd(text, element.Start, offset);
                if (headerEnd < 0 || offset <= headerEnd)
                    return ContextInTag(text, offset, element, isSqv);
            }
        }

        return ContextFromPrefix(text, offset, isSqv);
    }

    public static IReadOnlyList<TemplateCompletionItem> GetItems(
        string text,
        int offset,
        string sourcePath)
    {
        var context = GetContext(text, offset, sourcePath);
        return GetItems(context, text);
    }

    public static IReadOnlyList<TemplateCompletionItem> GetItems(
        TemplateCompletionContext context,
        string text)
    {
        if (context == null) return Array.Empty<TemplateCompletionItem>();

        switch (context.Kind)
        {
            case TemplateCompletionKind.Event:
                return Filter(
                    TemplateCatalog.BuiltIn.Events,
                    context.Prefix,
                    item => context.IsSqv ? item.Name : item.CanonicalName,
                    item =>
                    {
                        var name = context.IsSqv ? item.Name : item.CanonicalName;
                        return new TemplateCompletionItem(name, 23, "Square event", name);
                    });
            case TemplateCompletionKind.Directive:
                return VueDirectives
                    .Where(name => name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(name => new TemplateCompletionItem(name, 14, "Vue directive", name))
                    .ToArray();
            case TemplateCompletionKind.CssClass:
                return ExtractCssClassNames(text)
                    .Where(name => name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(name => new TemplateCompletionItem(name, 12, "CSS class", name))
                    .ToArray();
            case TemplateCompletionKind.Attribute:
                return Filter(
                    TemplateCatalog.BuiltIn.Properties,
                    context.Prefix,
                    item => item.Name,
                    item => new TemplateCompletionItem(item.Name, 10, item.CanonicalName, item.Name));
            case TemplateCompletionKind.Tag:
                return Filter(
                    TemplateCatalog.BuiltIn.Components,
                    context.Prefix,
                    item => item.TagName,
                    item => new TemplateCompletionItem(
                        item.TagName,
                        item.IsBuiltIn ? 7 : 14,
                        item.TypeName,
                        item.TagName));
            default:
                return Array.Empty<TemplateCompletionItem>();
        }
    }

    private static IReadOnlyList<TemplateCompletionItem> Filter<T>(
        IEnumerable<T> source,
        string prefix,
        Func<T, string> name,
        Func<T, TemplateCompletionItem> map)
    {
        return source
            .Where(item => name(item).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(map)
            .ToArray();
    }

    private static TemplateCompletionContext ContextInTag(
        string text,
        int offset,
        TemplateElementContext element,
        bool isSqv)
    {
        var nameStart = element.Start + 1;
        var nameEnd = nameStart + element.TagName.Length;
        if (offset <= nameEnd)
        {
            var prefix = SafeSlice(text, nameStart, offset);
            return new TemplateCompletionContext(TemplateCompletionKind.Tag, prefix, element.TagName, isSqv);
        }

        if (TryGetClassPrefix(text, offset, out var classPrefix))
            return new TemplateCompletionContext(TemplateCompletionKind.CssClass, classPrefix, element.TagName, isSqv);

        var token = GetTokenPrefix(text, offset, out var tokenStart);
        if (isSqv && tokenStart > 0 && text[tokenStart - 1] == '@')
            return new TemplateCompletionContext(TemplateCompletionKind.Event, token, element.TagName, isSqv);
        if (!isSqv && token.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            return new TemplateCompletionContext(TemplateCompletionKind.Event, token, element.TagName, false);
        if (isSqv && (token.StartsWith("v-", StringComparison.OrdinalIgnoreCase) ||
                      token.StartsWith(":", StringComparison.Ordinal) ||
                      token.StartsWith("#", StringComparison.Ordinal)))
            return new TemplateCompletionContext(TemplateCompletionKind.Directive, token, element.TagName, isSqv);

        return new TemplateCompletionContext(TemplateCompletionKind.Attribute, token, element.TagName, isSqv);
    }

    private static TemplateCompletionContext ContextFromPrefix(string text, int offset, bool isSqv)
    {
        var token = GetTokenPrefix(text, offset, out var tokenStart);
        if (isSqv && tokenStart > 0 && text[tokenStart - 1] == '@')
            return new TemplateCompletionContext(TemplateCompletionKind.Event, token, string.Empty, isSqv);
        if (!isSqv && token.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            return new TemplateCompletionContext(TemplateCompletionKind.Event, token, string.Empty, false);
        if (tokenStart > 0 && text[tokenStart - 1] == '<')
            return new TemplateCompletionContext(TemplateCompletionKind.Tag, token, string.Empty, isSqv);
        if (TryGetClassPrefix(text, offset, out var classPrefix))
            return new TemplateCompletionContext(TemplateCompletionKind.CssClass, classPrefix, string.Empty, isSqv);
        if (isSqv && token.StartsWith("v-", StringComparison.OrdinalIgnoreCase))
            return new TemplateCompletionContext(TemplateCompletionKind.Directive, token, string.Empty, isSqv);
        return new TemplateCompletionContext(TemplateCompletionKind.None, token, string.Empty, isSqv);
    }

    private static TemplateElementContext FindSqxElement(IEnumerable<SqxSyntaxNode> nodes, int offset)
    {
        foreach (var element in nodes.OfType<SqxElementSyntax>())
        {
            if (offset < element.Origin.Offset || offset > element.Origin.End) continue;
            return FindSqxElement(element.Children, offset) ??
                   new TemplateElementContext(element.TagName, element.Origin.Offset);
        }
        return null;
    }

    private static TemplateElementContext FindSqvElement(IEnumerable<SqvSyntaxNode> nodes, int offset)
    {
        foreach (var element in nodes.OfType<SqvElementSyntax>())
        {
            if (offset < element.Origin.Offset || offset > element.Origin.End) continue;
            return FindSqvElement(element.Children, offset) ??
                   new TemplateElementContext(element.TagName, element.Origin.Offset);
        }
        return null;
    }

    private static int FindHeaderEnd(string text, int elementStart, int offset)
    {
        var start = Math.Min(Math.Max(elementStart, 0), text.Length);
        var limit = Math.Min(text.Length, Math.Max(offset, start));
        var quote = '\0';
        for (var index = start; index < limit; index++)
        {
            var value = text[index];
            if (quote != '\0')
            {
                if (value == quote) quote = '\0';
                continue;
            }
            if (value is '"' or '\'') quote = value;
            else if (value == '>') return index;
        }
        return -1;
    }

    private sealed class TemplateElementContext
    {
        public TemplateElementContext(string tagName, int start)
        {
            TagName = tagName ?? string.Empty;
            Start = start;
        }

        public string TagName { get; }
        public int Start { get; }
    }

    private static string GetTokenPrefix(string text, int offset, out int start)
    {
        start = offset;
        while (start > 0 && IsTokenCharacter(text[start - 1])) start--;
        return SafeSlice(text, start, offset);
    }

    private static bool TryGetClassPrefix(string text, int offset, out string prefix)
    {
        var start = offset;
        while (start > 0 && IsTokenCharacter(text[start - 1])) start--;

        var quote = start - 1;
        while (quote >= 0 && text[quote] is not ('"' or '\'')) quote--;
        if (quote < 0)
        {
            prefix = string.Empty;
            return false;
        }

        var tagStart = text.LastIndexOf('<', quote);
        var tagEnd = text.LastIndexOf('>', quote);
        if (tagStart <= tagEnd)
        {
            prefix = string.Empty;
            return false;
        }

        var beforeQuote = text.Substring(tagStart + 1, quote - tagStart - 1);
        if (!Regex.IsMatch(beforeQuote, @"\bclass\s*=\s*$", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(beforeQuote, @"(?::class|v-bind:class)\s*=\s*$", RegexOptions.IgnoreCase))
        {
            prefix = string.Empty;
            return false;
        }

        prefix = SafeSlice(text, start, offset);
        return true;
    }

    private static IReadOnlyCollection<string> ExtractCssClassNames(string text)
    {
        var document = SquareDocumentService.ParseSyntaxTree(text, "Styles.sqx").ParsedSqxDocument;
        var style = document?.Syntax?.Style?.Css;
        if (style == null) return Array.Empty<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selector in style.Rules.SelectMany(rule => rule.Selectors))
        {
            var value = selector.Text;
            for (var index = 0; index + 1 < value.Length; index++)
            {
                if (value[index] != '.' || !IsCssIdentifierStart(value[index + 1])) continue;
                var start = ++index;
                while (index + 1 < value.Length && IsCssIdentifierPart(value[index + 1])) index++;
                names.Add(value.Substring(start, index - start + 1));
            }
        }
        return names;
    }

    private static bool IsCssIdentifierStart(char value) =>
        char.IsLetter(value) || value is '_' or '-';

    private static bool IsCssIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-';

    private static bool IsTokenCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '-' or '_' or ':' or '#';

    private static string SafeSlice(string text, int start, int end)
    {
        start = Math.Min(Math.Max(start, 0), text.Length);
        end = Math.Min(Math.Max(end, start), text.Length);
        return text.Substring(start, end - start);
    }
}
