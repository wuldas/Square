using Square.Compiler.LanguageServices;
using Square.Compiler.Parser;
using Square.Compiler.ParserCore;

namespace Square.Compiler.Syntax;

internal sealed class SqxTemplateSyntaxParser
{
    private readonly List<CoreToken> _tokens;
    private readonly string _source;
    private readonly int _baseOffset;
    private readonly bool _tolerant;
    private readonly List<SquareDiagnostic> _diagnostics = new();
    private int _index;

    private SqxTemplateSyntaxParser(List<CoreToken> tokens, string source, int baseOffset, bool tolerant)
    {
        _tokens = tokens;
        _source = source;
        _baseOffset = baseOffset;
        _tolerant = tolerant;
    }

    public static SqxTemplateSyntax Parse(string source, int baseOffset = 0, bool tolerant = false)
    {
        var tokens = new SqxCoreLexer(source ?? string.Empty, tolerant).Tokenize();
        return new SqxTemplateSyntaxParser(tokens, source ?? string.Empty, baseOffset, tolerant).ParseDocument();
    }

    private SqxTemplateSyntax ParseDocument()
    {
        var roots = new List<SqxSyntaxNode>();
        while (Peek().Type != CoreTokenType.Eof)
        {
            var node = ParseNode();
            if (node != null) roots.Add(node);
        }
        return new SqxTemplateSyntax(roots.ToArray(), _diagnostics);
    }

    private SqxSyntaxNode ParseNode()
    {
        var token = Peek();
        switch (token.Type)
        {
            case CoreTokenType.OpenTag:
                return ParseElement();
            case CoreTokenType.Text:
                _index++;
                return new SqxTextSyntax(token.Text, Range(token.Offset, token.Text.Length));
            case CoreTokenType.OpenBraceExpr:
                _index++;
                return new SqxExpressionSyntax(token.Text, Range(token.Offset, GetExpressionLength(token)));
            default:
                if (token.Type == CoreTokenType.EndTag)
                {
                    if (!_tolerant)
                        throw Error("Unexpected closing tag </" + token.Text + ">", token.Offset);
                    _diagnostics.Add(Diagnostic("SQX0001",
                        "Unexpected closing tag </" + token.Text + ">", token.Offset + 2, token.Text.Length));
                }
                _index++;
                return null;
        }
    }

    private SqxElementSyntax ParseElement()
    {
        var open = Expect(CoreTokenType.OpenTag);
        var name = Expect(CoreTokenType.Identifier);
        ValidateElementName(name, false);
        var attributes = new List<SqxAttributeSyntax>();
        while (Peek().Type is not (CoreTokenType.CloseTag or CoreTokenType.CloseSelfTag or CoreTokenType.Eof))
        {
            var attribute = ParseAttribute();
            if (attribute != null) attributes.Add(attribute);
        }
        if (Peek().Type == CoreTokenType.CloseSelfTag)
        {
            var close = Next();
            return new SqxElementSyntax(
                name.Text,
                attributes.ToArray(),
                Array.Empty<SqxSyntaxNode>(),
                true,
                Range(open.Offset, close.Offset + 2 - open.Offset),
                Range(name.Offset, name.Text.Length));
        }
        if (Peek().Type == CoreTokenType.Eof && _tolerant)
        {
            return new SqxElementSyntax(
                name.Text,
                attributes.ToArray(),
                Array.Empty<SqxSyntaxNode>(),
                false,
                Range(open.Offset, Math.Max(0, Peek().Offset - open.Offset)),
                Range(name.Offset, name.Text.Length));
        }

        Expect(CoreTokenType.CloseTag);
        var children = new List<SqxSyntaxNode>();
        while (Peek().Type != CoreTokenType.Eof)
        {
            if (Peek().Type == CoreTokenType.EndTag)
            {
                var end = Next();
                ValidateElementName(end, true);
                if (end.Text != name.Text)
                {
                    if (!_tolerant)
                        throw Error("Closing tag </" + end.Text + "> does not match <" + name.Text + ">", end.Offset);
                    _diagnostics.Add(Diagnostic("SQX0001",
                        "Closing tag </" + end.Text + "> does not match <" + name.Text + ">",
                        end.Offset + 2, end.Text.Length));
                }
                return new SqxElementSyntax(
                    name.Text,
                    attributes.ToArray(),
                    children.ToArray(),
                    false,
                    Range(open.Offset, end.Offset + end.Text.Length + 3 - open.Offset),
                    Range(name.Offset, name.Text.Length),
                    Range(end.Offset + 2, end.Text.Length));
            }
            var child = ParseNode();
            if (child != null) children.Add(child);
        }
        if (!_tolerant) throw Error("Unclosed element <" + name.Text + ">", open.Offset);
        _diagnostics.Add(Diagnostic("SQX0001", "Unclosed element <" + name.Text + ">", open.Offset, name.Text.Length + 1));
        return new SqxElementSyntax(
            name.Text,
            attributes.ToArray(),
            children.ToArray(),
            false,
            Range(open.Offset, Math.Max(0, Peek().Offset - open.Offset)),
            Range(name.Offset, name.Text.Length));
    }

