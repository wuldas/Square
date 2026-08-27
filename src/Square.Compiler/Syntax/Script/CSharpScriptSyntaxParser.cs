using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Square.Compiler.Syntax;

internal static class CSharpScriptSyntaxParser
{
    private const string WrapperPrefix =
        "\nnamespace __SquareScriptSyntax\n{\npartial class __Component\n{\n";
    private const string WrapperSuffix = "\n}\n}\n";

    public static CSharpScriptSyntax Parse(string content, int documentContentOffset)
    {
        content ??= string.Empty;
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        var originalTree = CSharpSyntaxTree.ParseText(content, options);
        var originalRoot = originalTree.GetCompilationUnitRoot();
        var usingLength = originalRoot.Usings.Count == 0
            ? 0
            : originalRoot.Usings[originalRoot.Usings.Count - 1].FullSpan.End;
        var usingText = content.Substring(0, usingLength);
        var bodyText = content.Substring(usingLength);
        var syntheticBodyStart = usingText.Length + WrapperPrefix.Length;
        var syntheticText = usingText + WrapperPrefix + bodyText + WrapperSuffix;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            syntheticText,
            options,
            "__SquareScriptSyntax.g.cs");
        var root = syntaxTree.GetCompilationUnitRoot();
        var component = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(declaration => declaration.Identifier.ValueText == "__Component");
        var sourceMap = new RoslynSourceMap(
            documentContentOffset,
            usingLength,
            usingLength,
            syntheticBodyStart,
            bodyText.Length);
        var diagnostics = syntaxTree.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => new CSharpScriptDiagnostic(
                diagnostic.Id,
                diagnostic.GetMessage(),
                sourceMap.ToDocumentRange(diagnostic.Location.SourceSpan)))
            .ToArray();
        return new CSharpScriptSyntax(
            syntaxTree,
            root,
            root.Usings.ToArray(),
            component.Members.ToArray(),
            bodyText,
            sourceMap,
            diagnostics);
    }
}
