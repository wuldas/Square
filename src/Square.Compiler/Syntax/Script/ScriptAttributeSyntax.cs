using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal sealed class ScriptAttributeSyntax
{
    public ScriptAttributeSyntax(
        string name,
        string value,
        SquareSourceRange fullRange,
        SquareSourceRange nameRange,
        SquareSourceRange valueRange)
    {
        Name = name ?? string.Empty;
        Value = value ?? string.Empty;
        FullRange = fullRange;
        NameRange = nameRange;
        ValueRange = valueRange;
    }

    public string Name { get; }
    public string Value { get; }
    public SquareSourceRange FullRange { get; }
    public SquareSourceRange NameRange { get; }
    public SquareSourceRange ValueRange { get; }
}
