namespace Square.Markup;

public sealed class SqxParseException : Exception
{
    public string DiagnosticId { get; }
    public string DiagnosticMessage { get; }
    public int Offset { get; }
    public int Line { get; }
    public int Column { get; }

    public SqxParseException(string message, int line, int column)
        : this(message, "SQX0001", -1, line, column)
    {
    }

    public SqxParseException(
        string message,
        string diagnosticId,
        int offset,
        int line,
        int column)
        : base($"{message} ({line}:{column})")
    {
        DiagnosticId = string.IsNullOrWhiteSpace(diagnosticId) ? "SQX0001" : diagnosticId;
        DiagnosticMessage = message;
        Offset = offset;
        Line = line;
        Column = column;
    }
}
