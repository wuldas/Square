using System;
using System.Collections.Generic;
using Square.Backends;
using Square.Controls;
using Square.Extensions.RichText;
using Square.Graphics;
using Square.Graphics.Svg;
using Square.UI.Svg;
using Square.Rendering;
using Square.Text.Glyph;
using Square.UI;
using System.Numerics;
using Xunit;

namespace Square.Backends.Tests;

public class SoftwareRendererTests
{
    private static byte BlendOpaque(byte source, byte sourceAlpha, byte destination)
        => (byte)((source * sourceAlpha + destination * (255 - sourceAlpha) + 127) / 255);

    private static RenderContext CreateContext(int w, int h)
    {
        var bmp = new Bitmap(w, h);
        return new RenderContext(bmp, 1f);
    }

    [Fact]
    public void SoftwareRendererSupportsPartialRendering()
    {
        using var context = CreateContext(4, 4);
        Assert.True(context.SupportsPartialRendering);
    }

    [Fact]
    public void RendersSvgImageThroughImageControl()
    {
        using var context = CreateContext(40, 40);
        context.Clear(Color.White);
        using var svg = SvgImage.Parse("""
            <svg viewBox="0 0 20 20">
              <rect width="20" height="20" fill="#123456" />
              <circle cx="10" cy="10" r="4" fill="#ffffff" />
            </svg>
            """);
        var image = new Square.Controls.Image { ImageContent = svg, Geometry = new Rect(0, 0, 40, 40) };

        image.Paint(context);

        var corner = context.GetBitmap().GetPixel(2, 2);
        var center = context.GetBitmap().GetPixel(20, 20);
        Assert.Equal((byte)0x12, corner[2]);
        Assert.Equal((byte)0x34, corner[1]);
        Assert.Equal((byte)0x56, corner[0]);
        Assert.Equal((byte)255, center[2]);
        Assert.Equal((byte)255, center[1]);
        Assert.Equal((byte)255, center[0]);
    }

    [Fact]
    public void RendersSvgDocumentElementTree()
    {
        using var context = CreateContext(40, 40);
        context.Clear(Color.White);
        var svg = new SVGSVGElement { Geometry = new Rect(0, 0, 40, 40) };
        svg.SetProperty("ViewBox", "0 0 20 20");
        var group = new SVGGElement();
        group.SetProperty("Fill", "#123456");
        var rect = new SVGRectElement();
        rect.SetProperty("Width", 20);
        rect.SetProperty("Height", 20);
        var circle = new SVGCircleElement();
        circle.SetProperty("CenterX", 10);
        circle.SetProperty("CenterY", 10);
        circle.SetProperty("Radius", 4);
        circle.SetProperty("Fill", "#ffffff");
        group.Children.Add(rect);
        group.Children.Add(circle);
        svg.Children.Add(group);

        svg.Paint(context);

        var corner = context.GetBitmap().GetPixel(2, 2);
        var center = context.GetBitmap().GetPixel(20, 20);
        Assert.Equal((byte)0x12, corner[2]);
        Assert.Equal((byte)0x34, corner[1]);
        Assert.Equal((byte)0x56, corner[0]);
        Assert.Equal((byte)255, center[2]);
        Assert.Equal((byte)255, center[1]);
        Assert.Equal((byte)255, center[0]);
    }

    [Fact]
    public void RendersSvgDocumentPathAndPolygonGeometry()
    {
        using var context = CreateContext(80, 40);
        context.Clear(Color.White);
        var svg = new SVGSVGElement { Geometry = new Rect(0, 0, 80, 40) };
        svg.SetProperty("ViewBox", "0 0 80 40");

        var path = new SVGPathElement();
        path.SetProperty("Data", "M 5 5 L 35 5 L 20 35 Z");
        path.SetProperty("Fill", "#123456");
        svg.Children.Add(path);

        var polygon = new SVGPolygonElement();
        polygon.SetProperty("Points", "45,5 75,5 60,35");
        polygon.SetProperty("Fill", "none");
        polygon.SetProperty("Stroke", "#654321");
        polygon.SetProperty("StrokeWidth", 3);
        svg.Children.Add(polygon);

        svg.Paint(context);

        var pathInterior = context.GetBitmap().GetPixel(20, 15);
        var polygonEdge = context.GetBitmap().GetPixel(60, 34);
        Assert.Equal((byte)0x12, pathInterior[2]);
        Assert.Equal((byte)0x34, pathInterior[1]);
        Assert.Equal((byte)0x56, pathInterior[0]);
        Assert.Equal((byte)0x65, polygonEdge[2]);
        Assert.Equal((byte)0x43, polygonEdge[1]);
        Assert.Equal((byte)0x21, polygonEdge[0]);
    }

    [Fact]
    public void PresentSubmitsFrameBuffer()
    {
        Bitmap? presented = null;
        var bitmap = new Bitmap(4, 4);
        var context = new RenderContext(bitmap, 1f, frame => presented = frame);

        context.Present();

        Assert.Same(bitmap, presented);
    }

    [Fact]
    public void CaptureBitmapReturnsIndependentFrameCopy()
    {
        var context = CreateContext(4, 4);
        context.Clear(Color.Red);

        using var captured = ((IRenderBitmapSource)context).CaptureBitmap();
        context.Clear(Color.Blue);

        Assert.Equal(255, captured.Pixels[2]);
        Assert.Equal(0, captured.Pixels[0]);
        Assert.Equal(255, context.GetBitmap().Pixels[0]);
        Assert.NotSame(captured.Pixels, context.GetBitmap().Pixels);
    }

    [Fact]
    public void SoftwareSurfaceSupportsPaddedStrideCaptureAndPresent()
    {
        var surface = new TestSoftwareSurface(5, 3, 32);
        var context = new RenderContext(surface, new Size(5, 3), 1f);
        context.Clear(Color.Red);
        context.Present([new Rect(1, 1, 2, 1)]);

        using var captured = context.CaptureBitmap();
        Assert.Equal(255, captured.GetPixel(4, 2)[2]);
        Assert.Equal(0, surface.Rows[0][20]);
        Assert.Equal(new Rect(1, 1, 2, 1), Assert.Single(surface.LastDirtyRects!));
    }

    [Fact]
    public void RenderContextResizesAndDisposesOwnedSoftwareSurface()
    {
        var surface = new TestSoftwareSurface(2, 2, 16);
        var context = new RenderContext(surface, new Size(2, 2), 1f);

        context.Resize(new Size(7, 5));
        context.Dispose();

        Assert.Equal(7, surface.Width);
        Assert.Equal(5, surface.Height);
        Assert.Equal(1, surface.ResizeCount);
        Assert.Equal(1, surface.DisposeCount);
    }

    [Fact]
    public void ResizeRecreatesFrameBufferAtNewCanvasSize()
    {
        Bitmap? presented = null;
        var original = new Bitmap(10, 10);
        var context = new RenderContext(original, 1f, frame => presented = frame);

        context.Resize(new Size(320, 180));
        context.Clear(Color.Blue);
        context.Present();

        Assert.Equal(new Size(320, 180), context.CanvasSize);
        Assert.NotSame(original, context.GetBitmap());
        Assert.Same(context.GetBitmap(), presented);
        Assert.Equal(255, presented!.Pixels[0]);
    }

    [Fact]
    public void DpiScaleKeepsLogicalCanvasAndRasterizesToPhysicalPixels()
    {
        var bitmap = new Bitmap(20, 10);
        var context = new RenderContext(bitmap, new Size(10, 5), 2f);

        context.Clear(Color.Transparent);
        context.FillRect(new Rect(1, 1, 2, 2), new SolidColorBrush(Color.Red));

        Assert.Equal(new Size(10, 5), context.CanvasSize);
        Assert.Equal(2f, context.DpiScale);
        Assert.Equal(0, bitmap.Pixels[(1 * bitmap.Width + 1) * 4 + 3]);
        Assert.Equal(255, bitmap.Pixels[(2 * bitmap.Width + 2) * 4 + 3]);
        Assert.Equal(255, bitmap.Pixels[(5 * bitmap.Width + 5) * 4 + 3]);
        Assert.Equal(0, bitmap.Pixels[(6 * bitmap.Width + 6) * 4 + 3]);
    }

    [Fact]
    public void DpiResizeUpdatesScaleAndPhysicalFrameBuffer()
    {
        var context = new RenderContext(new Bitmap(10, 10), new Size(10, 10), 1f);

        context.Resize(new Size(12, 8), 1.5f);

        Assert.Equal(new Size(12, 8), context.CanvasSize);
        Assert.Equal(1.5f, context.DpiScale);
        Assert.Equal(18, context.GetBitmap().Width);
        Assert.Equal(12, context.GetBitmap().Height);
    }

    [Fact]
    public void DpiScaleConvertsLogicalDirtyRectsToPhysicalPixels()
    {
        IReadOnlyList<Rect>? presentedDirtyRects = null;
        var context = new RenderContext(
            new Bitmap(20, 20),
            new Size(10, 10),
            2f,
            (_, dirtyRects) => presentedDirtyRects = dirtyRects);

        context.Present([new Rect(1.25f, 2.25f, 3.5f, 4.5f)]);

        var dirtyRect = Assert.Single(presentedDirtyRects!);
        Assert.Equal(new Rect(2, 4, 8, 10), dirtyRect);
    }

    [Fact]
    public void LayersCompositeOverlappingPrimitivesWithGroupOpacityAndRestoreAfterPop()
    {
        var context = CreateContext(3, 1);
        context.Clear(Color.Transparent);

        context.PushLayer(new Rect(0, 0, 2, 1), 0.5f);
        context.FillRect(new Rect(0, 0, 2, 1), new SolidColorBrush(Color.Red));
        context.FillRect(new Rect(1, 0, 1, 1), new SolidColorBrush(Color.Red));
        context.PopLayer();
        context.FillRect(new Rect(2, 0, 1, 1), new SolidColorBrush(Color.Red));

        var pixels = context.GetBitmap().Pixels;
        Assert.InRange(pixels[3], 127, 128);
        Assert.InRange(pixels[7], 127, 128);
        Assert.Equal(255, pixels[11]);
    }

    [Fact]
    public void NestedLayersCompositeEachGroupOnce()
    {
        var context = CreateContext(1, 1);
        context.Clear(Color.Transparent);

        context.PushLayer(new Rect(0, 0, 1, 1), 0.5f);
        context.PushLayer(new Rect(0, 0, 1, 1), 0.5f);
        context.FillRect(new Rect(0, 0, 1, 1), new SolidColorBrush(Color.Red));
        context.PopLayer();
        context.PopLayer();

        Assert.InRange(context.GetBitmap().Pixels[3], 63, 64);
    }

