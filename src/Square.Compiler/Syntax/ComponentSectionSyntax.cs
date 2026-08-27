using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal abstract class ComponentSectionSyntax
{
    protected ComponentSectionSyntax(
        ComponentSectionKind kind,
        SquareSourceRange fullRange,
        SquareSourceRange openingTagRange,
        SquareSourceRange contentRange,
        SquareSourceRange closingTagRange,
        string contentText,
        bool isClosed)
    {
        Kind = kind;
        FullRange = fullRange;
        OpeningTagRange = openingTagRange;
        ContentRange = contentRange;
        ClosingTagRange = closingTagRange;
        ContentText = contentText ?? string.Empty;
        IsClosed = isClosed;
    }

    public ComponentSectionKind Kind { get; }
    public SquareSourceRange FullRange { get; }
    public SquareSourceRange OpeningTagRange { get; }
    public SquareSourceRange ContentRange { get; }
    public SquareSourceRange ClosingTagRange { get; }
    public string ContentText { get; }
    public bool IsClosed { get; }
}

internal sealed class TemplateSectionSyntax : ComponentSectionSyntax
{
    public TemplateSectionSyntax(
        SquareSourceRange fullRange,
        SquareSourceRange openingTagRange,
        SquareSourceRange contentRange,
        SquareSourceRange closingTagRange,
        string contentText,
        bool isClosed)
        : base(ComponentSectionKind.Template, fullRange, openingTagRange, contentRange, closingTagRange, contentText, isClosed)
    {
    }
}

internal sealed class StyleSectionSyntax : ComponentSectionSyntax
{
    public StyleSectionSyntax(
        SquareSourceRange fullRange,
        SquareSourceRange openingTagRange,
        SquareSourceRange contentRange,
        SquareSourceRange closingTagRange,
        string contentText,
        bool isClosed)
        : base(ComponentSectionKind.Style, fullRange, openingTagRange, contentRange, closingTagRange, contentText, isClosed)
    {
    }
}
