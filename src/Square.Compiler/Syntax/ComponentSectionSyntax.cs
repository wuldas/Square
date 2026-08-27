using Square.Compiler.LanguageServices;
using Square.Compiler.Parser;
using Square.Compiler.ParserCore;
using Square.Compiler.Template.Ir;
using Square.Compiler.Template.Lowering;

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
        bool isClosed,
        ComponentDialect dialect,
        bool tolerant)
        : base(ComponentSectionKind.Template, fullRange, openingTagRange, contentRange, closingTagRange, contentText, isClosed)
    {
        if (dialect == ComponentDialect.Sqv)
        {
            try
            {
                SqvSyntax = SqvTemplateSyntaxParser.Parse(contentText, contentRange.Offset, tolerant);
            }
            catch (SqxParseException)
            {
                SqvSyntax = new SqvTemplateSyntax(Array.Empty<SqvSyntaxNode>());
                Ir = new TemplateIrDocument(Array.Empty<TemplateIrNode>());
                return;
            }
            try
            {
                Ir = SqvTemplateLowerer.Lower(SqvSyntax);
            }
            catch (SqxParseException)
            {
                Ir = new TemplateIrDocument(Array.Empty<TemplateIrNode>());
            }
        }
        else
        {
            try
            {
                SqxSyntax = SqxTemplateSyntaxParser.Parse(contentText, contentRange.Offset, tolerant);
            }
            catch (CoreParseException)
            {
                SqxSyntax = new SqxTemplateSyntax(Array.Empty<SqxSyntaxNode>());
                Ir = new TemplateIrDocument(Array.Empty<TemplateIrNode>());
                return;
            }
            catch (SqxParseException)
            {
                SqxSyntax = new SqxTemplateSyntax(Array.Empty<SqxSyntaxNode>());
                Ir = new TemplateIrDocument(Array.Empty<TemplateIrNode>());
                return;
            }
            try
            {
                Ir = SqxTemplateLowerer.Lower(SqxSyntax);
            }
            catch (SqxParseException)
            {
                Ir = new TemplateIrDocument(Array.Empty<TemplateIrNode>());
            }
        }
    }

    public SqxTemplateSyntax SqxSyntax { get; }
    public SqvTemplateSyntax SqvSyntax { get; }
    public TemplateIrDocument Ir { get; }
}
