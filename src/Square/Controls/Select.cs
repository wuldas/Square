using System.Numerics;
using Square.Events;
using Square.Graphics;
using Square.UI;

namespace Square.Controls;

/// <summary>下拉选择控件，类似 HTML <c>select</c>。</summary>
public class Select : UIElement, IPopupElement, ITextSelectable
{
    /// <inheritdoc/>
    public bool IsLayoutOverlay => false;
    /// <summary>当前选中的值。</summary>
    public string Value { get => GetProperty<string>(nameof(Value)) ?? ""; set => SetProperty(nameof(Value), value); }
    /// <summary>可选项集合。</summary>
    public string[] Options { get => GetProperty<string[]>(nameof(Options)) ?? []; set => SetProperty(nameof(Options), value ?? []); }
    /// <summary>占位提示文本。</summary>
    public string Placeholder { get => GetProperty<string>(nameof(Placeholder)) ?? "Select"; set => SetProperty(nameof(Placeholder), value); }
    /// <summary>下拉是否处于打开状态。</summary>
    public bool IsOpen { get; private set; }
    /// <inheritdoc/>
    public bool IsPopupOpen => IsOpen && Options.Length > 0;
    /// <inheritdoc/>
    public Rect PopupBounds => GetDropDownRect();
    /// <inheritdoc/>
    public bool DismissOnPointerDownOutside => true;
    /// <inheritdoc/>
    public bool CloseOnEscape => true;
    private int _hoveredOption = -1;

    /// <inheritdoc/>
    public string SelectableText => string.IsNullOrEmpty(Value) ? Placeholder : Value;
    /// <inheritdoc/>
    public Rect SelectableTextBounds => ControlDrawing.GetTextBounds(this, SelectableText, 14f, new Point(Geometry.X + 8, Geometry.Y + 8));

    /// <inheritdoc/>
    public override int ZIndex
    {
        get => IsOpen ? 1000 : base.ZIndex;
        set => base.ZIndex = value;
    }

    /// <summary>初始化 <see cref="Select"/> 的新实例。</summary>
    public Select()
    {
        AddEventListener<KeyboardEvent>(StandardEvents.KeyDown, e =>
        {
            if (HandleKey(e.KeyCode, e.ShiftKey, e.ControlKey, e.AltKey))
                e.PreventDefault();
        });
    }

