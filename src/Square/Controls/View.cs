using Square.Events;
using Square.Graphics;
using Square.UI;
using Square.UI.Scrolling;

namespace Square.Controls;

/// <summary>基础容器视图，仅绘制背景，类似 HTML <c>div</c>。</summary>
public class View : UIElement
{
    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        var background = ControlDrawing.GetStyledColor(this, "background", Color.Transparent);
        ControlDrawing.DrawStyledBackground(ctx, this, background);
    }
}

/// <summary>
/// Scrollable viewport backed by the framework's CSS overflow, clipping and wheel pipeline.
/// The default mode scrolls vertically and clips horizontal overflow.
/// </summary>
public class ScrollViewer : View
{
    public ScrollViewer()
    {
        Style.SetCascaded("overflow-x", "hidden", int.MinValue);
        Style.SetCascaded("overflow-y", "auto", int.MinValue);
    }

    public float HorizontalOffset => ScrollLeft;
    public float VerticalOffset => ScrollTop;
    public float ExtentWidth => ScrollContentSize.Width;
    public float ExtentHeight => ScrollContentSize.Height;
    public float ViewportWidth => GetScrollbarMetrics().ViewportRect.Width;
    public float ViewportHeight => GetScrollbarMetrics().ViewportRect.Height;
    public float ScrollableWidth => GetScrollbarMetrics().MaxScrollX;
    public float ScrollableHeight => GetScrollbarMetrics().MaxScrollY;

    /// <summary>滚动到指定偏移量。</summary>
    public void ScrollTo(float horizontalOffset, float verticalOffset)
    {
        ScrollLeft = horizontalOffset;
        ScrollTop = verticalOffset;
    }
    /// <summary>将后代元素滚动到当前视口内。</summary>
    public void ScrollTo(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (ReferenceEquals(element, this)) return;

        var nestedScroll = default(Point);
        Element? current = element.Parent;
        for (; current != null && !ReferenceEquals(current, this); current = current.Parent)
        {
            if (current.MapsScrollOffsetForChildren())
                nestedScroll = new Point(
                    nestedScroll.X + current.ScrollLeft,
                    nestedScroll.Y + current.ScrollTop);
        }
        if (!ReferenceEquals(current, this))
            throw new ArgumentException("The element must be a descendant of this ScrollViewer.", nameof(element));

        var viewport = GetScrollViewportRect();
        var targetLeft = element.Geometry.Left - viewport.Left - nestedScroll.X;
        var targetTop = element.Geometry.Top - viewport.Top - nestedScroll.Y;
        var targetRight = element.Geometry.Right - viewport.Left - nestedScroll.X;
        var targetBottom = element.Geometry.Bottom - viewport.Top - nestedScroll.Y;
        var visibleWidth = Math.Max(0, viewport.Width);
        var visibleHeight = Math.Max(0, viewport.Height);
        var nextX = ScrollLeft;
        var nextY = ScrollTop;

        if (element.Geometry.Width > visibleWidth || targetLeft < nextX)
            nextX = targetLeft;
        else if (targetRight > nextX + visibleWidth)
            nextX = targetRight - visibleWidth;
        if (element.Geometry.Height > visibleHeight || targetTop < nextY)
            nextY = targetTop;
        else if (targetBottom > nextY + visibleHeight)
            nextY = targetBottom - visibleHeight;

        ScrollTo(nextX, nextY);
    }

    /// <summary><see cref="ScrollTo(Element)"/> 的别名。</summary>
    public void ScrollIntoView(Element element) => ScrollTo(element);

    /// <summary>处理 ScrollViewer 的默认键盘滚动行为。</summary>
    protected override void OnDefaultAction(Event e)
    {
        base.OnDefaultAction(e);
        if (e.DefaultPrevented || e is not KeyboardEvent key ||
            !string.Equals(e.Type, StandardEvents.KeyDown, StringComparison.OrdinalIgnoreCase))
            return;

        var handled = key.KeyCode switch
        {
            33 => ScrollBy(0, -Math.Max(1, ViewportHeight)),
            34 => ScrollBy(0, Math.Max(1, ViewportHeight)),
            35 => SetVerticalOffset(ScrollableHeight),
            36 => SetVerticalOffset(0),
            37 => ScrollBy(-ScrollbarKeyStep, 0),
            38 => ScrollBy(0, -ScrollbarKeyStep),
            39 => ScrollBy(ScrollbarKeyStep, 0),
            40 => ScrollBy(0, ScrollbarKeyStep),
            _ => false
        };
        if (handled || key.KeyCode is 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40)
            e.PreventDefault();
    }

    private const float ScrollbarKeyStep = 40;

    private bool SetVerticalOffset(float value)
    {
        var before = ScrollTop;
        ScrollTop = value;
        return Math.Abs(before - ScrollTop) > 0.01f;
    }

    /// <summary>滚动到顶部。</summary>
    public void ScrollToTop() => ScrollTop = 0;
    /// <summary>滚动到底部。</summary>
    public void ScrollToBottom() => ScrollTop = ScrollableHeight;
}
