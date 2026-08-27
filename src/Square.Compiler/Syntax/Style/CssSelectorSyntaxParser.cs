using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal static class CssSelectorSyntaxParser
{
    public static IReadOnlyList<CssCompoundStepSyntax> Parse(string text, int documentOffset)
    {
        text ??= string.Empty;
        var steps = new List<CssCompoundStepSyntax>();
        var parts = new List<CssSimpleSelectorSyntax>();
        var pending = CssCombinator.Descendant;
        var position = 0;
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                Flush(parts, steps, pending);
                while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
                if (position < text.Length && text[position] is '>' or '+' or '~') continue;
                pending = CssCombinator.Descendant;
                continue;
            }
            if (text[position] is '>' or '+' or '~')
            {
                Flush(parts, steps, pending);
                pending = text[position] switch
                {
                    '>' => CssCombinator.Child,
                    '+' => CssCombinator.Adjacent,
                    _ => CssCombinator.GeneralSibling
                };
                position++;
                while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
                continue;
            }

            var start = position;
            if (text[position] == '*')
            {
                position++;
                parts.Add(Simple(CssSimpleSelectorKind.Universal, "*", start, position));
                continue;
            }
            if (text[position] == '.')
            {
                position++;
                var name = ReadIdentifier(text, ref position);
                if (name.Length > 0) parts.Add(Simple(CssSimpleSelectorKind.Class, name, start, position));
                continue;
            }
            if (text[position] == '#')
            {
                position++;
                var name = ReadIdentifier(text, ref position);
                if (name.Length > 0) parts.Add(Simple(CssSimpleSelectorKind.Id, name, start, position));
                continue;
            }
            if (text[position] == '[')
            {
                parts.Add(ParseAttribute(text, documentOffset, ref position));
                continue;
            }
            if (text[position] == ':')
            {
                var doubleColon = position + 1 < text.Length && text[position + 1] == ':';
                position += doubleColon ? 2 : 1;
                var name = ReadIdentifier(text, ref position);
                if (position < text.Length && text[position] == '(')
                {
                    var argumentStart = position++;
                    var depth = 1;
                    var quote = '\0';
                    while (position < text.Length && depth > 0)
                    {
                        var value = text[position++];
                        if (quote != '\0')
                        {
                            if (value == '\\' && position < text.Length) position++;
                            else if (value == quote) quote = '\0';
                            continue;
                        }
                        if (value is '"' or '\'') quote = value;
                        else if (value == '(') depth++;
                        else if (value == ')') depth--;
                    }
                    var argumentEnd = Math.Max(argumentStart + 1, position - 1);
                    name += "(" + text.Substring(argumentStart + 1, argumentEnd - argumentStart - 1) + ")";
                }
                var kind = doubleColon || name.Equals("before", StringComparison.OrdinalIgnoreCase) ||
                           name.Equals("after", StringComparison.OrdinalIgnoreCase)
                    ? CssSimpleSelectorKind.PseudoElement
                    : CssSimpleSelectorKind.PseudoClass;
                if (name.Length > 0) parts.Add(Simple(kind, name, start, position));
                continue;
            }
            if (IsIdentifierStart(text[position]))
            {
                var name = ReadIdentifier(text, ref position);
                parts.Add(Simple(CssSimpleSelectorKind.Type, name, start, position));
                continue;
            }
            position++;
        }
        Flush(parts, steps, pending);
        return steps.ToArray();

        CssSimpleSelectorSyntax Simple(CssSimpleSelectorKind kind, string name, int start, int end) =>
            new CssSimpleSelectorSyntax(kind, name, Range(documentOffset, start, end - start));
    }

    private static CssSimpleSelectorSyntax ParseAttribute(string text, int documentOffset, ref int position)
    {
        var start = position++;
        SkipWhitespace(text, ref position);
        var name = ReadIdentifier(text, ref position);
        SkipWhitespace(text, ref position);
        var op = CssAttributeSelectorOperator.Presence;
        foreach (var candidate in new[] { "~=", "|=", "^=", "$=", "*=", "=" })
        {
            if (!StartsWith(text, position, candidate)) continue;
            op = candidate switch
            {
                "~=" => CssAttributeSelectorOperator.Includes,
                "|=" => CssAttributeSelectorOperator.DashMatch,
                "^=" => CssAttributeSelectorOperator.PrefixMatch,
                "$=" => CssAttributeSelectorOperator.SuffixMatch,
                "*=" => CssAttributeSelectorOperator.SubstringMatch,
                _ => CssAttributeSelectorOperator.Equals
            };
            position += candidate.Length;
            break;
        }
        SkipWhitespace(text, ref position);
        string value = null;
        if (op != CssAttributeSelectorOperator.Presence && position < text.Length)
        {
            if (text[position] is '"' or '\'')
            {
                var quote = text[position++];
                var valueStart = position;
                while (position < text.Length && text[position] != quote)
                {
                    if (text[position] == '\\' && position + 1 < text.Length) position += 2;
                    else position++;
                }
                value = text.Substring(valueStart, position - valueStart);
                if (position < text.Length) position++;
            }
            else
            {
                if (text[position] == '#') position++;
                value = ReadIdentifier(text, ref position);
            }
        }
        SkipWhitespace(text, ref position);
        var sensitivity = CssAttributeCaseSensitivity.Default;
        if (position < text.Length && text[position] is 'i' or 'I' or 's' or 'S')
        {
            sensitivity = text[position] is 'i' or 'I'
                ? CssAttributeCaseSensitivity.Insensitive
                : CssAttributeCaseSensitivity.Sensitive;
            position++;
            SkipWhitespace(text, ref position);
        }
        while (position < text.Length && text[position] != ']') position++;
        if (position < text.Length) position++;
        return new CssSimpleSelectorSyntax(
            CssSimpleSelectorKind.Attribute,
            name,
            Range(documentOffset, start, position - start),
            op,
            value,
            sensitivity);
    }

    private static void Flush(
        List<CssSimpleSelectorSyntax> parts,
        List<CssCompoundStepSyntax> steps,
        CssCombinator combinator)
    {
        if (parts.Count == 0) return;
        var start = parts[0].Range.Offset;
        var end = parts[parts.Count - 1].Range.End;
        steps.Add(new CssCompoundStepSyntax(
            parts.ToArray(),
            combinator,
            new SquareSourceRange(start, end - start)));
        parts.Clear();
    }

    private static string ReadIdentifier(string text, ref int position)
    {
        var start = position;
        while (position < text.Length && IsIdentifierPart(text[position])) position++;
        return text.Substring(start, position - start);
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
    }

    private static bool StartsWith(string text, int position, string value) =>
        position + value.Length <= text.Length &&
        string.CompareOrdinal(text, position, value, 0, value.Length) == 0;

    private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value is '_' or '-';
    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value is '_' or '-';
    private static SquareSourceRange Range(int offset, int start, int length) =>
        new SquareSourceRange(offset + start, Math.Max(0, length));
}
