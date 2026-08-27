using Square.Compiler.Syntax;

namespace Square.Compiler.LanguageServices;

public static class TemplateDefinitionService
{
    public static string GetTagNameAt(string text, string sourcePath, int offset)
    {
        text ??= string.Empty;
        offset = Math.Max(0, Math.Min(offset, text.Length));
        var template = SquareDocumentService.ParseSyntaxTree(text, sourcePath ?? string.Empty)
            .ParsedSqxDocument?.Syntax?.Template;
        if (template?.SqxSyntax != null) return FindSqx(template.SqxSyntax.Roots, offset);
        if (template?.SqvSyntax != null) return FindSqv(template.SqvSyntax.Roots, offset);
        return null;
    }

    private static string FindSqx(IEnumerable<SqxSyntaxNode> nodes, int offset)
    {
        foreach (var element in nodes.OfType<SqxElementSyntax>())
        {
            if (ContainsName(element.TagName, element.Origin.Offset, offset)) return element.TagName;
            var child = FindSqx(element.Children, offset);
            if (child != null) return child;
        }
        return null;
    }

    private static string FindSqv(IEnumerable<SqvSyntaxNode> nodes, int offset)
    {
        foreach (var element in nodes.OfType<SqvElementSyntax>())
        {
            if (ContainsName(element.TagName, element.Origin.Offset, offset)) return element.TagName;
            var child = FindSqv(element.Children, offset);
            if (child != null) return child;
        }
        return null;
    }

    private static bool ContainsName(string tagName, int elementOffset, int offset)
    {
        var start = elementOffset + 1;
        return offset >= start && offset <= start + tagName.Length;
    }
}
