namespace Square.UI;

/// <summary>元素失效标志位（Square 扩展；用于决定引擎重算范围）。</summary>
[Flags]
public enum ElementInvalidation
{
    /// <summary>无失效。</summary>
    None = 0,
    /// <summary>需要重绘。</summary>
    Paint = 1 << 0,
    /// <summary>需要重新布局。</summary>
    Layout = 1 << 1,
    /// <summary>样式变更需要重新匹配。</summary>
    Style = 1 << 2,
    /// <summary>显示树结构需要重建。</summary>
    DisplayTree = 1 << 3,
    /// <summary>命中测试缓存需要失效。</summary>
    HitTest = 1 << 4,
    /// <summary>仅复合视觉状态变更，复用已有绘制命令。</summary>
    Composite = 1 << 5
}