using Square.Graphics;

namespace Square.UI.Scrolling;

/// <summary>滚动条的设备行为 profile。</summary>
public enum ScrollbarDeviceProfile
{
    /// <summary>由宿主选择；当前桌面宿主解析为 Desktop。</summary>
    Auto,
    /// <summary>Chrome desktop 风格，滚动条占用 gutter 并可交互。</summary>
    Desktop,
    /// <summary>Chrome mobile 风格，滚动条为瞬时 overlay。</summary>
    Mobile
}

/// <summary>CSS scrollbar-width 的已解析值。</summary>
public enum ScrollbarWidthMode
{
    Auto,
    Thin,
    None
}

/// <summary>scrollbar-gutter 的已解析值。</summary>
public enum ScrollbarGutterMode
{
    Auto,
    Stable,
    StableBothEdges
}

/// <summary>滚动条的可命中部件。</summary>
public enum ScrollbarPart
{
    None,
    VerticalBackButton,
    VerticalTrack,
    VerticalThumb,
    VerticalForwardButton,
    HorizontalBackButton,
    HorizontalTrack,
    HorizontalThumb,
    HorizontalForwardButton,
    Corner
}

/// <summary>一组同时供布局、绘制和命中测试使用的滚动条几何。</summary>
public readonly record struct ScrollbarMetrics(
    Rect ViewportRect,
    Rect VerticalGutter,
    Rect HorizontalGutter,
    Rect VerticalBackButton,
    Rect VerticalTrack,
    Rect VerticalThumb,
    Rect VerticalForwardButton,
    Rect HorizontalBackButton,
    Rect HorizontalTrack,
    Rect HorizontalThumb,
    Rect HorizontalForwardButton,
    Rect Corner,
    float ScrollbarThickness,
    float ThumbThickness,
    float MaxScrollX,
    float MaxScrollY,
    bool HasVertical,
    bool HasHorizontal,
    bool ReservesVerticalGutter,
    bool ReservesHorizontalGutter,
    bool IsOverlay)
{
    /// <summary>按统一几何命中 scrollbar 部件；移动 overlay 不参与命中。</summary>
    public ScrollbarPart HitTest(Point point)
    {
        if (IsOverlay) return ScrollbarPart.None;
        if (Contains(VerticalThumb, point)) return ScrollbarPart.VerticalThumb;
        if (Contains(HorizontalThumb, point)) return ScrollbarPart.HorizontalThumb;
        if (Contains(VerticalBackButton, point)) return ScrollbarPart.VerticalBackButton;
        if (Contains(VerticalForwardButton, point)) return ScrollbarPart.VerticalForwardButton;
        if (Contains(HorizontalBackButton, point)) return ScrollbarPart.HorizontalBackButton;
        if (Contains(HorizontalForwardButton, point)) return ScrollbarPart.HorizontalForwardButton;
        if (Contains(VerticalTrack, point)) return ScrollbarPart.VerticalTrack;
        if (Contains(HorizontalTrack, point)) return ScrollbarPart.HorizontalTrack;
        if (Contains(Corner, point)) return ScrollbarPart.Corner;
        return ScrollbarPart.None;
    }

    private static bool Contains(Rect rect, Point point) => !rect.IsEmpty && rect.Contains(point);
}

/// <summary>计算核心滚动容器的 scrollbar 几何，不持有元素或渲染状态。</summary>
public static class ScrollbarGeometry
{
    private const float DesktopThickness = 15;
    private const float DesktopThumbThickness = 9;
    private const float DesktopMinimumThumbLength = 17;
    private const float DesktopButtonLength = 18;
    private const float MobileThickness = 4;
    private const float MobileMinimumThumbLength = 4;

    public static ScrollbarDeviceProfile ResolveProfile(ScrollbarDeviceProfile profile) =>
        profile == ScrollbarDeviceProfile.Auto ? ScrollbarDeviceProfile.Desktop : profile;

