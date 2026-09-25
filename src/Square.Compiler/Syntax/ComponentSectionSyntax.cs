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

internal sealed class TemplateXmlnsDeclaration
{
    internal TemplateXmlnsDeclaration(string prefix, string namespaceUri, SquareSourceRange range)
    {
        Prefix = prefix;
        NamespaceUri = namespaceUri;
        Range = range;
    }

    internal string Prefix { get; }
    internal string NamespaceUri { get; }
    internal SquareSourceRange Range { get; }
}

internal sealed class TemplateSectionSyntax : ComponentSectionSyntax
{
    private readonly List<SquareDiagnostic> _diagnostics = new();

    public TemplateSectionSyntax(
        SquareSourceRange fullRange,
        SquareSourceRange openingTagRange,
        SquareSourceRange contentRange,
        SquareSourceRange closingTagRange,
        string contentText,
        bool isClosed,
        ComponentDialect dialect,
        bool tolerant,
        string openingTagText,
        bool parseTemplateBody = true,
        TemplateCatalog catalog = null,
        TemplateResolutionContext context = null)
        : base(ComponentSectionKind.Template, fullRange, openingTagRange, contentRange, closingTagRange, contentText, isClosed)
    {
        XmlnsDeclarations = ReadXmlnsDeclarations(openingTagText ?? string.Empty, openingTagRange.Offset);
        if (!parseTemplateBody)
        {
            Ir = new TemplateIrDocument(Array.Empty<TemplateIrNode>());
            return;
        }
        if (dialect == ComponentDialect.Sqv)
        {
            try
            {
                SqvSyntax = SqvTemplateSyntaxParser.Parse(contentText, contentRange.Offset, tolerant, catalog, context);
            }
            catch (SqxParseException exception)
            {
                _diagnostics.Add(ToDiagnostic(exception, "SQV0001"));
                SqvSyntax = new SqvTemplateSyntax(Array.Empty<SqvSyntaxNode>());
            }
            _diagnostics.AddRange(SqvSyntax.Diagnostics);
            try
            {
                Ir = SqvTemplateLowerer.Lower(SqvSyntax);
            }
            catch (SqxParseException exception)
            {
                _diagnostics.Add(ToDiagnostic(exception, "SQV0001"));
                Ir = new TemplateIrDocument(Array.Empty<TemplateIrNode>());
            }
        }
        else
        {
            try
            {
                SqxSyntax = SqxTemplateSyntaxParser.Parse(contentText, contentRange.Offset, tolerant, catalog, context);
            }
            catch (CoreParseException exception)
            {
                _diagnostics.Add(ToDiagnostic(exception, "SQX0001", contentRange.Offset));
                SqxSyntax = new SqxTemplateSyntax(Array.Empty<SqxSyntaxNode>());
            }
            catch (SqxParseException exception)
            {
                _diagnostics.Add(ToDiagnostic(exception, "SQX0001"));
                SqxSyntax = new SqxTemplateSyntax(Array.Empty<SqxSyntaxNode>());
            }
            _diagnostics.AddRange(SqxSyntax.Diagnostics);
            try
            {
                Ir = SqxTemplateLowerer.Lower(SqxSyntax);
            }
            catch (SqxParseException exception)
            {
                _diagnostics.Add(ToDiagnostic(exception, "SQX0001"));
                Ir = new TemplateIrDocument(Array.Empty<TemplateIrNode>());
            }
        }
    }

    public SqxTemplateSyntax SqxSyntax { get; }
    public SqvTemplateSyntax SqvSyntax { get; }
    public TemplateIrDocument Ir { get; }
    public IReadOnlyList<TemplateXmlnsDeclaration> XmlnsDeclarations { get; }

    /// <summary>模板内容的失败事实（严格模式对应抛出同一个异常）；宽容模式保留可恢复语法树。</summary>
    public IReadOnlyList<SquareDiagnostic> Diagnostics => _diagnostics;

    public bool HasErrors => _diagnostics.Count > 0;
    private static IReadOnlyList<TemplateXmlnsDeclaration> ReadXmlnsDeclarations(string opening, int offset)
    {
        var declarations = new List<TemplateXmlnsDeclaration>();
        var position = 1;
        while (position < opening.Length && !char.IsWhiteSpace(opening[position]) && opening[position] != '>') position++;
        while (position < opening.Length)
        {
            while (position < opening.Length && char.IsWhiteSpace(opening[position])) position++;
            if (position >= opening.Length || opening[position] is '/' or '>') break;
            var nameStart = position;
            while (position < opening.Length && !char.IsWhiteSpace(opening[position]) && opening[position] is not ('=' or '/' or '>')) position++;
            if (position == nameStart) { position++; continue; }
            var name = opening.Substring(nameStart, position - nameStart);
            while (position < opening.Length && char.IsWhiteSpace(opening[position])) position++;
            var value = string.Empty;
            if (position < opening.Length && opening[position] == '=')
            {
                position++;
                while (position < opening.Length && char.IsWhiteSpace(opening[position])) position++;
                if (position < opening.Length && opening[position] is '"' or '\'')
                {
                    var quote = opening[position++];
                    var valueStart = position;
                    while (position < opening.Length && opening[position] != quote) position++;
                    value = opening.Substring(valueStart, position - valueStart);
                    if (position < opening.Length) position++;
                }
                else
                {
                    var valueStart = position;
                    while (position < opening.Length && !char.IsWhiteSpace(opening[position]) && opening[position] is not ('/' or '>')) position++;
                    value = opening.Substring(valueStart, position - valueStart);
                }
            }
            if (name == "xmlns" || name.StartsWith("xmlns:", StringComparison.Ordinal))
                declarations.Add(new TemplateXmlnsDeclaration(
                    name == "xmlns" ? string.Empty : name.Length == 6 ? ":" : name.Substring(6), value,
                    new SquareSourceRange(offset + nameStart, name.Length)));
        }
        return declarations;
    }

    private static SquareDiagnostic ToDiagnostic(SqxParseException exception, string fallbackId) =>
        new(string.IsNullOrWhiteSpace(exception.DiagnosticId) ? fallbackId : exception.DiagnosticId,
            SquareDiagnosticSeverity.Error,
            exception.Message,
            new SquareSourceRange(Math.Max(0, exception.Position), Math.Max(0, exception.Length)),
            string.Empty);

    private static SquareDiagnostic ToDiagnostic(CoreParseException exception, string fallbackId, int baseOffset) =>
        new(string.IsNullOrWhiteSpace(exception.DiagnosticId) ? fallbackId : exception.DiagnosticId,
            SquareDiagnosticSeverity.Error,
            exception.Message,
            new SquareSourceRange(baseOffset + Math.Max(0, exception.Position), Math.Max(0, exception.Length)),
            string.Empty);
}
