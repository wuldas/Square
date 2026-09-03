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

    /// <summary>滚动到顶部。</summary>
    public void ScrollToTop() => ScrollTop = 0;
    /// <summary>滚动到底部。</summary>
    public void ScrollToBottom() => ScrollTop = ScrollableHeight;
}