    public static ScrollbarMetrics Calculate(
        Rect bounds,
        Size contentSize,
        Point offset,
        bool verticalEnabled = true,
        bool horizontalEnabled = false,
        bool alwaysShowVertical = false,
        bool alwaysShowHorizontal = false,
        ScrollbarDeviceProfile profile = ScrollbarDeviceProfile.Desktop,
        ScrollbarWidthMode width = ScrollbarWidthMode.Auto,
        ScrollbarGutterMode gutter = ScrollbarGutterMode.Auto)
    {
        var resolvedProfile = ResolveProfile(profile);
        var isOverlay = resolvedProfile == ScrollbarDeviceProfile.Mobile;
        var thickness = ResolveThickness(isOverlay, width);
        var thumbThickness = ResolveThumbThickness(isOverlay, width);
        var minimumThumbLength = ResolveMinimumThumbLength(isOverlay, width);
        var hidden = width == ScrollbarWidthMode.None;
        var bothEdges = !isOverlay && gutter == ScrollbarGutterMode.StableBothEdges;
        var stableGutter = !isOverlay &&
            (gutter == ScrollbarGutterMode.Stable || bothEdges);
        var safeBounds = new Rect(bounds.X, bounds.Y, Math.Max(0, bounds.Width), Math.Max(0, bounds.Height));
        var safeContent = new Size(Math.Max(0, contentSize.Width), Math.Max(0, contentSize.Height));

        var hasVertical = !hidden && verticalEnabled &&
            (alwaysShowVertical || safeContent.Height > safeBounds.Height);
        var hasHorizontal = !hidden && horizontalEnabled &&
            (alwaysShowHorizontal || safeContent.Width > safeBounds.Width);
        var reservesVertical = !isOverlay && !hidden && verticalEnabled && (stableGutter || hasVertical);
        var reservesHorizontal = !isOverlay && !hidden && horizontalEnabled && (stableGutter || hasHorizontal);
        if (!isOverlay)
        {
            for (var i = 0; i < 3; i++)
            {
                var viewportWidth = Math.Max(0, safeBounds.Width -
                    (reservesVertical ? thickness * (bothEdges ? 2 : 1) : 0));
                var viewportHeight = Math.Max(0, safeBounds.Height -
                    (reservesHorizontal ? thickness * (bothEdges ? 2 : 1) : 0));
                var nextVertical = !hidden && verticalEnabled &&
                    (alwaysShowVertical || safeContent.Height > viewportHeight);
                var nextHorizontal = !hidden && horizontalEnabled &&
                    (alwaysShowHorizontal || safeContent.Width > viewportWidth);
                var nextReservesVertical = !hidden && verticalEnabled && (stableGutter || nextVertical);
                var nextReservesHorizontal = !hidden && horizontalEnabled && (stableGutter || nextHorizontal);
                if (nextVertical == hasVertical && nextHorizontal == hasHorizontal &&
                    nextReservesVertical == reservesVertical && nextReservesHorizontal == reservesHorizontal) break;
                hasVertical = nextVertical;
                hasHorizontal = nextHorizontal;
                reservesVertical = nextReservesVertical;
                reservesHorizontal = nextReservesHorizontal;
            }
        }

        var viewport = new Rect(
            safeBounds.X + (!isOverlay && bothEdges && reservesVertical ? thickness : 0),
            safeBounds.Y + (!isOverlay && bothEdges && reservesHorizontal ? thickness : 0),
            Math.Max(0, safeBounds.Width - (!isOverlay && reservesVertical ? thickness * (bothEdges ? 2 : 1) : 0)),
            Math.Max(0, safeBounds.Height - (!isOverlay && reservesHorizontal ? thickness * (bothEdges ? 2 : 1) : 0)));
        var maxScrollX = Math.Max(0, safeContent.Width - viewport.Width);
        var maxScrollY = Math.Max(0, safeContent.Height - viewport.Height);
        var scrollX = ClampFinite(offset.X, 0, maxScrollX);
        var scrollY = ClampFinite(offset.Y, 0, maxScrollY);

        if (isOverlay)
            return CreateOverlayMetrics(
                safeBounds, safeContent, scrollX, scrollY, thickness, minimumThumbLength,
                hasVertical, hasHorizontal, maxScrollX, maxScrollY);

        var verticalGutter = hasVertical
            ? new Rect(viewport.Right, viewport.Top, thickness, viewport.Height)
            : Rect.Empty;
        var horizontalGutter = hasHorizontal
            ? new Rect(viewport.Left, viewport.Bottom, viewport.Width, thickness)
            : Rect.Empty;
        var corner = hasVertical && hasHorizontal
            ? new Rect(viewport.Right, viewport.Bottom, thickness, thickness)
            : Rect.Empty;

        var verticalButtonLength = hasVertical ? Math.Min(DesktopButtonLength * (thickness / DesktopThickness), verticalGutter.Height / 2) : 0;
        var horizontalButtonLength = hasHorizontal ? Math.Min(DesktopButtonLength * (thickness / DesktopThickness), horizontalGutter.Width / 2) : 0;
        var verticalBack = hasVertical
            ? new Rect(verticalGutter.X, verticalGutter.Y, verticalGutter.Width, verticalButtonLength)
            : Rect.Empty;
        var verticalForward = hasVertical
            ? new Rect(verticalGutter.X, verticalGutter.Bottom - verticalButtonLength, verticalGutter.Width, verticalButtonLength)
            : Rect.Empty;
        var horizontalBack = hasHorizontal
            ? new Rect(horizontalGutter.X, horizontalGutter.Y, horizontalButtonLength, horizontalGutter.Height)
            : Rect.Empty;
        var horizontalForward = hasHorizontal
            ? new Rect(horizontalGutter.Right - horizontalButtonLength, horizontalGutter.Y, horizontalButtonLength, horizontalGutter.Height)
            : Rect.Empty;
        var verticalTrack = hasVertical
            ? new Rect(verticalGutter.X, verticalBack.Bottom, verticalGutter.Width,
                Math.Max(0, verticalGutter.Height - verticalBack.Height - verticalForward.Height))
            : Rect.Empty;
        var horizontalTrack = hasHorizontal
            ? new Rect(horizontalBack.Right, horizontalGutter.Y,
                Math.Max(0, horizontalGutter.Width - horizontalBack.Width - horizontalForward.Width), horizontalGutter.Height)
            : Rect.Empty;
        var verticalThumbLength = ThumbLength(verticalTrack.Height, viewport.Height, safeContent.Height, minimumThumbLength);
        var horizontalThumbLength = ThumbLength(horizontalTrack.Width, viewport.Width, safeContent.Width, minimumThumbLength);
        var verticalThumbY = Position(verticalTrack.Y, verticalTrack.Height, verticalThumbLength, scrollY, maxScrollY);
        var horizontalThumbX = Position(horizontalTrack.X, horizontalTrack.Width, horizontalThumbLength, scrollX, maxScrollX);
        var verticalThumb = hasVertical
            ? new Rect(verticalGutter.X + (verticalGutter.Width - thumbThickness) / 2, verticalThumbY,
                thumbThickness, verticalThumbLength)
            : Rect.Empty;
        var horizontalThumb = hasHorizontal
            ? new Rect(horizontalThumbX, horizontalGutter.Y + (horizontalGutter.Height - thumbThickness) / 2,
                horizontalThumbLength, thumbThickness)
            : Rect.Empty;

        return new ScrollbarMetrics(
            viewport, verticalGutter, horizontalGutter,
            verticalBack, verticalTrack, verticalThumb, verticalForward,
            horizontalBack, horizontalTrack, horizontalThumb, horizontalForward,
            corner, thickness, thumbThickness, maxScrollX, maxScrollY,
            hasVertical, hasHorizontal, reservesVertical, reservesHorizontal, false);
    }

