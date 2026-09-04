using System.Globalization;
using Square.Graphics;
using Square.UI;
using Square.UI.Scrolling;

namespace Square.Rendering.Paint;

public static class ScrollbarPainter
{
    private const float ButtonGlyphTipAngle = 75f;
    private static readonly float ButtonGlyphLongAxisRatio =
        1 / (2 * MathF.Tan(ButtonGlyphTipAngle * MathF.PI / 360f));
    private static readonly Color DefaultThumb = Color.FromRgba(128, 128, 128, 170);
    private static readonly Color DefaultMobileThumb = Color.FromRgba(128, 128, 128, 128);
    private static readonly Color DefaultTrack = Color.FromRgba(220, 220, 220, 80);
    private static readonly Color DefaultButtonGlyph = Color.FromRgba(0, 0, 0, 110);

    internal static void Paint(IRenderContext context, Element element)
    {
        if (!element.IsScrollbarChromeVisible) return;
        var metrics = element.GetScrollbarMetrics();
        if (!metrics.HasVertical && !metrics.HasHorizontal) return;

        var (thumb, track) = ResolveColors(element, metrics.IsOverlay);
        var activePart = element.ScrollbarInteractionPart;
        var hoverPart = element.ScrollbarHoverPart;
        var hasThumbStyles = element.HasScrollbarPseudoStylesFor(ScrollbarPseudoElements.Thumb);
        var hasTrackStyles = element.HasScrollbarPseudoStylesFor(ScrollbarPseudoElements.Track) ||
            element.HasScrollbarPseudoStylesFor(ScrollbarPseudoElements.TrackPiece);
        var scrollbarBackground = TryResolvePseudoColor(
            element, ScrollbarPseudoElements.Scrollbar, ScrollbarPart.None, activePart, hoverPart, out var scrollbarColor)
            ? scrollbarColor
            : (Color?)null;
        var verticalThumb = ResolvePseudoColor(element, ScrollbarPseudoElements.Thumb,
            ScrollbarPart.VerticalThumb, activePart, hoverPart, thumb);
        var horizontalThumb = ResolvePseudoColor(element, ScrollbarPseudoElements.Thumb,
            ScrollbarPart.HorizontalThumb, activePart, hoverPart, thumb);
        var verticalTrack = ResolvePseudoColor(element, ScrollbarPseudoElements.Track,
            ScrollbarPart.VerticalTrack, activePart, hoverPart, scrollbarBackground ?? track);
        verticalTrack = ResolvePseudoColor(element, ScrollbarPseudoElements.TrackPiece,
            ScrollbarPart.VerticalTrack, activePart, hoverPart, verticalTrack);
        var horizontalTrack = ResolvePseudoColor(element, ScrollbarPseudoElements.Track,
            ScrollbarPart.HorizontalTrack, activePart, hoverPart, scrollbarBackground ?? track);
        horizontalTrack = ResolvePseudoColor(element, ScrollbarPseudoElements.TrackPiece,
            ScrollbarPart.HorizontalTrack, activePart, hoverPart, horizontalTrack);
        var buttonBackground = TryResolvePseudoColor(
            element, ScrollbarPseudoElements.Button, ScrollbarPart.None, activePart, hoverPart, out var buttonColor)
            ? buttonColor
            : (Color?)null;
        Color? cornerBackground = null;
        if (TryResolvePseudoColor(
                element, ScrollbarPseudoElements.Corner, ScrollbarPart.Corner, activePart, hoverPart, out var cornerColor) ||
            TryResolvePseudoColor(
                element, ScrollbarPseudoElements.Resizer, ScrollbarPart.Corner, activePart, hoverPart, out cornerColor))
            cornerBackground = cornerColor;
        if (IsScrollbarPseudoDisplayNone(element, ScrollbarPseudoElements.Thumb))
        {
            verticalThumb = Color.Transparent;
            horizontalThumb = Color.Transparent;
        }
        if (IsScrollbarPseudoDisplayNone(element, ScrollbarPseudoElements.Track) ||
            IsScrollbarPseudoDisplayNone(element, ScrollbarPseudoElements.TrackPiece))
        {
            verticalTrack = Color.Transparent;
            horizontalTrack = Color.Transparent;
        }
        verticalThumb = WithOpacity(verticalThumb, ParseScrollbarOpacity(element.GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Thumb,
            GetScrollbarPseudoState(activePart, hoverPart, ScrollbarPart.VerticalThumb), "opacity")));
        horizontalThumb = WithOpacity(horizontalThumb, ParseScrollbarOpacity(element.GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Thumb,
            GetScrollbarPseudoState(activePart, hoverPart, ScrollbarPart.HorizontalThumb), "opacity")));
        verticalTrack = WithOpacity(verticalTrack, ParseScrollbarOpacity(element.GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Track,
            GetScrollbarPseudoState(activePart, hoverPart, ScrollbarPart.VerticalTrack), "opacity")));
        horizontalTrack = WithOpacity(horizontalTrack, ParseScrollbarOpacity(element.GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Track,
            GetScrollbarPseudoState(activePart, hoverPart, ScrollbarPart.HorizontalTrack), "opacity")));
        if (buttonBackground.HasValue)
            buttonBackground = WithOpacity(buttonBackground.Value, ParseScrollbarOpacity(
                element.GetScrollbarPseudoStyle(ScrollbarPseudoElements.Button, "", "opacity")));
        var cornerOpacity = element.GetScrollbarPseudoStyle(ScrollbarPseudoElements.Corner, "", "opacity") ??
            element.GetScrollbarPseudoStyle(ScrollbarPseudoElements.Resizer, "", "opacity");
        if (cornerBackground.HasValue || cornerOpacity != null)
            cornerBackground = WithOpacity(cornerBackground ?? track, ParseScrollbarOpacity(cornerOpacity));
        var buttonGlyph = WithOpacity(DefaultButtonGlyph, ParseScrollbarOpacity(
            element.GetScrollbarPseudoStyle(ScrollbarPseudoElements.Button, "", "opacity")));
        var verticalThumbRadius = ParseScrollbarRadius(element.GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Thumb, GetScrollbarPseudoState(activePart, hoverPart, ScrollbarPart.VerticalThumb),
            "border-radius"));
        var horizontalThumbRadius = ParseScrollbarRadius(element.GetScrollbarPseudoStyle(
            ScrollbarPseudoElements.Thumb, GetScrollbarPseudoState(activePart, hoverPart, ScrollbarPart.HorizontalThumb),
            "border-radius"));
        var chromeOpacity = element.ScrollbarOpacity * ParseScrollbarOpacity(
            element.GetScrollbarPseudoStyle(ScrollbarPseudoElements.Scrollbar, "", "opacity"));
        Paint(
            context,
            metrics,
            thumb,
            track,
            buttonGlyph,
            chromeOpacity,
            activePart,
            hoverPart,
            verticalThumbRadius: verticalThumbRadius,
            horizontalThumbRadius: horizontalThumbRadius,
            buttonBackground: buttonBackground,
            cornerBackground: cornerBackground,
            applyThumbStateColors: !hasThumbStyles,
            applyTrackStateColors: !hasTrackStyles,
            verticalThumbColor: hasThumbStyles ? verticalThumb : null,
            horizontalThumbColor: hasThumbStyles ? horizontalThumb : null,
            verticalTrackColor: hasTrackStyles ? verticalTrack : null,
            horizontalTrackColor: hasTrackStyles ? horizontalTrack : null);
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
        Color? pressedThumb = null,
        float? verticalThumbRadius = null,
        float? horizontalThumbRadius = null,
        Color? buttonBackground = null,
        Color? cornerBackground = null,
        bool applyStateColors = true,
        bool applyThumbStateColors = true,
        bool applyTrackStateColors = true,
        Color? verticalThumbColor = null,
        Color? horizontalThumbColor = null,
        Color? verticalTrackColor = null,
        Color? horizontalTrackColor = null)
    {
        if (!metrics.HasVertical && !metrics.HasHorizontal || opacity <= 0.001f) return;
        var resolvedApplyThumbStateColors = applyStateColors && applyThumbStateColors;
        var resolvedApplyTrackStateColors = applyStateColors && applyTrackStateColors;
        var verticalBaseThumb = verticalThumbColor ?? thumb;
        var horizontalBaseThumb = horizontalThumbColor ?? thumb;
        var verticalBaseTrack = verticalTrackColor ?? track;
        var horizontalBaseTrack = horizontalTrackColor ?? track;
        var verticalThumb = pressedPart == ScrollbarPart.VerticalThumb && pressedThumb.HasValue
            ? pressedThumb.Value
            : resolvedApplyThumbStateColors
                ? ResolveStateColor(verticalBaseThumb, ScrollbarPart.VerticalThumb, pressedPart, hoverPart, 36, 20)
                : verticalBaseThumb;
        var horizontalThumb = pressedPart == ScrollbarPart.HorizontalThumb && pressedThumb.HasValue
            ? pressedThumb.Value
            : resolvedApplyThumbStateColors
                ? ResolveStateColor(horizontalBaseThumb, ScrollbarPart.HorizontalThumb, pressedPart, hoverPart, 36, 20)
                : horizontalBaseThumb;
        var verticalTrack = resolvedApplyTrackStateColors
            ? ResolveStateColor(verticalBaseTrack, ScrollbarPart.VerticalTrack, pressedPart, hoverPart, -16, 10)
            : verticalBaseTrack;
        var horizontalTrack = resolvedApplyTrackStateColors
            ? ResolveStateColor(horizontalBaseTrack, ScrollbarPart.HorizontalTrack, pressedPart, hoverPart, -16, 10)
            : horizontalBaseTrack;
        var verticalBackButton = applyStateColors
            ? ResolveStateColor(buttonGlyph, ScrollbarPart.VerticalBackButton, pressedPart, hoverPart, -24, 16)
            : buttonGlyph;
        var verticalForwardButton = applyStateColors
            ? ResolveStateColor(buttonGlyph, ScrollbarPart.VerticalForwardButton, pressedPart, hoverPart, -24, 16)
            : buttonGlyph;
        var horizontalBackButton = applyStateColors
            ? ResolveStateColor(buttonGlyph, ScrollbarPart.HorizontalBackButton, pressedPart, hoverPart, -24, 16)
            : buttonGlyph;
        var horizontalForwardButton = applyStateColors
            ? ResolveStateColor(buttonGlyph, ScrollbarPart.HorizontalForwardButton, pressedPart, hoverPart, -24, 16)
            : buttonGlyph;
        if (metrics.IsOverlay)
        {
            PaintThumb(context, metrics.VerticalThumb, WithOpacity(verticalThumb, opacity), verticalThumbRadius);
            PaintThumb(context, metrics.HorizontalThumb, WithOpacity(horizontalThumb, opacity), horizontalThumbRadius);
            return;
        }

        if (opacity < 0.999f)
        {
            verticalThumb = WithOpacity(verticalThumb, opacity);
            horizontalThumb = WithOpacity(horizontalThumb, opacity);
            verticalTrack = WithOpacity(verticalTrack, opacity);
            horizontalTrack = WithOpacity(horizontalTrack, opacity);
            verticalBackButton = WithOpacity(verticalBackButton, opacity);
            verticalForwardButton = WithOpacity(verticalForwardButton, opacity);
            horizontalBackButton = WithOpacity(horizontalBackButton, opacity);
            horizontalForwardButton = WithOpacity(horizontalForwardButton, opacity);
            track = WithOpacity(track, opacity);
        }

        PaintTrack(context, metrics.VerticalGutter, verticalTrack);
        PaintTrack(context, metrics.HorizontalGutter, horizontalTrack);
        PaintTrack(context, metrics.Corner, cornerBackground.HasValue
            ? WithOpacity(cornerBackground.Value, opacity)
            : track);
        if (buttonBackground.HasValue)
        {
            var background = WithOpacity(buttonBackground.Value, opacity);
            PaintTrack(context, metrics.VerticalBackButton, background);
            PaintTrack(context, metrics.VerticalForwardButton, background);
            PaintTrack(context, metrics.HorizontalBackButton, background);
            PaintTrack(context, metrics.HorizontalForwardButton, background);
        }
        PaintVerticalButton(context, metrics.VerticalBackButton, up: true, verticalBackButton);
        PaintVerticalButton(context, metrics.VerticalForwardButton, up: false, verticalForwardButton);
        PaintHorizontalButton(context, metrics.HorizontalBackButton, left: true, horizontalBackButton);
        PaintHorizontalButton(context, metrics.HorizontalForwardButton, left: false, horizontalForwardButton);
        PaintThumb(context, metrics.VerticalThumb, verticalThumb, verticalThumbRadius);
        PaintThumb(context, metrics.HorizontalThumb, horizontalThumb, horizontalThumbRadius);
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

    private static void PaintThumb(IRenderContext context, Rect rect, Color color, float? radius = null)
    {
        if (rect.IsEmpty || color.A == 0) return;
        var resolvedRadius = Math.Clamp(radius ?? Math.Min(rect.Width, rect.Height) / 2,
            0, Math.Min(rect.Width, rect.Height) / 2);
        context.FillGeometry(new RoundedRectGeometry(rect, resolvedRadius, resolvedRadius), Brush.FromColor(color));
    }

    private static void PaintVerticalButton(IRenderContext context, Rect rect, bool up, Color color)
    {
        if (rect.IsEmpty || color.A == 0) return;
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var size = Math.Min(rect.Width, rect.Height) * 0.28f;
        var longAxisSize = size * ButtonGlyphLongAxisRatio;
        var path = up
            ? CreateRoundedTriangle(
                new Point(center.X, center.Y - longAxisSize),
                new Point(center.X - size, center.Y + longAxisSize),
                new Point(center.X + size, center.Y + longAxisSize),
                size * 0.32f)
            : CreateRoundedTriangle(
                new Point(center.X, center.Y + longAxisSize),
                new Point(center.X + size, center.Y - longAxisSize),
                new Point(center.X - size, center.Y - longAxisSize),
                size * 0.32f);
        context.FillPath(path, Brush.FromColor(color));
    }

    private static void PaintHorizontalButton(IRenderContext context, Rect rect, bool left, Color color)
    {
        if (rect.IsEmpty || color.A == 0) return;
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var size = Math.Min(rect.Width, rect.Height) * 0.28f;
        var longAxisSize = size * ButtonGlyphLongAxisRatio;
        var path = left
            ? CreateRoundedTriangle(
                new Point(center.X - longAxisSize, center.Y),
                new Point(center.X + longAxisSize, center.Y + size),
                new Point(center.X + longAxisSize, center.Y - size),
                size * 0.32f)
            : CreateRoundedTriangle(
                new Point(center.X + longAxisSize, center.Y),
                new Point(center.X - longAxisSize, center.Y - size),
                new Point(center.X - longAxisSize, center.Y + size),
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

    private static Color ResolvePseudoColor(
        Element element,
        string pseudoElement,
        ScrollbarPart part,
        ScrollbarPart pressedPart,
        ScrollbarPart hoverPart,
        Color fallback) =>
        TryResolvePseudoColor(element, pseudoElement, part, pressedPart, hoverPart, out var color)
            ? color
            : fallback;

    private static bool TryResolvePseudoColor(
        Element element,
        string pseudoElement,
        ScrollbarPart part,
        ScrollbarPart pressedPart,
        ScrollbarPart hoverPart,
        out Color color)
    {
        var state = GetScrollbarPseudoState(pressedPart, hoverPart, part);
        foreach (var property in new[] { "background-color", "background" })
        {
            var value = state.Length == 0
                ? null
                : element.GetScrollbarPseudoStyle(pseudoElement, state, property);
            value ??= element.GetScrollbarPseudoStyle(pseudoElement, "", property);
            if (TryParsePseudoColor(element, value, out color)) return true;
        }
        color = default;
        return false;
    }

    private static bool IsScrollbarPseudoDisplayNone(Element element, string pseudoElement) =>
        string.Equals(element.GetScrollbarPseudoStyle(pseudoElement, "", "display")?.Trim(),
            "none", StringComparison.OrdinalIgnoreCase);

    private static string GetScrollbarPseudoState(
        ScrollbarPart pressedPart,
        ScrollbarPart hoverPart,
        ScrollbarPart part) =>
        pressedPart == part ? "active" : hoverPart == part ? "hover" : "";

    private static bool TryParsePseudoColor(Element element, string? value, out Color color)
    {
        if (string.Equals(value?.Trim(), "currentcolor", StringComparison.OrdinalIgnoreCase))
            return Color.TryParse(element.Style.Get("color"), out color);
        return Color.TryParse(value, out color);
    }

    private static float ParseScrollbarOpacity(string? value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) &&
        float.IsFinite(opacity)
            ? Math.Clamp(opacity, 0, 1)
            : 1;

    private static float? ParseScrollbarRadius(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2].Trim();
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius) &&
               float.IsFinite(radius) && radius >= 0
            ? radius
            : null;
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
