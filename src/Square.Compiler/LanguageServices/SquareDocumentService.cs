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
    private const int SyntaxTreeCacheCapacity = 128;
    private static readonly object SyntaxTreeCacheGate = new();
    private static readonly Dictionary<string, SyntaxTreeCacheEntry> SyntaxTreeCache =
        new(StringComparer.Ordinal);
    private static readonly LinkedList<string> SyntaxTreeCacheOrder = new();

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

        var result = new SquareParseResult(
            syntaxResult.SourcePath,
            syntaxResult.SourceText,
            diagnostics,
            syntaxResult.ParsedDocument);
        StoreSyntaxTree(
            source ?? string.Empty,
            syntaxResult.SourcePath,
            new SquareParseResult(
                syntaxResult.SourcePath,
                syntaxResult.SourceText,
                Array.Empty<SquareDiagnostic>(),
                syntaxResult.ParsedDocument));
        return result;
    }

    public static SquareParseResult ParseSyntaxTree(string source, string sourcePath)
    {
        source ??= string.Empty;
        sourcePath ??= string.Empty;
        lock (SyntaxTreeCacheGate)
        {
            if (SyntaxTreeCache.TryGetValue(sourcePath, out var cached) &&
                cached.Source.Equals(source, StringComparison.Ordinal))
            {
                SyntaxTreeCacheOrder.Remove(cached.Node);
                SyntaxTreeCacheOrder.AddFirst(cached.Node);
                return cached.Result;
            }
        }

        var sourceText = SourceText.From(source, Encoding.UTF8);
        SquareParseResult result;
        try
        {
            object document = sourcePath.EndsWith(".sqv", StringComparison.OrdinalIgnoreCase)
                ? SqvParser.ParseTolerant(source, sourcePath)
                : SqxCoreParserFacade.Parse(source, sourcePath, strictTemplate: false, tolerant: true);
            result = new SquareParseResult(sourcePath, sourceText, Array.Empty<SquareDiagnostic>(), document);
        }
        catch
        {
            result = ParseSyntax(source, sourcePath);
        }

        StoreSyntaxTree(source, sourcePath, result);
        return result;
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

    internal static void InvalidateSyntaxTree(string sourcePath)
    {
        sourcePath ??= string.Empty;
        lock (SyntaxTreeCacheGate)
        {
            if (!SyntaxTreeCache.TryGetValue(sourcePath, out var cached)) return;
            SyntaxTreeCacheOrder.Remove(cached.Node);
            SyntaxTreeCache.Remove(sourcePath);
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
            var length = Math.Max(0, Math.Min(exception.Length, sourceText.Length - position));
            var diagnostic = new SquareDiagnostic(
                diagnosticId,
                SquareDiagnosticSeverity.Error,
                exception.Message,
                new SquareSourceRange(position, length),
                sourcePath);

            return new SquareParseResult(
                sourcePath,
                sourceText,
                new[] { diagnostic },
                null);
        }
    }

    private static void StoreSyntaxTree(string source, string sourcePath, SquareParseResult result)
    {
        lock (SyntaxTreeCacheGate)
        {
            if (SyntaxTreeCache.TryGetValue(sourcePath, out var previous))
            {
                SyntaxTreeCacheOrder.Remove(previous.Node);
                SyntaxTreeCache.Remove(sourcePath);
            }
            var node = SyntaxTreeCacheOrder.AddFirst(sourcePath);
            SyntaxTreeCache[sourcePath] = new SyntaxTreeCacheEntry(source, result, node);
            while (SyntaxTreeCache.Count > SyntaxTreeCacheCapacity)
            {
                var last = SyntaxTreeCacheOrder.Last;
                if (last == null) break;
                SyntaxTreeCacheOrder.RemoveLast();
                SyntaxTreeCache.Remove(last.Value);
            }
        }
    }

    private sealed class SyntaxTreeCacheEntry
    {
        public SyntaxTreeCacheEntry(string source, SquareParseResult result, LinkedListNode<string> node)
        {
            Source = source;
            Result = result;
            Node = node;
        }

        public string Source { get; }
        public SquareParseResult Result { get; }
        public LinkedListNode<string> Node { get; }
    }
}
