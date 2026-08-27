using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal static class CssSyntaxParser
{
    public static CssStyleSheetSyntax Parse(string source, int documentOffset)
    {
        source ??= string.Empty;
        var rules = new List<CssRuleSyntax>();
        var atRules = new List<CssAtRuleSyntax>();
        var diagnostics = new List<CssSyntaxDiagnostic>();
        var position = 0;
        while (position < source.Length)
        {
            SkipTrivia(source, ref position);
            if (position >= source.Length) break;
            if (source[position] == '@')
            {
                ParseAtRule(source, documentOffset, ref position, atRules, diagnostics);
                continue;
            }
            var ruleStart = position;
            var openBrace = FindTopLevel(source, position, '{');
            if (openBrace < 0)
            {
                diagnostics.Add(new CssSyntaxDiagnostic(
                    "Expected '{' after CSS selector",
                    Range(documentOffset, position, source.Length - position)));
                break;
            }
            var closeBrace = FindMatchingBrace(source, openBrace);
            if (closeBrace < 0)
            {
                diagnostics.Add(new CssSyntaxDiagnostic(
                    "Unclosed CSS rule block",
                    Range(documentOffset, openBrace, 1)));
                closeBrace = source.Length;
            }

            var selectorBounds = TrimBounds(source, ruleStart, openBrace);
            var selectors = ParseSelectors(source, documentOffset, selectorBounds.Start, selectorBounds.End);
            var declarationEnd = closeBrace < source.Length ? closeBrace : source.Length;
            var declarations = ParseDeclarations(source, documentOffset, openBrace + 1, declarationEnd, diagnostics);
            var fullEnd = closeBrace < source.Length ? closeBrace + 1 : source.Length;
            rules.Add(new CssRuleSyntax(
                selectors,
                declarations,
                Range(documentOffset, ruleStart, fullEnd - ruleStart),
                Range(documentOffset, selectorBounds.Start, selectorBounds.End - selectorBounds.Start),
                Range(documentOffset, openBrace, fullEnd - openBrace)));
            if (closeBrace >= source.Length) break;
            position = closeBrace + 1;
        }
        return new CssStyleSheetSyntax(rules.ToArray(), atRules.ToArray(), diagnostics.ToArray());
    }

    private static void ParseAtRule(
        string source,
        int documentOffset,
        ref int position,
        List<CssAtRuleSyntax> atRules,
        List<CssSyntaxDiagnostic> diagnostics)
    {
        var start = position++;
        var nameStart = position;
        while (position < source.Length &&
               (char.IsLetterOrDigit(source[position]) || source[position] is '-' or '_')) position++;
        var name = source.Substring(nameStart, position - nameStart);
        while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
        var preludeStart = position;
        var terminator = FindAtRuleTerminator(source, position);
        if (terminator < 0)
        {
            diagnostics.Add(new CssSyntaxDiagnostic(
                "Unclosed CSS at-rule @" + name,
                Range(documentOffset, start, source.Length - start)));
            position = source.Length;
            return;
        }

        var preludeBounds = TrimBounds(source, preludeStart, terminator);
        var prelude = source.Substring(preludeBounds.Start, preludeBounds.End - preludeBounds.Start);
        if (source[terminator] == ';')
        {
            atRules.Add(new CssAtRuleSyntax(
                name,
                prelude,
                Range(documentOffset, start, terminator - start + 1),
                Range(documentOffset, preludeBounds.Start, preludeBounds.End - preludeBounds.Start),
                Range(documentOffset, terminator, 0),
                Array.Empty<CssRuleSyntax>(),
                Array.Empty<CssDeclarationSyntax>(),
                Array.Empty<CssAtRuleSyntax>()));
            position = terminator + 1;
            return;
        }

        var closeBrace = FindMatchingBrace(source, terminator);
        var blockEnd = closeBrace < 0 ? source.Length : closeBrace;
        if (closeBrace < 0)
            diagnostics.Add(new CssSyntaxDiagnostic(
                "Unclosed CSS at-rule block @" + name,
                Range(documentOffset, terminator, 1)));
        IReadOnlyList<CssRuleSyntax> rules;
        IReadOnlyList<CssDeclarationSyntax> declarations;
        IReadOnlyList<CssAtRuleSyntax> nestedAtRules;
        if (ContainsNestedRules(name))
        {
            var nested = Parse(
                source.Substring(terminator + 1, blockEnd - terminator - 1),
                documentOffset + terminator + 1);
            rules = nested.Rules;
            declarations = Array.Empty<CssDeclarationSyntax>();
            nestedAtRules = nested.AtRules;
            diagnostics.AddRange(nested.Diagnostics);
        }
        else
        {
            rules = Array.Empty<CssRuleSyntax>();
            declarations = ParseDeclarations(
                source,
                documentOffset,
                terminator + 1,
                blockEnd,
                diagnostics);
            nestedAtRules = Array.Empty<CssAtRuleSyntax>();
        }
        var fullEnd = closeBrace < 0 ? source.Length : closeBrace + 1;
        atRules.Add(new CssAtRuleSyntax(
            name,
            prelude,
            Range(documentOffset, start, fullEnd - start),
            Range(documentOffset, preludeBounds.Start, preludeBounds.End - preludeBounds.Start),
            Range(documentOffset, terminator, fullEnd - terminator),
            rules,
            declarations,
            nestedAtRules));
        position = fullEnd;
    }

    private static bool ContainsNestedRules(string name) =>
        name.Equals("media", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("keyframes", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("supports", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("container", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("layer", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("document", StringComparison.OrdinalIgnoreCase);

    private static int FindAtRuleTerminator(string source, int start)
    {
        var quote = '\0';
        var parenthesisDepth = 0;
        for (var position = start; position < source.Length; position++)
        {
            var value = source[position];
            if (quote != '\0')
            {
                if (value == '\\' && position + 1 < source.Length) position++;
                else if (value == quote) quote = '\0';
                continue;
            }
            if (value is '"' or '\'') quote = value;
            else if (value == '(') parenthesisDepth++;
            else if (value == ')') parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
            else if (parenthesisDepth == 0 && value is ';' or '{') return position;
        }
        return -1;
    }

    private static IReadOnlyList<CssSelectorSyntax> ParseSelectors(
        string source,
        int documentOffset,
        int start,
        int end)
    {
        var selectors = new List<CssSelectorSyntax>();
        var segmentStart = start;
        var quote = '\0';
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        for (var position = start; position <= end; position++)
        {
            var atEnd = position == end;
            var value = atEnd ? '\0' : source[position];
            if (!atEnd && quote != '\0')
            {
                if (value == '\\' && position + 1 < end) position++;
                else if (value == quote) quote = '\0';
                continue;
            }
            if (!atEnd && value is '"' or '\'') quote = value;
            else if (!atEnd && value == '[') bracketDepth++;
            else if (!atEnd && value == ']') bracketDepth = Math.Max(0, bracketDepth - 1);
            else if (!atEnd && value == '(') parenthesisDepth++;
            else if (!atEnd && value == ')') parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
            else if (atEnd || value == ',' && bracketDepth == 0 && parenthesisDepth == 0)
            {
                var bounds = TrimBounds(source, segmentStart, position);
                if (bounds.End > bounds.Start)
                {
                    selectors.Add(new CssSelectorSyntax(
                        source.Substring(bounds.Start, bounds.End - bounds.Start),
                        Range(documentOffset, bounds.Start, bounds.End - bounds.Start)));
                }
                segmentStart = position + 1;
            }
        }
        return selectors;
    }

    private static IReadOnlyList<CssDeclarationSyntax> ParseDeclarations(
        string source,
        int documentOffset,
        int start,
        int end,
        List<CssSyntaxDiagnostic> diagnostics)
    {
        var declarations = new List<CssDeclarationSyntax>();
        var segmentStart = start;
        var quote = '\0';
        var parenthesisDepth = 0;
        for (var position = start; position <= end; position++)
        {
            var atEnd = position == end;
            var value = atEnd ? '\0' : source[position];
            if (!atEnd && quote == '\0' &&
                position + 1 < end && source[position] == '/' && source[position + 1] == '*')
            {
                var commentEnd = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                if (commentEnd < 0 || commentEnd >= end)
                {
                    diagnostics.Add(new CssSyntaxDiagnostic(
                        "Unclosed CSS comment",
                        Range(documentOffset, position, end - position)));
                    return declarations;
                }
                if (IsWhitespace(source, segmentStart, position)) segmentStart = commentEnd + 2;
                position = commentEnd + 1;
                continue;
            }
            if (!atEnd && quote != '\0')
            {
                if (value == '\\' && position + 1 < end) position++;
                else if (value == quote) quote = '\0';
                continue;
            }
            if (!atEnd && value is '"' or '\'') quote = value;
            else if (!atEnd && value == '(') parenthesisDepth++;
            else if (!atEnd && value == ')') parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
            else if (atEnd || value == ';' && parenthesisDepth == 0)
            {
                ParseDeclaration(source, documentOffset, segmentStart, position, declarations, diagnostics);
                segmentStart = position + 1;
            }
        }
        return declarations;
    }

    private static void ParseDeclaration(
        string source,
        int documentOffset,
        int start,
        int end,
        List<CssDeclarationSyntax> declarations,
        List<CssSyntaxDiagnostic> diagnostics)
    {
        var bounds = TrimBounds(source, start, end);
        if (bounds.End <= bounds.Start) return;
        var colon = FindTopLevel(source, bounds.Start, ':', bounds.End);
        if (colon < 0)
        {
            diagnostics.Add(new CssSyntaxDiagnostic(
                "Expected ':' in CSS declaration",
                Range(documentOffset, bounds.Start, bounds.End - bounds.Start)));
            return;
        }
        var propertyBounds = TrimBounds(source, bounds.Start, colon);
        var valueBounds = TrimBounds(source, colon + 1, bounds.End);
        var important = false;
        const string importantText = "!important";
        if (valueBounds.End - valueBounds.Start >= importantText.Length)
        {
            var importantStart = valueBounds.End - importantText.Length;
            if (string.Compare(source, importantStart, importantText, 0, importantText.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                important = true;
                valueBounds = TrimBounds(source, valueBounds.Start, importantStart);
            }
        }
        declarations.Add(new CssDeclarationSyntax(
            source.Substring(propertyBounds.Start, propertyBounds.End - propertyBounds.Start),
            source.Substring(valueBounds.Start, valueBounds.End - valueBounds.Start),
            important,
            Range(documentOffset, bounds.Start, bounds.End - bounds.Start),
            Range(documentOffset, propertyBounds.Start, propertyBounds.End - propertyBounds.Start),
            Range(documentOffset, valueBounds.Start, valueBounds.End - valueBounds.Start)));
    }

    private static int FindMatchingBrace(string source, int openBrace)
    {
        var depth = 1;
        var quote = '\0';
        for (var position = openBrace + 1; position < source.Length; position++)
        {
            var value = source[position];
            if (quote != '\0')
            {
                if (value == '\\' && position + 1 < source.Length) position++;
                else if (value == quote) quote = '\0';
                continue;
            }
            if (position + 1 < source.Length && value == '/' && source[position + 1] == '*')
            {
                var commentEnd = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                if (commentEnd < 0) return -1;
                position = commentEnd + 1;
                continue;
            }
            if (value is '"' or '\'') quote = value;
            else if (value == '{') depth++;
            else if (value == '}' && --depth == 0) return position;
        }
        return -1;
    }

    private static int FindTopLevel(string source, int start, char target, int end = -1)
    {
        if (end < 0) end = source.Length;
        var quote = '\0';
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        for (var position = start; position < end; position++)
        {
            var value = source[position];
            if (quote != '\0')
            {
                if (value == '\\' && position + 1 < end) position++;
                else if (value == quote) quote = '\0';
                continue;
            }
            if (value is '"' or '\'') quote = value;
            else if (value == '[') bracketDepth++;
            else if (value == ']') bracketDepth = Math.Max(0, bracketDepth - 1);
            else if (value == '(') parenthesisDepth++;
            else if (value == ')') parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
            else if (value == target && bracketDepth == 0 && parenthesisDepth == 0) return position;
        }
        return -1;
    }

    private static void SkipTrivia(string source, ref int position)
    {
        while (position < source.Length)
        {
            if (char.IsWhiteSpace(source[position]))
            {
                position++;
                continue;
            }
            if (position + 1 < source.Length && source[position] == '/' && source[position + 1] == '*')
            {
                var end = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                position = end < 0 ? source.Length : end + 2;
                continue;
            }
            break;
        }
    }

    private static (int Start, int End) TrimBounds(string source, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(source[start])) start++;
        while (end > start && char.IsWhiteSpace(source[end - 1])) end--;
        return (start, end);
    }

    private static bool IsWhitespace(string source, int start, int end)
    {
        for (var index = start; index < end; index++)
            if (!char.IsWhiteSpace(source[index])) return false;
        return true;
    }

    private static SquareSourceRange Range(int documentOffset, int offset, int length) =>
        new SquareSourceRange(documentOffset + offset, Math.Max(0, length));
}
