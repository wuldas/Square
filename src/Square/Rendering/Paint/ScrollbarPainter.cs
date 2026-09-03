using Square.Graphics;
using Square.UI;
using Square.UI.Scrolling;

namespace Square.Rendering.Paint;

public static class ScrollbarPainter
{
    private static readonly Color DefaultThumb = Color.FromRgba(128, 128, 128, 170);
    private static readonly Color DefaultMobileThumb = Color.FromRgba(128, 128, 128, 128);
    private static readonly Color DefaultTrack = Color.FromRgba(220, 220, 220, 80);
    private static readonly Color DefaultButtonGlyph = Color.FromRgba(0, 0, 0, 110);

    internal static void Paint(IRenderContext context, Element element)
    {
        var metrics = element.GetScrollbarMetrics();
        if (!metrics.HasVertical && !metrics.HasHorizontal) return;

        var (thumb, track) = ResolveColors(element, metrics.IsOverlay);
        Paint(
            context,
            metrics,
            thumb,
            track,
            DefaultButtonGlyph,
            element.ScrollbarOpacity,
            element.ScrollbarInteractionPart,
            element.ScrollbarHoverPart);
    }

    /// <summary>使用指定颜色绘制一组共享 scrollbar 几何。</summary>
    public static void Paint(
        IRenderContext context,
        ScrollbarMetrics metrics,
        Color thumb,
        Color track,
        Color buttonGlyph,
        float opacity = 1f,
        ScrollbarPart pressedPart = ScrollbarPart.None,
        ScrollbarPart hoverPart = ScrollbarPart.None,
        Color? pressedThumb = null)
    {
        if (!metrics.HasVertical && !metrics.HasHorizontal) return;
        var verticalThumb = pressedPart == ScrollbarPart.VerticalThumb && pressedThumb.HasValue
            ? pressedThumb.Value
            : ResolveStateColor(thumb, ScrollbarPart.VerticalThumb, pressedPart, hoverPart, 36, 20);
        var horizontalThumb = pressedPart == ScrollbarPart.HorizontalThumb && pressedThumb.HasValue
            ? pressedThumb.Value
            : ResolveStateColor(thumb, ScrollbarPart.HorizontalThumb, pressedPart, hoverPart, 36, 20);
        var verticalTrack = ResolveStateColor(
            track, ScrollbarPart.VerticalTrack, pressedPart, hoverPart, -16, 10);
        var horizontalTrack = ResolveStateColor(
            track, ScrollbarPart.HorizontalTrack, pressedPart, hoverPart, -16, 10);
        var verticalBackButton = ResolveStateColor(
            buttonGlyph, ScrollbarPart.VerticalBackButton, pressedPart, hoverPart, -24, 16);
        var verticalForwardButton = ResolveStateColor(
            buttonGlyph, ScrollbarPart.VerticalForwardButton, pressedPart, hoverPart, -24, 16);
        var horizontalBackButton = ResolveStateColor(
            buttonGlyph, ScrollbarPart.HorizontalBackButton, pressedPart, hoverPart, -24, 16);
        var horizontalForwardButton = ResolveStateColor(
            buttonGlyph, ScrollbarPart.HorizontalForwardButton, pressedPart, hoverPart, -24, 16);
        if (metrics.IsOverlay)
        {
            PaintThumb(context, metrics.VerticalThumb, WithOpacity(verticalThumb, opacity));
            PaintThumb(context, metrics.HorizontalThumb, WithOpacity(horizontalThumb, opacity));
            return;
        }

        PaintTrack(context, metrics.VerticalGutter, verticalTrack);
        PaintTrack(context, metrics.HorizontalGutter, horizontalTrack);
        PaintTrack(context, metrics.Corner, track);
        PaintVerticalButton(context, metrics.VerticalBackButton, up: true, verticalBackButton);
        PaintVerticalButton(context, metrics.VerticalForwardButton, up: false, verticalForwardButton);
        PaintHorizontalButton(context, metrics.HorizontalBackButton, left: true, horizontalBackButton);
        PaintHorizontalButton(context, metrics.HorizontalForwardButton, left: false, horizontalForwardButton);
        PaintThumb(context, metrics.VerticalThumb, verticalThumb);
        PaintThumb(context, metrics.HorizontalThumb, horizontalThumb);
    }

    private static void PaintTrack(IRenderContext context, Rect rect, Color color)
    {
        if (rect.IsEmpty || color.A == 0) return;
        var inset = Math.Min(1, Math.Min(rect.Width, rect.Height) / 2);
        var painted = new Rect(
            rect.X + inset,
            rect.Y + inset,
            Math.Max(0, rect.Width - inset * 2),
            Math.Max(0, rect.Height - inset * 2));
        if (!painted.IsEmpty) context.FillRect(painted, Brush.FromColor(color));
    }

    private static void PaintThumb(IRenderContext context, Rect rect, Color color)
    {
        if (rect.IsEmpty || color.A == 0) return;
        var radius = Math.Min(rect.Width, rect.Height) / 2;
        context.FillGeometry(new RoundedRectGeometry(rect, radius, radius), Brush.FromColor(color));
    }

