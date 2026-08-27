using Square.Compiler.LanguageServices;

namespace Square.Compiler.Template.Ir;

internal abstract class TemplateIrNode
{
    protected TemplateIrNode(SquareSourceRange origin)
    {
        Origin = origin;
    }

    public SquareSourceRange Origin { get; }
}

internal sealed class TemplateIrElement : TemplateIrNode
{
    public TemplateIrElement(
        string tagName,
        IReadOnlyList<TemplateIrAttribute> attributes,
        IReadOnlyList<TemplateIrNode> children,
        SquareSourceRange origin)
        : base(origin)
    {
        TagName = tagName ?? string.Empty;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        Children = children ?? throw new ArgumentNullException(nameof(children));
    }

    public string TagName { get; }
    public IReadOnlyList<TemplateIrAttribute> Attributes { get; }
    public IReadOnlyList<TemplateIrNode> Children { get; }
}

internal sealed class TemplateIrText : TemplateIrNode
{
    public TemplateIrText(string text, SquareSourceRange origin) : base(origin)
    {
        Text = text ?? string.Empty;
    }

    public string Text { get; }
}

internal sealed class TemplateIrExpression : TemplateIrNode
{
    public TemplateIrExpression(string expression, SquareSourceRange origin) : base(origin)
    {
        Expression = expression ?? string.Empty;
    }

    public string Expression { get; }
}

internal sealed class TemplateIrFor : TemplateIrNode
{
    public TemplateIrFor(
        string sourceExpression,
        string itemName,
        string indexName,
        string keyExpression,
        IReadOnlyList<TemplateIrNode> children,
        SquareSourceRange origin,
        IReadOnlyList<TemplateIrNode> fallback = null)
        : base(origin)
    {
        SourceExpression = sourceExpression ?? string.Empty;
        ItemName = itemName ?? "item";
        IndexName = indexName;
        KeyExpression = keyExpression;
        Children = children ?? throw new ArgumentNullException(nameof(children));
        Fallback = fallback ?? Array.Empty<TemplateIrNode>();
    }

    public string SourceExpression { get; }
    public string ItemName { get; }
    public string IndexName { get; }
    public string KeyExpression { get; }
    public IReadOnlyList<TemplateIrNode> Children { get; }
    public IReadOnlyList<TemplateIrNode> Fallback { get; }
}

internal sealed class TemplateIrIfChain : TemplateIrNode
{
    public TemplateIrIfChain(
        IReadOnlyList<TemplateIrIfBranch> branches,
        SquareSourceRange origin)
        : base(origin)
    {
        Branches = branches ?? throw new ArgumentNullException(nameof(branches));
    }

    public IReadOnlyList<TemplateIrIfBranch> Branches { get; }
}

internal sealed class TemplateIrSlot : TemplateIrNode
{
    public TemplateIrSlot(
        string name,
        bool nameIsExpression,
        string scopeExpression,
        IReadOnlyList<TemplateIrNode> children,
        SquareSourceRange origin,
        TemplateIrSlotScope scope = null)
        : base(origin)
    {
        Name = name ?? string.Empty;
        NameIsExpression = nameIsExpression;
        ScopeExpression = scopeExpression;
        Scope = scope;
        Children = children ?? throw new ArgumentNullException(nameof(children));
    }

    public string Name { get; }
    public bool NameIsExpression { get; }
    public string ScopeExpression { get; }
    public TemplateIrSlotScope Scope { get; }
    public IReadOnlyList<TemplateIrNode> Children { get; }
}

internal sealed class TemplateIrSlotScope
{
    public TemplateIrSlotScope(
        string wholePropertiesName,
        IReadOnlyList<TemplateIrSlotBinding> properties,
        SquareSourceRange origin)
    {
        WholePropertiesName = wholePropertiesName;
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        Origin = origin;
    }

    public string WholePropertiesName { get; }
    public IReadOnlyList<TemplateIrSlotBinding> Properties { get; }
    public SquareSourceRange Origin { get; }
}

internal sealed class TemplateIrSlotBinding
{
    public TemplateIrSlotBinding(
        string propertyName,
        string localName,
        SquareSourceRange origin)
    {
        PropertyName = propertyName ?? string.Empty;
        LocalName = localName ?? string.Empty;
        Origin = origin;
    }

    public string PropertyName { get; }
    public string LocalName { get; }
    public string TypeName { get; set; }
    public SquareSourceRange Origin { get; }
}

internal sealed class TemplateIrAttribute
{
    public TemplateIrAttribute(
        string name,
        string value,
        bool isExpression,
        SquareSourceRange origin,
        TemplateIrAttributeKind kind = TemplateIrAttributeKind.Property,
        string argumentExpression = null,
        bool isModelEvent = false,
        IReadOnlyList<TemplateIrNode> fragmentNodes = null)
    {
        Name = name ?? string.Empty;
        Value = value;
        IsExpression = isExpression;
        Origin = origin;
        Kind = kind;
        ArgumentExpression = argumentExpression;
        IsModelEvent = isModelEvent;
        FragmentNodes = fragmentNodes;
    }

    public string Name { get; }
    public string Value { get; }
    public bool IsExpression { get; }
    public SquareSourceRange Origin { get; }
    public TemplateIrAttributeKind Kind { get; }
    public string ArgumentExpression { get; }
    public bool IsModelEvent { get; }
    public IReadOnlyList<TemplateIrNode> FragmentNodes { get; }
}

internal enum TemplateIrAttributeKind
{
    Property,
    Event,
    DynamicProperty,
    DynamicEvent,
    ObjectProperties,
    ObjectEvents
}

internal sealed class TemplateIrIfBranch
{
    public TemplateIrIfBranch(
        string condition,
        bool isElse,
        IReadOnlyList<TemplateIrNode> children,
        SquareSourceRange origin)
    {
        Condition = condition;
        IsElse = isElse;
        Children = children ?? throw new ArgumentNullException(nameof(children));
        Origin = origin;
    }

    public string Condition { get; }
    public bool IsElse { get; }
    public IReadOnlyList<TemplateIrNode> Children { get; }
    public SquareSourceRange Origin { get; }
}
