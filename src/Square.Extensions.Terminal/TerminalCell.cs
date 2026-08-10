namespace Square.Extensions.Terminal;

/// <summary>A grapheme and its visual style in the terminal grid.</summary>
public readonly record struct TerminalCell
{
    /// <summary>Creates a single-column character cell.</summary>
    public TerminalCell(char character, TerminalStyle style) : this(character.ToString(), style, 1) { }

    /// <summary>Creates a terminal cell.</summary>
    public TerminalCell(string text, TerminalStyle style, byte columnSpan)
    {
        Text = text;
        Style = style;
        ColumnSpan = columnSpan;
    }

    /// <summary>Text stored by a leading cell. Continuation cells contain an empty string.</summary>
    public string Text { get; }
    /// <summary>Visual style for this physical grid cell.</summary>
    public TerminalStyle Style { get; }
    /// <summary>Zero for a continuation cell, otherwise the number of occupied columns.</summary>
    public byte ColumnSpan { get; }
    /// <summary>Compatibility accessor for single-character callers.</summary>
    public char Character => Text.Length == 0 ? '\0' : Text[0];
    /// <summary>Whether this cell continues the wide grapheme to its left.</summary>
    public bool IsContinuation => ColumnSpan == 0;
    /// <summary>Whether this is an ordinary blank cell.</summary>
    public bool IsBlank => ColumnSpan == 1 && Text == " ";

    /// <summary>Creates a blank cell using the supplied style.</summary>
    public static TerminalCell Blank(TerminalStyle style) => new(" ", style, 1);
    /// <summary>Creates a leading cell.</summary>
    public static TerminalCell Lead(string text, TerminalStyle style, int columnSpan) =>
        new(text, style, checked((byte)columnSpan));
    /// <summary>Creates the second physical cell occupied by a wide grapheme.</summary>
    public static TerminalCell Continuation(TerminalStyle style) => new("", style, 0);
}
