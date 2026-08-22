using System.Globalization;
using Square.CSS.Properties;
using Square.Controls;
using Square.Graphics;
using Square.UI;

namespace Square.Rendering.Paint;

internal static class CssBoxPainter
{
    internal enum BorderStyle
    {
        None,
        Solid,
        Inset,
        Outset
    }

    internal readonly record struct BorderEdge(float Width, Color Color, BorderStyle Style);

    public static void PaintBeforeContent(IRenderContext context, Element element)
    {
        if (element is IPopupElement { IsLayoutOverlay: true }) return;
        if (TablePaintMetadataStore.TryGetActive(element, out var tableMetadata) && tableMetadata.SuppressCssBox)
        {
            context.PushClip(Rect.Empty);
            return;
        }

        var geometry = element.Geometry;
        var roundedGeometry = TryGetRoundedGeometry(element, geometry, out var rounded) ? rounded : null;
        var declarations = element.Style.GetAll();
        if (declarations.ContainsKey("box-shadow") &&
            BoxShadow.TryParseList(element.Style.Get("box-shadow"), out var shadows))
            BoxShadowRendering.Draw(context, geometry, roundedGeometry, shadows);

        // View already paints colors understood by its styled-background helper.
        if (!ViewPaintsBackground(element) &&
            TryGetBackgroundColor(element, declarations, out var background) && background.A > 0)
        {
            var brush = new SolidColorBrush(background);
            if (roundedGeometry != null)
            {
                if (roundedGeometry.IsUniform)
                    context.FillGeometry(roundedGeometry, brush);
                else
                    context.FillPath(roundedGeometry.ToPath(), brush);
            }
            else
                context.FillRect(geometry, brush);
        }

    }

    public static void PaintAfterContent(IRenderContext context, Element element)
    {
        if (element is IPopupElement { IsLayoutOverlay: true }) return;
        if (TablePaintMetadataStore.TryGetActive(element, out var tableMetadata) && tableMetadata.SuppressCssBox)
        {
            context.PopClip();
            return;
        }

        if (tableMetadata?.UseCollapsedBorderFragments == true)
            PaintCollapsedBorderFragments(context, tableMetadata.CollapsedBorderFragments);
        else
            PaintBorder(context, element, element.Style.GetAll());
    }

    public static void PaintAfterChildren(IRenderContext context, Element element)
    {
        if (element is IPopupElement { IsLayoutOverlay: true }) return;
        if (TablePaintMetadataStore.TryGetActive(element, out var tableMetadata) && tableMetadata.SuppressCssBox)
            return;
        PaintOutline(context, element, element.Style.GetAll());
    }

    private static void PaintCollapsedBorderFragments(
        IRenderContext context,
        IReadOnlyList<TableBorderFragment> fragments)
    {
        foreach (var fragment in fragments)
            if (!fragment.Bounds.IsEmpty && fragment.Color.A > 0)
                context.FillRect(fragment.Bounds, new SolidColorBrush(fragment.Color));
    }

    private static void PaintBorder(IRenderContext context, Element element, IReadOnlyDictionary<string, string> declarations)
    {
        if (!TryGetBorderEdges(element, declarations, out var edges)) return;

        var box = element.Geometry;
        var top = GetPaintWidth(edges[0], box.Height);
        var right = GetPaintWidth(edges[1], box.Width);
        var bottom = GetPaintWidth(edges[2], box.Height);
        var left = GetPaintWidth(edges[3], box.Width);

        var roundedGeometry = TryGetRoundedGeometry(element, box, out var rounded) ? rounded : null;
        if (roundedGeometry != null && PaintRoundedBorder(context, roundedGeometry, edges, top, right, bottom, left))
            return;

        if (top > 0)
            PaintBorderSide(context, new Rect(box.X, box.Y, box.Width, top), edges[0], highlight: true);
        if (bottom > 0)
            PaintBorderSide(context, new Rect(box.X, box.Bottom - bottom, box.Width, bottom), edges[2], highlight: false);

        var middleTop = box.Y + top;
        var middleHeight = Math.Max(0, box.Height - top - bottom);
        if (left > 0 && middleHeight > 0)
            PaintBorderSide(context, new Rect(box.X, middleTop, left, middleHeight), edges[3], highlight: true);
        if (right > 0 && middleHeight > 0)
            PaintBorderSide(context, new Rect(box.Right - right, middleTop, right, middleHeight), edges[1], highlight: false);
    }

