using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal enum CssSimpleSelectorKind
{
    Type,
    Class,
    Id,
    Universal,
    PseudoClass,
    PseudoElement,
    Attribute
}

internal enum CssAttributeSelectorOperator
{
    Presence,
    Equals,
    Includes,
    DashMatch,
    PrefixMatch,
    SuffixMatch,
    SubstringMatch,
    Invalid
}

internal enum CssAttributeCaseSensitivity
{
    Default,
    Insensitive,
    Sensitive
}

internal enum CssCombinator
{
    Descendant,
    Child,
    Adjacent,
    GeneralSibling
}

internal sealed class CssSimpleSelectorSyntax
{
    public CssSimpleSelectorSyntax(
        CssSimpleSelectorKind kind,
        string name,
        SquareSourceRange range,
        CssAttributeSelectorOperator attributeOperator = CssAttributeSelectorOperator.Presence,
        string attributeValue = null,
        CssAttributeCaseSensitivity attributeCaseSensitivity = CssAttributeCaseSensitivity.Default)
    {
        Kind = kind;
        Name = name ?? string.Empty;
        Range = range;
        AttributeOperator = attributeOperator;
        AttributeValue = attributeValue;
        AttributeCaseSensitivity = attributeCaseSensitivity;
    }

    public CssSimpleSelectorKind Kind { get; }
    public string Name { get; }
    public SquareSourceRange Range { get; }
    public CssAttributeSelectorOperator AttributeOperator { get; }
    public string AttributeValue { get; }
    public CssAttributeCaseSensitivity AttributeCaseSensitivity { get; }
}

internal sealed class CssCompoundStepSyntax
{
    public CssCompoundStepSyntax(
        IReadOnlyList<CssSimpleSelectorSyntax> parts,
        CssCombinator combinator,
        SquareSourceRange range)
    {
        Parts = parts ?? throw new ArgumentNullException(nameof(parts));
        Combinator = combinator;
        Range = range;
    }

    public IReadOnlyList<CssSimpleSelectorSyntax> Parts { get; }
    public CssCombinator Combinator { get; }
    public SquareSourceRange Range { get; }
}
