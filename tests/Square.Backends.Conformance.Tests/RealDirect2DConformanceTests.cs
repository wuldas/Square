using System.Diagnostics;
using System.Numerics;
using Square.Backends.Direct2D;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using Square.Text.Glyph;
#if PLATFORM_WIN32
using Square.Platform.Win32;
#endif
using Xunit;

namespace Square.Backends.Conformance.Tests;

public sealed class RealDirect2DConformanceTests
{
    [Fact]
    [Trait("Category", "RealDirect2D")]
    public void WindowedContextPresentsActualDirect2DPixels()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SQUARE_RUN_REAL_DIRECT2D_CONFORMANCE"),
                "1",
                StringComparison.Ordinal))
            return;

#if PLATFORM_WIN32
        var platformFactory = new Win32PlatformFactory();
        RenderBackendRegistry.Register(new Direct2DBackendFactory());
        using var host = platformFactory.CreateHost(new PlatformHostCreateInfo
        {
            Title = "Square Direct2D conformance",
            Width = 128,
            Height = 96,
            RenderBackend = "Direct2D",
            TitleStyle = TitleStyle.Hidden,
            BorderStyle = BorderStyle.None
        });
        host.Show();

        using var context = host.CreateRenderContext();
        Assert.False(context.SupportsPartialRendering);
        Assert.IsNotAssignableFrom<IRenderBitmapSource>(context);
        using var image = new Bitmap(1, 1);
        image.SetPixels([255, 0, 0, 255]);

        DrawFrame();
        context.Present();
        host.ShowAfterFirstFrame();
        ((IDpiResizableRenderContext)context).Resize(context.CanvasSize, context.DpiScale);
        image.SetPixels([255, 0, 255, 255]);
        DrawFrame();
        context.Present();

        Assert.True(platformFactory.TryCaptureByProcessId(Process.GetCurrentProcess().Id, out var captured));
        using var bitmap = Assert.IsType<Bitmap>(captured);
        var redPixels = CountPixels(bitmap, static pixel =>
            pixel[2] > 220 && pixel[1] < 50 && pixel[0] < 50);
        var bluePixels = CountPixels(bitmap, static pixel =>
            pixel[0] > 180 && pixel[1] < 100 && pixel[2] < 100);
        var greenPixels = CountPixels(bitmap, static pixel =>
            pixel[1] > 100 && pixel[0] < 120 && pixel[2] < 120);
        var blackPixels = CountPixels(bitmap, static pixel =>
            pixel[0] < 25 && pixel[1] < 25 && pixel[2] < 25);
        var magentaPixels = CountPixels(bitmap, static pixel =>
            pixel[0] > 180 && pixel[1] < 80 && pixel[2] > 180);
        Assert.True(redPixels > 200,
            "The actual Win32 window capture did not contain the Direct2D red rectangle. " +
            DescribeBitmap(bitmap));
        Assert.True(bluePixels > 100, DescribeBitmap(bitmap));
        Assert.True(greenPixels > 100, DescribeBitmap(bitmap));
        Assert.True(blackPixels > 20, DescribeBitmap(bitmap));
        Assert.True(magentaPixels > 50, DescribeBitmap(bitmap));
        AssertPixelNear(bitmap, host.DpiScale, 90, 30, 255, 0, 0, 10);
        AssertPixelNear(bitmap, host.DpiScale, 72, 24, 255, 0, 255, 10);
        AssertPixelNear(bitmap, host.DpiScale, 22, 34, 0, 0, 255, 10);
        AssertPixelNear(bitmap, host.DpiScale, 46, 24, 128, 255, 128, 12);
        AssertPixelNear(bitmap, host.DpiScale, 12, 76, 255, 128, 0, 12);
        AssertPixelNear(bitmap, host.DpiScale, 8, 76, 255, 255, 255, 8);
        Assert.True(CountDarkPixels(bitmap, host.DpiScale, new Rect(66, 36, 20, 18)) > 5,
            "Direct2D text coverage did not produce ink in the expected text region.");
        Assert.True(CountDarkPixels(bitmap, host.DpiScale, new Rect(96, 50, 20, 20)) > 2,
            "Direct2D supplementary-rune fallback did not produce visible ink.");
        AssertGlyphBottomRow(bitmap, host.DpiScale);

        void DrawFrame()
        {
            context.Clear(Color.White);
            context.PushTransform(Matrix3x2.CreateTranslation(2, 2));
            context.PushClip(new Rect(0, 0, context.CanvasSize.Width - 4, context.CanvasSize.Height - 4));
            context.PushClip(new RoundedRectGeometry(
                new Rect(0, 0, context.CanvasSize.Width - 4, context.CanvasSize.Height - 4),
                4,
                4));
            Assert.Throws<InvalidOperationException>(() => context.PopLayer());
            Assert.Throws<InvalidOperationException>(() => context.Clear(Color.Black));
            context.Flush();
            context.FillRect(
                new Rect(0, 0, context.CanvasSize.Width - 4, 12),
                new LinearGradientBrush(
                    new Point(0, 0),
                    new Point(context.CanvasSize.Width - 4, 0),
                    new GradientStop(0, Color.Blue),
                    new GradientStop(1, Color.Green)));
            context.FillGeometry(
                new EllipseGeometry(new Point(20, 32), 10, 8),
                Brush.FromColor(Color.Blue));
            context.DrawPath(
                PathGeometry.Create()
                    .MoveTo(new Point(4, 55))
                    .ArcTo(new Rect(4, 45, 28, 20), 180, 180),
                new Pen(Brush.FromColor(Color.Black), 2, new StrokeStyle
                {
                    Cap = LineCap.Round,
                    Join = LineJoin.Round,
                    DashArray = [3, 2]
                }));
            context.PushLayer(new Rect(34, 18, 26, 20), 0.5f);
            Assert.Throws<InvalidOperationException>(() => context.PopClip());
            context.FillRect(new Rect(34, 18, 20, 20), Brush.FromColor(Color.Green));
            context.FillRect(new Rect(40, 18, 20, 20), Brush.FromColor(Color.Green));
            context.Flush();
            context.PopLayer();
            context.DrawImage(image, new Rect(66, 18, 12, 12));
            context.DrawText(
                new TextLayout("D2D", new Font("Segoe UI", 12)),
                new Point(66, 36),
                Brush.FromColor(Color.Black));
            context.DrawText(
                new TextLayout("😀", new Font("Segoe UI", 16)),
                new Point(96, 50),
                Brush.FromColor(Color.Black));
            context.FillRect(
                new Rect(84, 22, 28, 20),
                Brush.FromColor(Color.Red));
            context.PopClip();
            context.PopClip();
            context.PopTransform();
            context.PushTransform(Matrix3x2.CreateTranslation(10, 66));
            context.PushClip(new RectGeometry(new Rect(0, 0, 20, 20)));
            context.FillRect(new Rect(-10, -10, 40, 40), Brush.FromColor(Color.FromRgb(255, 128, 0)));
            context.PopClip();
            context.PopTransform();
            context.DrawText(
                new TextLayout("gypq", new Font("Segoe UI", 24)),
                new Point(34, 55),
                Brush.FromColor(Color.Black));
        }