    private static void PaintBorderSide(IRenderContext context, Rect rect, BorderEdge edge, bool highlight)
    {
        context.FillRect(rect, new SolidColorBrush(Resolve3dBorderColor(edge, highlight)));
    }

    private static Color Resolve3dBorderColor(BorderEdge edge, bool highlight)
    {
        if (edge.Style is not (BorderStyle.Inset or BorderStyle.Outset))
            return edge.Color;
        var light = ControlDrawing.Blend(edge.Color, Color.White, 0.55f);
        var dark = ControlDrawing.Blend(edge.Color, Color.Black, 0.35f);
        var raised = edge.Style == BorderStyle.Outset;
        return (raised == highlight) ? light : dark;
    }

    private static bool PaintRoundedBorder(
        IRenderContext context, RoundedRectGeometry geometry,
        BorderEdge[] edges, float top, float right, float bottom, float left)
    {
        if (top != right || top != bottom || top != left) return false;
        if (top <= 0) return true;
        var color = edges[0].Color;
        for (var i = 1; i < edges.Length; i++)
            if (edges[i].Color != color) return false;

        if (geometry.IsUniform)
        {
            var half = top / 2f;
            var strokeBox = new Rect(geometry.Rect.X + half, geometry.Rect.Y + half,
                geometry.Rect.Width - top, geometry.Rect.Height - top);
            var strokeRadius = Math.Max(0, geometry.RadiusX - half);
            context.DrawGeometry(
                new RoundedRectGeometry(strokeBox, strokeRadius, strokeRadius),
                Pen.FromColor(color, top));
        }
        else
        {
            context.DrawPath(geometry.ToPath(), Pen.FromColor(color, top));
        }
        return true;
    }

    private static void PaintOutline(IRenderContext context, Element element, IReadOnlyDictionary<string, string> declarations)
    {
        if (!TryGetOutline(element, declarations, out var outline) || outline.Style != BorderStyle.Solid ||
            outline.Width <= 0 || outline.Color.A == 0) return;

        var box = element.Geometry;
        var width = outline.Width;
        var brush = new SolidColorBrush(outline.Color);
        context.FillRect(new Rect(box.X - width, box.Y - width, box.Width + width * 2, width), brush);
        context.FillRect(new Rect(box.X - width, box.Bottom, box.Width + width * 2, width), brush);
        context.FillRect(new Rect(box.X - width, box.Y, width, box.Height), brush);
        context.FillRect(new Rect(box.Right, box.Y, width, box.Height), brush);
    }

    private static bool TryGetBackgroundColor(
        Element element,
        IReadOnlyDictionary<string, string> declarations,
        out Color color)
    {
        color = default;
        if (declarations.ContainsKey("background-color") &&
            TryParseColor(element.Style.Get("background-color"), element, out color)) return true;
        return declarations.ContainsKey("background") &&
            TryParseColor(element.Style.Get("background"), element, out color);
    }

    private static bool ViewPaintsBackground(Element element)
    {
        if (element is not View) return false;
        var value = element.Style.Get("background-color");
        if (string.IsNullOrWhiteSpace(value)) value = element.Style.Get("background");
        return Color.TryParse(value, out _);
    }

