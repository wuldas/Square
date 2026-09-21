using Square.Compiler.LanguageServices;

namespace Square.Compiler.Syntax;

internal sealed class SqxTemplateSyntax
{
    public SqxTemplateSyntax(IReadOnlyList<SqxSyntaxNode> roots, IReadOnlyList<SquareDiagnostic> diagnostics = null)
    {
        Roots = roots ?? throw new ArgumentNullException(nameof(roots));
        Diagnostics = diagnostics ?? Array.Empty<SquareDiagnostic>();
    }

    public IReadOnlyList<SqxSyntaxNode> Roots { get; }
    /// <summary>宽容解析记录到的失败事实（严格模式对应抛出同一个异常）。</summary>
    public IReadOnlyList<SquareDiagnostic> Diagnostics { get; }
}

internal abstract class SqxSyntaxNode
{
    protected SqxSyntaxNode(SquareSourceRange origin) { Origin = origin; }
    public SquareSourceRange Origin { get; }
}

internal sealed class SqxElementSyntax : SqxSyntaxNode
{
    public SqxElementSyntax(
        string tagName,
        IReadOnlyList<SqxAttributeSyntax> attributes,
        IReadOnlyList<SqxSyntaxNode> children,
        bool isSelfClosing,
        SquareSourceRange origin,
        SquareSourceRange tagNameRange = default,
        SquareSourceRange closeTagNameRange = default)
        : base(origin)
    {
        TagName = tagName ?? string.Empty;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        Children = children ?? throw new ArgumentNullException(nameof(children));
        IsSelfClosing = isSelfClosing;
        TagNameRange = tagNameRange;
        CloseTagNameRange = closeTagNameRange;
    }

    public string TagName { get; }
    public IReadOnlyList<SqxAttributeSyntax> Attributes { get; }
    public IReadOnlyList<SqxSyntaxNode> Children { get; }
    public bool IsSelfClosing { get; }
    /// <summary>开标签名的精确源范围（不含 `<` 与空白）。</summary>
    public SquareSourceRange TagNameRange { get; }
    /// <summary>闭标签名的精确源范围；自闭合或无闭标签时为 default。</summary>
    public SquareSourceRange CloseTagNameRange { get; }
}

internal sealed class SqxTextSyntax : SqxSyntaxNode
{
    public SqxTextSyntax(string text, SquareSourceRange origin) : base(origin) { Text = text ?? string.Empty; }
    public string Text { get; }
}

internal sealed class SqxExpressionSyntax : SqxSyntaxNode
{
    public SqxExpressionSyntax(string expression, SquareSourceRange origin) : base(origin)
    {
        Expression = expression ?? string.Empty;
    }
    public string Expression { get; }
}

internal sealed class SqxAttributeSyntax
{
    public SqxAttributeSyntax(
        string name,
        string value,
        bool isExpression,
        SquareSourceRange fullRange,
        SquareSourceRange nameRange,
        SquareSourceRange valueRange,
        IReadOnlyList<SqxSyntaxNode> fragmentNodes = null)
    {
        Name = name ?? string.Empty;
        Value = value;
        IsExpression = isExpression;
        FullRange = fullRange;
        NameRange = nameRange;
        ValueRange = valueRange;
        FragmentNodes = fragmentNodes;
    }

    public string Name { get; }
    public string Value { get; }
    public bool IsExpression { get; }
    public SquareSourceRange FullRange { get; }
    public SquareSourceRange NameRange { get; }
    public SquareSourceRange ValueRange { get; }
    public IReadOnlyList<SqxSyntaxNode> FragmentNodes { get; }
}