#else
        throw new PlatformNotSupportedException("Real Direct2D conformance requires Win32.");
#endif
    }

    private static int CountPixels(Bitmap bitmap, Func<ReadOnlySpan<byte>, bool> predicate)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (predicate(bitmap.GetPixel(x, y))) count++;
        return count;
    }

    private static string DescribeBitmap(Bitmap bitmap)
    {
        var white = CountPixels(bitmap, static pixel => pixel[0] > 240 && pixel[1] > 240 && pixel[2] > 240);
        var black = CountPixels(bitmap, static pixel => pixel[0] < 15 && pixel[1] < 15 && pixel[2] < 15);
        var blue = CountPixels(bitmap, static pixel => pixel[0] > 220 && pixel[1] < 50 && pixel[2] < 50);
        var green = CountPixels(bitmap, static pixel => pixel[1] > 100 && pixel[0] < 100 && pixel[2] < 100);
        return $"Capture={bitmap.Width}x{bitmap.Height}, white={white}, black={black}, blue={blue}, green={green}.";
    }

    private static void AssertPixelNear(
        Bitmap bitmap,
        float dpiScale,
        float logicalX,
        float logicalY,
        byte red,
        byte green,
        byte blue,
        byte tolerance)
    {
        var x = Math.Clamp((int)MathF.Round(logicalX * dpiScale), 0, bitmap.Width - 1);
        var y = Math.Clamp((int)MathF.Round(logicalY * dpiScale), 0, bitmap.Height - 1);
        var pixel = bitmap.GetPixel(x, y);
        Assert.InRange(pixel[2], (byte)Math.Max(0, red - tolerance), (byte)Math.Min(255, red + tolerance));
        Assert.InRange(pixel[1], (byte)Math.Max(0, green - tolerance), (byte)Math.Min(255, green + tolerance));
        Assert.InRange(pixel[0], (byte)Math.Max(0, blue - tolerance), (byte)Math.Min(255, blue + tolerance));
    }

    private static int CountDarkPixels(Bitmap bitmap, float dpiScale, Rect logicalRect)
    {
        var left = Math.Clamp((int)MathF.Floor(logicalRect.Left * dpiScale), 0, bitmap.Width);
        var top = Math.Clamp((int)MathF.Floor(logicalRect.Top * dpiScale), 0, bitmap.Height);
        var right = Math.Clamp((int)MathF.Ceiling(logicalRect.Right * dpiScale), left, bitmap.Width);
        var bottom = Math.Clamp((int)MathF.Ceiling(logicalRect.Bottom * dpiScale), top, bitmap.Height);
        var count = 0;
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel[0] < 80 && pixel[1] < 80 && pixel[2] < 80) count++;
        }
        return count;
    }

    private static void AssertGlyphBottomRow(Bitmap bitmap, float dpiScale)
    {
        var font = new Font("Segoe UI", 24);
        var glyph = Assert.IsType<RasterizedGlyph>(
            new SystemGlyphRasterizer().Rasterize(font.WithSize(font.Size * dpiScale), 'g'));
        var baseline = 55 + TextMetrics.GetBaselineOffset(
            font,
            TextMetrics.GetLineHeight(font, TextLayout.DefaultLineHeight));
        var bottomRow = (int)MathF.Round(baseline * dpiScale) + glyph.OffsetY + LastCoverageRow(glyph);
        var left = (int)MathF.Round(34 * dpiScale) + glyph.OffsetX;

        Assert.True(HasDarkPixel(bitmap, left, left + glyph.Width, bottomRow),
            $"Direct2D dropped the glyph's final coverage row at physical y={bottomRow}.");
        Assert.False(HasDarkPixel(bitmap, left, left + glyph.Width, bottomRow + 1),
            "Direct2D glyph ink extended below the rasterized coverage bitmap.");
    }

    private static int LastCoverageRow(RasterizedGlyph glyph)
    {
        for (var y = glyph.Height - 1; y >= 0; y--)
        for (var x = 0; x < glyph.Width; x++)
            if (glyph.Coverage[y * glyph.Stride + x] > 0) return y;
        return -1;
    }

    private static bool HasDarkPixel(Bitmap bitmap, int left, int right, int y)
    {
        if ((uint)y >= (uint)bitmap.Height) return false;
        for (var x = Math.Max(0, left); x < Math.Min(bitmap.Width, right); x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel[0] < 180 && pixel[1] < 180 && pixel[2] < 180) return true;
        }
        return false;
    }
}
