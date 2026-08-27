using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal sealed class ScriptSectionSyntax : ComponentSectionSyntax
{
    public ScriptSectionSyntax(
        SquareSourceRange fullRange,
        SquareSourceRange openingTagRange,
        SquareSourceRange contentRange,
        SquareSourceRange closingTagRange,
        string openingTagText,
        string contentText,
        bool isClosed)
        : base(
            ComponentSectionKind.Script,
            fullRange,
            openingTagRange,
            contentRange,
            closingTagRange,
            contentText,
            isClosed)
    {
        Metadata = ScriptMetadataParser.Parse(openingTagText, openingTagRange);
        CSharp = CSharpScriptSyntaxParser.Parse(contentText, contentRange.Offset);
    }

    public ScriptMetadataSyntax Metadata { get; }
    public CSharpScriptSyntax CSharp { get; }
}
