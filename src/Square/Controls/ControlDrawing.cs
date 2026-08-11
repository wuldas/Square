using System.Globalization;
using System.Text;
using Square.Graphics;
using Square.UI;
using FontManager = global::Square.Text.FontManager;

namespace Square.Controls;

internal sealed class DomTextContent
{
    private readonly global::Square.UI.Text _node = new();

    public DomTextContent(Element owner)
    {
        owner.ChildNodes.Add(_node);
    }

    public string Text
    {
        get => _node.Data;
        set => _node.Data = value ?? "";
    }
}

internal static class ControlDrawing
{
    /// <summary>从元素 CSS 字体相关属性解析 <see cref="Font"/>（font-family/size/weight/style）。</summary>
    internal static Font ResolveFont(Element element, float defaultSize)
    {
        // 优先 CSS；控件属性 FontSize 作为缺省字号
        var family = element.Style.Get("font-family") ?? "";
        if (string.IsNullOrEmpty(family)) family = "sans-serif";

        var sizeCss = element.Style.Get("font-size") ?? "";
        var weightCss = element.Style.Get("font-weight") ?? "";
        var styleCss = element.Style.Get("font-style") ?? "";

        return FontManager.Instance.FromCss(
            family,
            string.IsNullOrEmpty(sizeCss) ? null : sizeCss,
            string.IsNullOrEmpty(weightCss) ? null : weightCss,
            string.IsNullOrEmpty(styleCss) ? null : styleCss,
            defaultSize);
    }

    internal static Size MeasureText(Element element, string text, float defaultSize, Size? maxSize = null)
    {
        var font = ResolveFont(element, defaultSize);
        var whiteSpace = ResolveWhiteSpace(element);
        var layout = new TextLayout(text, font)
        {
            MaxSize = maxSize ?? new Size(float.MaxValue, float.MaxValue),
            Alignment = ResolveTextAlignment(element),
            Direction = ResolveTextDirection(element),
            UnicodeBidi = ResolveUnicodeBidi(element),
            WhiteSpace = whiteSpace,
            LetterSpacing = ResolveTextLength(element, "letter-spacing", font.Size),
            WordSpacing = ResolveTextLength(element, "word-spacing", font.Size),
            TextTransform = ResolveTextTransform(element),
            TextIndent = ResolveTextLength(element, "text-indent", font.Size),
            TextDecorationLines = ResolveTextDecorationLines(element)
        };
        var lineHeight = GetStyledLineHeight(element, font.Size);
        layout.LineHeight = lineHeight / font.Size;
        return layout.Measure();
    }

    internal static Rect GetTextBounds(Element element, string text, float defaultSize, Point origin)
    {
        if (string.IsNullOrEmpty(text)) return Rect.Empty;
        var size = MeasureText(element, text, defaultSize);
        return new Rect(origin.X, origin.Y, size.Width, size.Height);
    }

