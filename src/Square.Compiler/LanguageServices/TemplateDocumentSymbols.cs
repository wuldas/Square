using Microsoft.CodeAnalysis.CSharp.Syntax;
using Square.Compiler.Syntax;

namespace Square.Compiler.LanguageServices;

public sealed class TemplateDocumentSymbol
{
    public TemplateDocumentSymbol(
        string name,
        string detail,
        SquareSourceRange range,
        SquareSourceRange selectionRange,
        IReadOnlyList<TemplateDocumentSymbol> children,
        int kind = 19)
    {
        Name = name ?? string.Empty;
        Detail = detail ?? string.Empty;
        Range = range;
        SelectionRange = selectionRange;
        Children = children ?? throw new ArgumentNullException(nameof(children));
        Kind = kind;
    }

    public string Name { get; }
    public string Detail { get; }
    public SquareSourceRange Range { get; }
    public SquareSourceRange SelectionRange { get; }
    public IReadOnlyList<TemplateDocumentSymbol> Children { get; }
    public int Kind { get; }
}

public static class TemplateDocumentSymbols
{
    public static IReadOnlyList<TemplateDocumentSymbol> GetSymbols(
        string text,
        string sourcePath,
        TemplateCatalog catalog = null,
        TemplateResolutionContext resolutionContext = null)
    {
        catalog ??= TemplateCatalog.BuiltIn;
        resolutionContext ??= new TemplateResolutionContext(string.Empty, Array.Empty<string>());
        var document = SquareDocumentService.ParseSyntaxTree(text ?? string.Empty, sourcePath ?? string.Empty)
            .ParsedSqxDocument?.Syntax;
        if (document == null) return Array.Empty<TemplateDocumentSymbol>();
        var symbols = document.Template?.SqxSyntax != null
            ? CollectSqx(document.Template.SqxSyntax.Roots, catalog, resolutionContext).ToList()
            : document.Template?.SqvSyntax != null
                ? CollectSqv(document.Template.SqvSyntax.Roots, catalog, resolutionContext).ToList()
                : new List<TemplateDocumentSymbol>();
        if (document.Script?.CSharp != null) symbols.AddRange(CollectScript(document.Script.CSharp));
        return symbols;
    }

    private static IReadOnlyList<TemplateDocumentSymbol> CollectSqx(
        IEnumerable<SqxSyntaxNode> nodes,
        TemplateCatalog catalog,
        TemplateResolutionContext context) =>
        nodes.OfType<SqxElementSyntax>()
            .Select(element => Create(
                element.TagName,
                element.Origin,
                element.TagNameRange,
                CollectSqx(element.Children, catalog, context),
                catalog,
                context))
            .ToArray();

    private static IReadOnlyList<TemplateDocumentSymbol> CollectSqv(
        IEnumerable<SqvSyntaxNode> nodes,
        TemplateCatalog catalog,
        TemplateResolutionContext context) =>
        nodes.OfType<SqvElementSyntax>()
            .Select(element => Create(
                element.TagName,
                element.Origin,
                element.TagNameRange,
                CollectSqv(element.Children, catalog, context),
                catalog,
                context))
            .ToArray();

    private static TemplateDocumentSymbol Create(
        string tagName,
        SquareSourceRange range,
        SquareSourceRange tagNameRange,
        IReadOnlyList<TemplateDocumentSymbol> children,
        TemplateCatalog catalog,
        TemplateResolutionContext context)
    {
        var resolution = catalog.ResolveComponent(tagName, context);
        var selectionRange = tagNameRange.Length > 0
            ? tagNameRange
            : new SquareSourceRange(range.Offset + 1, tagName.Length);
        return new TemplateDocumentSymbol(
            tagName,
            resolution.Status == TemplateElementResolutionStatus.Resolved ? resolution.Component.TypeName : tagName,
            range,
            selectionRange,
            children);
    }

    private static IEnumerable<TemplateDocumentSymbol> CollectScript(CSharpScriptSyntax script)
    {
        foreach (var field in script.Members.OfType<FieldDeclarationSyntax>())
        {
            var range = script.SourceMap.ToDocumentRange(field.Span);
            foreach (var variable in field.Declaration.Variables)
            {
                yield return new TemplateDocumentSymbol(
                    variable.Identifier.ValueText,
                    field.Declaration.Type + " field",
                    range,
                    script.SourceMap.ToDocumentRange(variable.Identifier.Span),
                    Array.Empty<TemplateDocumentSymbol>(),
                    8);
            }
        }
        foreach (var property in script.Members.OfType<PropertyDeclarationSyntax>())
            yield return new TemplateDocumentSymbol(
                property.Identifier.ValueText,
                property.Type + " property",
                script.SourceMap.ToDocumentRange(property.Span),
                script.SourceMap.ToDocumentRange(property.Identifier.Span),
                Array.Empty<TemplateDocumentSymbol>(),
                7);
        foreach (var method in script.Members.OfType<MethodDeclarationSyntax>())
            yield return new TemplateDocumentSymbol(
                method.Identifier.ValueText,
                method.ReturnType + " " + method.Identifier.ValueText + method.ParameterList,
                script.SourceMap.ToDocumentRange(method.Span),
                script.SourceMap.ToDocumentRange(method.Identifier.Span),
                Array.Empty<TemplateDocumentSymbol>(),
                6);
    }
}
