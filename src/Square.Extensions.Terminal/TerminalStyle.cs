namespace Square.Extensions.Terminal;

/// <summary>Identifies how a terminal color is encoded.</summary>
public enum TerminalColorKind
{
    /// <summary>Use the terminal's default foreground or background.</summary>
    Default,
    /// <summary>Use an entry from the ANSI 256-color palette.</summary>
    Indexed,
    /// <summary>Use an explicit RGB color.</summary>
    Rgb,
}

/// <summary>Represents a default, indexed, or 24-bit terminal color.</summary>
public readonly record struct TerminalColor
{
    private TerminalColor(TerminalColorKind kind, byte index, byte red, byte green, byte blue)
    {
        Kind = kind;
        Index = index;
        Red = red;
        Green = green;
        Blue = blue;
    }

    /// <summary>The color encoding.</summary>
    public TerminalColorKind Kind { get; }
    /// <summary>The ANSI palette index when <see cref="Kind"/> is <see cref="TerminalColorKind.Indexed"/>.</summary>
    public byte Index { get; }
    /// <summary>The red component when <see cref="Kind"/> is <see cref="TerminalColorKind.Rgb"/>.</summary>
    public byte Red { get; }
    /// <summary>The green component when <see cref="Kind"/> is <see cref="TerminalColorKind.Rgb"/>.</summary>
    public byte Green { get; }
    /// <summary>The blue component when <see cref="Kind"/> is <see cref="TerminalColorKind.Rgb"/>.</summary>
    public byte Blue { get; }

    /// <summary>The terminal default color.</summary>
    public static TerminalColor Default => default;
    /// <summary>Creates an ANSI indexed color.</summary>
    public static TerminalColor FromIndex(byte index) => new(TerminalColorKind.Indexed, index, 0, 0, 0);
    /// <summary>Creates a 24-bit RGB color.</summary>
    public static TerminalColor FromRgb(byte red, byte green, byte blue) =>
        new(TerminalColorKind.Rgb, 0, red, green, blue);
}

/// <summary>Visual attributes applied to a terminal cell.</summary>
public readonly record struct TerminalStyle(
    TerminalColor Foreground,
    TerminalColor Background,
    bool Bold = false,
    bool Dim = false,
    bool Italic = false,
    bool Underline = false,
    bool Inverse = false,
    bool Strike = false)
{
    /// <summary>The default terminal style.</summary>
    public static TerminalStyle Default => new(TerminalColor.Default, TerminalColor.Default);
}
