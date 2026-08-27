using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal sealed class CssSyntaxDiagnostic
{
    public CssSyntaxDiagnostic(string message, SquareSourceRange range)
    {
        Message = message ?? string.Empty;
        Range = range;
    }

    public string Message { get; }
    public SquareSourceRange Range { get; }
}

internal sealed class CssStyleSheetSyntax
{
    public CssStyleSheetSyntax(
        IReadOnlyList<CssRuleSyntax> rules,
        IReadOnlyList<CssAtRuleSyntax> atRules,
        IReadOnlyList<CssSyntaxDiagnostic> diagnostics)
    {
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        AtRules = atRules ?? throw new ArgumentNullException(nameof(atRules));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public IReadOnlyList<CssRuleSyntax> Rules { get; }
    public IReadOnlyList<CssAtRuleSyntax> AtRules { get; }
    public IReadOnlyList<CssSyntaxDiagnostic> Diagnostics { get; }
}

internal sealed class CssSelectorSyntax
{
    public CssSelectorSyntax(string text, SquareSourceRange range)
    {
        Text = text ?? string.Empty;
        Range = range;
        Steps = CssSelectorSyntaxParser.Parse(Text, range.Offset);
    }

    public string Text { get; }
    public SquareSourceRange Range { get; }
    public IReadOnlyList<CssCompoundStepSyntax> Steps { get; }
}

internal sealed class CssRuleSyntax
{
    public CssRuleSyntax(
        IReadOnlyList<CssSelectorSyntax> selectors,
        IReadOnlyList<CssDeclarationSyntax> declarations,
        SquareSourceRange fullRange,
        SquareSourceRange selectorRange,
        SquareSourceRange blockRange)
    {
        Selectors = selectors ?? throw new ArgumentNullException(nameof(selectors));
        Declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
        FullRange = fullRange;
        SelectorRange = selectorRange;
        BlockRange = blockRange;
    }

    public IReadOnlyList<CssSelectorSyntax> Selectors { get; }
    public IReadOnlyList<CssDeclarationSyntax> Declarations { get; }
    public SquareSourceRange FullRange { get; }
    public SquareSourceRange SelectorRange { get; }
    public SquareSourceRange BlockRange { get; }
}

internal sealed class CssDeclarationSyntax
{
    public CssDeclarationSyntax(
        string property,
        string value,
        bool important,
        SquareSourceRange fullRange,
        SquareSourceRange propertyRange,
        SquareSourceRange valueRange)
    {
        Property = property ?? string.Empty;
        Value = value ?? string.Empty;
        Important = important;
        FullRange = fullRange;
        PropertyRange = propertyRange;
        ValueRange = valueRange;
    }

    public string Property { get; }
    public string Value { get; }
    public bool Important { get; }
    public SquareSourceRange FullRange { get; }
    public SquareSourceRange PropertyRange { get; }
    public SquareSourceRange ValueRange { get; }
}

internal sealed class CssAtRuleSyntax
{
    public CssAtRuleSyntax(
        string name,
        string prelude,
        SquareSourceRange fullRange,
        SquareSourceRange preludeRange,
        SquareSourceRange blockRange,
        IReadOnlyList<CssRuleSyntax> rules,
        IReadOnlyList<CssDeclarationSyntax> declarations,
        IReadOnlyList<CssAtRuleSyntax> atRules)
    {
        Name = name ?? string.Empty;
        Prelude = prelude ?? string.Empty;
        FullRange = fullRange;
        PreludeRange = preludeRange;
        BlockRange = blockRange;
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        Declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
        AtRules = atRules ?? throw new ArgumentNullException(nameof(atRules));
    }

    public string Name { get; }
    public string Prelude { get; }
    public SquareSourceRange FullRange { get; }
    public SquareSourceRange PreludeRange { get; }
    public SquareSourceRange BlockRange { get; }
    public IReadOnlyList<CssRuleSyntax> Rules { get; }
    public IReadOnlyList<CssDeclarationSyntax> Declarations { get; }
    public IReadOnlyList<CssAtRuleSyntax> AtRules { get; }
}