    [Fact]
    public void LayerPreservesSemiTransparentContentColor()
    {
        var context = CreateContext(1, 1);
        context.Clear(Color.Transparent);

        context.PushLayer(new Rect(0, 0, 1, 1), 0.5f);
        context.FillRect(
            new Rect(0, 0, 1, 1),
            new SolidColorBrush(Color.FromRgba(200, 100, 50, 128)));
        context.PopLayer();

        var pixel = context.GetBitmap().GetPixel(0, 0);
        Assert.InRange(pixel[3], 63, 64);
        Assert.InRange(pixel[2], 198, 202);
        Assert.InRange(pixel[1], 98, 102);
        Assert.InRange(pixel[0], 47, 52);
    }

    [Fact]
    public void LayerClipsDrawingToItsBounds()
    {
        var context = CreateContext(3, 1);
        context.Clear(Color.Blue);

        context.PushLayer(new Rect(1, 0, 1, 1), 0.5f);
        context.FillRect(new Rect(0, 0, 3, 1), new SolidColorBrush(Color.Red));
        context.PopLayer();

        Assert.Equal([255, 0, 0, 255], context.GetBitmap().GetPixel(0, 0).ToArray());
        Assert.Equal([255, 0, 0, 255], context.GetBitmap().GetPixel(2, 0).ToArray());
        var center = context.GetBitmap().GetPixel(1, 0);
        Assert.InRange(center[2], 127, 128);
        Assert.Equal(0, center[1]);
        Assert.InRange(center[0], 127, 128);
        Assert.Equal(255, center[3]);
    }

    [Fact]
    public void LayerBoundsScaleToPhysicalPixels()
    {
        var context = new RenderContext(new Bitmap(4, 2), new Size(2, 1), 2f);
        context.Clear(Color.Transparent);

        context.PushLayer(new Rect(0.5f, 0, 0.5f, 1), 0.5f);
        context.FillRect(new Rect(0, 0, 2, 1), new SolidColorBrush(Color.Red));
        context.PopLayer();

        var pixels = context.GetBitmap().Pixels;
        Assert.Equal(0, pixels[3]);
        Assert.InRange(pixels[7], 127, 128);
        Assert.Equal(0, pixels[11]);
        Assert.Equal(0, pixels[15]);
    }

    [Fact]
    public void ClearInsideLayerOnlyClearsLayerBounds()
    {
        var context = CreateContext(3, 1);
        context.Clear(Color.Blue);

        context.PushLayer(new Rect(1, 0, 1, 1), 1f);
        context.Clear(Color.Red);
        context.PopLayer();

        Assert.Equal([255, 0, 0, 255], context.GetBitmap().GetPixel(0, 0).ToArray());
        Assert.Equal([0, 0, 255, 255], context.GetBitmap().GetPixel(1, 0).ToArray());
        Assert.Equal([255, 0, 0, 255], context.GetBitmap().GetPixel(2, 0).ToArray());
    }

    [Fact]
    public void LayerBuffersAreReusedWithinBoundedContextPool()
    {
        using var context = CreateContext(64, 64);
        context.Clear(Color.White);

        context.PushLayer(new Rect(0, 0, 32, 32), 0.5f);
        context.FillRect(new Rect(0, 0, 32, 32), new SolidColorBrush(Color.Red));
        context.PopLayer();
        var allocations = context.LayerBufferAllocationCount;

        context.PushLayer(new Rect(0, 0, 32, 32), 0.5f);
        context.FillRect(new Rect(0, 0, 32, 32), new SolidColorBrush(Color.Blue));
        context.PopLayer();

        Assert.Equal(1, allocations);
        Assert.Equal(allocations, context.LayerBufferAllocationCount);
        Assert.Equal(1, context.LayerBufferReuseCount);
        Assert.Equal(1, context.RetainedLayerBufferCount);
        Assert.InRange(context.RetainedLayerBufferBytes, 1, LayerBufferPool.MaxRetainedBytes);
    }

    [Fact]
    public void LayerBufferPoolEnforcesBucketAndByteBounds()
    {
        var pool = new LayerBufferPool();
        var buffers = Enumerable.Range(0, 8)
            .Select(_ => pool.Rent(LayerBufferPool.MaxBufferBytes))
            .ToArray();

        foreach (var buffer in buffers) pool.Return(buffer);

        Assert.Equal(LayerBufferPool.MaxBuffersPerBucket, pool.RetainedBufferCount);
        Assert.True(pool.RetainedBytes <= LayerBufferPool.MaxRetainedBytes);
    }