    internal static float MeasureRenderedTextWidth(string text, Font font, float letterSpacing = 0, float wordSpacing = 0)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var lineWidth = 0f;
        var maxWidth = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n')
            {
                maxWidth = Math.Max(maxWidth, lineWidth);
                lineWidth = 0;
                continue;
            }
            lineWidth += MeasureRenderedRuneAdvance(rune, font) + letterSpacing +
                (Rune.IsWhiteSpace(rune) ? wordSpacing : 0);
        }
        return Math.Max(maxWidth, lineWidth);
    }

    internal static float MeasureRenderedRuneAdvance(Rune rune, Font font)
        => TextMetrics.GetGlyphMetrics(font, rune).AdvanceX;

    internal static (float Left, float Right) MeasureRenderedRuneInkBounds(Rune rune, Font font)
    {
        var glyph = TextMetrics.GetGlyphMetrics(font, rune);
        return (
            Math.Min(0, glyph.InkBounds.Left),
            Math.Max(glyph.AdvanceX, glyph.InkBounds.Right));
    }

    internal static void DrawText(
        IRenderContext context, Element element, string text, Point position, Color defaultColor, float defaultSize,
        float? lineHeight = null, bool useStyledColor = true, Size? maxSize = null,
        BidiDirection? direction = null, BidiTextMode? unicodeBidi = null,
        TextWrappingOptions? wrappingOptions = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        var font = ResolveFont(element, defaultSize);
        var color = useStyledColor ? GetStyledColor(element, "color", defaultColor) : defaultColor;
        var whiteSpace = ResolveWhiteSpace(element);
        var layout = new TextLayout(text, font)
        {
            MaxSize = maxSize ?? new Size(float.MaxValue, float.MaxValue),
            Alignment = ResolveTextAlignment(element),
            Direction = direction ?? ResolveTextDirection(element),
            UnicodeBidi = unicodeBidi ?? ResolveUnicodeBidi(element),
            WhiteSpace = wrappingOptions?.WhiteSpace ?? whiteSpace,
            LetterSpacing = wrappingOptions?.LetterSpacing ?? ResolveTextLength(element, "letter-spacing", font.Size),
            WordSpacing = wrappingOptions?.WordSpacing ?? ResolveTextLength(element, "word-spacing", font.Size),
            TextTransform = wrappingOptions?.TextTransform ?? ResolveTextTransform(element),
            TextIndent = wrappingOptions?.TextIndent ?? ResolveTextLength(element, "text-indent", font.Size),
            CollapseNewlines = wrappingOptions?.CollapseNewlines ?? false,
            TextDecorationLines = wrappingOptions?.TextDecorationLines ?? ResolveTextDecorationLines(element)
        };
        if (font.Size > 0)
            layout.LineHeight = (lineHeight ?? GetStyledLineHeight(element, font.Size)) / font.Size;
        context.DrawText(layout, position, new SolidColorBrush(color));
    }

    private static TextAlignment ResolveTextAlignment(Element element)
        => (element.Style.Get("text-align") ?? "").Trim().ToLowerInvariant() switch
        {
            "center" => TextAlignment.Center,
            "right" => TextAlignment.Right,
            "justify" => TextAlignment.Justify,
            _ => TextAlignment.Left
        };

    internal static BidiDirection ResolveTextDirection(Element element) =>
        string.Equals(element.Style.Get("direction")?.Trim(), "rtl", StringComparison.OrdinalIgnoreCase)
            ? BidiDirection.Rtl
            : BidiDirection.Ltr;

    internal static BidiTextMode ResolveUnicodeBidi(Element element) =>
        (element.Style.Get("unicode-bidi") ?? "").Trim().ToLowerInvariant() switch
        {
            "embed" => BidiTextMode.Embed,
            "bidi-override" => BidiTextMode.BidiOverride,
            _ => BidiTextMode.Normal
        };

    internal static TextWhiteSpaceMode ResolveWhiteSpace(Element element) =>
        (element.Style.Get("white-space") ?? "normal").Trim().ToLowerInvariant() switch
        {
            "pre" => TextWhiteSpaceMode.Pre,
            "nowrap" => TextWhiteSpaceMode.Nowrap,
            "pre-wrap" => TextWhiteSpaceMode.PreWrap,
            "pre-line" => TextWhiteSpaceMode.PreLine,
            _ => TextWhiteSpaceMode.Normal
        };

    internal static TextTransformMode ResolveTextTransform(Element element) =>
        (element.Style.Get("text-transform") ?? "none").Trim().ToLowerInvariant() switch
        {
            "capitalize" => TextTransformMode.Capitalize,
            "uppercase" => TextTransformMode.Uppercase,
            "lowercase" => TextTransformMode.Lowercase,
            _ => TextTransformMode.None
        };

    internal static TextDecorationLine ResolveTextDecorationLines(Element element)
    {
        var value = (element.Style.Get("text-decoration-line") ?? element.Style.Get("text-decoration") ?? "none")
            .Trim().ToLowerInvariant();
        if (value == "none") return TextDecorationLine.None;

        var result = TextDecorationLine.None;
        foreach (var token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            result |= token switch
            {
                "underline" => TextDecorationLine.Underline,
                "overline" => TextDecorationLine.Overline,
                "line-through" => TextDecorationLine.LineThrough,
                _ => TextDecorationLine.None
            };
        return result;
    }

    internal static float ResolveTextLength(Element element, string property, float fontSize)
    {
        var value = (element.Style.Get(property) ?? "normal").Trim();
        if (value.Length == 0 || value.Equals("normal", StringComparison.OrdinalIgnoreCase)) return 0;
        if (value.EndsWith("em", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var em))
            return em * fontSize;
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) value = value[..^2].Trim();
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    internal static void DrawInputFrame(IRenderContext context, UIElement element)
    {
        var background = element.IsEnabled
            ? GetStyledColor(element, "background", Color.White)
            : Color.FromRgb(240, 240, 240);
        var border = GetStyledColor(
            element,
            "border-color",
            element.IsFocused ? Color.FromRgb(0, 95, 184) : Color.FromRgb(165, 170, 176));
        var borderWidth = GetStyledFloat(element, "border-width", element.IsFocused ? 2 : 1);
        DrawStyledBackground(context, element, background);
        DrawStyledBorder(context, element, border, borderWidth);
    }

    internal static void DrawStyledBackground(IRenderContext context, UIElement element, Color background)
    {
        if (background.A == 0) return;
        var radius = GetStyledRadius(element, element.Geometry);
        if (radius <= 0)
        {
            context.FillRect(element.Geometry, new SolidColorBrush(background));
            return;
        }

        context.FillGeometry(new RoundedRectGeometry(element.Geometry, radius, radius), new SolidColorBrush(background));
    }

    internal static void DrawStyledBorder(IRenderContext context, UIElement element, Color color, float width)
    {
        if (width <= 0 || color.A == 0) return;
        var radius = GetStyledRadius(element, element.Geometry);
        if (radius <= 0)
        {
            var geometry = element.Geometry;
            var horizontalWidth = Math.Min(width, geometry.Width);
            var verticalWidth = Math.Min(width, geometry.Height);
            var brush = new SolidColorBrush(color);
            context.FillRect(new Rect(geometry.X, geometry.Y, geometry.Width, verticalWidth), brush);
            context.FillRect(new Rect(geometry.X, geometry.Bottom - verticalWidth, geometry.Width, verticalWidth), brush);
            context.FillRect(new Rect(
                geometry.X,
                geometry.Y + verticalWidth,
                horizontalWidth,
                Math.Max(0, geometry.Height - verticalWidth * 2)), brush);
            context.FillRect(new Rect(
                geometry.Right - horizontalWidth,
                geometry.Y + verticalWidth,
                horizontalWidth,
                Math.Max(0, geometry.Height - verticalWidth * 2)), brush);
            return;
        }

        context.DrawGeometry(new RoundedRectGeometry(element.Geometry, radius, radius), Pen.FromColor(color, width));
    }

    internal static float GetStyledFloat(Element element, string name, float fallback)
    {
        var raw = element.Style.Get(name) ?? "";
        if (string.IsNullOrEmpty(raw)) return fallback;
        raw = raw.Replace("px", "", StringComparison.OrdinalIgnoreCase).Trim();
        return float.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    internal static float GetStyledRadius(Element element, Rect geometry)
    {
        var raw = element.Style.Get("border-radius") ?? "";
        if (string.IsNullOrWhiteSpace(raw)) return 0;

        var token = raw.Trim().Split([' ', '/'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token)) return 0;

        var max = MathF.Max(0, MathF.Min(geometry.Width, geometry.Height) / 2f);
        float value;
        if (token.EndsWith("%", StringComparison.Ordinal))
        {
            var percent = token[..^1].Trim();
            if (!float.TryParse(percent, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
                return 0;
            value = MathF.Min(geometry.Width, geometry.Height) * value / 100f;
        }
        else
        {
            token = token.Replace("px", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (!float.TryParse(token, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
                return 0;
        }

        return Math.Clamp(value, 0, max);
    }

    internal static float GetStyledLineHeight(Element element, float fontSize)
    {
        var value = (element.Style.Get("line-height") ?? "").Trim();
        if (string.IsNullOrEmpty(value))
            return MathF.Round(TextMetrics.GetLineHeight(ResolveFont(element, fontSize), TextLayout.DefaultLineHeight));

        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            return Math.Max(1, GetStyledFloat(element, "line-height", fontSize * TextLayout.DefaultLineHeight));

        return float.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var multiplier)
            ? Math.Max(1, fontSize * multiplier)
            : MathF.Round(TextMetrics.GetLineHeight(ResolveFont(element, fontSize), TextLayout.DefaultLineHeight));
    }

    internal static Color GetStyledColor(Element element, string name, Color fallback)
    {
        var value = element.Style.Get(name) ?? "";
        if (name == "background")
        {
            var backgroundColor = element.Style.Get("background-color") ?? "";
            if (!string.IsNullOrWhiteSpace(backgroundColor)) value = backgroundColor;
        }
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return Color.TryParse(value, out var color) ? color : fallback;
    }

    internal static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new Color(
            BlendChannel(from.R, to.R, amount),
            BlendChannel(from.G, to.G, amount),
            BlendChannel(from.B, to.B, amount),
            from.A);
    }

    private static byte BlendChannel(byte from, byte to, float amount) =>
        (byte)Math.Clamp((int)MathF.Round(from + (to - from) * amount), 0, 255);
}
