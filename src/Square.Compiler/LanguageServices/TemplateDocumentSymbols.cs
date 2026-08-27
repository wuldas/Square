using Square.Compiler.Syntax;

namespace Square.Compiler.LanguageServices;

public sealed class TemplateDocumentSymbol
{
    public TemplateDocumentSymbol(
        string name,
        string detail,
        SquareSourceRange range,
        SquareSourceRange selectionRange,
        IReadOnlyList<TemplateDocumentSymbol> children)
    {
        Name = name ?? string.Empty;
        Detail = detail ?? string.Empty;
        Range = range;
        SelectionRange = selectionRange;
        Children = children ?? throw new ArgumentNullException(nameof(children));
    }

    public string Name { get; }
    public string Detail { get; }
    public SquareSourceRange Range { get; }
    public SquareSourceRange SelectionRange { get; }
    public IReadOnlyList<TemplateDocumentSymbol> Children { get; }
}

public static class TemplateDocumentSymbols
{
    public static IReadOnlyList<TemplateDocumentSymbol> GetSymbols(string text, string sourcePath)
    {
        var document = SquareDocumentService.ParseSyntaxTree(text ?? string.Empty, sourcePath ?? string.Empty)
            .ParsedSqxDocument?.Syntax?.Template;
        if (document?.SqxSyntax != null) return CollectSqx(document.SqxSyntax.Roots);
        if (document?.SqvSyntax != null) return CollectSqv(document.SqvSyntax.Roots);
        return Array.Empty<TemplateDocumentSymbol>();
    }

    private static IReadOnlyList<TemplateDocumentSymbol> CollectSqx(IEnumerable<SqxSyntaxNode> nodes) =>
        nodes.OfType<SqxElementSyntax>()
            .Select(element => Create(element.TagName, element.Origin, CollectSqx(element.Children)))
            .ToArray();

    private static IReadOnlyList<TemplateDocumentSymbol> CollectSqv(IEnumerable<SqvSyntaxNode> nodes) =>
        nodes.OfType<SqvElementSyntax>()
            .Select(element => Create(element.TagName, element.Origin, CollectSqv(element.Children)))
            .ToArray();

    private static TemplateDocumentSymbol Create(
        string tagName,
        SquareSourceRange range,
        IReadOnlyList<TemplateDocumentSymbol> children) =>
        new(
            tagName,
            TemplateCatalog.BuiltIn.GetComponent(tagName).TypeName,
            range,
            new SquareSourceRange(range.Offset + 1, tagName.Length),
            children);
}
