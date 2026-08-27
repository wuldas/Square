using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal enum ScriptMetadataDiagnosticKind
{
    InvalidAttribute,
    UnknownAttribute,
    DuplicateAttribute,
    UnsupportedLanguage,
    InvalidAccess,
    InvalidNamespace,
    InvalidComponentName
}

internal sealed class ScriptMetadataDiagnostic
{
    public ScriptMetadataDiagnostic(
        ScriptMetadataDiagnosticKind kind,
        string message,
        SquareSourceRange range)
    {
        Kind = kind;
        Message = message ?? string.Empty;
        Range = range;
    }

    public ScriptMetadataDiagnosticKind Kind { get; }
    public string Message { get; }
    public SquareSourceRange Range { get; }
}

internal sealed class ScriptMetadataSyntax
{
    public ScriptMetadataSyntax(
        string language,
        string namespaceName,
        string componentName,
        string access,
        IReadOnlyList<ScriptAttributeSyntax> attributes,
        IReadOnlyList<ScriptMetadataDiagnostic> diagnostics)
    {
        Language = language ?? "csharp";
        Namespace = namespaceName;
        ComponentName = componentName;
        Access = access ?? "public";
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public string Language { get; }
    public string Namespace { get; }
    public string ComponentName { get; }
    public string Access { get; }
    public IReadOnlyList<ScriptAttributeSyntax> Attributes { get; }
    public IReadOnlyList<ScriptMetadataDiagnostic> Diagnostics { get; }
}
