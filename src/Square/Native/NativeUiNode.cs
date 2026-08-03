using Square.Graphics;
using Square.UI;

namespace Square.Native;

/// <summary>保留控件语义的只读 UI 树快照，供 HTML 等原生 UI 输出目标使用。</summary>
public sealed class NativeUiNode
{
    /// <summary>源控件种类。</summary>
    public required string Kind { get; init; }

    /// <summary>源元素。Adapter 只能读取，不应直接修改元素树。</summary>
    public required Element SourceElement { get; init; }

    /// <summary>Square 布局结果；语义 HTML 默认不据此进行绝对定位。</summary>
    public Rect Bounds { get; init; }

    /// <summary>快照时已应用的最终样式。</summary>
    public IReadOnlyDictionary<string, string> Style { get; init; } = new Dictionary<string, string>();

    /// <summary>元素 class 列表。</summary>
    public IReadOnlyList<string> Classes { get; init; } = [];

    /// <summary>可见子元素快照。</summary>
    public IReadOnlyList<NativeUiNode> Children { get; init; } = [];
}
