using System.Runtime.CompilerServices;
using Android.Graphics;
using Square.Graphics;
using Square.Text.Glyph;
using AndroidBitmap = Android.Graphics.Bitmap;
using AndroidColor = Android.Graphics.Color;
using AndroidFont = Android.Graphics.Typeface;
using SquareBitmap = Square.Text.Glyph.RasterizedGlyph;
using SquareFont = Square.Graphics.Font;
using SquareFontStyle = Square.Graphics.FontStyle;
using SquareFontWeight = Square.Graphics.FontWeight;

namespace Square.Platform.Android;

/// <summary>使用 Android Typeface/Paint 提供系统字体回退和字形覆盖率。</summary>
internal static class AndroidGlyphRasterizer
{
    public static SquareBitmap? Rasterize(SquareFont font, char character)
    {
        if (character is '\0' or '\r' or '\n') return null;
        using var paint = CreatePaint(font);
        var value = character.ToString();
        var bounds = new global::Android.Graphics.Rect();
        paint.GetTextBounds(value, 0, value.Length, bounds);
        var width = Math.Max(1, bounds.Width());
        var height = Math.Max(1, bounds.Height());
        using var bitmap = AndroidBitmap.CreateBitmap(width, height, AndroidBitmap.Config.Argb8888!);
        bitmap.EraseColor(AndroidColor.Transparent);
        using (var canvas = new Canvas(bitmap))
            canvas.DrawText(value, -bounds.Left, -bounds.Top, paint);

        var pixels = new int[checked(width * height)];
        bitmap.GetPixels(pixels, 0, width, 0, 0, width, height);
        var coverage = new byte[checked(((width + 3) & ~3) * height)];
        var stride = (width + 3) & ~3;
        for (var y = 0; y < height; y++)
        {
            var sourceOffset = y * width;
            var targetOffset = y * stride;
            for (var x = 0; x < width; x++)
                coverage[targetOffset + x] = (byte)(pixels[sourceOffset + x] >>> 24);
        }

        return new SquareBitmap
        {
            Width = width,
            Height = height,
            Stride = stride,
            OffsetX = bounds.Left,
            OffsetY = bounds.Top,
            AdvanceX = paint.MeasureText(value),
            Coverage = coverage
        };
    }

    public static FontMetrics? GetMetrics(SquareFont font)
    {
        using var paint = CreatePaint(font);
        var metrics = paint.GetFontMetrics()!;
        return new FontMetrics(metrics.Top, metrics.Ascent, metrics.Descent, metrics.Bottom, metrics.Leading);
    }

    private static Paint CreatePaint(SquareFont font)
    {
        var style = TypefaceStyle.Normal;
        if (font.Weight >= SquareFontWeight.Bold) style |= TypefaceStyle.Bold;
        if (font.Style is SquareFontStyle.Italic or SquareFontStyle.Oblique) style |= TypefaceStyle.Italic;
        var family = AndroidFontPolicy.ResolveGenericFamily(font.Family);
        var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.SubpixelText);
        paint.SetTypeface(AndroidFont.Create(family, style));
        paint.TextSize = Math.Max(1f, font.Size);
        paint.Color = AndroidColor.White;
        return paint;
    }

}

internal static class AndroidGlyphRasterizerRegistration
{
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        SystemGlyphRasterizer.RegisterPlatformRasterizer(AndroidGlyphRasterizer.Rasterize);
        SystemGlyphRasterizer.RegisterPlatformFontMetrics(AndroidGlyphRasterizer.GetMetrics);
    }
#pragma warning restore CA2255
}
