using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler.Parser;

namespace Square.Compiler.LanguageServices;

/// <summary>
/// The result of analyzing one SQX/SQV document.
/// </summary>
public sealed class SquareParseResult
{
    public SquareParseResult(
        string sourcePath,
        SourceText sourceText,
        IEnumerable<SquareDiagnostic> diagnostics)
        : this(sourcePath, sourceText, diagnostics, null)
    {
    }

    internal SquareParseResult(
        string sourcePath,
        SourceText sourceText,
        IEnumerable<SquareDiagnostic> diagnostics,
        object parsedDocument)
    {
        if (sourcePath == null) throw new ArgumentNullException(nameof(sourcePath));
        if (sourceText == null) throw new ArgumentNullException(nameof(sourceText));
        if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));

        SourcePath = sourcePath;
        SourceText = sourceText;
        Diagnostics = ImmutableArray.CreateRange(diagnostics);
        ParsedDocument = parsedDocument;
    }

    public string SourcePath { get; }

    public SourceText SourceText { get; }

    public ImmutableArray<SquareDiagnostic> Diagnostics { get; }

    public bool IsSuccess => Diagnostics.IsDefaultOrEmpty;

    internal object ParsedDocument { get; }

    internal SqxDocument ParsedSqxDocument => ParsedDocument as SqxDocument;
}
