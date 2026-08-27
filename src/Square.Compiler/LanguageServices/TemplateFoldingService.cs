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
        if (document?.Syntax?.Template?.SqxSyntax != null)
            CollectSqxElements(document.Syntax.Template.SqxSyntax.Roots, text, ranges);
        else if (document?.Syntax?.Template?.SqvSyntax != null)
            CollectSqvElements(document.Syntax.Template.SqvSyntax.Roots, text, ranges);
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

    private static void CollectSqxElements(
        IEnumerable<SqxSyntaxNode> nodes,
        string text,
        List<TemplateFoldingRange> ranges)
    {
        foreach (var element in nodes.OfType<SqxElementSyntax>())
        {
            AddElementRange(element.Origin, element.IsSelfClosing, text, ranges);
            CollectSqxElements(element.Children, text, ranges);
        }
    }

    private static void CollectSqvElements(
        IEnumerable<SqvSyntaxNode> nodes,
        string text,
        List<TemplateFoldingRange> ranges)
    {
        foreach (var element in nodes.OfType<SqvElementSyntax>())
        {
            AddElementRange(element.Origin, element.IsSelfClosing, text, ranges);
            CollectSqvElements(element.Children, text, ranges);
        }
    }

    private static void AddElementRange(
        SquareSourceRange range,
        bool isSelfClosing,
        string text,
        List<TemplateFoldingRange> ranges)
    {
        if (isSelfClosing) return;
        ranges.Add(new TemplateFoldingRange(
            LineOf(text, range.Offset),
            LineOf(text, Math.Max(range.Offset, range.End - 1)),
            "region"));
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