    internal static bool TryGetBorderEdges(
        Element element,
        IReadOnlyDictionary<string, string> declarations,
        out BorderEdge[] edges)
    {
        var style = element.Style;
        var borderProperties = new[]
        {
            "border", "border-width", "border-color", "border-style",
            "border-top", "border-top-width", "border-top-color", "border-top-style",
            "border-right", "border-right-width", "border-right-color", "border-right-style",
            "border-bottom", "border-bottom-width", "border-bottom-color", "border-bottom-style",
            "border-left", "border-left-width", "border-left-color", "border-left-style"
        };
        if (!borderProperties.Any(declarations.ContainsKey))
        {
            edges = [];
            return false;
        }

        var currentColor = TryParseColor(style.Get("color"), element, out var parsedColor)
            ? parsedColor
            : Color.Black;
        var hasGlobalWidth = declarations.ContainsKey("border-width");
        var hasGlobalStyleDeclaration = declarations.ContainsKey("border") || declarations.ContainsKey("border-style");
        var defaultStyle = hasGlobalWidth && !hasGlobalStyleDeclaration ? BorderStyle.Solid : BorderStyle.None;
        edges =
        [
            new BorderEdge(3, currentColor, defaultStyle),
            new BorderEdge(3, currentColor, defaultStyle),
            new BorderEdge(3, currentColor, defaultStyle),
            new BorderEdge(3, currentColor, defaultStyle)
        ];

        if (TryParseBorderShorthand(style.Get("border"), element, out var border))
            for (var i = 0; i < edges.Length; i++) edges[i] = border;
        if (TryParseLengths(style.Get("border-width"), out var widths))
            for (var i = 0; i < edges.Length; i++) edges[i] = edges[i] with { Width = widths[i] };
        if (TryParseColors(style.Get("border-color"), element, out var colors))
            for (var i = 0; i < edges.Length; i++) edges[i] = edges[i] with { Color = colors[i] };
        if (TryParseStyles(style.Get("border-style"), out var styles))
            for (var i = 0; i < edges.Length; i++) edges[i] = edges[i] with { Style = styles[i] };

        var names = new[] { "top", "right", "bottom", "left" };
        for (var i = 0; i < names.Length; i++)
        {
            var prefix = $"border-{names[i]}";
            var hasEdgeDeclaration = declarations.ContainsKey(prefix) || declarations.ContainsKey($"{prefix}-width") ||
                declarations.ContainsKey($"{prefix}-color") || declarations.ContainsKey($"{prefix}-style");
            var hasEdgeStyleDeclaration = declarations.ContainsKey(prefix) || declarations.ContainsKey($"{prefix}-style");
            if ((hasGlobalWidth || declarations.ContainsKey($"{prefix}-width")) &&
                !hasGlobalStyleDeclaration && hasEdgeDeclaration && !hasEdgeStyleDeclaration)
                edges[i] = edges[i] with { Style = BorderStyle.Solid };
            if (TryParseBorderShorthand(style.Get(prefix), element, out var edge)) edges[i] = edge;
            if (TryParseLength(style.Get($"{prefix}-width"), out var width)) edges[i] = edges[i] with { Width = width };
            if (TryParseColor(style.Get($"{prefix}-color"), element, out var color)) edges[i] = edges[i] with { Color = color };
            if (TryParseStyle(style.Get($"{prefix}-style"), out var edgeStyle)) edges[i] = edges[i] with { Style = edgeStyle };
        }

        return true;
    }

    private static bool TryGetOutline(
        Element element,
        IReadOnlyDictionary<string, string> declarations,
        out BorderEdge outline)
    {
        var style = element.Style;
        if (!declarations.ContainsKey("outline") && !declarations.ContainsKey("outline-width") &&
            !declarations.ContainsKey("outline-color") && !declarations.ContainsKey("outline-style"))
        {
            outline = default;
            return false;
        }

        var currentColor = TryParseColor(style.Get("color"), element, out var parsedColor)
            ? parsedColor
            : Color.Black;
        outline = new BorderEdge(3, currentColor, BorderStyle.None);
        if (TryParseBorderShorthand(style.Get("outline"), element, out var shorthand)) outline = shorthand;
        if (TryParseLength(style.Get("outline-width"), out var width)) outline = outline with { Width = width };
        if (TryParseColor(style.Get("outline-color"), element, out var color)) outline = outline with { Color = color };
        if (TryParseStyle(style.Get("outline-style"), out var outlineStyle)) outline = outline with { Style = outlineStyle };
        return true;
    }

    private static bool TryParseBorderShorthand(string? value, Element element, out BorderEdge border)
    {
        border = new BorderEdge(3, Color.Black, BorderStyle.None);
        if (string.IsNullOrWhiteSpace(value)) return false;

        var width = 3f;
        var color = TryParseColor(element.Style.Get("color"), element, out var currentColor)
            ? currentColor
            : Color.Black;
        var style = BorderStyle.None;
        var found = false;
        foreach (var token in Tokenize(value))
        {
            if (TryParseStyle(token, out var parsedStyle)) style = parsedStyle;
            else if (TryParseLength(token, out var parsedWidth)) width = parsedWidth;
            else if (TryParseColor(token, element, out var parsedColor)) color = parsedColor;
            else return false;
            found = true;
        }

        border = new BorderEdge(width, color, style);
        return found;
    }

