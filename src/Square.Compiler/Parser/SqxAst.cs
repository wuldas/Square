using Square.Compiler.Syntax;

namespace Square.Compiler.Parser
{
    /// <summary>
    /// AST 节点种类。结构原语统一为 <see cref="Directive"/>（由 DirectiveCatalog 识别）。
    /// </summary>
    internal enum SqxNodeKind
    {
        Element,
        Text,
        Expression,
        /// <summary>结构指令（Show/For/Switch/Match/Slot/Router/Route 等）。</summary>
        Directive
    }

    internal abstract class SqxNode
    {
        public SqxNodeKind Kind;
        public int Line;
        public int Column;
        public int Position;
    }

    internal class SqxElement : SqxNode
    {
        public string TagName = "";
        /// <summary>目录归一化之前的源标签名。</summary>
        public string OriginalTagName;
        /// <summary>开标签名的精确源偏移与长度。</summary>
        public int TagNamePosition;
        public int TagNameLength;
        /// <summary>闭标签名的精确源偏移与长度；无闭标签时为 -1 / 0。</summary>
        public int CloseTagNamePosition = -1;
        public int CloseTagNameLength;
        /// <summary>Catalog 归一化后的主标签（如 Outlet → Slot）；非指令时为 null。</summary>
        public string DirectiveId;
        public List<SqxAttribute> Attributes = new List<SqxAttribute>();
        public List<SqxNode> Children = new List<SqxNode>();
        public TemplateSlotScope SlotScope;
    }

    internal class SqxText : SqxNode
    {
        public string Text = "";
    }

    internal class SqxExpression : SqxNode
    {
        public string Expression = "";
    }

    internal class SqxAttribute
    {
        public string Name = "";
        public string RawValue;
        public bool IsExpression;
        public List<SqxNode> FragmentNodes;
        public int Line;
        public int Position;
        public int ValuePosition = -1;
        public int ValueLength;
        public bool IsModelEvent;
        /// <summary>模型写回的目标成员名（Value / IsChecked）；仅 IsModelEvent 时有值。</summary>
        public string ModelMemberName;
        /// <summary>模型写回需应用的修饰符（trim / number）；仅 IsModelEvent 时有值。</summary>
        public IReadOnlyList<string> ModelModifiers;
        public bool IsDynamicProperty;
        public bool IsDynamicEvent;
        public string ArgumentExpression;
    }

    internal class SqxTemplate
    {
        public List<SqxNode> Roots = new List<SqxNode>();
    }

    internal class SqxDocument
    {
        public ComponentDocumentSyntax Syntax;
        public string SourcePath = "";
        public string Name = "";
        public SqxTemplate Template = new SqxTemplate();
        public string Namespace;
        public string Access = "public";
    }
}