    /// <inheritdoc/>
    public override Size Measure(Size availableSize) => new(200, 36);

    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        if (!ControlDrawing.UsesWidgetAppearance(this))
            ControlDrawing.DrawInputFrame(ctx, this);
        var value = string.IsNullOrEmpty(Value) ? Placeholder : Value;
        var color = string.IsNullOrEmpty(Value) ? Color.FromRgb(125, 130, 136) : Color.Black;
        ControlDrawing.DrawText(ctx, this, value, new Point(Geometry.X + 8, Geometry.Y + 8), color, 14f);
        var arrowY = Geometry.Y + Geometry.Height / 2f;
        var arrow = IsOpen
            ? PathGeometry.Create().MoveTo(new Point(Geometry.Right - 20, arrowY + 3)).LineTo(new Point(Geometry.Right - 15, arrowY - 2)).LineTo(new Point(Geometry.Right - 10, arrowY + 3))
            : PathGeometry.Create().MoveTo(new Point(Geometry.Right - 20, arrowY - 2)).LineTo(new Point(Geometry.Right - 15, arrowY + 3)).LineTo(new Point(Geometry.Right - 10, arrowY - 2));
        ctx.DrawPath(arrow, Pen.FromColor(Color.FromRgb(70, 75, 80), 1.5f));

    }

    /// <inheritdoc/>
    public void PaintPopup(IRenderContext ctx)
    {
        if (!IsPopupOpen) return;
        var popup = GetDropDownRect();
        ctx.FillRect(popup, new SolidColorBrush(Color.White));
        ctx.DrawRect(popup, Pen.FromColor(Color.FromRgb(145, 150, 156)));
        for (var i = 0; i < Options.Length; i++)
        {
            var row = new Rect(popup.X + 1, popup.Y + 1 + i * 32, popup.Width - 2, 32);
            if (i == _hoveredOption)
                ctx.FillRect(row, new SolidColorBrush(Color.FromRgb(230, 242, 252)));
            else if (Options[i] == Value)
                ctx.FillRect(row, new SolidColorBrush(Color.FromRgb(242, 247, 250)));
            ControlDrawing.DrawText(ctx, this, Options[i], new Point(row.X + 8, row.Y + 7), Color.Black, 14f);
        }
    }

    /// <inheritdoc/>
    public override Element? HitTest(Point point)
    {
        if (!IsVisible) return null;
        return Geometry.Contains(point) ? this : null;
    }

    /// <inheritdoc/>
    public Element? HitTestPopup(Point point) => IsPopupOpen && PopupBounds.Contains(point) ? this : null;
    /// <inheritdoc/>
    public bool ContainsPopupInteraction(Point point) => Geometry.Contains(point) || PopupBounds.Contains(point);
    /// <inheritdoc/>
    public bool HandlePopupKey(int keyCode, bool shift, bool control, bool alt)
        => HandleKey(keyCode, shift, control, alt);
    /// <inheritdoc/>
    public Point MapPointToContent(Point point) => point;
    /// <inheritdoc/>
    public void ClosePopup() => CloseDropDown();

    /// <summary>处理键盘输入，返回是否已处理。</summary>
    public bool HandleKey(int keyCode, bool shift = false, bool control = false, bool alt = false)
    {
        if (!IsEnabled || Options.Length == 0) return false;

        if (!IsOpen)
        {
            if (keyCode is not (13 or 32) && !(alt && keyCode == 40)) return false;
            OpenDropDown();
            return true;
        }

        switch (keyCode)
        {
            case 38:
                MoveHoveredOption(-1);
                return true;
            case 40:
                MoveHoveredOption(1);
                return true;
            case 36:
                SetHoveredOption(0);
                return true;
            case 35:
                SetHoveredOption(Options.Length - 1);
                return true;
            case 13:
            case 32:
                SelectOption(_hoveredOption >= 0 ? _hoveredOption : GetSelectedOptionIndex());
                return true;
            case 27:
                CloseDropDown();
                return true;
            case 9:
                CloseDropDown();
                return false;
            default:
                return false;
        }
    }

    /// <summary>处理指针按下事件。</summary>
    public void HandlePointerDown(Point point)
    {
        if (!IsEnabled) return;
        if (Geometry.Contains(point))
        {
            ToggleDropDown();
            return;
        }

        if (!IsOpen) return;
        var popup = GetDropDownRect();
        if (popup.Contains(point))
        {
            var index = Math.Clamp((int)((point.Y - popup.Y - 1) / 32), 0, Options.Length - 1);
            SelectOption(index);
        }
    }

    /// <summary>处理指针移动事件，返回悬停项是否变化。</summary>
    public bool HandlePointerMove(Point point)
    {
        var next = IsOpen && GetDropDownRect().Contains(point)
            ? Math.Clamp((int)((point.Y - GetDropDownRect().Y - 1) / 32), 0, Options.Length - 1)
            : -1;
        if (_hoveredOption == next) return false;
        _hoveredOption = next;
        InvalidatePaint();
        return true;
    }

    /// <summary>关闭下拉列表。</summary>
    public void CloseDropDown()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _hoveredOption = -1;
        Parent?.InvalidatePaint();
        InvalidatePaint();
    }

    private void ToggleDropDown()
    {
        if (!IsEnabled || Options.Length == 0) return;
        if (IsOpen) CloseDropDown();
        else OpenDropDown();
    }

    private void OpenDropDown()
    {
        IsOpen = true;
        _hoveredOption = GetSelectedOptionIndex();
        Parent?.InvalidatePaint();
        InvalidatePaint();
    }

    private int GetSelectedOptionIndex()
    {
        var index = Array.IndexOf(Options, Value);
        return index >= 0 ? index : 0;
    }

    private void MoveHoveredOption(int direction)
    {
        var index = _hoveredOption >= 0 ? _hoveredOption : GetSelectedOptionIndex();
        SetHoveredOption((index + direction + Options.Length) % Options.Length);
    }

    private void SetHoveredOption(int index)
    {
        if (_hoveredOption == index) return;
        _hoveredOption = index;
        InvalidatePaint();
    }

    private void SelectOption(int index)
    {
        if (index < 0 || index >= Options.Length) return;
        var nextValue = Options[index];
        var changed = !string.Equals(Value, nextValue, StringComparison.Ordinal);
        Value = nextValue;
        CloseDropDown();
        if (changed) DispatchEvent(StandardEvents.CreateChange());
    }

    private Rect GetDropDownRect() => new(Geometry.X, Geometry.Bottom + 2, Geometry.Width, Options.Length * 32 + 2);
}

/// <summary>弹出层相对锚点的放置方位。</summary>
public enum PopupPlacement { Bottom, Top, Left, Right }
/// <summary>弹出层相对锚点的对齐方式。</summary>
public enum PopupAlignment { Start, Center, End }

/// <summary>Top-level anchored content that does not paint in its layout-tree position.</summary>
public class Popup : View, IPopupElement
{
    private bool _isOpen;

    /// <summary>初始化 <see cref="Popup"/> 的新实例。</summary>
    public Popup()
    {
        Style.SetCascaded("position", "absolute", int.MinValue);
        Style.SetCascaded("box-shadow", "0 4px 8px 2px rgba(0,0,0,0.48)", int.MinValue);
    }

