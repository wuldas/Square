using Microsoft.CodeAnalysis.Text;

namespace Square.Compiler.LanguageServices;

/// <summary>
/// A compiler/tooling diagnostic with a stable ID and source range.
/// </summary>
public sealed class SquareDiagnostic
{
    public SquareDiagnostic(
        string id,
        SquareDiagnosticSeverity severity,
        string message,
        SquareSourceRange range,
        string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Diagnostic ID is required.", nameof(id));
        if (message == null) throw new ArgumentNullException(nameof(message));
        if (sourcePath == null) throw new ArgumentNullException(nameof(sourcePath));

        Id = id;
        Severity = severity;
        Message = message;
        Range = range;
        SourcePath = sourcePath;
    }

    public string Id { get; }

    public SquareDiagnosticSeverity Severity { get; }

    public string Message { get; }

    public SquareSourceRange Range { get; }

    public string SourcePath { get; }

    public LinePositionSpan GetLinePositionSpan(SourceText sourceText) =>
        Range.ToLinePositionSpan(sourceText);
}
