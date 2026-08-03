using System.Text;
using SkiaSharp;
using Square.Graphics;
using Square.Text.Glyph;

namespace Square.Backends.Skia;

internal sealed class SkiaTextMetricsProvider : ITextMetricsProvider
{
    private readonly Dictionary<FontKey, FontMetrics> _fontMetrics = [];
    private readonly Dictionary<GlyphKey, GlyphMetrics> _glyphMetrics = [];
    private readonly Dictionary<TypefaceKey, SKTypeface> _typefaces = [];
    private readonly FontCollection _fonts = FontCollection.Shared;
    private readonly object _sync = new();

    public bool TryGetFontMetrics(Font font, out FontMetrics metrics)
    {
        var key = FontKey.From(font);
        lock (_sync)
        {
            if (_fontMetrics.TryGetValue(key, out metrics)) return true;
            using var skFont = CreateFont(font, null);
            var skMetrics = skFont.Metrics;
            metrics = new FontMetrics(
                skMetrics.Top,
                skMetrics.Ascent,
                skMetrics.Descent,
                skMetrics.Bottom,
                skMetrics.Leading);
            _fontMetrics[key] = metrics;
            return true;
        }
    }

    public bool TryGetGlyphMetrics(Font font, Rune rune, out GlyphMetrics metrics)
    {
        var key = new GlyphKey(FontKey.From(font), rune.Value);
        lock (_sync)
        {
            if (_glyphMetrics.TryGetValue(key, out metrics)) return true;
            using var skFont = CreateFont(font, rune.Value);
            Span<int> codePoints = [rune.Value];
            Span<ushort> glyphs = stackalloc ushort[1];
            skFont.GetGlyphs(codePoints, glyphs);
            Span<float> widths = stackalloc float[1];
            Span<SKRect> bounds = stackalloc SKRect[1];
            skFont.GetGlyphWidths(glyphs, widths, bounds, null);
            var boundsValue = bounds[0];
            metrics = new GlyphMetrics(
                Math.Max(0, widths[0]),
                new Rect(
                    boundsValue.Left,
                    boundsValue.Top,
                    Math.Max(0, boundsValue.Width),
                    Math.Max(0, boundsValue.Height)));
            _glyphMetrics[key] = metrics;
            return true;
        }
    }

    internal SKFont CreateFont(Font font, int? codePoint)
    {
        var family = ResolveFamily(font.Family);
        var style = CreateStyle(font);
        var key = new TypefaceKey(family, font.Weight, font.Style, codePoint);
        SKTypeface typeface;
        lock (_sync)
        {
            if (!_typefaces.TryGetValue(key, out typeface!))
            {
                var customFace = _fonts.ResolveCustomFace(family, font.Weight, font.Style);
                if (customFace?.GetData() is { } data)
                {
                    using var skData = SKData.CreateCopy(data);
                    typeface = SKTypeface.FromData(skData, customFace.Offset);
                }
                else
                {
                    typeface = codePoint is int value
                        ? SKFontManager.Default.MatchCharacter(family, style, null, value)
                        : SKTypeface.FromFamilyName(family, style);
                }
                typeface ??= SKTypeface.Default;
                _typefaces[key] = typeface;
            }
        }

        return new SKFont(typeface, Math.Max(1, font.Size))
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.Normal,
            Subpixel = true,
            LinearMetrics = true
        };
    }

    private static string ResolveFamily(string family) => family.ToLowerInvariant() switch
    {
        "sans-serif" or "system-ui" or "ui-sans-serif" => OperatingSystem.IsWindows() ? "Segoe UI" : "DejaVu Sans",
        "serif" or "ui-serif" => OperatingSystem.IsWindows() ? "Times New Roman" : "DejaVu Serif",
        "monospace" or "ui-monospace" => OperatingSystem.IsWindows() ? "Consolas" : "DejaVu Sans Mono",
        _ => family
    };

    private static SKFontStyle CreateStyle(Font font) => new(
        (int)font.Weight,
        (int)SKFontStyleWidth.Normal,
        font.Style == FontStyle.Normal ? SKFontStyleSlant.Upright : SKFontStyleSlant.Italic);

    private readonly record struct FontKey(string Family, float Size, FontWeight Weight, FontStyle Style)
    {
        internal static FontKey From(Font font) => new(font.Family, font.Size, font.Weight, font.Style);
    }

    private readonly record struct GlyphKey(FontKey Font, int CodePoint);
    private readonly record struct TypefaceKey(string Family, FontWeight Weight, FontStyle Style, int? CodePoint);
}
