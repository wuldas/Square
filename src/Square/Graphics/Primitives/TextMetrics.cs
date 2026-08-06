using System.Globalization;
using System.Text;

namespace Square.Graphics;

/// <summary>字体垂直度量（相对基线，向上为负）。</summary>
public readonly record struct FontMetrics(float Top, float Ascent, float Descent, float Bottom, float Leading)
{
    /// <summary>字体包围盒高度（<see cref="Bottom"/> - <see cref="Top"/>）。</summary>
    public float Height => Math.Max(0, Bottom - Top);
}

/// <summary>字形度量（前进宽度与墨迹包围盒）。</summary>
public readonly record struct GlyphMetrics(float AdvanceX, Rect InkBounds);

/// <summary>文本度量提供器接口，由后端实现以提供权威字体/字形 bounds。</summary>
public interface ITextMetricsProvider
{
    /// <summary>尝试获取字体度量。</summary>
    /// <returns>成功返回 true。</returns>
    bool TryGetFontMetrics(Font font, out FontMetrics metrics);
    /// <summary>尝试获取字形度量。</summary>
    /// <returns>成功返回 true。</returns>
    bool TryGetGlyphMetrics(Font font, Rune rune, out GlyphMetrics metrics);
}

/// <summary>文本度量入口，统一布局、选择区和 dirty bounds 的字体基准。</summary>
public static class TextMetrics
{
    private static ITextMetricsProvider? _provider;

    /// <summary>注册度量提供器。</summary>
    public static void RegisterProvider(ITextMetricsProvider provider)
        => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <summary>获取字体度量；无提供器时返回估算值。</summary>
    public static FontMetrics GetFontMetrics(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (_provider?.TryGetFontMetrics(font, out var metrics) == true && IsValid(metrics))
            return metrics;

        var height = Math.Max(1, font.Size * TextLayout.DefaultLineHeight);
        var ascent = font.Size * 0.8f;
        return new FontMetrics(-ascent, -ascent, height - ascent, height - ascent, 0);
    }

    /// <summary>获取字形度量；无提供器时返回估算值。</summary>
    public static GlyphMetrics GetGlyphMetrics(Font font, Rune rune)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (IsZeroAdvanceCategory(rune)) return new GlyphMetrics(0, Rect.Empty);
        if (_provider?.TryGetGlyphMetrics(font, rune, out var metrics) == true && IsValid(metrics))
            return metrics;

        var advance = TextLayout.MeasureRuneAdvanceFallback(rune, font);
        var fontMetrics = GetFontMetrics(font);
        return new GlyphMetrics(advance, new Rect(0, fontMetrics.Top, advance, fontMetrics.Height));
    }

    /// <summary>按字号和行高倍数计算行高。</summary>
    public static float GetLineHeight(Font font, float lineHeightMultiplier)
        => Math.Max(1, font.Size * lineHeightMultiplier);

    /// <summary>计算行盒顶部到基线的偏移。</summary>
    public static float GetBaselineOffset(Font font, float lineHeight)
    {
        var metrics = GetFontMetrics(font);
        var ascent = Math.Max(0, -metrics.Ascent);
        var descent = Math.Max(0, metrics.Descent);
        return (lineHeight - ascent - descent) / 2f + ascent;
    }

    /// <summary>计算字形在行盒内的墨迹包围盒（已平移到基线位置）。</summary>
    public static Rect GetGlyphBoundsInLine(Font font, Rune rune, float lineHeight)
    {
        var glyph = GetGlyphMetrics(font, rune);
        if (glyph.InkBounds.IsEmpty) return Rect.Empty;
        return glyph.InkBounds.Offset(0, GetBaselineOffset(font, lineHeight));
    }

    /// <summary>测量整段文本的墨迹包围盒（含换行和字形溢出）。</summary>
    public static Rect MeasureInkBounds(TextLayout layout, Point origin)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (string.IsNullOrEmpty(layout.Text)) return Rect.Empty;

        var lineHeight = GetLineHeight(layout.Font, layout.LineHeight);
        var lines = TextWrapping.Wrap(layout.Text, layout.MaxSize.Width,
            (_, rune) => GetGlyphMetrics(layout.Font, rune).AdvanceX, layout.WrappingOptions);
        var result = Rect.Empty;
        var hasBounds = false;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var x = layout.GetLineOriginX(origin.X, lineIndex, line.Width);
            var lineTop = origin.Y + lineIndex * lineHeight;
            foreach (var visualRune in layout.EnumerateVisualRunes(line))
            {
                var glyph = GetGlyphMetrics(layout.Font, visualRune.Glyph);
                var ink = GetGlyphBoundsInLine(layout.Font, visualRune.Glyph, lineHeight).Offset(x, lineTop);
                if (!ink.IsEmpty)
                {
                    result = hasBounds ? Rect.Union(result, ink) : ink;
                    hasBounds = true;
                }
                x += visualRune.Advance;
            }
        }

        var layoutBounds = new Rect(origin, layout.Measure());
        foreach (var decoration in layout.GetDecorationRects(origin))
            result = hasBounds ? Rect.Union(result, decoration) : decoration;
        return hasBounds ? Rect.Union(layoutBounds, result) : layoutBounds;
    }

    internal static bool IsZeroAdvanceCategory(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format;
    }

    private static bool IsValid(FontMetrics metrics)
        => float.IsFinite(metrics.Top) && float.IsFinite(metrics.Ascent) &&
           float.IsFinite(metrics.Descent) && float.IsFinite(metrics.Bottom) &&
           float.IsFinite(metrics.Leading) && metrics.Bottom >= metrics.Top;

    private static bool IsValid(GlyphMetrics metrics)
        => float.IsFinite(metrics.AdvanceX) && metrics.AdvanceX >= 0 &&
           float.IsFinite(metrics.InkBounds.X) && float.IsFinite(metrics.InkBounds.Y) &&
           float.IsFinite(metrics.InkBounds.Width) && float.IsFinite(metrics.InkBounds.Height);
}
