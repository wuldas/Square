namespace Square.Extensions.Terminal;

/// <summary>A character and its visual style in the terminal grid.</summary>
public readonly record struct TerminalCell(char Character, TerminalStyle Style)
{
    /// <summary>Creates a blank cell using the supplied style.</summary>
    public static TerminalCell Blank(TerminalStyle style) => new(' ', style);
}
