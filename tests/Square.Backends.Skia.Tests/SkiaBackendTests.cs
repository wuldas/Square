using System.Numerics;
using System.Text;
using SkiaSharp;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Controls;
using Square.Graphics;
using Square.Rendering;
using Square.Text.Fonts;
using Square.UI;
using Xunit;

namespace Square.Backends.Skia.Tests;

public sealed class SkiaBackendTests
{
    [Fact]
    public void ExtensionRegistersAndSelectsBackend()
    {
        var application = new TestApplication();

        var result = application.UseSkiaBackend();

        Assert.Same(application, result);
        Assert.Equal("Skia", application.RenderBackend);
        Assert.IsType<SkiaBackendFactory>(RenderBackendRegistry.Get("skia"));
    }

    [Fact]
    public void RegisteredMetricsMatchSkiaFontBounds()
    {
        SkiaRegistration.Register();
        var family = OperatingSystem.IsWindows() ? "Segoe UI" : "DejaVu Sans";
        var font = new Font(family, 20, FontWeight.SemiBold, FontStyle.Italic);
        using var typeface = SKTypeface.FromFamilyName(
            family,
            new SKFontStyle((int)font.Weight, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Italic));
        using var skFont = new SKFont(typeface, font.Size)
        {
            LinearMetrics = true,
            Subpixel = true
        };

        var actual = TextMetrics.GetFontMetrics(font);
        var expected = skFont.Metrics;

        Assert.Equal(expected.Top, actual.Top, 3);
        Assert.Equal(expected.Ascent, actual.Ascent, 3);
        Assert.Equal(expected.Descent, actual.Descent, 3);
        Assert.Equal(expected.Bottom, actual.Bottom, 3);
        Assert.Equal(expected.Leading, actual.Leading, 3);
    }

    [Fact]
    public void RegisteredGlyphMetricsMatchSkiaBounds()
    {
        SkiaRegistration.Register();
        var family = OperatingSystem.IsWindows() ? "Segoe UI" : "DejaVu Sans";
        var font = new Font(family, 20);
        using var typeface = SKTypeface.FromFamilyName(family);
        using var skFont = new SKFont(typeface, font.Size)
        {
            LinearMetrics = true,
            Subpixel = true
        };
        Span<int> codePoints = ['g'];
        Span<ushort> glyphs = stackalloc ushort[1];
        skFont.GetGlyphs(codePoints, glyphs);
        Span<float> widths = stackalloc float[1];
        Span<SKRect> bounds = stackalloc SKRect[1];
        skFont.GetGlyphWidths(glyphs, widths, bounds, null);

        var actual = TextMetrics.GetGlyphMetrics(font, new Rune('g'));

        Assert.Equal(widths[0], actual.AdvanceX, 3);
        Assert.Equal(bounds[0].Left, actual.InkBounds.Left, 3);
        Assert.Equal(bounds[0].Top, actual.InkBounds.Top, 3);
        Assert.Equal(bounds[0].Right, actual.InkBounds.Right, 3);
        Assert.Equal(bounds[0].Bottom, actual.InkBounds.Bottom, 3);
    }

