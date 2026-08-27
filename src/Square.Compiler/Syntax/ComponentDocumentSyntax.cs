using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal sealed class ComponentDocumentSyntax
{
    public ComponentDocumentSyntax(
        ComponentDialect dialect,
        string sourcePath,
        string sourceText,
        TemplateSectionSyntax template,
        ScriptSectionSyntax script,
        StyleSectionSyntax style)
    {
        Dialect = dialect;
        SourcePath = sourcePath ?? string.Empty;
        SourceText = sourceText ?? string.Empty;
        Template = template;
        Script = script;
        Style = style;
    }

    public ComponentDialect Dialect { get; }
    public string SourcePath { get; }
    public string SourceText { get; }
    public TemplateSectionSyntax Template { get; }
    public ScriptSectionSyntax Script { get; }
    public StyleSectionSyntax Style { get; }
}

internal sealed class ComponentSectionDiagnostic
{
    public ComponentSectionDiagnostic(
        ComponentSectionDiagnosticKind kind,
        string message,
        SquareSourceRange range)
    {
        Kind = kind;
        Message = message ?? string.Empty;
        Range = range;
    }

    public ComponentSectionDiagnosticKind Kind { get; }
    public string Message { get; }
    public SquareSourceRange Range { get; }
}

internal sealed class ComponentSectionScanResult
{
    public ComponentSectionScanResult(
        ComponentDocumentSyntax document,
        IReadOnlyList<ComponentSectionDiagnostic> diagnostics)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public ComponentDocumentSyntax Document { get; }
    public IReadOnlyList<ComponentSectionDiagnostic> Diagnostics { get; }
}
