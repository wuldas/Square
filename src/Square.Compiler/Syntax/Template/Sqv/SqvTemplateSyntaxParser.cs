using Square.Compiler.LanguageServices;
using Square.Compiler.Parser;
using Square.Html;

namespace Square.Compiler.Syntax;

internal sealed class SqvTemplateSyntaxParser
{
    private readonly List<SqvToken> _tokens;
    private readonly string _source;
    private readonly int _baseOffset;
    private readonly bool _tolerant;
    private readonly List<SquareDiagnostic> _diagnostics = new();
    private readonly TemplateCatalog _catalog;
    private readonly TemplateResolutionContext _context;
    private int _svgDepth;
    private int _index;

    private SqvTemplateSyntaxParser(List<SqvToken> tokens, string source, int baseOffset, bool tolerant,
        TemplateCatalog catalog, TemplateResolutionContext context)
    {
        _tokens = tokens;
        _source = source;
        _baseOffset = baseOffset;
        _tolerant = tolerant;
        _catalog = catalog ?? TemplateCatalog.BuiltIn;
        _context = context ?? new TemplateResolutionContext(string.Empty, Array.Empty<string>());
    }

    public static SqvTemplateSyntax Parse(string source, int baseOffset = 0, bool tolerant = false,
        TemplateCatalog catalog = null, TemplateResolutionContext context = null)
    {
        var tokens = new SqvLexer(source ?? string.Empty, baseOffset, tolerant).Tokenize();
        return new SqvTemplateSyntaxParser(tokens, source ?? string.Empty, baseOffset, tolerant, catalog, context).ParseDocument();
    }

    private SqvTemplateSyntax ParseDocument()
    {
        var roots = new List<SqvSyntaxNode>();
        while (Peek().Type != SqvTokenType.Eof)
        {
            var node = ParseNode();
            if (node != null) roots.Add(node);
        }
        return new SqvTemplateSyntax(roots.ToArray(), _diagnostics);
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
        ValidateElementName(name, false);
        var attributes = new List<SqvAttributeSyntax>();
        while (Peek().Type is not (SqvTokenType.CloseTag or SqvTokenType.CloseSelfTag or SqvTokenType.Eof))
        {
            var attribute = ParseAttribute();
            if (attribute == null) continue;
            if (attribute.Name == "xmlns" || attribute.Name.StartsWith("xmlns:", StringComparison.Ordinal))
            {
                if (!_tolerant)
                    throw new SqxParseException("xmlns may only be declared on the top-level template.",
                        attribute.NameRange.Offset, "SQXE001", attribute.NameRange.Length);
                _diagnostics.Add(new SquareDiagnostic("SQXE001", SquareDiagnosticSeverity.Error,
                    "xmlns may only be declared on the top-level template.", attribute.NameRange, string.Empty));
            }
            attributes.Add(attribute);
        }
        var isName = attributes.FirstOrDefault(attribute => attribute.Name == "is")?.Value;
        var resolution = TemplateTagResolver.Resolve(_catalog, _context, name.Text, _svgDepth > 0, isName);
        SqvElementSyntax Create(IReadOnlyList<SqvSyntaxNode> children, bool selfClosing,
            SquareSourceRange range, SquareSourceRange closingRange = default) =>
            new(name.Text, attributes.ToArray(), children, selfClosing, range,
                new SquareSourceRange(Absolute(name.Offset), name.Text.Length), closingRange) { Resolution = resolution };

        if (Peek().Type == SqvTokenType.CloseSelfTag)
        {
            var close = Next();
            return Create(Array.Empty<SqvSyntaxNode>(), true,
                new SquareSourceRange(Absolute(open.Offset), close.Offset + 2 - open.Offset));
        }
        if (Peek().Type == SqvTokenType.Eof && _tolerant)
            return Create(Array.Empty<SqvSyntaxNode>(), false,
                new SquareSourceRange(Absolute(open.Offset), Math.Max(0, Peek().Offset - open.Offset)));

        var startTagEnd = Expect(SqvTokenType.CloseTag);
        if (TemplateTagResolver.IsHtmlVoid(name.Text, resolution))
            return Create(Array.Empty<SqvSyntaxNode>(), true,
                new SquareSourceRange(Absolute(open.Offset), startTagEnd.Offset + 1 - open.Offset));

        var svg = TemplateTagResolver.IsSvg(resolution);
        if (svg) _svgDepth++;
        try
        {
            var children = new List<SqvSyntaxNode>();
            while (Peek().Type != SqvTokenType.Eof)
            {
                if (Peek().Type == SqvTokenType.EndTag)
                {
                    var end = Next();
                    ValidateElementName(end, true);
                    var matches = resolution.Status == TemplateElementResolutionStatus.Resolved &&
                        resolution.Component.Kind == TemplateElementKind.Extension
                        ? end.Text == name.Text
                        : string.Equals(end.Text, name.Text, StringComparison.OrdinalIgnoreCase);
                    if (!matches)
                    {
                        if (!_tolerant)
                            throw Error("Closing tag </" + end.Text + "> does not match <" + name.Text + ">", end.Offset);
                        _diagnostics.Add(Diagnostic("SQV0001",
                            "Closing tag </" + end.Text + "> does not match <" + name.Text + ">",
                            end.Offset + 2, end.Text.Length));
                    }
                    return Create(children.ToArray(), false,
                        new SquareSourceRange(Absolute(open.Offset), end.Offset + end.Text.Length + 3 - open.Offset),
                        new SquareSourceRange(Absolute(end.Offset + 2), end.Text.Length));
                }
                var child = ParseNode();
                if (child != null) children.Add(child);
            }
            if (!_tolerant) throw Error("Unclosed element <" + name.Text + ">", open.Offset);
            _diagnostics.Add(Diagnostic("SQV0001", "Unclosed element <" + name.Text + ">", open.Offset, name.Text.Length + 1));
            return Create(children.ToArray(), false,
                new SquareSourceRange(Absolute(open.Offset), Math.Max(0, Peek().Offset - open.Offset)));
        }
        finally
        {
            if (svg) _svgDepth--;
        }
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


    private void ValidateElementName(SqvToken token, bool closing)
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
        new(id, SquareDiagnosticSeverity.Error, message,
            new SquareSourceRange(Absolute(offset), Math.Max(0, length)), string.Empty);

    private SqxParseException Error(string message, int offset) =>
        new(message, Absolute(offset), "SQV0001");
}