    [Fact]
    public async Task RegisteredCustomFontBytesProvideSkiaMetrics()
    {
        var path = FindSystemFontPath();
        if (path == null) return;
        await new FontFace("SquareSkiaCustom", path).LoadAsync();
        using var context = new SkiaBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(8, 8)
        });

        var metrics = TextMetrics.GetGlyphMetrics(new Font("SquareSkiaCustom", 16), new Rune('A'));

        Assert.True(metrics.AdvanceX > 0);
        Assert.False(metrics.InkBounds.IsEmpty);
    }

    [Fact]
    public void DisplayTreeSelectionUsesSkiaGlyphBounds()
    {
        SkiaRegistration.Register();
        var root = new View { Geometry = new Rect(0, 0, 100, 40) };
        var text = new Square.Controls.Text("g")
        {
            FontSize = 20,
            Geometry = new Rect(4, 5, 40, 24)
        };
        text.Style.Set("font-family", OperatingSystem.IsWindows() ? "Segoe UI" : "DejaVu Sans");
        root.Children.Add(text);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        var fragment = Assert.Single(tree.CollectTextFragments(root));
        var character = Assert.Single(fragment.Characters);
        var lineHeight = TextMetrics.GetLineHeight(fragment.Font, TextLayout.DefaultLineHeight);
        var glyph = TextMetrics.GetGlyphBoundsInLine(fragment.Font, new Rune('g'), lineHeight);

        Assert.Equal(Math.Min(character.Bounds.Y, character.Bounds.Y + glyph.Top), character.SelectionBounds.Top, 3);
        Assert.Equal(Math.Max(character.Bounds.Bottom, character.Bounds.Y + glyph.Bottom), character.SelectionBounds.Bottom, 3);
        Assert.Equal(TextMetrics.GetGlyphMetrics(fragment.Font, new Rune('g')).AdvanceX, character.Bounds.Width, 3);
    }

    [Fact]
    public void FactoryCreatesCapturableContext()
    {
        var factory = new SkiaBackendFactory();
        using var context = factory.CreateContext(new RenderContextCreateInfo { CanvasSize = new Size(8, 6) });

        Assert.Equal("Skia", factory.Name);
        Assert.Equal(new Size(8, 6), context.CanvasSize);
        Assert.True(context.SupportsPartialRendering);
        Assert.IsAssignableFrom<IRenderBitmapSource>(context);
    }

    [Fact]
    public void RendersSolidGeometryAndClip()
    {
        using var context = CreateContext(12, 12);
        context.Clear(Color.White);
        context.PushClip(new Rect(2, 2, 8, 8));
        context.FillGeometry(new EllipseGeometry(new Point(6, 6), 5, 5), Brush.FromColor(Color.Red));
        context.PopClip();

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        AssertPixel(bitmap, 6, 6, 255, 0, 0, 255);
        AssertPixel(bitmap, 0, 0, 255, 255, 255, 255);
    }

    [Fact]
    public void AppliesTransformsAndLayerOpacity()
    {
        using var context = CreateContext(12, 8);
        context.Clear(Color.Transparent);
        context.PushTransform(Matrix3x2.CreateTranslation(4, 1));
        context.PushLayer(new Rect(0, 0, 4, 4), 0.5f);
        context.FillRect(new Rect(0, 0, 4, 4), Brush.FromColor(Color.Blue));
        context.PopLayer();
        context.PopTransform();

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var pixel = bitmap.GetPixel(5, 2);
        Assert.InRange(pixel[0], (byte)126, (byte)129);
        Assert.InRange(pixel[3], (byte)126, (byte)129);
        AssertPixel(bitmap, 1, 2, 0, 0, 0, 0);
    }

    [Fact]
    public void LayerOpacityCompositesOverlappingPrimitivesOnce()
    {
        using var context = CreateContext(3, 1);
        context.Clear(Color.Transparent);
        context.PushLayer(new Rect(0, 0, 2, 1), 0.5f);
        context.FillRect(new Rect(0, 0, 2, 1), Brush.FromColor(Color.Red));
        context.FillRect(new Rect(1, 0, 1, 1), Brush.FromColor(Color.Red));
        context.PopLayer();

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        Assert.InRange(bitmap.GetPixel(0, 0)[3], (byte)126, (byte)129);
        Assert.InRange(bitmap.GetPixel(1, 0)[3], (byte)126, (byte)129);
        AssertPixel(bitmap, 2, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void DrawsAndRefreshesBitmapImages()
    {
        using var source = new Bitmap(1, 1);
        source.SetPixels([0, 0, 255, 255]);
        using var context = CreateContext(3, 1);

        context.DrawImage(source, new Rect(0, 0, 1, 1));
        source.SetPixels([255, 0, 0, 255]);
        context.DrawImage(source, new Rect(2, 0, 1, 1));

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        AssertPixel(bitmap, 0, 0, 255, 0, 0, 255);
        AssertPixel(bitmap, 2, 0, 0, 0, 255, 255);
    }

    [Fact]
    public void PresentSubmitsPhysicalDirtyRects()
    {
        Bitmap? presented = null;
        IReadOnlyList<Rect>? presentedDirtyRects = null;
        var factory = new SkiaBackendFactory();
        using var context = factory.CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(3, 2),
            DpiScale = 2,
            PresentFrame = (bitmap, dirtyRects) =>
            {
                presented = bitmap;
                presentedDirtyRects = dirtyRects;
            }
        });
        context.Clear(Color.Green);
        context.Present(null);

        context.Clear(Color.Red);
        context.Present([new Rect(0, 0, 1, 1)]);

        Assert.NotNull(presented);
        Assert.NotNull(presentedDirtyRects);
        Assert.Equal(new Rect(0, 0, 2, 2), Assert.Single(presentedDirtyRects));
        Assert.Equal(6, presented.Width);
        Assert.Equal(4, presented.Height);
        AssertPixel(presented, 0, 0, 255, 0, 0, 255);
        AssertPixel(presented, 5, 3, 0, 255, 0, 255);
    }

    [Fact]
    public void PartialPresentUpdatesOnlyTheRequestedPhysicalRegion()
    {
        Bitmap? presented = null;
        using var context = new SkiaBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(4, 3),
            PresentFrame = (bitmap, _) => presented = bitmap
        });

        context.Clear(Color.Green);
        context.Present(null);
        context.Clear(Color.Red);
        context.Present([new Rect(0, 0, 1, 1)]);

        Assert.NotNull(presented);
        AssertPixel(presented, 0, 0, 255, 0, 0, 255);
        AssertPixel(presented, 3, 2, 0, 255, 0, 255);
    }

    [Fact]
    public void TextRendersAtPhysicalDpiResolution()
    {
        using var context = new SkiaBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(80, 32),
            DpiScale = 2
        });
        context.Clear(Color.White);
        context.DrawText(
            new TextLayout("Skia", new Font("Segoe UI", 20)),
            new Point(2, 2),
            Brush.FromColor(Color.Black));

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var inkPixels = 0;
        var detailedBlocks = 0;
        for (var y = 0; y < bitmap.Height - 1; y += 2)
        {
            for (var x = 0; x < bitmap.Width - 1; x += 2)
            {
                var a = bitmap.GetPixel(x, y)[0];
                var b = bitmap.GetPixel(x + 1, y)[0];
                var c = bitmap.GetPixel(x, y + 1)[0];
                var d = bitmap.GetPixel(x + 1, y + 1)[0];
                if (a < 240) inkPixels++;
                if (b < 240) inkPixels++;
                if (c < 240) inkPixels++;
                if (d < 240) inkPixels++;
                var hasInk = a < 240 || b < 240 || c < 240 || d < 240;
                if (hasInk && (a != b || a != c || a != d))
                    detailedBlocks++;
            }
        }

        Assert.True(inkPixels > 200);
        Assert.True(detailedBlocks > 10);
    }

    [Fact]
    public void TextWrapsUsingLogicalAdvancesAtHighDpi()
    {
        using var context = new SkiaBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(48, 70),
            DpiScale = 2
        });
        context.Clear(Color.White);
        context.DrawText(
            new TextLayout("AAAA", new Font("Segoe UI", 20))
            {
                MaxSize = new Size(22, 70)
            },
            new Point(2, 2),
            Brush.FromColor(Color.Black));

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var secondLineHasInk = false;
        for (var y = 48; y < bitmap.Height && !secondLineHasInk; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y)[0] >= 240) continue;
                secondLineHasInk = true;
                break;
            }
        }

        Assert.True(secondLineHasInk);
    }

    [Fact]
    public void AppearanceAutoPaintsChromiumRoundedButtonCorners()
    {
        var engine = new CssEngine();
        var button = new Button("Save") { Geometry = new Rect(2, 2, 80, 28) };
        engine.ApplyStyles(button);

        using var context = CreateContext(90, 36);
        context.Clear(Color.FromRgb(255, 0, 0));
        var tree = new DisplayTree();
        tree.BuildFrom(button);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var corner = bitmap.GetPixel(2, 2);
        var interior = bitmap.GetPixel(20, 16);

        Assert.True(interior[2] > 200 && interior[1] > 200 && interior[0] > 200,
            "appearance:auto should fill the button interior from Chrome ButtonFace on Skia");
        Assert.Equal(255, corner[2]);
        Assert.Equal(0, corner[1]);
        Assert.Equal(0, corner[0]);
    }

    [Fact]
    public void AppearanceNoneKeepsAuthorBoxWithoutWidgetRadius()
    {
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "Button { appearance: none; background: #112233; border-radius: 0; border: none; }").Tokenize()).Parse());
        var button = new Button("Save") { Geometry = new Rect(2, 2, 80, 28) };
        engine.ApplyStyles(button);

        using var context = CreateContext(90, 36);
        context.Clear(Color.FromRgb(255, 0, 0));
        var tree = new DisplayTree();
        tree.BuildFrom(button);
        tree.Render(context);

        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var corner = bitmap.GetPixel(2, 2);
        Assert.Equal(0x11, corner[2]);
        Assert.Equal(0x22, corner[1]);
        Assert.Equal(0x33, corner[0]);
    }

    [Fact]
    public void DialogDescendantsPaintAutoAndAuthorChromeOnSkia()
    {
        var document = new UIDocument();
        document.Ui.Geometry = new Rect(0, 0, 240, 150);
        document.Body.Geometry = document.Ui.Geometry;
        var dialog = new Dialog { Geometry = new Rect(0, 0, 180, 100) };
        dialog.Style.Set("background", "#ff0000");
        var autoInput = new Input { Geometry = new Rect(10, 15, 70, 26) };
        var authorButton = new Button("None") { Geometry = new Rect(95, 15, 70, 26) };
        authorButton.SetProperty("class", "author-none");
        dialog.Children.Add(autoInput);
        dialog.Children.Add(authorButton);
        document.Body.Children.Add(dialog);
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer(
            ".author-none { appearance: none; background: #123456; border: 2px solid #abcdef; }").Tokenize()).Parse());
        engine.ApplyStylesToTree(document.Ui);
        dialog.Open();

        using var context = CreateContext(240, 150);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(document.Ui);
        tree.Render(context);
        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();

        var offset = new Point(
            (document.Ui.Geometry.Width - dialog.Geometry.Width) / 2f + dialog.HorizontalOffset,
            (document.Ui.Geometry.Height - dialog.Geometry.Height) / 2f + dialog.VerticalOffset);
        AssertFourEdges(bitmap, autoInput.Geometry, offset, expectedBorder: null);
        AssertFourEdges(bitmap, authorButton.Geometry, offset, Color.FromRgb(0xab, 0xcd, 0xef));
        AssertPixel(bitmap, (int)(offset.X + autoInput.Geometry.Right) - 8,
            (int)(offset.Y + autoInput.Geometry.Y) + 8, 255, 255, 255, 255);
        AssertPixel(bitmap, (int)(offset.X + authorButton.Geometry.Right) - 8,
            (int)(offset.Y + authorButton.Geometry.Y) + 8, 0x12, 0x34, 0x56, 255);
    }

    private static IRenderContext CreateContext(int width, int height)
        => new SkiaBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = new Size(width, height)
        });

    private static string? FindSystemFontPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "segoeui.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf"
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void AssertPixel(Bitmap bitmap, int x, int y, byte red, byte green, byte blue, byte alpha)
    {
        var pixel = bitmap.GetPixel(x, y);
        Assert.Equal(blue, pixel[0]);
        Assert.Equal(green, pixel[1]);
        Assert.Equal(red, pixel[2]);
        Assert.Equal(alpha, pixel[3]);
    }

    private static void AssertFourEdges(Bitmap bitmap, Rect geometry, Point offset, Color? expectedBorder)
    {
        var left = (int)(offset.X + geometry.X);
        var top = (int)(offset.Y + geometry.Y);
        var right = (int)(offset.X + geometry.Right) - 1;
        var bottom = (int)(offset.Y + geometry.Bottom) - 1;
        var middleX = (left + right) / 2;
        var middleY = (top + bottom) / 2;
        var edges = new[]
        {
            Enumerable.Range(top, 4).Select(y => (middleX, y)),
            Enumerable.Range(right - 3, 4).Select(x => (x, middleY)),
            Enumerable.Range(bottom - 3, 4).Select(y => (middleX, y)),
            Enumerable.Range(left, 4).Select(x => (x, middleY))
        };
        foreach (var edge in edges)
        {
            var found = edge.Any(point =>
            {
                var pixel = bitmap.GetPixel(point.Item1, point.Item2);
                return expectedBorder is { } color
                    ? pixel[0] == color.B && pixel[1] == color.G && pixel[2] == color.R
                    : pixel[0] < 245 && pixel[1] < 245 && pixel[2] < 245;
            });
            Assert.True(found, "Expected CSS border on every popup-translated edge.");
        }
    }

    private sealed class TestApplication : IRenderBackendApplication
    {
        public string RenderBackend { get; set; } = "Software";
    }
}