    /// <summary>锚定元素，用于定位弹出层。</summary>
    public Element? Anchor { get; set; }
    /// <summary>弹出方位。</summary>
    public PopupPlacement Placement { get; set; } = PopupPlacement.Bottom;
    /// <summary>对齐方式。</summary>
    public PopupAlignment Alignment { get; set; } = PopupAlignment.Start;
    /// <summary>水平偏移量。</summary>
    public float HorizontalOffset { get; set; }
    /// <summary>垂直偏移量。</summary>
    public float VerticalOffset { get; set; } = 4;
    /// <summary>是否在弹出层外按下指针时关闭。</summary>
    public bool DismissOnPointerDownOutside { get; set; } = true;
    /// <summary>是否在按下 ESC 时关闭。</summary>
    public bool CloseOnEscape { get; set; }
    /// <summary>溢出时是否翻转方位。</summary>
    public bool FlipOnOverflow { get; set; }
    /// <summary>是否将弹出层限制在视口内。</summary>
    public bool ConstrainToViewport { get; set; }
    /// <summary>获取或设置弹出层的打开状态。</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (value) Open();
            else Close();
        }
    }

    /// <inheritdoc/>
    public bool IsPopupOpen => _isOpen && !PopupBounds.IsEmpty;
    /// <inheritdoc/>
    public virtual Rect PopupBounds => ContentBounds;
    /// <summary>计算弹出内容区域。</summary>
    protected virtual Rect ContentBounds => GetPopupBounds();

    /// <inheritdoc/>
    public override int ZIndex
    {
        get => IsOpen ? 1000 : base.ZIndex;
        set => base.ZIndex = value;
    }

    /// <summary>打开弹出层。</summary>
    public virtual void Open()
    {
        if (_isOpen) return;
        _isOpen = true;
        Parent?.InvalidatePaint();
        InvalidatePaint();
        DispatchEvent(new Event("open"));
    }

    /// <summary>关闭弹出层。</summary>
    public virtual void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        Parent?.InvalidatePaint();
        InvalidatePaint();
        DispatchEvent(new Event("close"));
    }

    /// <inheritdoc/>
    public void ClosePopup() => Close();

    /// <inheritdoc/>
    public virtual bool ContainsPopupInteraction(Point point)
        => ContentBounds.Contains(point) || Anchor?.Geometry.Contains(point) == true;

    /// <inheritdoc/>
    public virtual bool HandlePopupKey(int keyCode, bool shift, bool control, bool alt) => false;

    /// <inheritdoc/>
    public virtual Point MapPointToContent(Point point)
    {
        var bounds = ContentBounds;
        return new Point(
            point.X - bounds.X + Geometry.X,
            point.Y - bounds.Y + Geometry.Y);
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext context)
    {
        // Popup content is replayed by PaintPopup in the top-level popup layer.
    }

    /// <inheritdoc/>
    public virtual void PaintPopup(IRenderContext context)
    {
        if (!IsPopupOpen) return;
        var bounds = ContentBounds;
        var translation = new Vector2(bounds.X - Geometry.X, bounds.Y - Geometry.Y);
        context.PushTransform(Matrix3x2.CreateTranslation(translation));
        if (BoxShadow.TryParseList(Style.Get("box-shadow"), out var shadows))
            BoxShadowRendering.Draw(context, Geometry,
                ControlDrawing.TryGetStyledRoundedGeometry(this, Geometry, out var roundedGeometry)
                    ? roundedGeometry
                    : null,
                shadows);
        context.PushClip(Geometry);
        var background = ControlDrawing.GetStyledColor(this, "background", Color.White);
        ControlDrawing.DrawStyledBackground(context, this, background);
        foreach (var child in Children.OrderBy(child => child.ZIndex))
            PaintPopupSubtree(context, child);
        context.PopClip();
        context.PopTransform();
    }

    /// <inheritdoc/>
    public virtual Element? HitTestPopup(Point point)
    {
        var bounds = ContentBounds;
        if (!IsPopupOpen || !bounds.Contains(point)) return null;
        var localPoint = MapPointToContent(point);
        foreach (var child in Children.OrderByDescending(child => child.ZIndex))
        {
            var hit = child.HitTest(localPoint);
            if (hit != null) return hit;
        }
        return this;
    }

    /// <summary>计算弹出层的边界矩形。</summary>
    protected virtual Rect GetPopupBounds()
    {
        if (Geometry.Width <= 0 || Geometry.Height <= 0) return Rect.Empty;
        var anchor = Anchor == null ? Geometry : GetAnchorBounds(Anchor);
        var x = Placement is PopupPlacement.Bottom or PopupPlacement.Top
            ? Align(anchor.X, anchor.Width, Geometry.Width, Alignment) + HorizontalOffset
            : Placement == PopupPlacement.Left
                ? anchor.X - Geometry.Width - HorizontalOffset
                : anchor.Right + HorizontalOffset;
        var y = Placement is PopupPlacement.Left or PopupPlacement.Right
            ? Align(anchor.Y, anchor.Height, Geometry.Height, Alignment) + VerticalOffset
            : Placement == PopupPlacement.Top
                ? anchor.Y - Geometry.Height - VerticalOffset
                : anchor.Bottom + VerticalOffset;
        var bounds = new Rect(x, y, Geometry.Width, Geometry.Height);
        var viewport = GetPopupViewportBounds();
        if (FlipOnOverflow && !viewport.IsEmpty)
        {
            bounds = Placement switch
            {
                PopupPlacement.Bottom when bounds.Bottom > viewport.Bottom &&
                    anchor.Y - Geometry.Height - VerticalOffset >= viewport.Y =>
                    new Rect(bounds.X, anchor.Y - Geometry.Height - VerticalOffset, bounds.Width, bounds.Height),
                PopupPlacement.Top when bounds.Y < viewport.Y &&
                    anchor.Bottom + Geometry.Height + VerticalOffset <= viewport.Bottom =>
                    new Rect(bounds.X, anchor.Bottom + VerticalOffset, bounds.Width, bounds.Height),
                PopupPlacement.Right when bounds.Right > viewport.Right &&
                    anchor.X - Geometry.Width - HorizontalOffset >= viewport.X =>
                    new Rect(anchor.X - Geometry.Width - HorizontalOffset, bounds.Y, bounds.Width, bounds.Height),
                PopupPlacement.Left when bounds.X < viewport.X &&
                    anchor.Right + Geometry.Width + HorizontalOffset <= viewport.Right =>
                    new Rect(anchor.Right + HorizontalOffset, bounds.Y, bounds.Width, bounds.Height),
                _ => bounds
            };
        }
        return ConstrainPopupBounds(bounds);
    }

    private static Rect GetAnchorBounds(Element anchor)
    {
        var bounds = anchor.Geometry;
        for (var current = anchor.Parent; current != null; current = current.Parent)
        {
            if (current.MapsScrollOffsetForChildren())
                bounds = bounds.Offset(-current.ScrollLeft, -current.ScrollTop);

            if (current is IPopupElement popup)
            {
                var popupBounds = popup.PopupBounds;
                var geometry = current.Geometry;
                return new Rect(
                    bounds.X + popupBounds.X - geometry.X,
                    bounds.Y + popupBounds.Y - geometry.Y,
                    bounds.Width,
                    bounds.Height);
            }
        }
        return bounds;
    }

    /// <summary>在启用 <see cref="ConstrainToViewport"/> 时把弹出层边界限制在视口内。</summary>
    protected Rect ConstrainPopupBounds(Rect bounds)
    {
        if (!ConstrainToViewport) return bounds;
        var viewport = GetPopupViewportBounds();
        if (viewport.IsEmpty) return bounds;
        var width = Math.Min(bounds.Width, viewport.Width);
        var height = Math.Min(bounds.Height, viewport.Height);
        return new Rect(
            Math.Clamp(bounds.X, viewport.X, viewport.Right - width),
            Math.Clamp(bounds.Y, viewport.Y, viewport.Bottom - height),
            width,
            height);
    }

    /// <summary>获取弹出层所在的视口边界。</summary>
    protected Rect GetPopupViewportBounds()
    {
        if (OwnerDocument is not UIDocument document) return Rect.Empty;
        if (!document.DocumentElement.Geometry.IsEmpty) return document.DocumentElement.Geometry;
        return !document.Body.Geometry.IsEmpty ? document.Body.Geometry : Rect.Empty;
    }

    private static float Align(float start, float anchorLength, float popupLength, PopupAlignment alignment) => alignment switch
    {
        PopupAlignment.Center => start + (anchorLength - popupLength) / 2f,
        PopupAlignment.End => start + anchorLength - popupLength,
        _ => start
    };

    private static void PaintPopupSubtree(IRenderContext context, Element element)
    {
        if (!element.IsVisible || element is IPopupElement) return;
        element.Paint(context);
        var clip = element.GetOverflowClipRect();
        if (!clip.IsEmpty) context.PushClip(clip);
        var offset = element.ScrollOffset;
        var scrolls = element.MapsScrollOffsetForChildren();
        if (scrolls) context.PushTransform(Matrix3x2.CreateTranslation(-offset.X, -offset.Y));
        foreach (var child in element.Children.OrderBy(child => child.ZIndex))
            PaintPopupSubtree(context, child);
        if (scrolls) context.PopTransform();
        if (!clip.IsEmpty) context.PopClip();
    }
}
