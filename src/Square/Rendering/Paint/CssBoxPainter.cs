using System.Globalization;
using Square.Controls;
using Square.Graphics;
using Square.UI;

namespace Square.Rendering.Paint;

internal static class CssBoxPainter
{
    internal enum BorderStyle
    {
        None,
        Solid
    }

    internal readonly record struct BorderEdge(float Width, Color Color, BorderStyle Style);

    public static void PaintBeforeContent(IRenderContext context, Element element)
    {
        if (element is IPopupElement) return;
        if (TablePaintMetadataStore.TryGetActive(element, out var tableMetadata) && tableMetadata.SuppressCssBox)
        {
            context.PushClip(Rect.Empty);
            return;
        }

        var geometry = element.Geometry;
        var radius = GetCornerRadius(element);
        var declarations = element.Style.GetAll();
        if (declarations.ContainsKey("box-shadow") &&
            BoxShadow.TryParseList(element.Style.Get("box-shadow"), out var shadows))
            BoxShadowRendering.Draw(context, geometry, radius, shadows);

        // View already paints colors understood by its styled-background helper.
        if (!ViewPaintsBackground(element) &&
            TryGetBackgroundColor(element, declarations, out var background) && background.A > 0)
        {
            var brush = new SolidColorBrush(background);
            if (radius > 0)
                context.FillGeometry(new RoundedRectGeometry(geometry, radius, radius), brush);
            else
                context.FillRect(geometry, brush);
        }

        if (tableMetadata?.UseCollapsedBorderFragments == true)
            PaintCollapsedBorderFragments(context, tableMetadata.CollapsedBorderFragments);
        else
            PaintBorder(context, element, declarations);
    }

    public static void PaintAfterContent(IRenderContext context, Element element)
    {
        if (element is IPopupElement) return;
        if (TablePaintMetadataStore.TryGetActive(element, out var tableMetadata) && tableMetadata.SuppressCssBox)
            context.PopClip();
    }

    public static void PaintAfterChildren(IRenderContext context, Element element)
    {
        if (element is IPopupElement) return;
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

        if (top > 0)
            context.FillRect(new Rect(box.X, box.Y, box.Width, top), new SolidColorBrush(edges[0].Color));
        if (bottom > 0)
            context.FillRect(new Rect(box.X, box.Bottom - bottom, box.Width, bottom), new SolidColorBrush(edges[2].Color));

        var middleTop = box.Y + top;
        var middleHeight = Math.Max(0, box.Height - top - bottom);
        if (left > 0 && middleHeight > 0)
            context.FillRect(new Rect(box.X, middleTop, left, middleHeight), new SolidColorBrush(edges[3].Color));
        if (right > 0 && middleHeight > 0)
            context.FillRect(new Rect(box.Right - right, middleTop, right, middleHeight), new SolidColorBrush(edges[1].Color));
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
        if (Color.TryParse(text, out color)) return true;
        if (string.Equals(text, "transparent", StringComparison.OrdinalIgnoreCase)) { color = Color.Transparent; return true; }
        if (string.Equals(text, "black", StringComparison.OrdinalIgnoreCase)) { color = Color.Black; return true; }
        if (string.Equals(text, "white", StringComparison.OrdinalIgnoreCase)) { color = Color.White; return true; }
        if (string.Equals(text, "red", StringComparison.OrdinalIgnoreCase)) { color = Color.Red; return true; }
        if (string.Equals(text, "green", StringComparison.OrdinalIgnoreCase)) { color = Color.Green; return true; }
        if (string.Equals(text, "blue", StringComparison.OrdinalIgnoreCase)) { color = Color.Blue; return true; }

        var rgba = text.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')');
        var rgb = text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')');
        if (!rgba && !rgb) return false;
        var parts = text[(rgba ? 5 : 4)..^1].Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != (rgba ? 4 : 3) || !TryParseByte(parts[0], out var red) ||
            !TryParseByte(parts[1], out var green) || !TryParseByte(parts[2], out var blue)) return false;
        var alpha = (byte)255;
        if (rgba && !TryParseAlpha(parts[3], out alpha)) return false;
        color = Color.FromRgba(red, green, blue, alpha);
        return true;
    }

    private static bool TryParseByte(string value, out byte result)
    {
        result = 0;
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !float.IsFinite(parsed) || parsed is < 0 or > 255) return false;
        result = (byte)MathF.Round(parsed);
        return true;
    }

    private static bool TryParseAlpha(string value, out byte result)
    {
        result = 0;
        var text = value.Trim();
        var percent = text.EndsWith('%');
        if (percent) text = text[..^1];
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !float.IsFinite(parsed))
            return false;
        parsed = percent ? parsed / 100f : parsed;
        if (parsed is < 0 or > 1) return false;
        result = (byte)MathF.Round(parsed * 255);
        return true;
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
        edge.Style == BorderStyle.Solid && edge.Color.A > 0
            ? Math.Clamp(edge.Width, 0, Math.Max(0, maximum))
            : 0;

    private static float GetCornerRadius(Element element)
    {
        var raw = element.Style.Get("border-radius") ?? "";
        var token = raw.Trim().Split([' ', '/'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token)) return 0;
        var maximum = MathF.Max(0, MathF.Min(element.Geometry.Width, element.Geometry.Height) / 2f);
        if (token.EndsWith('%') && float.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            return Math.Clamp(MathF.Min(element.Geometry.Width, element.Geometry.Height) * percent / 100f, 0, maximum);
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2];
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels)
            ? Math.Clamp(pixels, 0, maximum)
            : 0;
    }
}