    private SqxAttributeSyntax ParseAttribute()
    {
        if (Peek().Type != CoreTokenType.Identifier)
        {
            _index++;
            return null;
        }
        var name = Next();
        var nameRange = Range(name.Offset, name.Text.Length);
        if (Peek().Type != CoreTokenType.Equals)
            return new SqxAttributeSyntax(
                name.Text, null, false, nameRange, nameRange,
                new SquareSourceRange(nameRange.End, 0));
        _index++;
        var value = Peek();
        if (value.Type is not (CoreTokenType.StringLiteral or CoreTokenType.OpenBraceExpr or CoreTokenType.Identifier))
            return new SqxAttributeSyntax(
                name.Text, null, false, nameRange, nameRange,
                new SquareSourceRange(nameRange.End, 0));
        _index++;
        var expression = value.Type == CoreTokenType.OpenBraceExpr;
        var wrapped = value.Type is CoreTokenType.StringLiteral or CoreTokenType.OpenBraceExpr;
        var valueStart = value.Offset + (wrapped ? 1 : 0);
        if (expression)
            while (valueStart < _source.Length && char.IsWhiteSpace(_source[valueStart])) valueStart++;
        var valueRange = new SquareSourceRange(
            Absolute(valueStart),
            value.Text.Length);
        var fullEnd = valueRange.End + (wrapped ? 1 : 0);
        IReadOnlyList<SqxSyntaxNode> fragmentNodes = null;
        if (expression && name.Text.Equals("fallback", StringComparison.OrdinalIgnoreCase) &&
            value.Text.TrimStart().StartsWith("<", StringComparison.Ordinal))
        {
            var fragment = value.Text.Trim();
            if (fragment.StartsWith("<>", StringComparison.Ordinal) && fragment.EndsWith("</>", StringComparison.Ordinal))
                fragment = "<Fragment>" + fragment.Substring(2, fragment.Length - 5) + "</Fragment>";
            fragmentNodes = Parse(fragment, valueRange.Offset, _tolerant).Roots;
        }
        return new SqxAttributeSyntax(
            name.Text,
            value.Text,
            expression,
            new SquareSourceRange(nameRange.Offset, fullEnd - nameRange.Offset),
            nameRange,
            valueRange,
            fragmentNodes);
    }

    private CoreToken Peek() => _tokens[Math.Min(_index, _tokens.Count - 1)];
    private CoreToken Next() => _tokens[_index++];

    private CoreToken Expect(CoreTokenType type)
    {
        var token = Peek();
        if (token.Type == type) return Next();
        throw Error("Expected " + type + " but got " + token.Type, token.Offset);
    }

    private int Absolute(int offset) => _baseOffset + offset;
    private SquareSourceRange Range(int offset, int length) => new(Absolute(offset), Math.Max(0, length));

    private int GetExpressionLength(CoreToken token)
    {
        if (token.Text == "}") return 1;
        if (token.Text.TrimEnd().EndsWith("=>", StringComparison.Ordinal))
        {
            var arrow = _source.IndexOf("=>", token.Offset + 1, StringComparison.Ordinal);
            return arrow < 0 ? token.Text.Length + 1 : arrow + 2 - token.Offset;
        }
        var depth = 0;
        for (var position = token.Offset + 1; position < _source.Length; position++)
        {
            if (_source[position] == '{') depth++;
            else if (_source[position] == '}')
            {
                if (depth == 0) return position + 1 - token.Offset;
                depth--;
            }
        }
        return _source.Length - token.Offset;
    }

    private void ValidateElementName(CoreToken token, bool closing)
    {
        var atEnd = _tolerant && token.Offset + token.Text.Length >= _source.Length;
        var parsed = TemplateElementName.Parse(token.Text, atEnd);
        if (parsed.Status == TemplateElementNameStatus.Valid ||
            _tolerant && parsed.Status == TemplateElementNameStatus.Incomplete) return;
        var offset = closing ? token.Offset + 2 : token.Offset;
        if (_tolerant)
        {
            _diagnostics.Add(Diagnostic("SQXE001", "Invalid element name '" + token.Text + "'.", offset, token.Text.Length));
            return;
        }
        throw new SqxParseException(
            "Invalid element name '" + token.Text + "'.",
            Absolute(offset),
            "SQXE001",
            token.Text.Length);
    }

    private SquareDiagnostic Diagnostic(string id, string message, int offset, int length) =>
        new(id, SquareDiagnosticSeverity.Error, message, Range(offset, length), string.Empty);

    private SqxParseException Error(string message, int offset) =>
        new(message, Absolute(offset), "SQX0001");
}
