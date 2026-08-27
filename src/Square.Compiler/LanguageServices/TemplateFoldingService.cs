using Square.Compiler.Parser;
using Square.Compiler.Syntax;

namespace Square.Compiler.LanguageServices;

public sealed class TemplateFoldingRange
{
    public TemplateFoldingRange(int startLine, int endLine, string kind)
    {
        StartLine = startLine;
        EndLine = endLine;
        Kind = kind;
    }

    public int StartLine { get; }
    public int EndLine { get; }
    public string Kind { get; }
}

public static class TemplateFoldingService
{
    public static IReadOnlyList<TemplateFoldingRange> GetRanges(string text, string sourcePath)
    {
        text ??= string.Empty;
        var ranges = new List<TemplateFoldingRange>();
        var document = SquareDocumentService.ParseSyntaxTree(text, sourcePath ?? string.Empty).ParsedSqxDocument;
        AddSectionRanges(text, document?.Syntax, ranges);
        if (document?.Template != null)
            CollectElements(document.Template.Roots, text, ranges);
        if (document?.Syntax?.Style?.Css != null)
            CollectStyleRanges(document.Syntax.Style.Css, text, ranges);

        return ranges
            .Where(range => range.EndLine > range.StartLine)
            .GroupBy(range => range.StartLine + ":" + range.EndLine + ":" + range.Kind)
            .Select(group => group.First())
            .OrderBy(range => range.StartLine)
            .ThenBy(range => range.EndLine)
            .ToArray();
    }

    private static void AddSectionRanges(
        string text,
        ComponentDocumentSyntax document,
        List<TemplateFoldingRange> ranges)
    {
        if (document == null) return;
        foreach (var section in new ComponentSectionSyntax[]
                 { document.Template, document.Script, document.Style })
        {
            if (section == null || !section.IsClosed) continue;
            ranges.Add(new TemplateFoldingRange(
                LineOf(text, section.OpeningTagRange.Offset),
                LineOf(text, section.ClosingTagRange.Offset),
                "region"));
        }
    }

    private static void CollectStyleRanges(
        CssStyleSheetSyntax style,
        string text,
        List<TemplateFoldingRange> ranges)
    {
        foreach (var rule in style.Rules) AddRange(rule.FullRange);
        foreach (var atRule in style.AtRules) CollectAtRule(atRule);

        void CollectAtRule(CssAtRuleSyntax atRule)
        {
            AddRange(atRule.FullRange);
            foreach (var rule in atRule.Rules) AddRange(rule.FullRange);
            foreach (var child in atRule.AtRules) CollectAtRule(child);
        }

        void AddRange(SquareSourceRange range)
        {
            ranges.Add(new TemplateFoldingRange(
                LineOf(text, range.Offset),
                LineOf(text, Math.Max(range.Offset, range.End - 1)),
                "region"));
        }
    }

    private static void CollectElements(
        IEnumerable<SqxNode> nodes,
        string text,
        List<TemplateFoldingRange> ranges)
    {
        var siblings = nodes.OfType<SqxElement>().ToArray();
        for (var index = 0; index < siblings.Length; index++)
        {
            var element = siblings[index];
            var next = index + 1 < siblings.Length ? siblings[index + 1].Position : text.Length;
            var close = FindClosingTag(text, element, next);
            if (close > element.Position)
                ranges.Add(new TemplateFoldingRange(LineOf(text, element.Position), LineOf(text, close), "region"));
            CollectElements(element.Children, text, ranges);
        }

        foreach (var node in nodes)
        {
            if (node is TemplateForDirective forDirective)
                CollectElements(forDirective.Children, text, ranges);
            else if (node is TemplateIfChainDirective ifChain)
            {
                foreach (var branch in ifChain.Branches)
                    CollectElements(branch.Children, text, ranges);
            }
        }
    }

    private static int FindClosingTag(string text, SqxElement element, int limit)
    {
        var close = IndexOfTag(text, "</" + element.TagName, element.Position + element.TagName.Length + 1);
        if (close >= 0 && close < limit) return close;

        var headerEnd = text.IndexOf('>', element.Position);
        return headerEnd >= 0 && headerEnd < limit ? headerEnd : element.Position;
    }

    private static int IndexOfTag(string text, string prefix, int start)
    {
        var index = start;
        while (index < text.Length)
        {
            var at = text.IndexOf(prefix, index, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;
            var after = at + prefix.Length;
            if (after >= text.Length || text[after] is '>' or '/' || char.IsWhiteSpace(text[after]))
                return at;
            index = at + 1;
        }
        return -1;
    }

    private static int LineOf(string text, int offset)
    {
        var line = 0;
        offset = Math.Min(Math.Max(offset, 0), text.Length);
        for (var index = 0; index < offset; index++)
            if (text[index] == '\n') line++;
        return line;
    }
}
