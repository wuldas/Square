using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Square.Compiler.LanguageServices;

/// <summary>
/// A source range expressed as a zero-based absolute offset and length.
/// </summary>
public readonly struct SquareSourceRange : IEquatable<SquareSourceRange>
{
    public SquareSourceRange(int offset, int length)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

        Offset = offset;
        Length = length;
    }

    public int Offset { get; }

    public int Length { get; }

    public int End => checked(Offset + Length);

    public TextSpan ToTextSpan(SourceText sourceText)
    {
        if (sourceText == null) throw new ArgumentNullException(nameof(sourceText));

        var start = Math.Min(Offset, sourceText.Length);
        var length = Math.Min(Length, sourceText.Length - start);
        return new TextSpan(start, length);
    }

    public LinePositionSpan ToLinePositionSpan(SourceText sourceText)
    {
        if (sourceText == null) throw new ArgumentNullException(nameof(sourceText));
        return sourceText.Lines.GetLinePositionSpan(ToTextSpan(sourceText));
    }

    public bool Equals(SquareSourceRange other) =>
        Offset == other.Offset && Length == other.Length;

    public override bool Equals(object obj) =>
        obj is SquareSourceRange other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (Offset * 397) ^ Length;
        }
    }

    public static bool operator ==(SquareSourceRange left, SquareSourceRange right) => left.Equals(right);

    public static bool operator !=(SquareSourceRange left, SquareSourceRange right) => !left.Equals(right);

    public override string ToString() => $"{Offset}..{End}";
}
