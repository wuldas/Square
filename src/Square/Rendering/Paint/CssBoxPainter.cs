using System.Globalization;
using Square.CSS.Properties;
using Square.Controls;
using Square.Graphics;
using Square.UI;

namespace Square.Rendering.Paint;

internal static class CssBoxPainter
{
    private static readonly string[] AuthorBoxProperties =
    [
        "background", "background-color", "box-shadow",
        "border", "border-width", "border-color", "border-style",
        "border-top", "border-top-width", "border-top-color", "border-top-style",
        "border-right", "border-right-width", "border-right-color", "border-right-style",
        "border-bottom", "border-bottom-width", "border-bottom-color", "border-bottom-style",
        "border-left", "border-left-width", "border-left-color", "border-left-style",
        "border-radius", "border-top-left-radius", "border-top-right-radius",
        "border-bottom-right-radius", "border-bottom-left-radius"
    ];

    private static readonly string[] AuthorBorderProperties =
        AuthorBoxProperties.Where(property => property.StartsWith("border", StringComparison.Ordinal)).ToArray();

    private static readonly string[] AuthorOutlineProperties =
        ["outline", "outline-width", "outline-color", "outline-style", "outline-offset"];

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
        if (element is Button button && UsesDefaultButtonWidgetPaint(button))
        {
            PaintButtonWidget(context, button, geometry);
            return;
        }
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
        if (element is Button button && UsesDefaultButtonWidgetPaint(button)) return;
        if (element is Input input && ControlDrawing.UsesWidgetAppearance(input) && !HasAuthorBorderStyle(input))
        {
            PaintInputWidgetBorder(context, input, input.Geometry);
            return;
        }
        if (element is TextArea textArea && ControlDrawing.UsesWidgetAppearance(textArea) && !HasAuthorBorderStyle(textArea))
        {
            PaintTextAreaWidgetBorder(context, textArea, textArea.Geometry);
            return;
        }
        if (element is Select select && select.IsEnabled && ControlDrawing.UsesWidgetAppearance(select) &&
            !HasAuthorBorderStyle(select))
        {
            PaintSelectWidgetBorder(context, select, select.Geometry);
            return;
        }
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
        if (element is Button button && UsesDefaultButtonWidgetPaint(button) &&
            !HasAuthorOutlineStyle(button)) return;
        if (element is CheckBox checkBox && ControlDrawing.UsesWidgetAppearance(checkBox)) return;
        if (element is Radio radio && ControlDrawing.UsesWidgetAppearance(radio)) return;
        if (element is Input input && UsesDefaultWidgetFocusBorder(input))
        {
            PaintInputFocusBorder(context, input.Geometry);
            return;
        }
        if (element is TextArea textArea && UsesDefaultWidgetFocusBorder(textArea))
        {
            PaintInputFocusBorder(context, textArea.Geometry);
            return;
        }
        if (element is Select select && UsesDefaultWidgetFocusBorder(select))
        {
            PaintInputFocusBorder(context, select.Geometry);
            return;
        }
        if (TablePaintMetadataStore.TryGetActive(element, out var tableMetadata) && tableMetadata.SuppressCssBox)
            return;
        PaintOutline(context, element, element.Style.GetAll());
    }

    private static bool UsesDefaultWidgetFocusBorder(UIElement element)
    {
        if (!element.HasState(ElementState.Focus) ||
            TryGetRoundedGeometry(element, element.Geometry, out _)) return false;
        return TryParseLength(element.Style.Get("outline-width"), out var width) && width == 1 &&
            TryParseStyle(element.Style.Get("outline-style"), out var style) && style == BorderStyle.Solid &&
            string.Equals(element.Style.Get("outline-color"), "Highlight", StringComparison.OrdinalIgnoreCase) &&
            (!TryParseSignedLength(element.Style.Get("outline-offset"), out var offset) || offset == 0);
    }

    private static bool HasAuthorBoxStyle(Element element) =>
        AuthorBoxProperties.Any(element.Style.IsAuthorSpecified);

    internal static bool UsesDefaultButtonWidgetPaint(Button button) =>
        ControlDrawing.UsesWidgetAppearance(button) && !HasAuthorBoxStyle(button);

    private static bool HasAuthorBorderStyle(Element element) =>
        AuthorBorderProperties.Any(element.Style.IsAuthorSpecified);

    private static bool HasAuthorOutlineStyle(Element element) =>
        AuthorOutlineProperties.Any(element.Style.IsAuthorSpecified);

    private static void PaintButtonWidget(IRenderContext context, Button button, Rect geometry)
    {
        var (fill, border) = !button.IsEnabled
            ? (Color.FromRgb(238, 238, 238), Color.FromRgb(208, 208, 208))
            : button.HasState(ElementState.Active)
                ? (Color.FromRgb(245, 245, 245), Color.FromRgb(141, 141, 141))
                : button.HasState(ElementState.Hover)
                    ? (Color.FromRgb(229, 229, 229), Color.FromRgb(79, 79, 79))
                    : (Color.FromRgb(239, 239, 239), Color.FromRgb(118, 118, 118));
        if (button.Properties.HasValue(nameof(Button.Background)))
            fill = button.Background;
        var outer = new Rect(geometry.X + 1.5f, geometry.Y + 1.5f, geometry.Width - 2f, geometry.Height - 2f);
        context.FillGeometry(new RoundedRectGeometry(outer, 3, 3), new SolidColorBrush(border));
        var inner = new Rect(outer.X + 1, outer.Y + 1, outer.Width - 2, outer.Height - 2);
        context.FillGeometry(new RoundedRectGeometry(inner, 2, 2), new SolidColorBrush(fill));
        var left = MathF.Round(geometry.X, MidpointRounding.AwayFromZero);
        var top = MathF.Round(geometry.Y, MidpointRounding.AwayFromZero);
        var right = MathF.Round(geometry.Right, MidpointRounding.AwayFromZero) - 1;
        var bottom = MathF.Round(geometry.Bottom, MidpointRounding.AwayFromZero) - 1;
        var fillBrush = new SolidColorBrush(fill);
        context.FillRect(new Rect(left + 2, top + 4, right - left - 3, bottom - top - 5), fillBrush);
        context.FillRect(new Rect(left + 4, top + 2, right - left - 6, bottom - top - 2), fillBrush);
        var borderBrush = new SolidColorBrush(border);
        context.FillRect(new Rect(left + 4, top + 1, right - left - 6, 1), borderBrush);
        context.FillRect(new Rect(left + 4, bottom + 1, right - left - 6, 1), borderBrush);
        context.FillRect(new Rect(left + 1, top + 4, 1, bottom - top - 6), borderBrush);
        context.FillRect(new Rect(right, top + 4, 1, bottom - top - 6), borderBrush);
    }

    private static void PaintInputWidgetBorder(IRenderContext context, Input input, Rect geometry)
    {
        var left = MathF.Round(geometry.X, MidpointRounding.AwayFromZero);
        var top = MathF.Round(geometry.Y, MidpointRounding.AwayFromZero);
        var right = MathF.Round(geometry.Right, MidpointRounding.AwayFromZero) - 1;
        var bottom = MathF.Round(geometry.Bottom, MidpointRounding.AwayFromZero) - 1;
        var enabledBorder = input.HasState(ElementState.Hover)
            ? Color.FromRgb(79, 79, 79)
            : Color.FromRgb(118, 118, 118);
        var topLeft = new SolidColorBrush(input.IsEnabled
            ? enabledBorder
            : Color.FromRgb(212, 212, 212));
        var bottomRight = new SolidColorBrush(input.IsEnabled
            ? enabledBorder
            : Color.FromRgb(208, 208, 208));
        context.FillRect(new Rect(left + 1, top, right - left - 1, 1), topLeft);
        context.FillRect(new Rect(left, top + 1, 1, bottom - top - 1), topLeft);
        context.FillRect(new Rect(left + 1, bottom, right - left - 1, 1), bottomRight);
        context.FillRect(new Rect(right, top + 1, 1, bottom - top - 1), bottomRight);
    }

    private static void PaintInputFocusBorder(IRenderContext context, Rect geometry)
    {
        var left = MathF.Round(geometry.X, MidpointRounding.AwayFromZero);
        var top = MathF.Round(geometry.Y, MidpointRounding.AwayFromZero);
        var right = MathF.Round(geometry.Right, MidpointRounding.AwayFromZero) - 1;
        var bottom = MathF.Round(geometry.Bottom, MidpointRounding.AwayFromZero) - 1;
        var brush = new SolidColorBrush(Color.FromRgb(16, 16, 16));
        context.FillRect(new Rect(left + 2, top, right - left - 3, 1), brush);
        context.FillRect(new Rect(left + 1, top + 1, right - left - 1, 1), brush);
        context.FillRect(new Rect(left, top + 2, 2, bottom - top - 3), brush);
        context.FillRect(new Rect(right - 1, top + 2, 2, bottom - top - 3), brush);
        context.FillRect(new Rect(left + 1, bottom - 1, right - left - 1, 1), brush);
        context.FillRect(new Rect(left + 2, bottom, right - left - 3, 1), brush);
    }

    private static void PaintTextAreaWidgetBorder(IRenderContext context, TextArea textArea, Rect geometry)
    {
        var left = MathF.Round(geometry.X, MidpointRounding.AwayFromZero);
        var top = MathF.Round(geometry.Y, MidpointRounding.AwayFromZero);
        var right = MathF.Round(geometry.Right, MidpointRounding.AwayFromZero) - 1;
        var bottom = MathF.Round(geometry.Bottom, MidpointRounding.AwayFromZero) - 1;
        var brush = new SolidColorBrush(textArea.IsEnabled
            ? textArea.HasState(ElementState.Hover)
                ? Color.FromRgb(79, 79, 79)
                : Color.FromRgb(118, 118, 118)
            : Color.FromRgb(212, 212, 212));
        context.FillRect(new Rect(left, top, right - left + 1, 1), brush);
        context.FillRect(new Rect(left, bottom, right - left + 1, 1), brush);
        context.FillRect(new Rect(left, top + 1, 1, bottom - top - 1), brush);
        context.FillRect(new Rect(right, top + 1, 1, bottom - top - 1), brush);
    }

    private static void PaintSelectWidgetBorder(IRenderContext context, Select select, Rect geometry)
    {
        var left = MathF.Round(geometry.X, MidpointRounding.AwayFromZero);
        var top = MathF.Round(geometry.Y, MidpointRounding.AwayFromZero);
        var right = MathF.Round(geometry.Right, MidpointRounding.AwayFromZero) - 1;
        var bottom = MathF.Round(geometry.Bottom, MidpointRounding.AwayFromZero) - 1;
        var brush = new SolidColorBrush(select.HasState(ElementState.Hover)
            ? Color.FromRgb(79, 79, 79)
            : Color.FromRgb(118, 118, 118));
        context.FillRect(new Rect(left, top, right - left + 1, 1), brush);
        context.FillRect(new Rect(left, bottom, right - left + 1, 1), brush);
        context.FillRect(new Rect(left, top + 1, 1, bottom - top - 1), brush);
        context.FillRect(new Rect(right, top + 1, 1, bottom - top - 1), brush);
    }

    private static void PaintCollapsedBorderFragments(
        IRenderContext context,
        IReadOnlyList<TableBorderFragment> fragments)
    {
        foreach (var fragment in fragments)
            if (!fragment.Bounds.IsEmpty && fragment.Color.A > 0)
                context.FillRect(fragment.Bounds, new SolidColorBrush(fragment.Color));
    }

    internal static void PaintBorder(IRenderContext context, Element element) =>
        PaintBorder(context, element, element.Style.GetAll());

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
        var offset = TryParseSignedLength(element.Style.Get("outline-offset"), out var parsedOffset)
            ? parsedOffset
            : 0f;
        var outer = box.Inflate(offset + width, offset + width);
        var inner = box.Inflate(offset, offset);
        var brush = new SolidColorBrush(outline.Color);
        if (TryGetRoundedGeometry(element, box, out var rounded))
        {
            var expansion = offset + width / 2f;
            var strokeBox = box.Inflate(expansion, expansion);
            if (strokeBox.Width <= 0 || strokeBox.Height <= 0) return;
            var outlineGeometry = new RoundedRectGeometry(
                strokeBox,
                ExpandCorner(rounded.TopLeft, expansion),
                ExpandCorner(rounded.TopRight, expansion),
                ExpandCorner(rounded.BottomRight, expansion),
                ExpandCorner(rounded.BottomLeft, expansion));
            if (outlineGeometry.IsUniform)
                context.DrawGeometry(outlineGeometry, Pen.FromColor(outline.Color, width));
            else
                context.DrawPath(outlineGeometry.ToPath(), Pen.FromColor(outline.Color, width));
            return;
        }
        context.FillRect(new Rect(outer.X, outer.Y, outer.Width, width), brush);
        context.FillRect(new Rect(outer.X, outer.Bottom - width, outer.Width, width), brush);
        context.FillRect(new Rect(outer.X, inner.Y, width, inner.Height), brush);
        context.FillRect(new Rect(outer.Right - width, inner.Y, width, inner.Height), brush);
    }

    private static CornerRadius ExpandCorner(CornerRadius radius, float expansion) => new(
        Math.Max(0, radius.X + expansion),
        Math.Max(0, radius.Y + expansion));

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

    private static bool TryParseSignedLength(string? value, out float length)
    {
        length = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2];
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out length) &&
            float.IsFinite(length);
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
