using Square.Events;

namespace Square.UI;

/// <summary>
/// DOM 树节点基类（对齐 <c>Node</c>）。
/// 继承链：<see cref="EventTarget"/> → <see cref="Node"/> → <see cref="Element"/> | <see cref="Document"/>。
/// </summary>
public abstract class Node : EventTarget
{
    /// <summary>
    /// 节点类型（对齐 <c>nodeType</c> 常量子集）。
    /// </summary>
    public enum NodeType : ushort
    {
        /// <summary>元素节点。</summary>
        Element = 1,
        /// <summary>文本节点。</summary>
        Text = 3,
        /// <summary>文档节点。</summary>
        Document = 9
    }

    /// <summary>所属文档（对齐 <c>ownerDocument</c>；Document 自身通常为 null）。</summary>
    public Document? OwnerDocument { get; internal set; }

    /// <summary>
    /// 父节点（对齐 <c>parentNode</c>）。
    /// 子元素的父为 <see cref="Element"/>；documentElement 可仅通过 <see cref="OwnerDocument"/> 关联文档（ParentNode 为 null）。
    /// </summary>
    public Node? ParentNode { get; internal set; }

    /// <summary>父元素（对齐 <c>parentElement</c>）；父不是 Element 时为 null。</summary>
    public Element? ParentElement => ParentNode as Element;

    /// <summary>节点类型（对齐 <c>nodeType</c>）。</summary>
    public abstract NodeType NodeTypeValue { get; }

    /// <summary>节点名（对齐 <c>nodeName</c>：元素为标签名，文档为 <c>#document</c>）。</summary>
    public abstract string NodeName { get; }

    /// <summary>
    /// 事件路径父目标：先 <see cref="ParentNode"/>；无语义父节点的视觉 sidecar（HTML host 的
    /// 原生代理/列表标记）沿 <see cref="Element.VisualParent"/> 回到宿主；否则（非 Document）为 <see cref="OwnerDocument"/>。
    /// </summary>
    protected override EventTarget? GetEventParent()
    {
        if (ParentNode != null) return ParentNode;
        if (this is Element { VisualParent: { } visualParent }) return visualParent;
        if (this is Document) return null;
        return OwnerDocument;
    }
}
