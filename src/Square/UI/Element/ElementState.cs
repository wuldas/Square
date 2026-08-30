namespace Square.UI;

/// <summary>
/// 元素交互/伪类状态位（Square 扩展，供 CSS 如 <c>:hover</c>/<c>:focus</c> 匹配）。
/// </summary>
[Flags]
public enum ElementState : byte
{
    /// <summary>无状态。</summary>
    None = 0,
    /// <summary>指针悬停（:hover）。</summary>
    Hover = 1,
    /// <summary>键盘焦点（:focus）。</summary>
    Focus = 2,
    /// <summary>激活按下（:active）。</summary>
    Active = 4,
    /// <summary>禁用（:disabled）。</summary>
    Disabled = 8,
    /// <summary>选中（:checked）。</summary>
    Checked = 16,
    /// <summary>空内容（:empty 等）。</summary>
    Empty = 32,
    /// <summary>弹出内容已打开（:open）。</summary>
    Open = 64,
    /// <summary>键盘可见焦点（:focus-visible）。</summary>
    FocusVisible = 128
}

/// <summary><see cref="ElementState"/> 扩展方法。</summary>
public static class ElementStateExtensions
{
    /// <summary>是否包含指定标志。</summary>
    public static bool Has(this ElementState state, ElementState flag) => (state & flag) != 0;
}
