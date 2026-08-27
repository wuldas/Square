using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal sealed class CSharpScriptDiagnostic
{
    public CSharpScriptDiagnostic(string id, string message, SquareSourceRange range)
    {
        Id = id ?? string.Empty;
        Message = message ?? string.Empty;
        Range = range;
    }

    public string Id { get; }
    public string Message { get; }
    public SquareSourceRange Range { get; }
}

internal sealed class CSharpScriptSyntax
{
    public CSharpScriptSyntax(
        SyntaxTree syntaxTree,
        CompilationUnitSyntax root,
        IReadOnlyList<UsingDirectiveSyntax> usings,
        IReadOnlyList<MemberDeclarationSyntax> members,
        string bodyText,
        RoslynSourceMap sourceMap,
        IReadOnlyList<CSharpScriptDiagnostic> diagnostics)
    {
        SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Usings = usings ?? throw new ArgumentNullException(nameof(usings));
        Members = members ?? throw new ArgumentNullException(nameof(members));
        BodyText = bodyText ?? string.Empty;
        SourceMap = sourceMap ?? throw new ArgumentNullException(nameof(sourceMap));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public SyntaxTree SyntaxTree { get; }
    public CompilationUnitSyntax Root { get; }
    public IReadOnlyList<UsingDirectiveSyntax> Usings { get; }
    public IReadOnlyList<MemberDeclarationSyntax> Members { get; }
    public string BodyText { get; }
    public RoslynSourceMap SourceMap { get; }
    public IReadOnlyList<CSharpScriptDiagnostic> Diagnostics { get; }
}
