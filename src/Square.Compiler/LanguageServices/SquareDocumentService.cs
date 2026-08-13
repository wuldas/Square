using System.Text;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler.Directives;
using Square.Compiler.Parser;

namespace Square.Compiler.LanguageServices;

/// <summary>
/// Shared SQX/SQV parsing entry point for the generator and language tooling.
/// </summary>
public static class SquareDocumentService
{
    public static SquareParseResult Parse(string source, string sourcePath)
    {
        var syntaxResult = ParseSyntax(source, sourcePath);
        if (!syntaxResult.IsSuccess)
            return syntaxResult;

        var diagnostics = DirectiveValidator.CollectDiagnostics(
            syntaxResult.SourcePath,
            syntaxResult.SourceText.ToString(),
            syntaxResult.ParsedSqxDocument,
            DirectiveCatalog.BuiltIn);

        return new SquareParseResult(
            syntaxResult.SourcePath,
            syntaxResult.SourceText,
            diagnostics,
            syntaxResult.ParsedDocument);
    }

    public static SquareParseResult ParseSyntaxTree(string source, string sourcePath)
    {
        source ??= string.Empty;
        sourcePath ??= string.Empty;
        var sourceText = SourceText.From(source, Encoding.UTF8);
        try
        {
            object document = sourcePath.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase)
                ? SqvParser.ParseTolerant(source, sourcePath)
                : SqxCoreParserFacade.Parse(source, sourcePath, strictTemplate: false, tolerant: true);
            return new SquareParseResult(sourcePath, sourceText, Array.Empty<SquareDiagnostic>(), document);
        }
        catch
        {
            return ParseSyntax(source, sourcePath);
        }
    }

    public static SquareParseResult ParseTolerant(string source, string sourcePath)
    {
        var strictResult = ParseSyntax(source, sourcePath);
        if (strictResult.IsSuccess)
            return Parse(source, sourcePath);

        try
        {
            object document = sourcePath.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase)
                ? SqvParser.ParseTolerant(source, sourcePath)
                : SqxCoreParserFacade.Parse(source, sourcePath, strictTemplate: false, tolerant: true);
            return new SquareParseResult(
                strictResult.SourcePath,
                strictResult.SourceText,
                strictResult.Diagnostics,
                document);
        }
        catch
        {
            return strictResult;
        }
    }

    internal static SquareParseResult ParseSyntax(string source, string sourcePath)
    {
        source ??= string.Empty;
        sourcePath ??= string.Empty;

        var sourceText = SourceText.From(source, Encoding.UTF8);
        var isSqv = sourcePath.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase);

        try
        {
            object document = isSqv
                ? SqvParser.Parse(source, sourcePath)
                : SqxParser.Parse(source, sourcePath);

            return new SquareParseResult(
                sourcePath,
                sourceText,
                Array.Empty<SquareDiagnostic>(),
                document);
        }
        catch (SqxParseException exception)
        {
            var diagnosticId = string.IsNullOrWhiteSpace(exception.DiagnosticId)
                ? isSqv ? "SQV0001" : "SQX0001"
                : exception.DiagnosticId;
            var position = Math.Max(0, Math.Min(exception.Position, sourceText.Length));
            var diagnostic = new SquareDiagnostic(
                diagnosticId,
                SquareDiagnosticSeverity.Error,
                exception.Message,
                new SquareSourceRange(position, 0),
                sourcePath);

            return new SquareParseResult(
                sourcePath,
                sourceText,
                new[] { diagnostic },
                null);
        }
    }
}