    private static bool TryParseLengths(string? value, out float[] edges)
    {
        edges = [];
        var tokens = Tokenize(value);
        if (tokens.Count is < 1 or > 4) return false;
        var values = new float[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
            if (!TryParseLength(tokens[i], out values[i])) return false;
        edges = ExpandEdges(values);
        return true;
    }

    private static bool TryParseColors(string? value, Element element, out Color[] edges)
    {
        edges = [];
        var tokens = Tokenize(value);
        if (tokens.Count is < 1 or > 4) return false;
        var values = new Color[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
            if (!TryParseColor(tokens[i], element, out values[i])) return false;
        edges = ExpandEdges(values);
        return true;
    }

    private static bool TryParseStyles(string? value, out BorderStyle[] edges)
    {
        edges = [];
        var tokens = Tokenize(value);
        if (tokens.Count is < 1 or > 4) return false;
        var values = new BorderStyle[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
            if (!TryParseStyle(tokens[i], out values[i])) return false;
        edges = ExpandEdges(values);
        return true;
    }

    private static T[] ExpandEdges<T>(IReadOnlyList<T> values)
    {
        var top = values[0];
        var right = values.Count > 1 ? values[1] : top;
        var bottom = values.Count > 2 ? values[2] : top;
        var left = values.Count > 3 ? values[3] : right;
        return [top, right, bottom, left];
    }

    private static bool TryParseLength(string? value, out float length)
    {
        length = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (string.Equals(text, "thin", StringComparison.OrdinalIgnoreCase)) { length = 1; return true; }
        if (string.Equals(text, "medium", StringComparison.OrdinalIgnoreCase)) { length = 3; return true; }
        if (string.Equals(text, "thick", StringComparison.OrdinalIgnoreCase)) { length = 5; return true; }
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2];
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out length) &&
            float.IsFinite(length) && length >= 0;
    }

    private static bool TryParseStyle(string? value, out BorderStyle style)
    {
        if (string.Equals(value?.Trim(), "solid", StringComparison.OrdinalIgnoreCase))
        {
            style = BorderStyle.Solid;
            return true;
        }
        if (string.Equals(value?.Trim(), "inset", StringComparison.OrdinalIgnoreCase))
        {
            style = BorderStyle.Inset;
            return true;
        }
        if (string.Equals(value?.Trim(), "outset", StringComparison.OrdinalIgnoreCase))
        {
            style = BorderStyle.Outset;
            return true;
        }
        if (string.Equals(value?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            style = BorderStyle.None;
            return true;
        }
        style = default;
        return false;
    }

    private static bool TryParseColor(string? value, Element element, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (string.Equals(text, "currentcolor", StringComparison.OrdinalIgnoreCase))
            return TryParseColor(element.Style.Get("color"), element, out color);
        return Color.TryParse(text, out color);
    }

    private static List<string> Tokenize(string? value)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(value)) return tokens;
        var start = 0;
        var depth = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i < value.Length)
            {
                if (value[i] == '(') depth++;
                else if (value[i] == ')') depth--;
                if (!char.IsWhiteSpace(value[i]) || depth > 0) continue;
            }
            if (i > start) tokens.Add(value[start..i]);
            start = i + 1;
        }
        return tokens;
    }

    private static float GetPaintWidth(BorderEdge edge, float maximum) =>
        edge.Style != BorderStyle.None && edge.Color.A > 0
            ? Math.Clamp(edge.Width, 0, Math.Max(0, maximum))
            : 0;

    private static bool TryGetRoundedGeometry(Element element, Rect box, out RoundedRectGeometry geometry)
    {
        if (!CssBorderRadiusParser.TryResolve(element.Style.GetAll(), element.Style.Get, box, out geometry))
            return false;
        return geometry.TopLeft.X > 0 || geometry.TopLeft.Y > 0 ||
            geometry.TopRight.X > 0 || geometry.TopRight.Y > 0 ||
            geometry.BottomRight.X > 0 || geometry.BottomRight.Y > 0 ||
            geometry.BottomLeft.X > 0 || geometry.BottomLeft.Y > 0;
    }
}
