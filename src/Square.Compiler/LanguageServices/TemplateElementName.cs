using Microsoft.CodeAnalysis.CSharp;

namespace Square.Compiler.LanguageServices;

internal enum TemplateElementNameStatus
{
    Valid,
    Invalid,
    Incomplete
}

internal enum TemplateElementNameKind
{
    Unqualified,
    Qualified,
    ClrExact,
    Local
}

internal readonly struct TemplateElementName
{
    private TemplateElementName(
        TemplateElementNameStatus status,
        TemplateElementNameKind kind,
        string prefix,
        string localName,
        string clrTypeName)
    {
        Status = status;
        Kind = kind;
        Prefix = prefix ?? string.Empty;
        LocalName = localName ?? string.Empty;
        ClrTypeName = clrTypeName ?? string.Empty;
    }

    internal TemplateElementNameStatus Status { get; }
    internal TemplateElementNameKind Kind { get; }
    internal string Prefix { get; }
    internal string LocalName { get; }
    internal string ClrTypeName { get; }

    internal static TemplateElementName Parse(string tagName, bool atEndOfInput)
    {
        tagName ??= string.Empty;
        if (tagName.StartsWith("global::", StringComparison.Ordinal))
        {
            var clrName = tagName.Substring("global::".Length);
            if (IsClrName(clrName))
                return Valid(TemplateElementNameKind.ClrExact, clrTypeName: clrName);
            if (atEndOfInput && (clrName.Length == 0 || IsIncompleteClrName(clrName)))
                return Incomplete(TemplateElementNameKind.ClrExact, clrTypeName: clrName);
            return Invalid();
        }

        var firstColon = tagName.IndexOf(':');
        if (firstColon >= 0)
        {
            if (firstColon != tagName.LastIndexOf(':')) return Invalid();
            var prefix = tagName.Substring(0, firstColon);
            var localName = tagName.Substring(firstColon + 1);
            if (!IsMarkupName(prefix)) return Invalid();
            var kind = prefix.Equals("local", StringComparison.OrdinalIgnoreCase)
                ? TemplateElementNameKind.Local
                : TemplateElementNameKind.Qualified;
            if (localName.Length == 0)
                return atEndOfInput ? Incomplete(kind, prefix, localName) : Invalid();
            var validLocal = kind == TemplateElementNameKind.Local
                ? IsClrName(localName)
                : IsMarkupName(localName);
            return validLocal ? Valid(kind, prefix, localName, kind == TemplateElementNameKind.Local ? localName : string.Empty) : Invalid();
        }

        if (tagName.IndexOf('.') >= 0)
        {
            if (IsClrName(tagName)) return Valid(TemplateElementNameKind.ClrExact, clrTypeName: tagName);
            if (atEndOfInput && IsIncompleteClrName(tagName))
                return Incomplete(TemplateElementNameKind.ClrExact, clrTypeName: tagName);
            return Invalid();
        }

        return IsMarkupName(tagName)
            ? Valid(TemplateElementNameKind.Unqualified, localName: tagName)
            : Invalid();
    }

    private static TemplateElementName Valid(
        TemplateElementNameKind kind,
        string prefix = "",
        string localName = "",
        string clrTypeName = "") =>
        new(TemplateElementNameStatus.Valid, kind, prefix, localName, clrTypeName);

    private static TemplateElementName Incomplete(
        TemplateElementNameKind kind,
        string prefix = "",
        string localName = "",
        string clrTypeName = "") =>
        new(TemplateElementNameStatus.Incomplete, kind, prefix, localName, clrTypeName);

    private static TemplateElementName Invalid() =>
        new(TemplateElementNameStatus.Invalid, TemplateElementNameKind.Unqualified, string.Empty, string.Empty, string.Empty);

    private static bool IsMarkupName(string value)
    {
        if (string.IsNullOrEmpty(value) || !(value[0] == '_' || char.IsLetter(value[0]))) return false;
        return value.Skip(1).All(character => character == '_' || character == '-' || char.IsLetterOrDigit(character));
    }

    private static bool IsClrName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Split('.').All(IsClrIdentifier);

    private static bool IsIncompleteClrName(string value)
    {
        if (!value.EndsWith(".", StringComparison.Ordinal)) return false;
        var prefix = value.Substring(0, value.Length - 1);
        return IsClrName(prefix);
    }

    private static bool IsClrIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value[0] == '@')
        {
            if (value.Length == 1) return false;
            value = value.Substring(1);
            if (!(value[0] == '_' || char.IsLetter(value[0]))) return false;
            return value.Skip(1).All(character => character == '_' || char.IsLetterOrDigit(character));
        }
        return SyntaxFacts.IsValidIdentifier(value);
    }
}
