using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal static class ScriptMetadataParser
{
    private static readonly HashSet<string> SupportedNames = new HashSet<string>(
        new[] { "lang", "namespace", "name", "access" },
        StringComparer.OrdinalIgnoreCase);

    public static ScriptMetadataSyntax Parse(string openingTagText, SquareSourceRange openingTagRange)
    {
        openingTagText ??= string.Empty;
        var attributes = new List<ScriptAttributeSyntax>();
        var diagnostics = new List<ScriptMetadataDiagnostic>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = FindAttributeStart(openingTagText);

        while (position < openingTagText.Length)
        {
            SkipWhitespace(openingTagText, ref position);
            if (position >= openingTagText.Length || openingTagText[position] == '>') break;
            if (openingTagText[position] == '/' && position + 1 < openingTagText.Length && openingTagText[position + 1] == '>') break;

            var nameStart = position;
            if (!IsNameStart(openingTagText[position]))
            {
                diagnostics.Add(CreateDiagnostic(
                    ScriptMetadataDiagnosticKind.InvalidAttribute,
                    "Invalid script metadata",
                    openingTagRange,
                    position,
                    1));
                position++;
                continue;
            }
            position++;
            while (position < openingTagText.Length && IsNamePart(openingTagText[position])) position++;
            var nameEnd = position;
            var name = openingTagText.Substring(nameStart, nameEnd - nameStart);

            SkipWhitespace(openingTagText, ref position);
            if (position >= openingTagText.Length || openingTagText[position] != '=')
            {
                diagnostics.Add(CreateDiagnostic(
                    ScriptMetadataDiagnosticKind.InvalidAttribute,
                    "Script metadata '" + name + "' requires a quoted value",
                    openingTagRange,
                    nameStart,
                    nameEnd - nameStart));
                SkipToNextAttribute(openingTagText, ref position);
                continue;
            }
            position++;
            SkipWhitespace(openingTagText, ref position);
            if (position >= openingTagText.Length || openingTagText[position] is not ('"' or '\''))
            {
                diagnostics.Add(CreateDiagnostic(
                    ScriptMetadataDiagnosticKind.InvalidAttribute,
                    "Script metadata '" + name + "' requires a quoted value",
                    openingTagRange,
                    nameStart,
                    Math.Max(1, position - nameStart)));
                SkipToNextAttribute(openingTagText, ref position);
                continue;
            }

            var quote = openingTagText[position++];
            var valueStart = position;
            while (position < openingTagText.Length && openingTagText[position] != quote) position++;
            if (position >= openingTagText.Length)
            {
                diagnostics.Add(CreateDiagnostic(
                    ScriptMetadataDiagnosticKind.InvalidAttribute,
                    "Unclosed script metadata value for '" + name + "'",
                    openingTagRange,
                    valueStart,
                    openingTagText.Length - valueStart));
                break;
            }
            var valueEnd = position;
            var value = openingTagText.Substring(valueStart, valueEnd - valueStart);
            position++;
            var fullEnd = position;
            var attribute = new ScriptAttributeSyntax(
                name,
                value,
                AbsoluteRange(openingTagRange, nameStart, fullEnd - nameStart),
                AbsoluteRange(openingTagRange, nameStart, nameEnd - nameStart),
                AbsoluteRange(openingTagRange, valueStart, valueEnd - valueStart));
            attributes.Add(attribute);

            if (!SupportedNames.Contains(name))
            {
                diagnostics.Add(new ScriptMetadataDiagnostic(
                    ScriptMetadataDiagnosticKind.UnknownAttribute,
                    "Unknown script metadata '" + name + "'",
                    attribute.NameRange));
            }
            if (!seen.Add(name))
            {
                diagnostics.Add(new ScriptMetadataDiagnostic(
                    ScriptMetadataDiagnosticKind.DuplicateAttribute,
                    "Duplicate script metadata '" + name + "'",
                    attribute.NameRange));
            }
        }

        var language = GetFirstValue(attributes, "lang") ?? "csharp";
        var namespaceName = GetFirstValue(attributes, "namespace");
        var componentName = GetFirstValue(attributes, "name");
        var access = GetFirstValue(attributes, "access") ?? "public";
        AddValueDiagnostics(
            attributes,
            diagnostics,
            language,
            namespaceName,
            componentName,
            access,
            openingTagRange);
        return new ScriptMetadataSyntax(
            language,
            namespaceName,
            componentName,
            access,
            attributes.ToArray(),
            diagnostics.ToArray());
    }

    private static void AddValueDiagnostics(
        IReadOnlyList<ScriptAttributeSyntax> attributes,
        List<ScriptMetadataDiagnostic> diagnostics,
        string language,
        string namespaceName,
        string componentName,
        string access,
        SquareSourceRange openingTagRange)
    {
        if (!string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase))
        {
            var attribute = FindFirst(attributes, "lang");
            diagnostics.Add(new ScriptMetadataDiagnostic(
                ScriptMetadataDiagnosticKind.UnsupportedLanguage,
                "Unsupported script language '" + language + "'",
                attribute?.ValueRange ?? openingTagRange));
        }
        if (access != "public" && access != "internal")
        {
            var attribute = FindFirst(attributes, "access");
            diagnostics.Add(new ScriptMetadataDiagnostic(
                ScriptMetadataDiagnosticKind.InvalidAccess,
                "Script access must be 'public' or 'internal'",
                attribute?.ValueRange ?? openingTagRange));
        }
        if (!string.IsNullOrEmpty(namespaceName) && !IsValidNamespace(namespaceName))
        {
            var attribute = FindFirst(attributes, "namespace");
            diagnostics.Add(new ScriptMetadataDiagnostic(
                ScriptMetadataDiagnosticKind.InvalidNamespace,
                "Invalid script namespace '" + namespaceName + "'",
                attribute?.ValueRange ?? openingTagRange));
        }
        if (!string.IsNullOrEmpty(componentName) &&
            !Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(componentName))
        {
            var attribute = FindFirst(attributes, "name");
            diagnostics.Add(new ScriptMetadataDiagnostic(
                ScriptMetadataDiagnosticKind.InvalidComponentName,
                "Invalid component name '" + componentName + "'",
                attribute?.ValueRange ?? openingTagRange));
        }
    }

    private static bool IsValidNamespace(string namespaceName)
    {
        var parts = namespaceName.Split('.');
        if (parts.Length == 0) return false;
        for (var index = 0; index < parts.Length; index++)
        {
            if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(parts[index]))
                return false;
        }
        return true;
    }

    private static ScriptAttributeSyntax FindFirst(IReadOnlyList<ScriptAttributeSyntax> attributes, string name)
    {
        for (var index = 0; index < attributes.Count; index++)
        {
            if (string.Equals(attributes[index].Name, name, StringComparison.OrdinalIgnoreCase))
                return attributes[index];
        }
        return null;
    }

    private static string GetFirstValue(IReadOnlyList<ScriptAttributeSyntax> attributes, string name) =>
        FindFirst(attributes, name)?.Value;

    private static int FindAttributeStart(string openingTagText)
    {
        var position = openingTagText.IndexOf("script", StringComparison.OrdinalIgnoreCase);
        return position < 0 ? openingTagText.Length : position + "script".Length;
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
    }

    private static void SkipToNextAttribute(string text, ref int position)
    {
        while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] != '>') position++;
    }

    private static bool IsNameStart(char value) => char.IsLetter(value) || value == '_';

    private static bool IsNamePart(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-';

    private static ScriptMetadataDiagnostic CreateDiagnostic(
        ScriptMetadataDiagnosticKind kind,
        string message,
        SquareSourceRange openingTagRange,
        int relativeOffset,
        int length) =>
        new ScriptMetadataDiagnostic(
            kind,
            message,
            AbsoluteRange(openingTagRange, relativeOffset, Math.Max(0, length)));

    private static SquareSourceRange AbsoluteRange(
        SquareSourceRange openingTagRange,
        int relativeOffset,
        int length) =>
        new SquareSourceRange(openingTagRange.Offset + relativeOffset, length);
}