    [Fact]
    public void ResizeWithActiveLayerRestoresBackgroundBeforeResettingState()
    {
        using var context = CreateContext(2, 1);
        context.Clear(Color.Blue);
        context.PushLayer(new Rect(0, 0, 1, 1), 0.5f);
        context.FillRect(new Rect(0, 0, 1, 1), new SolidColorBrush(Color.Red));

        context.Resize(new Size(2, 1));

        Assert.Equal([255, 0, 0, 255], context.GetBitmap().GetPixel(0, 0).ToArray());
        Assert.Equal(1, context.RetainedLayerBufferCount);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    public void SimdSolidBlendMatchesScalarReference(int width)
    {
        using var context = CreateContext(width, 1);
        var bitmap = context.GetBitmap();
        var expected = new byte[bitmap.Pixels.Length];
        for (var x = 0; x < width; x++)
        {
            var offset = x * 4;
            bitmap.Pixels[offset] = expected[offset] = (byte)(x * 17 + 3);
            bitmap.Pixels[offset + 1] = expected[offset + 1] = (byte)(x * 11 + 5);
            bitmap.Pixels[offset + 2] = expected[offset + 2] = (byte)(x * 7 + 9);
            bitmap.Pixels[offset + 3] = expected[offset + 3] = 255;
        }

        const byte alpha = 137;
        for (var offset = 0; offset < expected.Length; offset += 4)
        {
            expected[offset] = BlendOpaque(40, alpha, expected[offset]);
            expected[offset + 1] = BlendOpaque(90, alpha, expected[offset + 1]);
            expected[offset + 2] = BlendOpaque(180, alpha, expected[offset + 2]);
        }

        context.FillRect(new Rect(0, 0, width, 1), new SolidColorBrush(Color.FromRgba(180, 90, 40, alpha)));

        Assert.Equal(expected, bitmap.Pixels);
    }

    [Fact]
    public void DisplayTreeCollectTextFragmentsUsesDrawTextCommands()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 40) };
        var text = new Square.Controls.Text("AB")
        {
            FontSize = 20,
            Geometry = new Rect(10, 5, 40, 24)
        };
        root.Children.Add(text);
        var tree = new DisplayTree();

        tree.BuildFrom(root);

        var fragment = Assert.Single(tree.CollectTextFragments(root));
        var advanceA = fragment.Characters[0].Bounds.Width;
        var advanceB = fragment.Characters[1].Bounds.Width;
        Assert.Same(text, fragment.Element);
        Assert.Equal("AB", fragment.Text);
        Assert.Equal(new Rect(10, 5, advanceA + advanceB, 24), fragment.Bounds);
        Assert.Equal(2, fragment.Characters.Count);
        Assert.Equal(new Rect(10, 5, advanceA, 24), fragment.Characters[0].Bounds);
        Assert.Equal(new Rect(10 + advanceA, 5, advanceB, 24), fragment.Characters[1].Bounds);
        Assert.Equal(0, fragment.HitTestOffset(new Point(11, 10)));
        Assert.Equal(1, fragment.HitTestOffset(new Point(10 + advanceA - 1, 10)));
        Assert.Equal(2, fragment.HitTestOffset(new Point(10 + advanceA + advanceB - 1, 10)));
    }

    [Fact]
    public void DisplayTreeTextFragmentsUseRenderedGlyphAdvances()
    {
        var root = new View { Geometry = new Rect(0, 0, 200, 40) };
        var text = new Square.Controls.Text("Ma")
        {
            FontSize = 20,
            Geometry = new Rect(0, 0, 80, 24)
        };
        root.Children.Add(text);
        var tree = new DisplayTree();

        tree.BuildFrom(root);

        var fragment = Assert.Single(tree.CollectTextFragments(root));
        Assert.Equal(2, fragment.Characters.Count);
        var layout = new TextLayout("Ma", new Font("Segoe UI", 20));
        var firstAdvance = layout.MeasureOffset(1);
        var secondAdvance = layout.MeasureOffset(2) - firstAdvance;
        Assert.Equal(firstAdvance, fragment.Characters[0].Bounds.Width);
        Assert.Equal(secondAdvance, fragment.Characters[1].Bounds.Width);
    }

    [Fact]
    public void DisplayTreeTextFragmentsMatchWrappedRendering()
    {
        var root = new View { Geometry = new Rect(0, 0, 80, 80) };
        var text = new Square.Controls.Text("AAAA")
        {
            FontSize = 20,
            Geometry = new Rect(2, 2, 22, 60)
        };
        root.Children.Add(text);
        var tree = new DisplayTree();

        tree.BuildFrom(root);

        var fragment = Assert.Single(tree.CollectTextFragments(root));
        var wrappedCharacter = fragment.Characters[2];
        Assert.True(wrappedCharacter.Bounds.Y > fragment.Characters[0].Bounds.Y);
        Assert.Equal(2, fragment.HitTestOffset(new Point(
            wrappedCharacter.Bounds.X + wrappedCharacter.Bounds.Width / 4f,
            wrappedCharacter.Bounds.Y + wrappedCharacter.Bounds.Height / 2f)));
        Assert.True(fragment.Bounds.Width <= text.Geometry.Width);
    }

    [Fact]
    public void DisplayTreeTextFragmentsWrapWholeWords()
    {
        var root = new View { Geometry = new Rect(0, 0, 180, 100) };
        var text = new Square.Controls.Text("alpha beta")
        {
            FontSize = 20,
            Geometry = new Rect(2, 2, 70, 80)
        };
        root.Children.Add(text);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        var fragment = Assert.Single(tree.CollectTextFragments(root));
        var betaStart = fragment.Characters.Single(character => character.StartOffset == 6);

        Assert.True(betaStart.Bounds.Y > fragment.Characters[0].Bounds.Y);
        Assert.All(fragment.Characters.Where(character => character.StartOffset >= 6),
            character => Assert.Equal(betaStart.Bounds.Y, character.Bounds.Y));
    }

    [Fact]
    public void DrawTextUsesReadableSeparatedGlyphs()
    {
        var context = CreateContext(180, 40);
        context.Clear(Color.White);
        context.DrawText(
            new TextLayout("Hello 012", new Font("Segoe UI", 20)),
            new Point(2, 2), new SolidColorBrush(Color.Black));

        var bitmap = context.GetBitmap();
        var darkPixels = 0;
        for (var i = 0; i < bitmap.Pixels.Length; i += 4)
            if (bitmap.Pixels[i] < 240 || bitmap.Pixels[i + 1] < 240 || bitmap.Pixels[i + 2] < 240)
                darkPixels++;

        Assert.True(darkPixels > 80);
    }

    [Fact]
    public void DrawTextWrapsWhenMaxWidthIsFinite()
    {
        var context = CreateContext(80, 80);
        context.Clear(Color.White);
        context.DrawText(
            new TextLayout("AAAA", new Font("Segoe UI", 20))
            {
                MaxSize = new Size(22, 80)
            },
            new Point(2, 2), new SolidColorBrush(Color.Black));

        var bitmap = context.GetBitmap();
        var secondLineHasInk = false;
        for (var y = 25; y < bitmap.Height && !secondLineHasInk; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var offset = (y * bitmap.Width + x) * 4;
                if (bitmap.Pixels[offset] >= 240 && bitmap.Pixels[offset + 1] >= 240 && bitmap.Pixels[offset + 2] >= 240)
                    continue;

                secondLineHasInk = true;
                break;
            }
        }

        Assert.True(secondLineHasInk);
    }

    [Fact]
    public void FallbackGlyphsAreNotHorizontallyMirrored()
    {
        Assert.True(RenderContext.IsFallbackGlyphPixelSet('C', 2, 0));
        Assert.False(RenderContext.IsFallbackGlyphPixelSet('C', 2, 4));
        Assert.True(RenderContext.IsFallbackGlyphPixelSet('L', 3, 0));
        Assert.False(RenderContext.IsFallbackGlyphPixelSet('L', 3, 4));
    }

    [Fact]
    public void SystemRasterizerDistinguishesLowercaseAndDigits()
    {
        var rasterizer = new SystemGlyphRasterizer();
        if (!rasterizer.IsAvailable) return;
        var font = new Font("Segoe UI", 20);

        var upper = Assert.IsType<RasterizedGlyph>(rasterizer.Rasterize(font, 'H'));
        var lower = Assert.IsType<RasterizedGlyph>(rasterizer.Rasterize(font, 'e'));
        var digit = Assert.IsType<RasterizedGlyph>(rasterizer.Rasterize(font, '0'));

        Assert.Contains(upper.Coverage, value => value > 0);
        Assert.Contains(lower.Coverage, value => value > 0);
        Assert.Contains(digit.Coverage, value => value > 0);
        Assert.False(upper.Coverage.SequenceEqual(lower.Coverage));
        Assert.False(lower.Coverage.SequenceEqual(digit.Coverage));
    }

    [Fact]
    public void TextLayoutUsesTheSameAdvancesAsSystemTextRendering()
    {
        var rasterizer = new SystemGlyphRasterizer();
        if (!rasterizer.IsAvailable) return;
        var font = new Font("Segoe UI", 20);
        const string text = "Wide 10";
        var expected = text.Sum(character => rasterizer.Rasterize(font, character)?.AdvanceX ?? 0);
        var layout = new TextLayout(text, font);

        Assert.Equal(expected, layout.Measure().Width);
        Assert.Equal(expected, layout.MeasureOffset(text.Length));
        Assert.Equal(text.Length, layout.HitTestOffset(expected));
    }

    [Fact]
    public void TextAlignmentOffsetsEachLineWithinFiniteWidth()
    {
        var font = new Font("Segoe UI", 16);
        var layout = new TextLayout("Align", font)
        {
            MaxSize = new Size(160, 40),
            Alignment = TextAlignment.Right
        };
        var context = CreateContext(160, 40);
        context.Clear(Color.White);

        context.DrawText(layout, Point.Zero, Brush.FromColor(Color.Black));

        var bitmap = context.GetBitmap();
        var firstInkX = bitmap.Width;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel[0] >= 240 && pixel[1] >= 240 && pixel[2] >= 240) continue;
                firstInkX = Math.Min(firstInkX, x);
            }
        }
        Assert.True(firstInkX > 80, $"Expected right-aligned ink, first ink x was {firstInkX}.");
    }

    [Fact]
    public void SystemRasterizerSupportsChineseAndJapaneseGlyphs()
    {
        var rasterizer = new SystemGlyphRasterizer();
        if (!rasterizer.IsAvailable) return;
        var font = new Font("Segoe UI", 20);

        var chinese = Assert.IsType<RasterizedGlyph>(rasterizer.Rasterize(font, '中'));
        var hiragana = Assert.IsType<RasterizedGlyph>(rasterizer.Rasterize(font, 'あ'));
        var katakana = Assert.IsType<RasterizedGlyph>(rasterizer.Rasterize(font, 'ア'));

        Assert.Contains(chinese.Coverage, value => value > 0);
        Assert.Contains(hiragana.Coverage, value => value > 0);
        Assert.Contains(katakana.Coverage, value => value > 0);
    }

    [Fact]
    public void EveryM1ControlProducesVisibleOutput()
    {
        var preview = new Bitmap(2, 2);
        for (var i = 0; i < preview.Pixels.Length; i += 4)
        {
            preview.Pixels[i + 2] = 255;
            preview.Pixels[i + 3] = 255;
        }

        var view = new View();
        view.Style.Set("background", "#eeeeee");
        var controls = new Element[]
        {
            view,
            new Square.Controls.Text("Text"),
            new Button("Button"),
            new Input { Placeholder = "Input" },
            new TextArea { Placeholder = "TextArea" },
            new CheckBox { TextContent = "Check", IsChecked = true },
            new Radio { TextContent = "Radio", IsChecked = true },
            new Select { Value = "Blue", Options = ["Blue", "Green"] },
            new Square.Controls.Image { ImageContent = preview },
            new Canvas()
        };

        foreach (var control in controls)
        {
            var context = CreateContext(240, 120);
            context.Clear(Color.Transparent);
            control.Geometry = new Rect(4, 4, 220, 100);
            control.Paint(context);

            Assert.Contains(context.GetBitmap().Pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
        }
    }

    [Fact]
    public void ButtonHoverAndActiveStatesProvideDefaultVisualFeedback()
    {
        static int Brightness(ReadOnlySpan<byte> pixel) => pixel[0] + pixel[1] + pixel[2];

        var button = new Button("Press") { Geometry = new Rect(2, 2, 100, 40) };

        var normalContext = CreateContext(110, 50);
        button.Paint(normalContext);
        var normal = normalContext.GetBitmap().GetPixel(8, 8);

        button.SetState(ElementState.Hover, true);
        var hoverContext = CreateContext(110, 50);
        button.Paint(hoverContext);
        var hover = hoverContext.GetBitmap().GetPixel(8, 8);

        button.SetState(ElementState.Active, true);
        var activeContext = CreateContext(110, 50);
        button.Paint(activeContext);
        var active = activeContext.GetBitmap().GetPixel(8, 8);

        Assert.True(Brightness(hover) > Brightness(normal));
        Assert.True(Brightness(active) < Brightness(normal));
    }

    [Fact]
    public void OverflowHiddenClipsRenderedChildrenToParentBounds()
    {
        var root = new View { Geometry = new Rect(0, 0, 40, 20) };
        root.Style.Set("overflow", "hidden");
        var child = new View { Geometry = new Rect(30, 0, 20, 20) };
        child.Style.Set("background", "#ff0000");
        root.Children.Add(child);
        var context = CreateContext(60, 30);
        context.Clear(Color.Transparent);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        tree.Render(context);

        var bitmap = context.GetBitmap();
        Assert.Equal(255, bitmap.Pixels[5 * bitmap.Stride + 35 * 4 + 2]);
        Assert.Equal(0, bitmap.Pixels[5 * bitmap.Stride + 45 * 4 + 3]);
    }

    [Fact]
    public void OverflowScrollTranslatesRenderedChildrenAndClipsToViewport()
    {
        var root = new View { Geometry = new Rect(0, 0, 20, 20) };
        root.Style.Set("overflow-y", "auto");
        root.SetScrollContentSize(new Size(20, 60));
        root.ScrollTop = 20;
        var child = new View { Geometry = new Rect(0, 20, 20, 20) };
        child.Style.Set("background", "#ff0000");
        root.Children.Add(child);
        var context = CreateContext(30, 30);
        context.Clear(Color.Transparent);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        tree.Render(context);

        var bitmap = context.GetBitmap();
        Assert.Equal(255, bitmap.Pixels[5 * bitmap.Stride + 5 * 4 + 2]);
        Assert.Equal(0, bitmap.Pixels[25 * bitmap.Stride + 5 * 4 + 3]);
    }

    [Fact]
    public void PopupRendersChildrenOnlyInTopLevelAnchoredPosition()
    {
        var root = new View { Geometry = new Rect(0, 0, 120, 100) };
        var anchor = new Button { Geometry = new Rect(50, 10, 30, 20) };
        var popup = new Popup
        {
            Geometry = new Rect(0, 0, 40, 30),
            Anchor = anchor,
            VerticalOffset = 2
        };
        popup.Style.Set("background", "#ff0000");
        var child = new View { Geometry = new Rect(5, 5, 10, 10) };
        child.Style.Set("background", "#0000ff");
        popup.Children.Add(child);
        root.Children.Add(anchor);
        root.Children.Add(popup);
        popup.Open();
        var context = CreateContext(120, 100);
        context.Clear(Color.Transparent);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        tree.Render(context);

        var bitmap = context.GetBitmap();
        Assert.Equal(0, bitmap.GetPixel(2, 2)[3]);
        Assert.Equal(255, bitmap.GetPixel(52, 34)[2]);
        Assert.Equal(255, bitmap.GetPixel(56, 38)[0]);
    }

    [Fact]
    public void ModalDialogRendersBackdropAndCenteredContent()
    {
        var document = new UIDocument();
        document.Body.Geometry = new Rect(0, 0, 120, 100);
        var dialog = new Dialog { Geometry = new Rect(0, 0, 40, 30) };
        dialog.Style.Set("background", "#ff0000");
        document.Body.Children.Add(dialog);
        dialog.Open();
        var context = CreateContext(120, 100);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(document.Body);

        tree.Render(context);

        var bitmap = context.GetBitmap();
        var backdrop = bitmap.GetPixel(5, 5);
        Assert.True(backdrop[0] < 200 && backdrop[1] < 200 && backdrop[2] < 200);
        Assert.Equal(255, bitmap.GetPixel(60, 50)[2]);
    }

    [Fact]
    public void MenuPopupRendersChecksSeparatorsAndSubmenuArrow()
    {
        var root = new View { Geometry = new Rect(0, 0, 320, 220) };
        var menu = new Menu { Geometry = new Rect(0, 0, 220, 110) };
        var checkedItem = new MenuItem
        {
            TextContent = "Grid",
            IsCheckable = true,
            IsChecked = true,
            Geometry = new Rect(0, 0, 220, 32)
        };
        var separator = new MenuSeparator { Geometry = new Rect(0, 32, 220, 9) };
        var submenuOwner = new MenuItem { TextContent = "Export", Geometry = new Rect(0, 41, 220, 32) };
        submenuOwner.Children.Add(new Menu { Geometry = new Rect(0, 0, 180, 60) });
        menu.Children.Add(checkedItem);
        menu.Children.Add(separator);
        menu.Children.Add(submenuOwner);
        root.Children.Add(menu);
        menu.OpenAt(new Point(20, 20));
        var context = CreateContext(320, 220);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        tree.Render(context);

        var bitmap = context.GetBitmap();
        Assert.True(CountColorNear(bitmap, Color.FromRgb(32, 36, 40), 20) > 20);
        Assert.True(CountColorNear(bitmap, Color.FromRgb(218, 221, 225), 12) > 50);
        Assert.Equal(255, bitmap.GetPixel(30, 26)[3]);
    }

    [Fact]
    public void LaidOutMenuPopupRendersEveryRow()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var bar = new MenuBar();
        var owner = new MenuItem { TextContent = "File" };
        var menu = new Menu();
        menu.Style.Set("background-color", "#123456");
        menu.Children.Add(new MenuItem { TextContent = "New" });
        menu.Children.Add(new MenuItem { TextContent = "Open" });
        menu.Children.Add(new MenuSeparator());
        menu.Children.Add(new MenuItem { TextContent = "Export" });
        owner.Children.Add(menu);
        bar.Children.Add(owner);
        root.Children.Add(bar);
        var layout = new LayoutEngine();
        layout.Measure(root, new Size(320, 220));
        layout.Arrange(root, new Rect(0, 0, 320, 220));
        menu.OpenFor(owner);
        var context = CreateContext(320, 220);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        tree.Render(context);

        Assert.Equal(105, menu.PopupBounds.Height);
        var pixel = context.GetBitmap().GetPixel(230, 90);
        Assert.Equal(0x56, pixel[0]);
        Assert.Equal(0x34, pixel[1]);
        Assert.Equal(0x12, pixel[2]);
    }

    [Fact]
    public void MenuBarPaintUsesCustomBackgroundAcrossItsGeometry()
    {
        var bar = new MenuBar { Geometry = new Rect(0, 0, 200, 32) };
        bar.Style.Set("background-color", "#123456");
        var context = CreateContext(220, 50);
        context.Clear(Color.White);

        bar.Paint(context);

        var pixel = context.GetBitmap().GetPixel(190, 16);
        Assert.Equal(0x56, pixel[0]);
        Assert.Equal(0x34, pixel[1]);
        Assert.Equal(0x12, pixel[2]);
    }

    [Fact]
    public void RetainedRendererDrawsFocusedTextCarets()
    {
        var controls = new UIElement[] { new Input(), new TextArea() };

        foreach (var control in controls)
        {
            control.Geometry = new Rect(4, 4, 220, 80);
            control.Focus();
            var context = CreateContext(240, 100);
            context.Clear(Color.White);
            var tree = new DisplayTree();
            tree.BuildFrom(control);

            tree.Render(context);

            Assert.True(ContainsBgra(context.GetBitmap(), 0, 0, 0, 255));
        }
    }

    [Fact]
    public void DirtyRenderRepaintsFocusedInputOutsideParentBounds()
    {
        var root = new View { Geometry = new Rect(0, 0, 280, 100) };
        var wrapper = new View { Geometry = new Rect(0, 0, 20, 20) };
        var input = new Input
        {
            Geometry = new Rect(40, 30, 200, 36),
            Value = "Still visible"
        };
        wrapper.Children.Add(input);
        root.Children.Add(wrapper);

        var context = CreateContext(280, 100);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        tree.Render(context);

        input.Focus();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();
        var union = dirty[0];
        for (var i = 1; i < dirty.Count; i++)
            union = DisplayTree.Union(union, dirty[i]);
        context.Clear(Color.White, union);
        context.PushClip(union);
        tree.Render(context, union);
        context.PopClip();

        var expectedRoot = new View { Geometry = root.Geometry };
        var expectedWrapper = new View { Geometry = wrapper.Geometry };
        var expectedInput = new Input { Geometry = input.Geometry, Value = input.Value };
        expectedInput.Focus();
        expectedWrapper.Children.Add(expectedInput);
        expectedRoot.Children.Add(expectedWrapper);
        var expectedContext = CreateContext(280, 100);
        expectedContext.Clear(Color.White);
        var expectedTree = new DisplayTree();
        expectedTree.BuildFrom(expectedRoot);
        expectedTree.Render(expectedContext);

        AssertRegionEqual(expectedContext.GetBitmap(), context.GetBitmap(), union);
    }

    [Fact]
    public void DirtyRenderRepaintsAllInputBordersAfterDialogInputLosesFocus()
    {
        var document = new UIDocument();
        document.Ui.Geometry = new Rect(0, 0, 500, 300);
        document.Body.Geometry = document.Ui.Geometry;
        var dialog = new Dialog { Geometry = new Rect(0, 0, 300, 160) };
        var input = new Input { Geometry = new Rect(20, 50, 220, 36), Value = "Dialog input" };
        dialog.Children.Add(input);
        document.Body.Children.Add(dialog);
        dialog.Open();
        input.Focus();
        var context = CreateContext(500, 300);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(document.Ui);
        tree.Render(context);

        input.Unfocus();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();
        var union = dirty.Aggregate(DisplayTree.Union);
        context.Clear(Color.White, union);
        context.PushClip(union);
        tree.Render(context, union);
        context.PopClip();

        var popupX = (document.Ui.Geometry.Width - dialog.Geometry.Width) / 2f;
        var popupY = (document.Ui.Geometry.Height - dialog.Geometry.Height) / 2f + dialog.VerticalOffset;
        var left = (int)(popupX + input.Geometry.X);
        var top = (int)(popupY + input.Geometry.Y);
        var right = (int)(popupX + input.Geometry.Right) - 1;
        var bottom = (int)(popupY + input.Geometry.Bottom) - 1;
        var bitmap = context.GetBitmap();
        AssertBorderPixel(bitmap, left + 20, top);
        AssertBorderPixel(bitmap, left + 20, bottom);
        AssertBorderPixel(bitmap, left, top + 10);
        AssertBorderPixel(bitmap, right, top + 10);
    }

    [Fact]
    public void DirtyRenderReplaysCommandsThatExtendOutsideElementBounds()
    {
        var element = new OverflowPaintElement
        {
            Geometry = new Rect(0, 0, 10, 10),
            PaintRect = new Rect(40, 4, 30, 12)
        };
        var context = CreateContext(90, 30);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(element);
        tree.Render(context);

        context.Clear(Color.White, new Rect(45, 6, 10, 8));
        tree.Render(context, new Rect(45, 6, 10, 8));

        var bitmap = context.GetBitmap();
        var insideDirtyIndex = 8 * bitmap.Stride + 50 * 4;
        var outsideDirtyIndex = 8 * bitmap.Stride + 42 * 4;
        Assert.Equal(255, bitmap.Pixels[insideDirtyIndex + 2]);
        Assert.Equal(255, bitmap.Pixels[outsideDirtyIndex + 2]);
    }

    [Fact]
    public void NestedClipIntersectsDirtyClip()
    {
        var context = CreateContext(30, 20);
        context.Clear(Color.White);
        context.PushClip(new Rect(0, 0, 10, 20));
        context.PushClip(new Rect(5, 0, 20, 20));
        context.FillRect(new Rect(0, 0, 30, 20), new SolidColorBrush(Color.Red));
        context.PopClip();
        context.PopClip();

        var bitmap = context.GetBitmap();
        Assert.Equal(255, bitmap.Pixels[5 * bitmap.Stride + 7 * 4 + 2]);
        Assert.Equal(255, bitmap.Pixels[5 * bitmap.Stride + 15 * 4]);
        Assert.Equal(255, bitmap.Pixels[5 * bitmap.Stride + 15 * 4 + 1]);
        Assert.Equal(255, bitmap.Pixels[5 * bitmap.Stride + 15 * 4 + 2]);
    }

    [Fact]
    public void LinearAndRadialGradientsVaryAcrossTheirGeometry()
    {
        var context = CreateContext(40, 20);
        context.FillRect(
            new Rect(0, 0, 20, 20),
            new LinearGradientBrush(
                Point.Zero, new Point(20, 0),
                new GradientStop(0, Color.Red),
                new GradientStop(1, Color.Blue)));
        context.FillGeometry(
            new EllipseGeometry(new Point(30, 10), 10, 10),
            new RadialGradientBrush(
                new Point(30, 10), 10,
                new GradientStop(0, Color.White),
                new GradientStop(1, Color.Black)));

        var bitmap = context.GetBitmap();
        Assert.True(bitmap.GetPixel(2, 10)[2] > bitmap.GetPixel(17, 10)[2]);
        Assert.True(bitmap.GetPixel(17, 10)[0] > bitmap.GetPixel(2, 10)[0]);
        Assert.True(bitmap.GetPixel(30, 10)[2] > bitmap.GetPixel(38, 10)[2]);
    }

    [Fact]
    public void GeometryClipsRejectPixelsInsideOnlyTheirBounds()
    {
        var context = CreateContext(60, 20);
        context.Clear(Color.Black);
        context.PushClip(new RoundedRectGeometry(new Rect(0, 0, 20, 20), 8, 8));
        context.FillRect(new Rect(0, 0, 20, 20), new SolidColorBrush(Color.Red));
        context.PopClip();
        context.PushClip(new EllipseGeometry(new Point(30, 10), 10, 10));
        context.FillRect(new Rect(20, 0, 20, 20), new SolidColorBrush(Color.Green));
        context.PopClip();
        context.PushClip(PathGeometry.Create()
            .MoveTo(new Point(40, 20))
            .LineTo(new Point(50, 0))
            .LineTo(new Point(60, 20))
            .Close());
        context.FillRect(new Rect(40, 0, 20, 20), new SolidColorBrush(Color.Blue));
        context.PopClip();

        var bitmap = context.GetBitmap();
        Assert.Equal(0, bitmap.GetPixel(0, 0)[2]);
        Assert.Equal(255, bitmap.GetPixel(10, 10)[2]);
        Assert.Equal(0, bitmap.GetPixel(20, 0)[1]);
        Assert.Equal(255, bitmap.GetPixel(30, 10)[1]);
        Assert.Equal(0, bitmap.GetPixel(40, 0)[0]);
        Assert.Equal(255, bitmap.GetPixel(50, 10)[0]);
    }

    [Fact]
    public void InputCaretAccountsForMixedHalfWidthAndFullWidthText()
    {
        var input = new Input { Value = "A中Ｂ", Geometry = new Rect(4, 4, 220, 36) };
        var asciiInput = new Input { Value = "ABC", Geometry = new Rect(4, 4, 220, 36) };
        input.Focus();
        asciiInput.Focus();
        var context = CreateContext(240, 50);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(input);

        tree.Render(context);

        Assert.True(input.CaretRect.X > asciiInput.CaretRect.X);
        var expectedCaretX = (int)input.CaretRect.X;
        var pixel = expectedCaretX * 4 + ((int)input.CaretRect.Y + 2) * context.GetBitmap().Stride;
        Assert.Equal(0, context.GetBitmap().Pixels[pixel]);
        Assert.Equal(0, context.GetBitmap().Pixels[pixel + 1]);
        Assert.Equal(0, context.GetBitmap().Pixels[pixel + 2]);
    }

    [Fact]
    public void FocusedTextSelectionIsRendered()
    {
        var input = new Input { Value = "Select", Geometry = new Rect(4, 4, 220, 36) };
        input.Focus();
        input.SelectAll();
        var context = CreateContext(240, 50);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(input);

        tree.Render(context);

        var bitmap = context.GetBitmap();
        var hasChromeBlueBackground = false;
        var hasWhiteForeground = false;
        var selectionTop = (int)input.Geometry.Y;
        var selectionBottom = (int)input.Geometry.Bottom;
        for (var y = selectionTop; y < selectionBottom; y++)
        {
            for (var x = 12; x < (int)input.CaretRect.X; x++)
            {
                var index = y * bitmap.Stride + x * 4;
                hasChromeBlueBackground |= bitmap.Pixels[index] == 255 && bitmap.Pixels[index + 1] == 144 && bitmap.Pixels[index + 2] == 51;
                hasWhiteForeground |= bitmap.Pixels[index] > 220 && bitmap.Pixels[index + 1] > 220 && bitmap.Pixels[index + 2] > 220;
            }
        }

        Assert.True(hasChromeBlueBackground);
        Assert.True(hasWhiteForeground);
    }

    [Fact]
    public void InputSelectionForegroundDoesNotBlendWithTextColor()
    {
        var colored = new Input { Value = "Select", Geometry = new Rect(4, 4, 220, 36) };
        colored.Style.Set("color", "#b42318");
        colored.Focus();
        colored.SelectAll();
        var plain = new Input { Value = "Select", Geometry = new Rect(4, 4, 220, 36) };
        plain.Focus();
        plain.SelectAll();

        var coloredContext = CreateContext(240, 50);
        coloredContext.Clear(Color.White);
        var coloredTree = new DisplayTree();
        coloredTree.BuildFrom(colored);
        coloredTree.Render(coloredContext);

        var plainContext = CreateContext(240, 50);
        plainContext.Clear(Color.White);
        var plainTree = new DisplayTree();
        plainTree.BuildFrom(plain);
        plainTree.Render(plainContext);

        AssertBitmapEqual(plainContext.GetBitmap(), coloredContext.GetBitmap());
    }

    [Fact]
    public void TextSelectionBoundsCoverGlyphDescenders()
    {
        var root = new View { Geometry = new Rect(0, 0, 100, 50) };
        var text = new Square.Controls.Text("pg")
        {
            FontSize = 20,
            Geometry = new Rect(4, 4, 80, 30)
        };
        root.Children.Add(text);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        var fragment = Assert.Single(tree.CollectTextFragments(root));
        var rasterizer = new SystemGlyphRasterizer();
        var font = new Font("Segoe UI", 20);
        var p = rasterizer.Rasterize(font, 'p');
        var g = rasterizer.Rasterize(font, 'g');
        var expectedBottom = Math.Max(p?.OffsetY + p?.Height ?? 0, g?.OffsetY + g?.Height ?? 0);

        Assert.All(fragment.Characters, character =>
            Assert.True(character.Bounds.Height >= expectedBottom));
    }

    [Fact]
    public void RichTextSelectionUsesCssBackgroundAndForeground()
    {
        var editor = new RichTextEditor(RichTextDocument.FromPlainText("Select"))
        {
            Geometry = new Rect(4, 4, 220, 50)
        };
        editor.Style.Set("selection-background-color", "#123456");
        editor.Style.Set("selection-color", "#fedcba");
        editor.SelectAll();
        var context = CreateContext(240, 60);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(editor);

        tree.Render(context);

        Assert.True(ContainsBgra(context.GetBitmap(), 0x56, 0x34, 0x12, 0xff));
        Assert.True(ContainsNearBgr(context.GetBitmap(), 0xba, 0xdc, 0xfe));
    }

    [Fact]
    public void RichTextSelectionForegroundDoesNotBlendWithRunColor()
    {
        var colored = new RichTextEditor(new RichTextDocument([
            RichTextBlock.Paragraph(new RichTextRun("Select", new RichTextMarks(Foreground: "#b42318")))
        ]))
        {
            Geometry = new Rect(4, 4, 220, 50)
        };
        colored.SelectAll();

        var plain = new RichTextEditor(RichTextDocument.FromPlainText("Select"))
        {
            Geometry = new Rect(4, 4, 220, 50)
        };
        plain.SelectAll();

        var coloredContext = CreateContext(240, 60);
        coloredContext.Clear(Color.White);
        var coloredTree = new DisplayTree();
        coloredTree.BuildFrom(colored);
        coloredTree.Render(coloredContext);

        var plainContext = CreateContext(240, 60);
        plainContext.Clear(Color.White);
        var plainTree = new DisplayTree();
        plainTree.BuildFrom(plain);
        plainTree.Render(plainContext);

        AssertBitmapEqual(plainContext.GetBitmap(), coloredContext.GetBitmap());
    }

    [Fact]
    public void CompactLineHeightSelectionCoversNaturalFontHeight()
    {
        var input = new Input { Value = "Compact", Geometry = new Rect(4, 4, 220, 30) };
        input.Style.Set("font-size", "14px");
        input.Style.Set("line-height", "14px");
        input.Focus();
        input.SelectAll();
        var context = CreateContext(240, 40);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(input);

        tree.Render(context);

        var bitmap = context.GetBitmap();
        var highlightedRows = 0;
        for (var y = (int)input.Geometry.Y; y < (int)input.Geometry.Bottom; y++)
        {
            var hasSelectionPixel = false;
            for (var x = 12; x < (int)input.CaretRect.X; x++)
            {
                var index = y * bitmap.Stride + x * 4;
                hasSelectionPixel |= bitmap.Pixels[index] == 255 &&
                    bitmap.Pixels[index + 1] == 144 && bitmap.Pixels[index + 2] == 51;
            }
            if (hasSelectionPixel) highlightedRows++;
        }

        Assert.True(highlightedRows >= 17);
    }

    [Fact]
    public void FocusedCaretBlinkFadesWithAnimationAndResetVisible()
    {
        var input = new Input { Value = "Blink", Geometry = new Rect(4, 4, 220, 36) };
        input.Focus();
        var context = CreateContext(240, 50);
        var tree = new DisplayTree();

        context.Clear(Color.White);
        tree.BuildFrom(input);
        tree.Render(context);
        var caretPixel = ((int)input.CaretRect.Y + 2) * context.GetBitmap().Stride + (int)input.CaretRect.X * 4;
        Assert.Equal(0, context.GetBitmap().Pixels[caretPixel]);
        Assert.Equal(0, context.GetBitmap().Pixels[caretPixel + 1]);

        Assert.False(input.ToggleCaretBlink());
        Thread.Sleep(720);
        Assert.True(input.ToggleCaretBlink());
        context.Clear(Color.White);
        tree.BuildFrom(input);
        tree.Render(context);
        Assert.True(context.GetBitmap().Pixels[caretPixel] is > 0 and < 255);
        Assert.True(context.GetBitmap().Pixels[caretPixel + 1] is > 0 and < 255);

        input.ResetCaretBlink();
        context.Clear(Color.White);
        tree.BuildFrom(input);
        tree.Render(context);
        Assert.Equal(0, context.GetBitmap().Pixels[caretPixel]);
        Assert.Equal(0, context.GetBitmap().Pixels[caretPixel + 1]);
    }

    [Fact]
    public void RetainedRendererReplaysGeometryCommands()
    {
        var radio = new Radio { IsChecked = true, Geometry = new Rect(4, 4, 100, 24) };
        var context = CreateContext(120, 40);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(radio);

        tree.Render(context);

        Assert.True(ContainsBgra(context.GetBitmap(), 212, 120, 0, 255));
    }

    [Fact]
    public void OpenSelectRendersAboveLaterSiblings()
    {
        var root = new View { Geometry = new Rect(0, 0, 240, 170) };
        var select = new Select
        {
            Geometry = new Rect(10, 10, 220, 36),
            Options = ["Blue", "Green", "Orange"],
            Value = "Blue"
        };
        var laterText = new Square.Controls.Text("For: ready")
        {
            Geometry = new Rect(10, 52, 220, 24)
        };
        root.Children.Add(select);
        root.Children.Add(laterText);
        var context = CreateContext(240, 170);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        select.HandlePointerDown(new Point(20, 20));
        tree.UpdateDirty();

        tree.Render(context);

        var expectedRoot = new View { Geometry = root.Geometry };
        var expectedSelect = new Select
        {
            Geometry = select.Geometry,
            Options = select.Options,
            Value = select.Value
        };
        expectedRoot.Children.Add(expectedSelect);
        expectedSelect.HandlePointerDown(new Point(20, 20));
        var expectedContext = CreateContext(240, 170);
        expectedContext.Clear(Color.White);
        var expectedTree = new DisplayTree();
        expectedTree.BuildFrom(expectedRoot);
        expectedTree.Render(expectedContext);

        AssertRegionEqual(
            expectedContext.GetBitmap(),
            context.GetBitmap(),
            new Rect(10, 48, 220, 98));
    }

    [Fact]
    public void DirtyRenderOpensSelectPopupToMatchFullFrame()
    {
        var root = new View { Geometry = new Rect(0, 0, 240, 170) };
        var select = new Select
        {
            Geometry = new Rect(10, 10, 220, 36),
            Options = ["Blue", "Green", "Orange"],
            Value = "Blue"
        };
        root.Children.Add(select);
        var context = CreateContext(240, 170);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        tree.Render(context);

        select.HandlePointerDown(new Point(20, 20));
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();
        var union = dirty.Aggregate(DisplayTree.Union);
        context.Clear(Color.White, union);
        tree.Render(context, union);

        var expectedRoot = new View { Geometry = root.Geometry };
        var expectedSelect = new Select
        {
            Geometry = select.Geometry,
            Options = select.Options,
            Value = select.Value
        };
        expectedRoot.Children.Add(expectedSelect);
        expectedSelect.HandlePointerDown(new Point(20, 20));
        var expectedContext = CreateContext(240, 170);
        expectedContext.Clear(Color.White);
        var expectedTree = new DisplayTree();
        expectedTree.BuildFrom(expectedRoot);
        expectedTree.Render(expectedContext);

        AssertRegionEqual(expectedContext.GetBitmap(), context.GetBitmap(), union);
    }

    [Fact]
    public void DirtyRenderClearsClosedSelectPopupToMatchFullFrame()
    {
        var root = new View { Geometry = new Rect(0, 0, 240, 170) };
        var select = new Select
        {
            Geometry = new Rect(10, 10, 220, 36),
            Options = ["Blue", "Green", "Orange"],
            Value = "Blue"
        };
        var laterText = new Square.Controls.Text("For: ready")
        {
            Geometry = new Rect(10, 52, 220, 24)
        };
        root.Children.Add(select);
        root.Children.Add(laterText);
        select.HandlePointerDown(new Point(20, 20));

        var context = CreateContext(240, 170);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        tree.Render(context);

        select.CloseDropDown();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();
        var union = dirty[0];
        for (var i = 1; i < dirty.Count; i++)
            union = DisplayTree.Union(union, dirty[i]);
        context.Clear(Color.White, union);
        tree.Render(context, union);

        var expectedRoot = new View { Geometry = root.Geometry };
        var expectedSelect = new Select
        {
            Geometry = select.Geometry,
            Options = select.Options,
            Value = select.Value
        };
        var expectedText = new Square.Controls.Text("For: ready") { Geometry = laterText.Geometry };
        expectedRoot.Children.Add(expectedSelect);
        expectedRoot.Children.Add(expectedText);
        var expectedContext = CreateContext(240, 170);
        expectedContext.Clear(Color.White);
        var expectedTree = new DisplayTree();
        expectedTree.BuildFrom(expectedRoot);
        expectedTree.Render(expectedContext);

        AssertRegionEqual(expectedContext.GetBitmap(), context.GetBitmap(), union);
    }

    [Fact]
    public void DirtyRenderWithTransformedVisualBoundsMatchesFullFrame()
    {
        var element = new TransformedColorElement
        {
            Geometry = new Rect(0, 0, 10, 10),
            FillColor = Color.Red
        };
        var context = CreateContext(140, 80);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(element);
        tree.Render(context);

        element.FillColor = Color.Green;
        element.InvalidatePaint();
        tree.UpdateDirty();
        var dirty = tree.CollectDirtyRects();
        var union = UnionAll(dirty);
        context.Clear(Color.White, union);
        tree.Render(context, union);

        var expected = new TransformedColorElement
        {
            Geometry = element.Geometry,
            FillColor = Color.Green
        };
        var expectedContext = CreateContext(140, 80);
        expectedContext.Clear(Color.White);
        var expectedTree = new DisplayTree();
        expectedTree.BuildFrom(expected);
        expectedTree.Render(expectedContext);

        AssertBitmapEqual(expectedContext.GetBitmap(), context.GetBitmap());
    }

    private static void AssertRegionEqual(Bitmap expected, Bitmap actual, Rect region)
    {
        for (var y = Math.Max(0, (int)region.Top); y < Math.Min(expected.Height, (int)region.Bottom); y++)
            for (var x = Math.Max(0, (int)region.Left); x < Math.Min(expected.Width, (int)region.Right); x++)
            {
                var i = y * expected.Stride + x * 4;
                Assert.Equal(expected.Pixels.AsSpan(i, 4).ToArray(), actual.Pixels.AsSpan(i, 4).ToArray());
            }
    }

    private static void AssertBitmapEqual(Bitmap expected, Bitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    private static void AssertBorderPixel(Bitmap bitmap, int x, int y)
    {
        var pixel = bitmap.GetPixel(x, y);
        Assert.True(pixel[0] < 220 && pixel[1] < 220 && pixel[2] < 220,
            $"Expected border at ({x},{y}), got BGR=({pixel[0]},{pixel[1]},{pixel[2]})");
    }

    private static Rect UnionAll(IReadOnlyList<Rect> rects)
    {
        Assert.NotEmpty(rects);
        var union = rects[0];
        for (var i = 1; i < rects.Count; i++)
            union = DisplayTree.Union(union, rects[i]);
        return union;
    }

    private static bool ContainsBgra(Bitmap bitmap, byte blue, byte green, byte red, byte alpha)
    {
        for (var i = 0; i < bitmap.Pixels.Length; i += 4)
        {
            if (bitmap.Pixels[i] == blue && bitmap.Pixels[i + 1] == green &&
                bitmap.Pixels[i + 2] == red && bitmap.Pixels[i + 3] == alpha)
                return true;
        }

        return false;
    }

    private static bool ContainsNearBgr(Bitmap bitmap, byte blue, byte green, byte red, int tolerance = 24)
    {
        for (var i = 0; i < bitmap.Pixels.Length; i += 4)
        {
            if (Math.Abs(bitmap.Pixels[i] - blue) <= tolerance &&
                Math.Abs(bitmap.Pixels[i + 1] - green) <= tolerance &&
                Math.Abs(bitmap.Pixels[i + 2] - red) <= tolerance)
                return true;
        }

        return false;
    }

    private sealed class OverflowPaintElement : UIElement
    {
        public Rect PaintRect { get; init; }

        public override void Paint(IRenderContext ctx)
        {
            ctx.FillRect(PaintRect, new SolidColorBrush(Color.Red));
        }
    }

    private sealed class TransformedColorElement : UIElement
    {
        public Color FillColor { get; set; }

        public override void Paint(IRenderContext ctx)
        {
            ctx.PushTransform(Matrix3x2.CreateTranslation(70, 20));
            ctx.FillRect(new Rect(0, 0, 30, 20), Brush.FromColor(FillColor));
            ctx.PopTransform();
        }
    }

    [Fact]
    public void ClearFillsAllPixels()
    {
        var ctx = CreateContext(10, 10);
        ctx.Clear(Color.Red);
        var bmp = ctx.GetBitmap();
        for (int i = 0; i < bmp.Pixels.Length; i += 4)
        {
            Assert.Equal(255, bmp.Pixels[i + 3]); // A
            Assert.Equal(0, bmp.Pixels[i]);     // B
            Assert.Equal(0, bmp.Pixels[i + 1]); // G
            Assert.Equal(255, bmp.Pixels[i + 2]); // R
        }
    }

    [Fact]
    public void FillRectOpaque()
    {
        var ctx = CreateContext(20, 20);
        ctx.Clear(Color.Black);
        ctx.FillRect(new Rect(5, 5, 10, 10), new SolidColorBrush(Color.White));
        var bmp = ctx.GetBitmap();
        // 中心像素应为白色
        var idx = 10 * bmp.Stride + 10 * 4;
        Assert.Equal(255, bmp.Pixels[idx + 3]);
        Assert.Equal(255, bmp.Pixels[idx]);
        Assert.Equal(255, bmp.Pixels[idx + 1]);
        Assert.Equal(255, bmp.Pixels[idx + 2]);
        // 角落像素应为黑色
        Assert.Equal(0, bmp.Pixels[0]);
        Assert.Equal(0, bmp.Pixels[1]);
        Assert.Equal(0, bmp.Pixels[2]);
    }

    [Fact]
    public void FillRectOpaqueCoversSimdTailPixels()
    {
        var ctx = CreateContext(37, 3);
        ctx.Clear(Color.Black);

        ctx.FillRect(new Rect(1, 1, 35, 1), new SolidColorBrush(Color.Blue));

        var bmp = ctx.GetBitmap();
        for (var x = 1; x < 36; x++)
        {
            var idx = bmp.Stride + x * 4;
            Assert.Equal(255, bmp.Pixels[idx]);
            Assert.Equal(0, bmp.Pixels[idx + 1]);
            Assert.Equal(0, bmp.Pixels[idx + 2]);
            Assert.Equal(255, bmp.Pixels[idx + 3]);
        }

        Assert.Equal(0, bmp.Pixels[bmp.Stride]);
        Assert.Equal(0, bmp.Pixels[bmp.Stride + 36 * 4]);
    }

    [Fact]
    public void FillRectSemiTransparent()
    {
        var ctx = CreateContext(10, 10);
        ctx.Clear(Color.Black);
        ctx.FillRect(new Rect(0, 0, 10, 10), new SolidColorBrush(255, 0, 0, 128));
        var bmp = ctx.GetBitmap();
        var idx = 5 * bmp.Stride + 5 * 4;
        // Black background (A=255) + 50% red = outA=255
        Assert.Equal(255, bmp.Pixels[idx + 3]);
        // R should be blended (~128)
        Assert.True(bmp.Pixels[idx + 2] > 100 && bmp.Pixels[idx + 2] < 200);
    }

    [Fact]
    public void FillRectSemiTransparentMatchesScalarBlendAcrossVectorAndTail()
    {
        const byte sourceRed = 201;
        const byte sourceGreen = 73;
        const byte sourceBlue = 29;
        const byte sourceAlpha = 137;
        var background = new Color(17, 91, 163, 255);
        var ctx = CreateContext(67, 1);
        ctx.Clear(background);

        ctx.FillRect(
            new Rect(0, 0, 67, 1),
            new SolidColorBrush(sourceRed, sourceGreen, sourceBlue, sourceAlpha));

        var inverseAlpha = 255 - sourceAlpha;
        var expectedBlue = BlendOpaqueDestination(sourceBlue, sourceAlpha, background.B, inverseAlpha);
        var expectedGreen = BlendOpaqueDestination(sourceGreen, sourceAlpha, background.G, inverseAlpha);
        var expectedRed = BlendOpaqueDestination(sourceRed, sourceAlpha, background.R, inverseAlpha);
        var bitmap = ctx.GetBitmap();
        for (var x = 0; x < bitmap.Width; x++)
        {
            var offset = x * 4;
            Assert.Equal(expectedBlue, bitmap.Pixels[offset]);
            Assert.Equal(expectedGreen, bitmap.Pixels[offset + 1]);
            Assert.Equal(expectedRed, bitmap.Pixels[offset + 2]);
            Assert.Equal(255, bitmap.Pixels[offset + 3]);
        }

        static byte BlendOpaqueDestination(byte source, byte sourceAlpha, byte destination, int inverseAlpha)
            => (byte)((source * sourceAlpha + destination * inverseAlpha + 127) / 255);
    }

    [Fact]
    public void FillEllipse()
    {
        var ctx = CreateContext(20, 20);
        ctx.Clear(Color.Black);
        ctx.FillGeometry(new EllipseGeometry(new Point(10, 10), 8, 8), new SolidColorBrush(Color.White));
        var bmp = ctx.GetBitmap();
        // 中心应为白色
        var idx = 10 * bmp.Stride + 10 * 4;
        Assert.Equal(255, bmp.Pixels[idx + 3]);
        // 角落应为黑色
        Assert.Equal(0, bmp.Pixels[0 + 2]);
    }

    [Fact]
    public void FillRoundedRect()
    {
        var ctx = CreateContext(20, 20);
        ctx.Clear(Color.Black);
        ctx.FillGeometry(new RoundedRectGeometry(new Rect(2, 2, 16, 16), 4, 4), new SolidColorBrush(Color.Red));
        var bmp = ctx.GetBitmap();
        // 中心应为红色
        var idx = 10 * bmp.Stride + 10 * 4;
        Assert.Equal(255, bmp.Pixels[idx + 2]); // R
    }

    [Fact]
    public void DrawRect()
    {
        var ctx = CreateContext(20, 20);
        ctx.Clear(Color.Black);
        ctx.DrawRect(new Rect(5, 5, 10, 10), Pen.FromColor(Color.White, 1));
        var bmp = ctx.GetBitmap();
        // 边框像素
        Assert.Equal(255, bmp.Pixels[5 * bmp.Stride + 5 * 4 + 3]);
        // 中心应为黑色（内部空）
        Assert.Equal(0, bmp.Pixels[10 * bmp.Stride + 10 * 4 + 2]);
    }

    [Fact]
    public void DrawLine()
    {
        var ctx = CreateContext(20, 20);
        ctx.Clear(Color.Black);
        var path = PathGeometry.Create()
            .MoveTo(new Point(2, 2))
            .LineTo(new Point(18, 18));
        ctx.DrawPath(path, Pen.FromColor(Color.White, 1));
        var bmp = ctx.GetBitmap();
        // 对角线中点应有像素
        var idx = 10 * bmp.Stride + 10 * 4;
        Assert.True(bmp.Pixels[idx + 3] > 0);
    }

    [Fact]
    public void EllipseAndDiagonalLineHaveAntialiasedEdges()
    {
        var ellipseContext = CreateContext(24, 24);
        ellipseContext.Clear(Color.Transparent);
        ellipseContext.FillGeometry(
            new EllipseGeometry(new Point(12, 12), 8, 8),
            new SolidColorBrush(Color.White));

        var lineContext = CreateContext(24, 24);
        lineContext.Clear(Color.Transparent);
        lineContext.DrawPath(
            PathGeometry.Create().MoveTo(new Point(3, 5)).LineTo(new Point(20, 16)),
            Pen.FromColor(Color.White, 2));

        Assert.Contains(AlphaValues(ellipseContext.GetBitmap()), alpha => alpha is > 0 and < 255);
        Assert.Contains(AlphaValues(lineContext.GetBitmap()), alpha => alpha is > 0 and < 255);
    }

    [Fact]
    public void FilledEllipseSupersamplesHighCurvatureRows()
    {
        var context = CreateContext(24, 24);
        context.Clear(Color.Transparent);
        context.FillGeometry(
            new EllipseGeometry(new Point(12, 12), 8, 8),
            new SolidColorBrush(Color.White));

        var bitmap = context.GetBitmap();
        Assert.InRange(AlphaAt(bitmap, 10, 4), 1, 254);
        Assert.Equal(AlphaAt(bitmap, 10, 4), AlphaAt(bitmap, 13, 4));
    }

    [Fact]
    public void TransformedEllipseUsesRotatedCoverage()
    {
        var context = CreateContext(48, 48);
        context.Clear(Color.Transparent);

        context.PushTransform(Matrix3x2.CreateRotation(MathF.PI / 4f, new Vector2(24, 24)));
        context.FillGeometry(
            new EllipseGeometry(new Point(24, 24), 16, 5),
            new SolidColorBrush(Color.White));
        context.PopTransform();

        var bitmap = context.GetBitmap();
        Assert.Equal(255, AlphaAt(bitmap, 24, 24));
        Assert.True(AlphaAt(bitmap, 16, 16) > 0);
        Assert.Equal(0, AlphaAt(bitmap, 8, 24));
    }

    [Fact]
    public void EllipseStrokeHasAntialiasedEdgesAndOpenCenter()
    {
        var context = CreateContext(32, 32);
        context.Clear(Color.Transparent);

        context.DrawGeometry(
            new EllipseGeometry(new Point(16, 16), 10, 8),
            Pen.FromColor(Color.White, 2));

        var bitmap = context.GetBitmap();
        var center = 16 * bitmap.Stride + 16 * 4;
        Assert.Equal(0, bitmap.Pixels[center + 3]);
        Assert.Contains(AlphaValues(bitmap), alpha => alpha is > 0 and < 255);
    }

    [Fact]
    public void EllipseStrokeSupersamplesHighCurvatureRows()
    {
        var context = CreateContext(32, 32);
        context.Clear(Color.Transparent);

        context.DrawGeometry(
            new EllipseGeometry(new Point(16, 16), 10, 8),
            Pen.FromColor(Color.White, 2));

        var bitmap = context.GetBitmap();
        var partialTopPixels = 0;
        for (var y = 5; y <= 8; y++)
            for (var x = 8; x <= 24; x++)
            {
                if (AlphaAt(bitmap, x, y) is > 0 and < 255)
                    partialTopPixels++;
            }

        Assert.True(partialTopPixels >= 4, $"Expected antialiased stroke pixels near the ellipse top, found {partialTopPixels}.");
    }

    [Fact]
    public void ThinDiagonalLineHasAntialiasedEdges()
    {
        var context = CreateContext(24, 24);
        context.Clear(Color.Transparent);
        context.DrawPath(
            PathGeometry.Create().MoveTo(new Point(3, 5)).LineTo(new Point(20, 16)),
            Pen.FromColor(Color.White, 1));

        Assert.Contains(AlphaValues(context.GetBitmap()), alpha => alpha is > 0 and < 255);
    }

    [Fact]
    public void FractionalHorizontalLineHasAntialiasedEdges()
    {
        var context = CreateContext(24, 12);
        context.Clear(Color.Transparent);
        context.DrawPath(
            PathGeometry.Create().MoveTo(new Point(3, 5.25f)).LineTo(new Point(20, 5.25f)),
            Pen.FromColor(Color.White, 1));

        Assert.Contains(AlphaValues(context.GetBitmap()), alpha => alpha is > 0 and < 255);
    }

    [Fact]
    public void TransformedRoundedRectScalesCornerRadius()
    {
        var context = CreateContext(80, 80);
        context.Clear(Color.Transparent);

        context.PushTransform(Matrix3x2.CreateScale(2));
        context.FillGeometry(
            new RoundedRectGeometry(new Rect(10, 10, 20, 20), 8, 8),
            new SolidColorBrush(Color.White));
        context.PopTransform();

        var bitmap = context.GetBitmap();
        Assert.Equal(0, AlphaAt(bitmap, 26, 21));
        Assert.True(AlphaAt(bitmap, 40, 21) > 0);
    }

    [Fact]
    public void TransformedPathScalesStrokeWidth()
    {
        var context = CreateContext(48, 48);
        context.Clear(Color.Transparent);

        context.PushTransform(Matrix3x2.CreateScale(2));
        context.DrawPath(
            PathGeometry.Create().MoveTo(new Point(4, 10)).LineTo(new Point(20, 10)),
            Pen.FromColor(Color.White, 4));
        context.PopTransform();

        var bitmap = context.GetBitmap();
        Assert.Equal(255, AlphaAt(bitmap, 24, 20));
        Assert.True(AlphaAt(bitmap, 24, 16) > 0);
        Assert.Equal(0, AlphaAt(bitmap, 24, 14));
    }

    [Fact]
    public void FilledPathStraightEdgesAreAntialiased()
    {
        var context = CreateContext(32, 32);
        context.Clear(Color.Transparent);
        context.FillPath(
            PathGeometry.Create()
                .MoveTo(new Point(5, 4))
                .LineTo(new Point(26, 11))
                .LineTo(new Point(17, 27))
                .Close(),
            new SolidColorBrush(Color.White));

        Assert.Contains(AlphaValues(context.GetBitmap()), alpha => alpha is > 0 and < 255);
    }

    [Fact]
    public void DiagonalLineCoverageRefreshesCachedLayerOpacity()
    {
        var context = CreateContext(28, 14);
        context.Clear(Color.Transparent);

        context.PushLayer(new Rect(0, 0, 14, 14), 0.5f);
        context.DrawPath(
            PathGeometry.Create().MoveTo(new Point(2, 2)).LineTo(new Point(10, 10)),
            Pen.FromColor(Color.White, 4));
        context.PopLayer();
        context.DrawPath(
            PathGeometry.Create().MoveTo(new Point(16, 2)).LineTo(new Point(24, 10)),
            Pen.FromColor(Color.White, 4));

        var bitmap = context.GetBitmap();
        Assert.InRange(AlphaAt(bitmap, 6, 6), 127, 128);
        Assert.Equal(255, AlphaAt(bitmap, 20, 6));
    }

    [Fact]
    public void RoundedRectFillLeavesCornersTransparent()
    {
        var context = CreateContext(32, 32);
        context.Clear(Color.Transparent);

        context.FillGeometry(
            new RoundedRectGeometry(new Rect(4, 4, 24, 24), 8, 8),
            new SolidColorBrush(Color.White));

        var bitmap = context.GetBitmap();
        Assert.Equal(0, AlphaAt(bitmap, 4, 4));
        Assert.Equal(255, AlphaAt(bitmap, 16, 16));
        Assert.True(AlphaAt(bitmap, 12, 4) > 0);
    }

    [Fact]
    public void RoundedRectStrokeDrawsCornerArcs()
    {
        var context = CreateContext(32, 32);
        context.Clear(Color.Transparent);

        context.DrawGeometry(
            new RoundedRectGeometry(new Rect(4, 4, 24, 24), 8, 8),
            Pen.FromColor(Color.White, 2));

        var bitmap = context.GetBitmap();
        Assert.Equal(0, AlphaAt(bitmap, 4, 4));
        Assert.True(AlphaAt(bitmap, 8, 6) > 0);
        Assert.Equal(0, AlphaAt(bitmap, 16, 16));
    }

    [Fact]
    public void WideRoundedRectStrokeUsesFastPath()
    {
        var context = CreateContext(640, 80);
        context.Clear(Color.Transparent);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 200; i++)
            context.DrawGeometry(
                new RoundedRectGeometry(new Rect(8, 8, 600, 38), 6, 6),
                Pen.FromColor(Color.White, 2));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500, $"Rounded rect stroke took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void StyledViewUsesBorderRadiusForBackground()
    {
        var view = new View { Geometry = new Rect(4, 4, 24, 24) };
        view.Style.Set("background", "#ffffff");
        view.Style.Set("border-radius", "8px");
        var context = CreateContext(32, 32);
        context.Clear(Color.Transparent);

        view.Paint(context);

        var bitmap = context.GetBitmap();
        Assert.Equal(0, AlphaAt(bitmap, 4, 4));
        Assert.Equal(255, AlphaAt(bitmap, 16, 16));
    }

    [Fact]
    public void DisplayTreeRendersStyledBoxShadowOutsideElementGeometry()
    {
        var view = new View { Geometry = new Rect(20, 20, 30, 20) };
        view.Style.Set("background", "#ffffff");
        view.Style.Set("border-radius", "5px");
        view.Style.Set("box-shadow", "0 4px 8px rgba(0,0,0,0.5)");
        var context = CreateContext(80, 70);
        context.Clear(Color.Transparent);
        var tree = new DisplayTree();
        tree.BuildFrom(view);

        tree.Render(context);

        var bitmap = context.GetBitmap();
        Assert.Equal(255, AlphaAt(bitmap, 35, 30));
        Assert.InRange(AlphaAt(bitmap, 35, 47), 1, 254);
        Assert.Equal(0, AlphaAt(bitmap, 5, 5));
    }

    [Fact]
    public void DisplayTreeRendersMultipleStyledBoxShadowsInCssPaintOrder()
    {
        var view = new View { Geometry = new Rect(20, 20, 20, 20) };
        view.Style.Set("background", "#ffffff");
        view.Style.Set("box-shadow", "-10px 0 0 #ff0000, 10px 0 0 #0000ff");
        var context = CreateContext(60, 60);
        context.Clear(Color.Transparent);
        var tree = new DisplayTree();
        tree.BuildFrom(view);

        tree.Render(context);

        var bitmap = context.GetBitmap();
        AssertPixel(bitmap, 12, 30, 255, 0, 0, 255);
        AssertPixel(bitmap, 47, 30, 0, 0, 255, 255);

        view.Style.Set("box-shadow", "10px 0 0 #ff0000, 10px 0 0 #0000ff");
        tree.UpdateDirty();
        tree.Render(context);
        AssertPixel(bitmap, 47, 30, 255, 0, 0, 255);
    }

    [Fact]
    public void DefaultMenuShadowIsVisibleOnWhiteBackground()
    {
        var root = new View { Geometry = new Rect(0, 0, 160, 120) };
        var menu = new Menu { Geometry = new Rect(0, 0, 80, 40) };
        root.Children.Add(menu);
        menu.OpenAt(new Point(30, 30));
        var context = CreateContext(160, 120);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        tree.Render(context);

        var pixel = context.GetBitmap().GetPixel(70, 76);
        Assert.True(pixel[0] < 180 && pixel[1] < 180 && pixel[2] < 180,
            $"Expected visible menu shadow, got BGR=({pixel[0]},{pixel[1]},{pixel[2]})");
    }

    private static void AssertPixel(Bitmap bitmap, int x, int y, byte r, byte g, byte b, byte a)
    {
        var pixel = bitmap.GetPixel(x, y);
        Assert.Equal(b, pixel[0]);
        Assert.Equal(g, pixel[1]);
        Assert.Equal(r, pixel[2]);
        Assert.Equal(a, pixel[3]);
    }

    private static IEnumerable<byte> AlphaValues(Bitmap bitmap)
    {
        for (var i = 3; i < bitmap.Pixels.Length; i += 4)
            yield return bitmap.Pixels[i];
    }

    private static byte AlphaAt(Bitmap bitmap, int x, int y) => bitmap.Pixels[y * bitmap.Stride + x * 4 + 3];

    private static int CountColorNear(Bitmap bitmap, Color color, int tolerance)
    {
        var count = 0;
        for (var i = 0; i < bitmap.Pixels.Length; i += 4)
        {
            if (Math.Abs(bitmap.Pixels[i] - color.B) <= tolerance &&
                Math.Abs(bitmap.Pixels[i + 1] - color.G) <= tolerance &&
                Math.Abs(bitmap.Pixels[i + 2] - color.R) <= tolerance &&
                bitmap.Pixels[i + 3] > 0)
                count++;
        }
        return count;
    }

    private sealed class TestSoftwareSurface : ISoftwareRenderSurface
    {
        private byte[][] _rows;
        private readonly int _stridePadding;

        public TestSoftwareSurface(int width, int height, int stride)
        {
            _stridePadding = stride - width * 4;
            Width = width;
            Height = height;
            Stride = stride;
            _rows = CreateRows(height, stride);
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Stride { get; private set; }
        public byte[][] Rows => _rows;
        public IReadOnlyList<Rect>? LastDirtyRects { get; private set; }
        public int ResizeCount { get; private set; }
        public int DisposeCount { get; private set; }

        public Span<byte> GetRowSpan(int y) => _rows[y];

        public void Resize(int width, int height)
        {
            Width = width;
            Height = height;
            Stride = width * 4 + _stridePadding;
            _rows = CreateRows(height, Stride);
            ResizeCount++;
        }

        public void Present(IReadOnlyList<Rect>? dirtyRects) => LastDirtyRects = dirtyRects;

        public void Dispose() => DisposeCount++;

        private static byte[][] CreateRows(int height, int stride)
        {
            var rows = new byte[height][];
            for (var y = 0; y < rows.Length; y++) rows[y] = new byte[stride];
            return rows;
        }
    }

    [Fact]
    public void DrawText()
    {
        var ctx = CreateContext(100, 30);
        ctx.Clear(Color.Black);
        var layout = new TextLayout("HELLO", new Font("Segoe UI", 16f));
        ctx.DrawText(layout, new Point(5, 5), new SolidColorBrush(Color.White));
        var bmp = ctx.GetBitmap();
        // 应有白色像素
        var hasWhite = false;
        for (int i = 0; i < bmp.Pixels.Length; i += 4)
            if (bmp.Pixels[i + 3] > 0 && bmp.Pixels[i + 2] > 0) { hasWhite = true; break; }
        Assert.True(hasWhite);
    }

    [Fact]
    public void ClipRect()
    {
        var ctx = CreateContext(20, 20);
        ctx.Clear(Color.Black);
        ctx.PushClip(new Rect(5, 5, 10, 10));
        ctx.FillRect(new Rect(0, 0, 20, 20), new SolidColorBrush(Color.White));
        ctx.PopClip();
        var bmp = ctx.GetBitmap();
        // 裁剪区内为白色
        Assert.Equal(255, bmp.Pixels[10 * bmp.Stride + 10 * 4 + 3]);
        // 裁剪区外为黑色
        Assert.Equal(0, bmp.Pixels[0 + 2]);
    }

    [Fact]
    public void DrawImage()
    {
        var ctx = CreateContext(20, 20);
        ctx.Clear(Color.Black);
        var src = new Bitmap(10, 10);
        for (int i = 0; i < src.Pixels.Length; i += 4)
        {
            src.Pixels[i] = 255;     // B
            src.Pixels[i + 1] = 0;   // G
            src.Pixels[i + 2] = 0;   // R
            src.Pixels[i + 3] = 255; // A
        }
        ctx.DrawImage(src, new Rect(0, 0, 10, 10));
        var bmp = ctx.GetBitmap();
        Assert.Equal(255, bmp.Pixels[5 * bmp.Stride + 5 * 4]);     // B
        Assert.Equal(255, bmp.Pixels[5 * bmp.Stride + 5 * 4 + 3]); // A
    }

    [Fact]
    public void DrawImageUnscaledOpaqueClipsAndCopiesRows()
    {
        var ctx = CreateContext(6, 4);
        ctx.Clear(Color.Black);
        var src = new Bitmap(4, 3);
        for (var y = 0; y < src.Height; y++)
            for (var x = 0; x < src.Width; x++)
            {
                var idx = y * src.Stride + x * 4;
                src.Pixels[idx] = (byte)(10 + x);
                src.Pixels[idx + 1] = (byte)(20 + y);
                src.Pixels[idx + 2] = 30;
                src.Pixels[idx + 3] = 255;
            }

        ctx.DrawImage(src, new Rect(4, 1, 4, 3));

        var bmp = ctx.GetBitmap();
        var copied = 2 * bmp.Stride + 5 * 4;
        Assert.Equal(11, bmp.Pixels[copied]);
        Assert.Equal(21, bmp.Pixels[copied + 1]);
        Assert.Equal(30, bmp.Pixels[copied + 2]);
        Assert.Equal(255, bmp.Pixels[copied + 3]);

        Assert.Equal(0, bmp.Pixels[2 * bmp.Stride + 3 * 4]);
    }

    [Fact]
    public void DrawImageUnscaledSemiTransparentBlendsRows()
    {
        var ctx = CreateContext(4, 4);
        ctx.Clear(Color.Black);
        var src = new Bitmap(2, 2);
        for (var i = 0; i < src.Pixels.Length; i += 4)
        {
            src.Pixels[i] = 0;
            src.Pixels[i + 1] = 0;
            src.Pixels[i + 2] = 255;
            src.Pixels[i + 3] = 128;
        }

        ctx.DrawImage(src, new Rect(1, 1, 2, 2));

        var bmp = ctx.GetBitmap();
        var idx = bmp.Stride + 4;
        Assert.Equal(255, bmp.Pixels[idx + 3]);
        Assert.Equal(255, bmp.Pixels[idx + 2]);
    }

    [Fact]
    public void DrawImageScaledHonorsRectangularClip()
    {
        var ctx = CreateContext(8, 8);
        ctx.Clear(Color.Black);
        var src = new Bitmap(2, 2);
        for (var i = 0; i < src.Pixels.Length; i += 4)
        {
            src.Pixels[i + 2] = 255;
            src.Pixels[i + 3] = 255;
        }

        ctx.PushClip(new Rect(2, 3, 3, 2));
        ctx.DrawImage(src, new Rect(0, 0, 8, 8));
        ctx.PopClip();

        var bitmap = ctx.GetBitmap();
        Assert.Equal(255, bitmap.Pixels[3 * bitmap.Stride + 2 * 4 + 2]);
        Assert.Equal(0, bitmap.Pixels[2 * bitmap.Stride + 2 * 4 + 2]);
        Assert.Equal(0, bitmap.Pixels[3 * bitmap.Stride + 5 * 4 + 2]);
    }
}
