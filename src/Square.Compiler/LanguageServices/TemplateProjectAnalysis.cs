using Square.Compiler.LanguageServices;
using Square.Compiler.Parser;
using Square.Compiler.Template.Ir;

namespace Square.Compiler.LanguageServices;

internal enum TemplateGeneratedSourceKind
{
    EventHandler,
    PropertyBinding,
    SlotBinding,
    Script
}

internal sealed class TemplateGeneratedSourceMapping
{
    internal TemplateGeneratedSourceMapping(
        TemplateGeneratedSourceKind kind,
        string sourcePath,
        Microsoft.CodeAnalysis.Text.TextSpan generatedSpan,
        SquareSourceRange sourceRange)
    {
        Kind = kind;
        SourcePath = sourcePath ?? string.Empty;
        GeneratedSpan = generatedSpan;
        SourceRange = sourceRange;
    }

    internal TemplateGeneratedSourceKind Kind { get; }
    internal string SourcePath { get; }
    internal Microsoft.CodeAnalysis.Text.TextSpan GeneratedSpan { get; }
    internal SquareSourceRange SourceRange { get; }
}

internal sealed class TemplateDocumentAnalysis
{
    internal TemplateDocumentAnalysis(
        SqxDocument document,
        TemplateResolutionContext context,
        IReadOnlyDictionary<string, TemplateElementResolution> resolutions,
        bool canEmit)
    {
        Document = document;
        Context = context;
        Resolutions = resolutions ?? new Dictionary<string, TemplateElementResolution>(StringComparer.Ordinal);
        CanEmit = canEmit;
    }

    internal SqxDocument Document { get; }
    internal TemplateResolutionContext Context { get; }
    internal IReadOnlyDictionary<string, TemplateElementResolution> Resolutions { get; }
    internal bool CanEmit { get; }
}

internal sealed class TemplateProjectAnalysis
{
    internal TemplateProjectAnalysis(
        TemplateCatalog catalog,
        Microsoft.CodeAnalysis.Compilation outputCompilation,
        IReadOnlyDictionary<string, string> generatedSources,
        IReadOnlyList<SquareDiagnostic> diagnostics,
        IReadOnlyDictionary<string, TemplateDocumentAnalysis> documents,
        IReadOnlyList<TemplateGeneratedSourceMapping> sourceMappings)
    {
        Catalog = catalog;
        OutputCompilation = outputCompilation;
        GeneratedSources = generatedSources;
        Diagnostics = diagnostics;
        Documents = documents;
        SourceMappings = sourceMappings;
    }

    internal TemplateCatalog Catalog { get; }
    internal Microsoft.CodeAnalysis.Compilation OutputCompilation { get; }
    internal IReadOnlyDictionary<string, string> GeneratedSources { get; }
    internal IReadOnlyList<SquareDiagnostic> Diagnostics { get; }
    internal IReadOnlyDictionary<string, TemplateDocumentAnalysis> Documents { get; }
    internal IReadOnlyList<TemplateGeneratedSourceMapping> SourceMappings { get; }
}