    private static ScrollbarMetrics CreateOverlayMetrics(
        Rect bounds,
        Size contentSize,
        float scrollX,
        float scrollY,
        float thickness,
        float minimumThumbLength,
        bool hasVertical,
        bool hasHorizontal,
        float maxScrollX,
        float maxScrollY)
    {
        var verticalLength = ThumbLength(bounds.Height, bounds.Height, contentSize.Height, minimumThumbLength);
        var horizontalLength = ThumbLength(bounds.Width, bounds.Width, contentSize.Width, minimumThumbLength);
        var verticalY = Position(bounds.Y, bounds.Height, verticalLength, scrollY, maxScrollY);
        var horizontalX = Position(bounds.X, bounds.Width, horizontalLength, scrollX, maxScrollX);
        var verticalThumb = hasVertical
            ? new Rect(bounds.Right - thickness, verticalY, thickness, verticalLength)
            : Rect.Empty;
        var horizontalThumb = hasHorizontal
            ? new Rect(horizontalX, bounds.Bottom - thickness, horizontalLength, thickness)
            : Rect.Empty;
        return new ScrollbarMetrics(
            bounds, Rect.Empty, Rect.Empty,
            Rect.Empty, Rect.Empty, verticalThumb, Rect.Empty,
            Rect.Empty, Rect.Empty, horizontalThumb, Rect.Empty,
            Rect.Empty, thickness, thickness, maxScrollX, maxScrollY,
            hasVertical, hasHorizontal, false, false, true);
    }

    private static float ResolveThickness(bool isOverlay, ScrollbarWidthMode width) =>
        isOverlay ? MobileThickness : width == ScrollbarWidthMode.Thin ? 10 : DesktopThickness;

    private static float ResolveThumbThickness(bool isOverlay, ScrollbarWidthMode width) =>
        isOverlay ? MobileThickness : width == ScrollbarWidthMode.Thin ? 6 : DesktopThumbThickness;

    private static float ResolveMinimumThumbLength(bool isOverlay, ScrollbarWidthMode width) =>
        isOverlay ? MobileMinimumThumbLength : width == ScrollbarWidthMode.Thin ? 11 : DesktopMinimumThumbLength;

    private static float ThumbLength(float trackLength, float viewportLength, float contentLength, float minimum)
    {
        if (trackLength <= 0) return 0;
        var ratio = contentLength > 0 ? viewportLength / contentLength : 1;
        return Math.Clamp(Math.Max(minimum, trackLength * ratio), 0, trackLength);
    }

    private static float Position(float start, float trackLength, float thumbLength, float offset, float maxOffset)
    {
        var travel = Math.Max(0, trackLength - thumbLength);
        return start + (maxOffset > 0 ? travel * offset / maxOffset : 0);
    }

    private static float ClampFinite(float value, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : min;
}
