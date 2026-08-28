using Square.Events;
using Square.Graphics;

namespace Square.UI;

/// <summary>水平对齐方式（Square 布局扩展）。</summary>
public enum HorizontalAlignment { Left, Center, Right, Stretch }

/// <summary>垂直对齐方式（Square 布局扩展）。</summary>
public enum VerticalAlignment { Top, Center, Bottom, Stretch }

/// <summary>
/// Square 原生可交互控件基类：在 <see cref="Element"/> 之上增加盒模型尺寸、边距、插槽与焦点。
/// </summary>
public abstract class UIElement : Element
{
    private bool _isFocusing;
    private bool _isUnfocusing;

    /// <summary>具名/默认插槽集合（组件组合用，Square 扩展）。</summary>
    public SlotCollection Slots { get; } = new();

    /// <summary>水平对齐。</summary>
    public HorizontalAlignment HorizontalAlign { get; set; } = HorizontalAlignment.Stretch;

    /// <summary>垂直对齐。</summary>
    public VerticalAlignment VerticalAlign { get; set; } = VerticalAlignment.Stretch;

    /// <summary>固定宽度；<see cref="float.NaN"/> 表示自动。</summary>
    public float Width { get; set; } = float.NaN;

    /// <summary>固定高度；<see cref="float.NaN"/> 表示自动。</summary>
    public float Height { get; set; } = float.NaN;

    /// <summary>最小宽度。</summary>
    public float MinWidth { get; set; } = 0;

    /// <summary>最小高度。</summary>
    public float MinHeight { get; set; } = 0;

    /// <summary>最大宽度。</summary>
    public float MaxWidth { get; set; } = float.PositiveInfinity;

    /// <summary>最大高度。</summary>
    public float MaxHeight { get; set; } = float.PositiveInfinity;

    /// <summary>左边距。</summary>
    public float MarginLeft { get; set; }

    /// <summary>上边距。</summary>
    public float MarginTop { get; set; }

    /// <summary>右边距。</summary>
    public float MarginRight { get; set; }

    /// <summary>下边距。</summary>
    public float MarginBottom { get; set; }

    /// <summary>左内边距。</summary>
    public float PaddingLeft { get; set; }

    /// <summary>上内边距。</summary>
    public float PaddingTop { get; set; }

    /// <summary>右内边距。</summary>
    public float PaddingRight { get; set; }

    /// <summary>下内边距。</summary>
    public float PaddingBottom { get; set; }

    /// <summary>是否禁用（对齐表单 disabled 语义）。</summary>
    public bool IsDisabled
    {
        get => GetProperty<bool>(nameof(IsDisabled));
        set => SetProperty(nameof(IsDisabled), value);
    }

    /// <summary>是否启用（与 <see cref="IsDisabled"/> 互反）。</summary>
    public bool IsEnabled
    {
        get => !IsDisabled;
        set => IsDisabled = !value;
    }

    /// <summary>是否拥有键盘焦点。</summary>
    public bool IsFocused { get; private set; }

    /// <summary>悬停提示文本（Square 扩展）。</summary>
    public string? Tooltip { get; set; }

    /// <summary>按固定宽/最小最大约束宽度。</summary>
    protected float ConstrainWidth(float width)
    {
        if (!float.IsNaN(Width)) return Width;
        return Math.Clamp(width, MinWidth, MaxWidth);
    }

    /// <summary>按固定高/最小最大约束高度。</summary>
    protected float ConstrainHeight(float height)
    {
        if (!float.IsNaN(Height)) return Height;
        return Math.Clamp(height, MinHeight, MaxHeight);
    }

    /// <inheritdoc />
    public override bool HasCustomMeasure => true;

    /// <inheritdoc />
    public override Size Measure(Size availableSize)
    {
        var w = ConstrainWidth(availableSize.Width);
        var h = ConstrainHeight(availableSize.Height);
        return new Size(w, h);
    }

    /// <summary>
    /// 获取焦点：派发不冒泡的 <c>focus</c> 与冒泡的 <c>focusin</c>（对齐 DOM 焦点事件）。
    /// </summary>
    public void Focus()
    {
        if (!IsEnabled || IsFocused || _isFocusing) return;
        _isFocusing = true;
        try
        {
            OnBeforeFocus();
            IsFocused = true;
            SetState(ElementState.Focus, true);
            DispatchEvent(StandardEvents.CreateFocus());
            if (IsFocused) DispatchEvent(StandardEvents.CreateFocusIn());
        }
        finally
        {
            _isFocusing = false;
        }
    }

    /// <summary>
    /// 失去焦点：派发不冒泡的 <c>blur</c> 与冒泡的 <c>focusout</c>。
    /// </summary>
    public void Unfocus()
    {
        if (!IsFocused || _isUnfocusing) return;
        _isUnfocusing = true;
        try
        {
            OnBeforeUnfocus();
            if (!IsFocused) return;
            IsFocused = false;
            SetState(ElementState.Focus, false);
            DispatchEvent(StandardEvents.CreateBlur());
            if (!IsFocused) DispatchEvent(StandardEvents.CreateFocusOut());
        }
        finally
        {
            _isUnfocusing = false;
        }
    }

    /// <summary>焦点事务开始、事件派发之前的控件准备钩子。</summary>
    protected virtual void OnBeforeFocus() { }

    /// <summary>失焦事务开始前的控件提交钩子。</summary>
    protected virtual void OnBeforeUnfocus() { }

    /// <inheritdoc />
    protected override void OnDetachedCore()
    {
        IsFocused = false;
        SetState(ElementState.Focus, false);
        _isFocusing = false;
        _isUnfocusing = false;
        base.OnDetachedCore();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(IsDisabled))
            SetState(ElementState.Disabled, IsDisabled);
    }
}
