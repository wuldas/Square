using Square.Graphics;
using Square.Html;
using Square.UI;

namespace Square.Controls;

/// <summary>Modal dialog rendered through the popup layer with a blocking backdrop.</summary>
public class Dialog : Popup
{
    private IFocusableElement? _restoreFocus;

    /// <summary>初始化 <see cref="Dialog"/> 的新实例。</summary>
    public Dialog()
    {
        CloseOnEscape = true;
        DismissOnPointerDownOutside = false;
        Style.SetCascaded("background", "#ffffff", int.MinValue);
    }

    /// <summary>是否以模态方式显示（带遮罩并阻挡外部交互）。</summary>
    public bool IsModal { get; set; } = true;
    /// <summary>是否在点击背景遮罩时关闭对话框。</summary>
    public bool CloseOnBackdropClick
    {
        get => DismissOnPointerDownOutside;
        set => DismissOnPointerDownOutside = value;
    }
    /// <summary>模态遮罩颜色。</summary>
    public Color BackdropColor { get; set; } = Color.FromRgba(0, 0, 0, 112);

    /// <summary>计算对话框内容区域，居中于视口。</summary>
    protected override Rect ContentBounds
    {
        get
        {
            var viewport = GetViewportBounds();
            if (viewport.IsEmpty) return base.ContentBounds;
            return new Rect(
                viewport.X + (viewport.Width - Geometry.Width) / 2f + HorizontalOffset,
                viewport.Y + (viewport.Height - Geometry.Height) / 2f + VerticalOffset,
                Geometry.Width,
                Geometry.Height);
        }
    }

    /// <inheritdoc/>
    public override Rect PopupBounds => IsModal ? GetViewportBounds() : ContentBounds;

    /// <inheritdoc/>
    public override void Open()
    {
        if (IsOpen) return;
        _restoreFocus = OwnerDocument?.DocumentElement is { } root
            ? CollectFocusables(root).LastOrDefault(element => element.IsFocused)
            : null;
        _restoreFocus?.Unfocus();
        base.Open();
        FindInitialFocus()?.Focus(focusVisible: true);
    }

    /// <inheritdoc/>
    public override void Close()
    {
        if (!IsOpen) return;
        foreach (var focused in CollectFocusables(this).Where(element => element.IsFocused))
            focused.Unfocus();
        base.Close();
        if (_restoreFocus is Element { IsAttached: true } && _restoreFocus.IsEnabled)
            _restoreFocus.Focus(focusVisible: true);
        _restoreFocus = null;
    }

    /// <inheritdoc/>
    public override bool ContainsPopupInteraction(Point point) => ContentBounds.Contains(point);

    /// <inheritdoc/>
    public override void PaintPopup(IRenderContext context)
    {
        if (!IsPopupOpen) return;
        if (IsModal)
            context.FillRect(GetViewportBounds(), new SolidColorBrush(BackdropColor));
        base.PaintPopup(context);
    }

    /// <inheritdoc/>
    public override Element? HitTestPopup(Point point)
    {
        if (!IsPopupOpen) return null;
        var hit = base.HitTestPopup(point);
        if (hit != null) return hit;
        return IsModal && GetViewportBounds().Contains(point) ? this : null;
    }

    private Rect GetViewportBounds()
    {
        if (OwnerDocument is UIDocument document)
        {
            if (!document.DocumentElement.Geometry.IsEmpty) return document.DocumentElement.Geometry;
            if (!document.Body.Geometry.IsEmpty) return document.Body.Geometry;
        }
        return Anchor?.Geometry ?? Geometry;
    }

    /// <summary>
    /// 收集子树内全部 Element 级可聚焦目标（含 UI 控件与 HTML host），
    /// 并经由 <see cref="HTMLElement.VisualSidecars"/> 覆盖不在语义 DOM 里的原生代理。
    /// </summary>
    private static List<IFocusableElement> CollectFocusables(Element root)
    {
        var result = new List<IFocusableElement>();
        Collect(root, result);
        return result;

        static void Collect(Element element, List<IFocusableElement> result)
        {
            if (element is IFocusableElement focusable) result.Add(focusable);
            foreach (var child in element.Children)
                Collect(child, result);
            if (element is not HTMLElement host) return;
            foreach (var sidecar in host.VisualSidecars)
                Collect(sidecar, result);
        }
    }

    private IFocusableElement? FindInitialFocus()
        => CollectFocusables(this).FirstOrDefault(element => element.IsEnabled && IsInitialFocusCandidate(element));

    /// <summary>初始焦点候选：原生交互控件，或 HTML 的 button/input/textarea/select/a/summary。</summary>
    private static bool IsInitialFocusCandidate(IFocusableElement element) => element switch
    {
        Button or Input or TextArea or CheckBox or Radio or Select or Link => true,
        HTMLElement host => host.LocalName is "button" or "input" or "textarea" or "select" or "a" or "summary",
        _ => false
    };
}