    private static void PaintVerticalButton(IRenderContext context, Rect rect, bool up, Color color)
    {
        if (rect.IsEmpty || color.A == 0) return;
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var size = Math.Min(rect.Width, rect.Height) * 0.28f;
        var path = up
            ? CreateRoundedTriangle(
                new Point(center.X, center.Y - size),
                new Point(center.X - size, center.Y + size),
                new Point(center.X + size, center.Y + size),
                size * 0.32f)
            : CreateRoundedTriangle(
                new Point(center.X, center.Y + size),
                new Point(center.X + size, center.Y - size),
                new Point(center.X - size, center.Y - size),
                size * 0.32f);
        context.FillPath(path, Brush.FromColor(color));
    }

    private static void PaintHorizontalButton(IRenderContext context, Rect rect, bool left, Color color)
    {
        if (rect.IsEmpty || color.A == 0) return;
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var size = Math.Min(rect.Width, rect.Height) * 0.28f;
        var path = left
            ? CreateRoundedTriangle(
                new Point(center.X - size, center.Y),
                new Point(center.X + size, center.Y + size),
                new Point(center.X + size, center.Y - size),
                size * 0.32f)
            : CreateRoundedTriangle(
                new Point(center.X + size, center.Y),
                new Point(center.X - size, center.Y - size),
                new Point(center.X - size, center.Y + size),
                size * 0.32f);
        context.FillPath(path, Brush.FromColor(color));
    }

    private static PathGeometry CreateRoundedTriangle(
        Point first,
        Point second,
        Point third,
        float rounding)
    {
        var firstRadius = CornerRadius(first, third, second, rounding);
        var secondRadius = CornerRadius(second, first, third, rounding);
        var thirdRadius = CornerRadius(third, second, first, rounding);
        var firstEntry = MoveTowards(first, third, firstRadius);
        var firstExit = MoveTowards(first, second, firstRadius);
        var secondEntry = MoveTowards(second, first, secondRadius);
        var secondExit = MoveTowards(second, third, secondRadius);
        var thirdEntry = MoveTowards(third, second, thirdRadius);
        var thirdExit = MoveTowards(third, first, thirdRadius);

        var path = PathGeometry.Create()
            .MoveTo(firstExit)
            .LineTo(secondEntry);
        AppendCornerArc(path, second, secondEntry, secondExit);
        path.LineTo(thirdEntry);
        AppendCornerArc(path, third, thirdEntry, thirdExit);
        path.LineTo(firstEntry);
        AppendCornerArc(path, first, firstEntry, firstExit);
        return path.Close();
    }

    private static float CornerRadius(Point vertex, Point previous, Point next, float requested) =>
        Math.Min(Math.Max(0.75f, requested), Math.Min(Distance(vertex, previous), Distance(vertex, next)) * 0.35f);

    private static Point MoveTowards(Point from, Point to, float distance)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        return length <= 0
            ? from
            : new Point(from.X + dx / length * distance, from.Y + dy / length * distance);
    }

    private static float Distance(Point left, Point right)
    {
        var dx = right.X - left.X;
        var dy = right.Y - left.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static void AppendCornerArc(PathGeometry path, Point center, Point start, Point end)
    {
        var radius = Distance(center, start);
        var startAngle = MathF.Atan2(start.Y - center.Y, start.X - center.X) * 180f / MathF.PI;
        var endAngle = MathF.Atan2(end.Y - center.Y, end.X - center.X) * 180f / MathF.PI;
        var sweep = endAngle - startAngle;
        while (sweep > 180) sweep -= 360;
        while (sweep <= -180) sweep += 360;
        path.ArcTo(
            new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2),
            startAngle,
            sweep);
    }

    private static (Color thumb, Color track) ResolveColors(Element element, bool overlay)
    {
        var value = element.Style.Get("scrollbar-color")?.Trim();
        if (!string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseColorPair(value, out var thumb, out var track))
                return (thumb, track);
        }
        return (overlay ? DefaultMobileThumb : DefaultThumb, DefaultTrack);
    }

    private static bool TryParseColorPair(string value, out Color thumb, out Color track)
    {
        thumb = default;
        track = default;
        var firstWhitespace = -1;
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '(')
            {
                if (depth == 0 && firstWhitespace < 0)
                    depth = 1;
                else if (depth > 0)
                    depth++;
                continue;
            }
            if (ch == ')' && depth > 0)
            {
                depth--;
                continue;
            }
            if (depth == 0 && char.IsWhiteSpace(ch))
            {
                firstWhitespace = i;
                break;
            }
        }

        if (firstWhitespace < 0) return false;
        var first = value[..firstWhitespace].Trim();
        var second = value[firstWhitespace..].Trim();
        return first.Length > 0 && second.Length > 0 &&
            Color.TryParse(first, out thumb) && Color.TryParse(second, out track);
    }

    private static Color WithOpacity(Color color, float opacity) =>
        Color.FromRgba(color.R, color.G, color.B, (byte)Math.Clamp(color.A * opacity, 0, 255));

    private static Color AdjustColor(Color color, int amount) =>
        Color.FromRgba(
            (byte)Math.Clamp(color.R + amount, 0, 255),
            (byte)Math.Clamp(color.G + amount, 0, 255),
            (byte)Math.Clamp(color.B + amount, 0, 255),
            color.A);

    private static Color ResolveStateColor(
        Color color,
        ScrollbarPart part,
        ScrollbarPart pressedPart,
        ScrollbarPart hoverPart,
        int pressedAmount,
        int hoverAmount) =>
        pressedPart == part ? AdjustColor(color, pressedAmount) :
        hoverPart == part ? AdjustColor(color, hoverAmount) :
        color;
}
