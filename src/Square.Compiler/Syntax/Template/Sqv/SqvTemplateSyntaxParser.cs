using Square.Compiler.LanguageServices;
using Square.Compiler.Parser;

namespace Square.Compiler.Syntax;

internal sealed class SqvTemplateSyntaxParser
{
    private readonly List<SqvToken> _tokens;
    private readonly string _source;
    private readonly int _baseOffset;
    private readonly bool _tolerant;
    private int _index;

    private SqvTemplateSyntaxParser(List<SqvToken> tokens, string source, int baseOffset, bool tolerant)
    {
        _tokens = tokens;
        _source = source;
        _baseOffset = baseOffset;
        _tolerant = tolerant;
    }

    public static SqvTemplateSyntax Parse(string source, int baseOffset = 0, bool tolerant = false)
    {
        var tokens = new SqvLexer(source ?? string.Empty, baseOffset, tolerant).Tokenize();
        return new SqvTemplateSyntaxParser(tokens, source ?? string.Empty, baseOffset, tolerant).ParseDocument();
    }

    private SqvTemplateSyntax ParseDocument()
    {
        var roots = new List<SqvSyntaxNode>();
        while (Peek().Type != SqvTokenType.Eof)
        {
            var node = ParseNode();
            if (node != null) roots.Add(node);
        }
        return new SqvTemplateSyntax(roots.ToArray());
    }

    private SqvSyntaxNode ParseNode()
    {
        var token = Peek();
        switch (token.Type)
        {
            case SqvTokenType.OpenTag:
                return ParseElement();
            case SqvTokenType.Text:
                _index++;
                return new SqvTextSyntax(
                    token.Text,
                    new SquareSourceRange(Absolute(token.Offset), token.Text.Length));
            case SqvTokenType.Interpolation:
                _index++;
                return new SqvInterpolationSyntax(
                    token.Text,
                    new SquareSourceRange(Absolute(token.Offset), GetInterpolationLength(token.Offset)));
            default:
                if (!_tolerant && token.Type == SqvTokenType.EndTag)
                    throw Error("Unexpected closing tag </" + token.Text + ">", token.Offset);
                _index++;
                return null;
        }
    }

    private SqvElementSyntax ParseElement()
    {
        var open = Expect(SqvTokenType.OpenTag);
        var name = Expect(SqvTokenType.Identifier);
        var attributes = new List<SqvAttributeSyntax>();
        while (Peek().Type is not (SqvTokenType.CloseTag or SqvTokenType.CloseSelfTag or SqvTokenType.Eof))
        {
            var attribute = ParseAttribute();
            if (attribute != null) attributes.Add(attribute);
        }

        if (Peek().Type == SqvTokenType.CloseSelfTag)
        {
            var close = Next();
            return new SqvElementSyntax(
                name.Text,
                attributes.ToArray(),
                Array.Empty<SqvSyntaxNode>(),
                true,
                new SquareSourceRange(Absolute(open.Offset), close.Offset + 2 - open.Offset));
        }

        Expect(SqvTokenType.CloseTag);
        var children = new List<SqvSyntaxNode>();
        while (Peek().Type != SqvTokenType.Eof)
        {
            if (Peek().Type == SqvTokenType.EndTag)
            {
                var end = Next();
                if (!string.Equals(end.Text, name.Text, StringComparison.OrdinalIgnoreCase) && !_tolerant)
                    throw Error("Closing tag </" + end.Text + "> does not match <" + name.Text + ">", end.Offset);
                return new SqvElementSyntax(
                    name.Text,
                    attributes.ToArray(),
                    children.ToArray(),
                    false,
                    new SquareSourceRange(
                        Absolute(open.Offset),
                        end.Offset + end.Text.Length + 3 - open.Offset));
            }
            var child = ParseNode();
            if (child != null) children.Add(child);
        }

        if (!_tolerant) throw Error("Unclosed element <" + name.Text + ">", open.Offset);
        var lastEnd = children.Count == 0 ? name.Offset + name.Text.Length : children[children.Count - 1].Origin.End - _baseOffset;
        return new SqvElementSyntax(
            name.Text,
            attributes.ToArray(),
            children.ToArray(),
            false,
            new SquareSourceRange(Absolute(open.Offset), Math.Max(0, lastEnd - open.Offset)));
    }

    private SqvAttributeSyntax ParseAttribute()
    {
        if (Peek().Type != SqvTokenType.Identifier)
        {
            _index++;
            return null;
        }
        var name = Next();
        var nameRange = new SquareSourceRange(Absolute(name.Offset), name.Text.Length);
        if (Peek().Type != SqvTokenType.Equals)
            return new SqvAttributeSyntax(
                name.Text,
                null,
                nameRange,
                nameRange,
                new SquareSourceRange(nameRange.End, 0));

        _index++;
        var value = Peek();
        if (value.Type is not (SqvTokenType.StringLiteral or SqvTokenType.Identifier))
            return new SqvAttributeSyntax(
                name.Text,
                null,
                nameRange,
                nameRange,
                new SquareSourceRange(nameRange.End, 0));
        _index++;
        var quoted = value.Type == SqvTokenType.StringLiteral;
        var valueRange = new SquareSourceRange(
            Absolute(value.Offset) + (quoted ? 1 : 0),
            value.Text.Length);
        var fullEnd = valueRange.End + (quoted ? 1 : 0);
        return new SqvAttributeSyntax(
            name.Text,
            value.Text,
            new SquareSourceRange(nameRange.Offset, fullEnd - nameRange.Offset),
            nameRange,
            valueRange);
    }

    private SqvToken Peek() => _tokens[Math.Min(_index, _tokens.Count - 1)];

    private SqvToken Next() => _tokens[_index++];

    private SqvToken Expect(SqvTokenType type)
    {
        var token = Peek();
        if (token.Type == type) return Next();
        throw Error("Expected " + type + " but got " + token.Type, token.Offset);
    }

    private int Absolute(int offset) => _baseOffset + offset;

    private int GetInterpolationLength(int offset)
    {
        var close = _source.IndexOf("}}", offset + 2, StringComparison.Ordinal);
        return close < 0 ? _source.Length - offset : close + 2 - offset;
    }

    private SqxParseException Error(string message, int offset) =>
        new(message, Absolute(offset), "SQV0001");
}
