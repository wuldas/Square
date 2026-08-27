using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal sealed class StyleSectionSyntax : ComponentSectionSyntax
{
    public StyleSectionSyntax(
        SquareSourceRange fullRange,
        SquareSourceRange openingTagRange,
        SquareSourceRange contentRange,
        SquareSourceRange closingTagRange,
        string contentText,
        bool isClosed)
        : base(
            ComponentSectionKind.Style,
            fullRange,
            openingTagRange,
            contentRange,
            closingTagRange,
            contentText,
            isClosed)
    {
        Css = CssSyntaxParser.Parse(contentText, contentRange.Offset);
    }

    public CssStyleSheetSyntax Css { get; }
}
