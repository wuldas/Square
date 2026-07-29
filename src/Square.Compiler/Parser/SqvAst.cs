namespace Square.Compiler.Parser;

/// <summary>
/// 模板前端共享的循环 IR 节点。
/// </summary>
internal sealed class TemplateForDirective : SqxNode
{
    public string SourceExpression = "";
    public string ItemName = "item";
    public string IndexName;
    public string KeyExpression;
    public int KeyPosition;
    public List<SqxNode> Children = new();
}

/// <summary>
/// 模板前端共享的条件链 IR 节点。
/// 每个分支保存原始条件（else 分支条件为 null）与对应子树。
/// </summary>
internal sealed class TemplateIfChainDirective : SqxNode
{
    public List<TemplateIfBranch> Branches = new();
}

internal sealed class TemplateIfBranch
{
    public string Condition;
    public bool IsElse;
    public int Position;
    public List<SqxNode> Children = new();
}

/// <summary>模板 IR 中的作用域插槽绑定。</summary>
internal sealed class TemplateSlotScope
{
    public string WholePropsName;
    public List<TemplateSlotPropertyBinding> Properties = new();
    public int Position;
}

internal sealed class TemplateSlotPropertyBinding
{
    public string PropertyName = "";
    public string LocalName = "";
    public string TypeName;
    public int Position;
}
