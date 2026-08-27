using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal static class ComponentSectionScanner
{
    public static ComponentSectionScanResult Scan(
        string source,
        string sourcePath,
        ComponentDialect dialect,
        bool tolerant)
    {
        source ??= string.Empty;
        sourcePath ??= string.Empty;
        var diagnostics = new List<ComponentSectionDiagnostic>();
        TemplateSectionSyntax template = null;
        ScriptSectionSyntax script = null;
        StyleSectionSyntax style = null;
        var position = 0;

        while (position < source.Length)
        {
            SkipWhitespace(source, ref position);
            if (position >= source.Length) break;
            if (StartsWith(source, position, "<!--"))
            {
                var commentEnd = source.IndexOf("-->", position + 4, StringComparison.Ordinal);
                if (commentEnd < 0)
                {
                    diagnostics.Add(new ComponentSectionDiagnostic(
                        ComponentSectionDiagnosticKind.UnclosedComment,
                        "Unclosed document comment",
                        new SquareSourceRange(position, source.Length - position)));
                    break;
                }
                position = commentEnd + 3;
                continue;
            }

            if (source[position] != '<')
            {
                diagnostics.Add(new ComponentSectionDiagnostic(
                    ComponentSectionDiagnosticKind.UnexpectedContent,
                    "Unexpected content outside a top-level section",
                    new SquareSourceRange(position, 1)));
                break;
            }

            var sectionStart = position;
            var nameStart = sectionStart + 1;
            var nameEnd = nameStart;
            while (nameEnd < source.Length && char.IsLetter(source[nameEnd])) nameEnd++;
            if (nameEnd == nameStart)
            {
                diagnostics.Add(new ComponentSectionDiagnostic(
                    ComponentSectionDiagnosticKind.InvalidSection,
                    "Invalid top-level section",
                    new SquareSourceRange(sectionStart, 1)));
                break;
            }

            var sourceName = source.Substring(nameStart, nameEnd - nameStart);
            var name = dialect == ComponentDialect.Sqv ? sourceName.ToLowerInvariant() : sourceName;
            if (!TryGetKind(name, out var kind))
            {
                diagnostics.Add(new ComponentSectionDiagnostic(
                    ComponentSectionDiagnosticKind.UnknownSection,
                    "Unknown top-level section <" + name + ">",
                    new SquareSourceRange(sectionStart, nameEnd - sectionStart)));
                break;
            }

            var openingEnd = FindTagEnd(source, nameEnd);
            if (openingEnd < 0)
            {
                diagnostics.Add(new ComponentSectionDiagnostic(
                    ComponentSectionDiagnosticKind.UnclosedOpeningTag,
                    "Unclosed <" + name + "> opening tag",
                    new SquareSourceRange(sectionStart, source.Length - sectionStart)));
                Assign(kind, CreateUnclosed(kind, source, sectionStart, source.Length, source.Length), ref template, ref script, ref style, diagnostics);
                break;
            }

            var contentStart = openingEnd + 1;
            var closeStart = FindClosingTag(source, name, kind, contentStart);
            if (closeStart < 0)
            {
                diagnostics.Add(new ComponentSectionDiagnostic(
                    ComponentSectionDiagnosticKind.UnclosedSection,
                    "Unclosed <" + name + "> section",
                    new SquareSourceRange(sectionStart, openingEnd - sectionStart + 1)));
                var recoveryStart = tolerant
                    ? FindFollowingSectionStart(source, contentStart, kind, dialect)
                    : -1;
                var contentEnd = recoveryStart >= 0 ? recoveryStart : source.Length;
                Assign(kind, CreateUnclosed(kind, source, sectionStart, contentStart, contentEnd), ref template, ref script, ref style, diagnostics);
                if (recoveryStart < 0) break;
                position = recoveryStart;
                continue;
            }

            var closeEnd = FindTagEnd(source, closeStart + name.Length + 2);
            if (closeEnd < 0)
            {
                diagnostics.Add(new ComponentSectionDiagnostic(
                    ComponentSectionDiagnosticKind.UnclosedClosingTag,
                    "Unclosed </" + name + "> tag",
                    new SquareSourceRange(closeStart, source.Length - closeStart)));
                Assign(kind, CreateUnclosed(kind, source, sectionStart, contentStart, closeStart), ref template, ref script, ref style, diagnostics);
                break;
            }

            var section = CreateSection(kind, source, sectionStart, openingEnd, contentStart, closeStart, closeEnd);
            Assign(kind, section, ref template, ref script, ref style, diagnostics);
            position = closeEnd + 1;
        }

        if (template == null)
        {
            diagnostics.Add(new ComponentSectionDiagnostic(
                ComponentSectionDiagnosticKind.MissingTemplate,
                "Missing required <template> section",
                new SquareSourceRange(0, 0)));
        }

        return new ComponentSectionScanResult(
            new ComponentDocumentSyntax(dialect, sourcePath, source, template, script, style),
            diagnostics.ToArray());
    }

    private static ComponentSectionSyntax CreateSection(
        ComponentSectionKind kind,
        string source,
        int sectionStart,
        int openingEnd,
        int contentStart,
        int closeStart,
        int closeEnd)
    {
        var fullRange = new SquareSourceRange(sectionStart, closeEnd - sectionStart + 1);
        var openingRange = new SquareSourceRange(sectionStart, openingEnd - sectionStart + 1);
        var contentRange = new SquareSourceRange(contentStart, closeStart - contentStart);
        var closingRange = new SquareSourceRange(closeStart, closeEnd - closeStart + 1);
        var content = source.Substring(contentStart, closeStart - contentStart);
        return kind switch
        {
            ComponentSectionKind.Template => new TemplateSectionSyntax(fullRange, openingRange, contentRange, closingRange, content, true),
            ComponentSectionKind.Script => new ScriptSectionSyntax(
                fullRange,
                openingRange,
                contentRange,
                closingRange,
                source.Substring(openingRange.Offset, openingRange.Length),
                content,
                true),
            _ => new StyleSectionSyntax(fullRange, openingRange, contentRange, closingRange, content, true)
        };
    }

    private static ComponentSectionSyntax CreateUnclosed(
        ComponentSectionKind kind,
        string source,
        int sectionStart,
        int contentStart,
        int contentEnd)
    {
        contentStart = Math.Min(contentStart, source.Length);
        contentEnd = Math.Max(contentStart, Math.Min(contentEnd, source.Length));
        var fullRange = new SquareSourceRange(sectionStart, contentEnd - sectionStart);
        var openingRange = new SquareSourceRange(sectionStart, contentStart - sectionStart);
        var contentRange = new SquareSourceRange(contentStart, contentEnd - contentStart);
        var closingRange = new SquareSourceRange(contentEnd, 0);
        var content = source.Substring(contentStart, contentEnd - contentStart);
        return kind switch
        {
            ComponentSectionKind.Template => new TemplateSectionSyntax(fullRange, openingRange, contentRange, closingRange, content, false),
            ComponentSectionKind.Script => new ScriptSectionSyntax(
                fullRange,
                openingRange,
                contentRange,
                closingRange,
                source.Substring(openingRange.Offset, openingRange.Length),
                content,
                false),
            _ => new StyleSectionSyntax(fullRange, openingRange, contentRange, closingRange, content, false)
        };
    }

    private static void Assign(
        ComponentSectionKind kind,
        ComponentSectionSyntax section,
        ref TemplateSectionSyntax template,
        ref ScriptSectionSyntax script,
        ref StyleSectionSyntax style,
        List<ComponentSectionDiagnostic> diagnostics)
    {
        var duplicate = kind switch
        {
            ComponentSectionKind.Template => template != null,
            ComponentSectionKind.Script => script != null,
            _ => style != null
        };
        if (duplicate)
        {
            diagnostics.Add(new ComponentSectionDiagnostic(
                ComponentSectionDiagnosticKind.DuplicateSection,
                "Duplicate <" + kind.ToString().ToLowerInvariant() + "> section",
                section.OpeningTagRange));
            return;
        }

        switch (kind)
        {
            case ComponentSectionKind.Template:
                template = (TemplateSectionSyntax)section;
                break;
            case ComponentSectionKind.Script:
                script = (ScriptSectionSyntax)section;
                break;
            default:
                style = (StyleSectionSyntax)section;
                break;
        }
    }

    private static bool TryGetKind(string name, out ComponentSectionKind kind)
    {
        switch (name)
        {
            case "template":
                kind = ComponentSectionKind.Template;
                return true;
            case "script":
                kind = ComponentSectionKind.Script;
                return true;
            case "style":
                kind = ComponentSectionKind.Style;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static int FindFollowingSectionStart(
        string source,
        int start,
        ComponentSectionKind currentKind,
        ComponentDialect dialect)
    {
        var position = start;
        while (position < source.Length)
        {
            var candidate = source.IndexOf('<', position);
            if (candidate < 0) return -1;
            if (!IsAtLineContentStart(source, candidate))
            {
                position = candidate + 1;
                continue;
            }

            var nameStart = candidate + 1;
            var nameEnd = nameStart;
            while (nameEnd < source.Length && char.IsLetter(source[nameEnd])) nameEnd++;
            var sourceName = source.Substring(nameStart, nameEnd - nameStart);
            var name = dialect == ComponentDialect.Sqv ? sourceName.ToLowerInvariant() : sourceName;
            if (TryGetKind(name, out var kind) && kind != currentKind &&
                (nameEnd >= source.Length || source[nameEnd] == '>' || source[nameEnd] == '/' || char.IsWhiteSpace(source[nameEnd])))
                return candidate;
            position = candidate + 1;
        }
        return -1;
    }

    private static bool IsAtLineContentStart(string source, int position)
    {
        for (var index = position - 1; index >= 0 && source[index] != '\n' && source[index] != '\r'; index--)
        {
            if (!char.IsWhiteSpace(source[index])) return false;
        }
        return true;
    }

    private static int FindClosingTag(
        string source,
        string name,
        ComponentSectionKind kind,
        int start)
    {
        if (kind == ComponentSectionKind.Template)
            return FindMatchingTemplateClose(source, name, start);
        return FindRawSectionClose(source, name, kind, start);
    }

    private static int FindRawSectionClose(
        string source,
        string name,
        ComponentSectionKind kind,
        int start)
    {
        var position = start;
        while (position < source.Length)
        {
            if (IsClosingTagAt(source, position, name)) return position;
            if (StartsWith(source, position, "/*"))
            {
                var commentEnd = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                if (commentEnd < 0) return -1;
                position = commentEnd + 2;
                continue;
            }
            if (kind == ComponentSectionKind.Script && StartsWith(source, position, "//"))
            {
                var lineEnd = source.IndexOf('\n', position + 2);
                position = lineEnd < 0 ? source.Length : lineEnd + 1;
                continue;
            }
            if (kind == ComponentSectionKind.Script &&
                TrySkipCSharpString(source, ref position))
                continue;
            if (source[position] is '"' or '\'')
            {
                SkipQuotedString(source, ref position, source[position]);
                continue;
            }
            position++;
        }
        return -1;
    }

    private static bool IsClosingTagAt(string source, int position, string name)
    {
        if (!StartsWithIgnoreCase(source, position, "</" + name)) return false;
        var after = position + name.Length + 2;
        return after >= source.Length || source[after] == '>' || char.IsWhiteSpace(source[after]);
    }

    private static bool TrySkipCSharpString(string source, ref int position)
    {
        var start = position;
        var verbatim = false;
        if (StartsWith(source, position, "$@\"") || StartsWith(source, position, "@$\""))
        {
            verbatim = true;
            position += 2;
        }
        else if (StartsWith(source, position, "@\""))
        {
            verbatim = true;
            position++;
        }
        else if (StartsWith(source, position, "$\""))
        {
            position++;
        }
        else if (source[position] != '"')
        {
            return false;
        }

        var quoteCount = CountRun(source, position, '"');
        if (quoteCount >= 3)
        {
            position += quoteCount;
            while (position < source.Length)
            {
                if (CountRun(source, position, '"') >= quoteCount)
                {
                    position += quoteCount;
                    return true;
                }
                position++;
            }
            return true;
        }

        position++;
        while (position < source.Length)
        {
            if (source[position] == '"')
            {
                if (verbatim && position + 1 < source.Length && source[position + 1] == '"')
                {
                    position += 2;
                    continue;
                }
                position++;
                return true;
            }
            if (!verbatim && source[position] == '\\' && position + 1 < source.Length)
                position += 2;
            else
                position++;
        }
        position = Math.Max(position, start + 1);
        return true;
    }

    private static void SkipQuotedString(string source, ref int position, char quote)
    {
        position++;
        while (position < source.Length)
        {
            if (source[position] == quote)
            {
                position++;
                return;
            }
            if (source[position] == '\\' && position + 1 < source.Length)
                position += 2;
            else
                position++;
        }
    }

    private static int CountRun(string source, int position, char value)
    {
        var count = 0;
        while (position + count < source.Length && source[position + count] == value) count++;
        return count;
    }

    private static int FindMatchingTemplateClose(string source, string name, int start)
    {
        var depth = 1;
        var position = start;
        while (position < source.Length)
        {
            if (source[position] == '{')
            {
                SkipTemplateExpression(source, ref position);
                continue;
            }
            if (source[position] != '<')
            {
                position++;
                continue;
            }

            var tagStart = position;
            if (StartsWith(source, tagStart, "<!--"))
            {
                var commentEnd = source.IndexOf("-->", tagStart + 4, StringComparison.Ordinal);
                if (commentEnd < 0) return -1;
                position = commentEnd + 3;
                continue;
            }

            var closing = tagStart + 1 < source.Length && source[tagStart + 1] == '/';
            var nameStart = tagStart + (closing ? 2 : 1);
            if (nameStart >= source.Length || !char.IsLetter(source[nameStart]))
            {
                position = tagStart + 1;
                continue;
            }

            var tagEnd = FindTagEnd(source, nameStart);
            if (tagEnd < 0) return -1;
            if (!IsSectionTag(source, nameStart, name))
            {
                position = tagEnd + 1;
                continue;
            }

            if (closing)
            {
                depth--;
                if (depth == 0) return tagStart;
            }
            else if (!IsSelfClosing(source, tagStart, tagEnd))
            {
                depth++;
            }
            position = tagEnd + 1;
        }
        return -1;
    }

    private static void SkipTemplateExpression(string source, ref int position)
    {
        var depth = 0;
        while (position < source.Length)
        {
            if (StartsWith(source, position, "/*"))
            {
                var commentEnd = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                position = commentEnd < 0 ? source.Length : commentEnd + 2;
                continue;
            }
            if (StartsWith(source, position, "//"))
            {
                var lineEnd = source.IndexOf('\n', position + 2);
                position = lineEnd < 0 ? source.Length : lineEnd + 1;
                continue;
            }
            if (TrySkipCSharpString(source, ref position)) continue;
            if (source[position] == '\'')
            {
                SkipQuotedString(source, ref position, '\'');
                continue;
            }
            if (source[position] == '{')
            {
                depth++;
                position++;
                continue;
            }
            if (source[position] == '}')
            {
                depth--;
                position++;
                if (depth == 0) return;
                continue;
            }
            position++;
        }
    }

    private static bool IsSectionTag(string source, int nameStart, string name)
    {
        if (nameStart + name.Length > source.Length ||
            string.Compare(source, nameStart, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;
        var after = nameStart + name.Length;
        return after >= source.Length || source[after] == '>' || source[after] == '/' || char.IsWhiteSpace(source[after]);
    }

    private static bool IsSelfClosing(string source, int tagStart, int tagEnd)
    {
        for (var index = tagEnd - 1; index > tagStart; index--)
        {
            if (char.IsWhiteSpace(source[index])) continue;
            return source[index] == '/';
        }
        return false;
    }

    private static int FindTagEnd(string source, int start)
    {
        var quote = '\0';
        var braceDepth = 0;
        for (var index = start; index < source.Length; index++)
        {
            var value = source[index];
            if (quote != '\0')
            {
                if (value == '\\' && index + 1 < source.Length)
                {
                    index++;
                    continue;
                }
                if (value == quote) quote = '\0';
                continue;
            }
            if (value is '"' or '\'') quote = value;
            else if (value == '{') braceDepth++;
            else if (value == '}' && braceDepth > 0) braceDepth--;
            else if (value == '>' && braceDepth == 0) return index;
        }
        return -1;
    }

    private static void SkipWhitespace(string source, ref int position)
    {
        while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
    }

    private static bool StartsWith(string source, int position, string value) =>
        position + value.Length <= source.Length &&
        string.CompareOrdinal(source, position, value, 0, value.Length) == 0;

    private static bool StartsWithIgnoreCase(string source, int position, string value) =>
        position + value.Length <= source.Length &&
        string.Compare(source, position, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;
}
