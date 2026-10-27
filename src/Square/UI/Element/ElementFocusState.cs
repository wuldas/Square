using Square.Events;

namespace Square.UI;

/// <summary>
/// Element 级可聚焦能力契约：供 <see cref="UIElement"/> 与 HTML host（<c>Square.Html.HTMLElement</c>）
/// 共用的最小焦点表面。平台层通过该接口把点击/键盘焦点路由到任意 Element 焦点目标，
/// 而不必依赖具体控件类型。SVG 等不可聚焦元素不实现该接口。
/// </summary>
internal interface IFocusableElement
{
    /// <summary>是否拥有键盘焦点。</summary>
    bool IsFocused { get; }

    /// <summary>是否启用；禁用的可聚焦元素不接受焦点。</summary>
    bool IsEnabled { get; }

    /// <summary>获取焦点：派发不冒泡的 <c>focus</c> 与冒泡的 <c>focusin</c>（对齐 DOM 焦点事件）。</summary>
    void Focus(bool focusVisible);

    /// <summary>失去焦点：派发不冒泡的 <c>blur</c> 与冒泡的 <c>focusout</c>。</summary>
    void Unfocus();
}

/// <summary>
/// Element 焦点事务的共享实现：守卫、before 钩子、伪状态与事件派发顺序与原
/// <see cref="UIElement"/> 焦点事务完全一致，供 <see cref="UIElement"/> 与 HTML host 复用，
/// 保证两类可聚焦元素具有相同的 focus/blur 行为与重入语义。
/// </summary>
internal sealed class ElementFocusState
{
    private bool _isFocused;
    private bool _isFocusing;
    private bool _isUnfocusing;

    /// <summary>是否拥有键盘焦点。</summary>
    public bool IsFocused => _isFocused;

    /// <summary>
    /// 聚焦事务：禁用或重入时忽略；已聚焦时仅同步 focus-visible。
    /// <paramref name="beforeFocus"/> 在状态与事件派发之前运行（控件的提交/准备钩子）。
    /// </summary>
    public void Focus(Element owner, bool focusVisible, Action? beforeFocus = null)
    {
        if (owner is not IFocusableElement { IsEnabled: true } || _isFocusing) return;
        if (_isFocused)
        {
            owner.SetState(ElementState.FocusVisible, focusVisible);
            return;
        }

        _isFocusing = true;
        try
        {
            beforeFocus?.Invoke();
            _isFocused = true;
            owner.SetState(ElementState.Focus, true);
            owner.SetState(ElementState.FocusVisible, focusVisible);
            owner.DispatchEvent(StandardEvents.CreateFocus());
            if (_isFocused) owner.DispatchEvent(StandardEvents.CreateFocusIn());
        }
        finally
        {
            _isFocusing = false;
        }
    }

    /// <summary>
    /// 失焦事务：<paramref name="beforeUnfocus"/> 内允许重新聚焦或取消失焦
    /// （守卫后重查 <see cref="IsFocused"/>）。
    /// </summary>
    public void Unfocus(Element owner, Action? beforeUnfocus = null)
    {
        if (!_isFocused || _isUnfocusing) return;
        _isUnfocusing = true;
        try
        {
            beforeUnfocus?.Invoke();
            if (!_isFocused) return;
            _isFocused = false;
            owner.SetState(ElementState.Focus, false);
            owner.SetState(ElementState.FocusVisible, false);
            owner.DispatchEvent(StandardEvents.CreateBlur());
            if (!_isFocused) owner.DispatchEvent(StandardEvents.CreateFocusOut());
        }
        finally
        {
            _isUnfocusing = false;
        }
    }

    /// <summary>元素从文档分离时复位焦点状态与事务守卫。</summary>
    public void Reset(Element owner)
    {
        _isFocused = false;
        owner.SetState(ElementState.Focus, false);
        owner.SetState(ElementState.FocusVisible, false);
        _isFocusing = false;
        _isUnfocusing = false;
    }
}
