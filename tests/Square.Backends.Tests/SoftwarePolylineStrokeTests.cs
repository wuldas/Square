using System.Numerics;
using Square.Backends;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Tests;

public class SoftwarePolylineStrokeTests
{
    [Theory]
    [InlineData(.5f)]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(5f)]
    public void CollinearSubdivisionDoesNotChangeStrokePixels(float width)
    {
        foreach (var end in new[] { new Point(44, 44), new Point(44, 8), new Point(8, 44) })
        {
            using var original = Context(52, 52);
            using var split = Context(52, 52);
            var start = new Point(8, 8);
            var path = Path([start, end]);
            var subdivided = Path(Enumerable.Range(0, 5)
                .Select(i => new Point(start.X + (end.X - start.X) * i / 4, start.Y + (end.Y - start.Y) * i / 4)).ToArray());
            var pen = Pen.FromColor(new Color(137, 193, 71, 127), width);
            original.DrawPath(path, pen);
            split.DrawPath(subdivided, pen);
            Assert.Equal(original.GetBitmap().Pixels, split.GetBitmap().Pixels);
        }
    }

    [Theory]
    [InlineData(20f, 8.25f, 64)]
    [InlineData(4f, 24.25f, 64)]
    [InlineData(20f, 24.25f, 4096)]
    public void SubpixelCollinearSubdivisionKeepsTheOriginalLine(float endX, float endY, int divisions)
    {
        var start = new Point(4, 8.25f);
        var end = new Point(endX, endY);
        var points = Enumerable.Range(0, divisions + 1)
            .Select(i => new Point(start.X + (end.X - start.X) * i / divisions,
                start.Y + (end.Y - start.Y) * i / divisions)).ToArray();
        using var original = Context(32, 32);
        using var split = Context(32, 32);
        var pen = Pen.FromColor(new Color(137, 193, 71, 127), 1);
        original.DrawPath(Path([start, end]), pen);
        split.DrawPath(Path(points), pen);
        Assert.Equal(original.GetBitmap().Pixels, split.GetBitmap().Pixels);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(128)]
    [InlineData(255)]
    public void RetracingAndDuplicateVerticesDoNotIncreaseOpacity(byte alpha)
    {
        using var original = Context(52, 52);
        using var repeated = Context(52, 52);
        var a = new Point(8, 8);
        var b = new Point(40, 40);
        var pen = Pen.FromColor(new Color(137, 193, 71, alpha), 3);
        original.DrawPath(Path([a, b]), pen);
        repeated.DrawPath(Path([a, a, b, b, a, a, b]), pen);
        Assert.Equal(original.GetBitmap().Pixels, repeated.GetBitmap().Pixels);
        Assert.All(Alphas(repeated.GetBitmap()), value => Assert.True(value <= alpha));
    }

    [Fact]
    public void JointUnionsSampleIdentitiesInsteadOfTakingMaximumCoverageCount()
    {
        using var context = Context(24, 24);
        context.DrawPath(Path([new(5, 14), new(14, 14), new(14, 5)]),
            Pen.FromColor(new Color(137, 193, 71, 128), 1.25f));
        // At (13,13), each arm covers 12 samples, but their union covers 15.
        // max(count) would yield 96; repeated source-over would exceed 128.
        Assert.Equal((byte)120, context.GetBitmap().GetPixel(13, 13)[3]);
        Assert.All(Alphas(context.GetBitmap()), value => Assert.True(value <= 128));
    }

    [Theory]
    [InlineData(0, .5f)]
    [InlineData(79, 1.25f)]
    [InlineData(128, 2f)]
    [InlineData(255, 5f)]
    public void SelfCrossingStrokeMatchesUnionOfOriginalFourByFourSamples(byte alpha, float width)
    {
        Point[] points = [new(4, 4), new(40, 40), new(4, 40), new(40, 4), new(4, 4)];
        var color = new Color(137, 193, 71, alpha);
        foreach (byte backgroundAlpha in new byte[] { 0, 87, 255 })
        {
            using var actual = Context(48, 48);
            using var expected = Context(48, 48);
            var background = new Color(17, 47, 113, backgroundAlpha);
            actual.Clear(background);
            expected.Clear(background);
            actual.DrawPath(Path(points), Pen.FromColor(color, width));
            PaintExpected(expected, UnionSamples(48, 48, [Doubles(points)], width), 48, 48, color);
            Assert.Equal(expected.GetBitmap().Pixels, actual.GetBitmap().Pixels);
        }
    }

    [Fact]
    public void SeparateDrawCallsStillCompositeIndependently()
    {
        using var context = Context(40, 40);
        var path = Path([new(4, 4), new(20, 20), new(34, 6)]);
        var pen = Pen.FromColor(new Color(137, 193, 71, 128), 4);
        context.DrawPath(path, pen);
        var once = context.GetBitmap().GetPixel(20, 20)[3];
        context.DrawPath(path, pen);
        var twice = context.GetBitmap().GetPixel(20, 20)[3];
        Assert.Equal((byte)128, once);
        Assert.Equal((byte)191, twice);
    }

    [Fact]
    public void ClosedFiguresAndMoveToBoundariesDoNotCreateConnectingStrokes()
    {
        Point[] first = [new(4, 4), new(18, 4), new(18, 18), new(4, 18), new(4, 4)];
        Point[] second = [new(32, 27), new(46, 27), new(39, 43), new(32, 27)];
        var path = Path(first[..^1]).Close().MoveTo(second[0]);
        foreach (var point in second[1..^1]) path.LineTo(point);
        path.Close();
        using var actual = Context(52, 48);
        using var expected = Context(52, 48);
        var color = new Color(137, 193, 71, 128);
        actual.DrawPath(path, Pen.FromColor(color, 2));
        PaintExpected(expected, UnionSamples(52, 48, [Doubles(first), Doubles(second)], 2), 52, 48, color);
        Assert.Equal(expected.GetBitmap().Pixels, actual.GetBitmap().Pixels);
        Assert.Equal((byte)0, actual.GetBitmap().GetPixel(25, 22)[3]);
    }

    [Fact]
    public void OverlappingSubpathsArePartOfOneStrokeOperation()
    {
        var a = new Point(4, 4);
        var b = new Point(36, 36);
        using var original = Context(44, 44);
        using var repeated = Context(44, 44);
        var color = new Color(137, 193, 71, 128);
        original.DrawPath(Path([a, b]), Pen.FromColor(color, 2));
        var path = Path([a, b]).MoveTo(a).LineTo(b).MoveTo(b).MoveTo(a).LineTo(b);
        repeated.DrawPath(path, Pen.FromColor(color, 2));
        Assert.Equal(original.GetBitmap().Pixels, repeated.GetBitmap().Pixels);
    }

    [Theory]
    [InlineData(0, false, 1f)]
    [InlineData(1, false, 1f)]
    [InlineData(2, true, 1f)]
    [InlineData(3, true, 2f)]
    public void UnionPreservesTransformedClipLayerAndDpiSemantics(int mode, bool layer, float dpi)
    {
        const int w = 72, h = 64;
        Point[] points = [new(4, 5), new(26, 23), new(8, 31), new(34, 7)];
        var transform = mode switch
        {
            1 => Matrix3x2.CreateRotation(.21f, new Vector2(20, 18)),
            2 => new Matrix3x2(1.25f, .1f, .2f, .8f, 2, 3),
            3 => Matrix3x2.CreateTranslation(1, -2),
            _ => Matrix3x2.Identity
        };
        using var actual = new RenderContext(new Bitmap(w, h), new Size(w / dpi, h / dpi), dpi);
        using var expected = new RenderContext(new Bitmap(w, h), new Size(w / dpi, h / dpi), dpi);
        var color = new Color(137, 193, 71, 129);
        foreach (var context in new[] { actual, expected })
        {
            context.Clear(new Color(17, 47, 113, 87));
            context.PushClip(new EllipseGeometry(new Point(20, 18), 18, 16));
            context.PushClip(new Rect(3, 3, 33, 29));
            if (layer) context.PushLayer(new Rect(5, 4, 29, 25), .63f);
        }
        actual.PushTransform(transform);
        actual.DrawPath(Path(points), Pen.FromColor(color, 1.75f));
        actual.PopTransform();
        var combined = transform * Matrix3x2.CreateScale(dpi);
        var devicePoints = points.Select(p => Vector2.Transform(new Vector2(p.X, p.Y), combined))
            .Select(p => ((double)p.X, (double)p.Y)).ToArray();
        var strokeScale = MathF.Max(MathF.Sqrt(combined.M11 * combined.M11 + combined.M12 * combined.M12),
            MathF.Sqrt(combined.M21 * combined.M21 + combined.M22 * combined.M22));
        var clip = new Rect(3 * dpi, 3 * dpi, 33 * dpi, 29 * dpi);
        var samples = UnionSamples(w, h, [devicePoints], 1.75f * strokeScale, clip);
        expected.PushTransform(Matrix3x2.CreateScale(1 / dpi));
        PaintExpected(expected, samples, w, h, color);
        expected.PopTransform();
        if (layer) { actual.PopLayer(); expected.PopLayer(); }
        Assert.Equal(expected.GetBitmap().Pixels, actual.GetBitmap().Pixels);
    }

    [Fact]
    public void FlattenedArcUnionsItsSegmentsAtOriginalSamplePrecision()
    {
        const int w = 64, h = 64;
        var oval = new Rect(9, 11, 40, 32);
        const float start = 17, sweep = 290;
        var points = new List<(double, double)>();
        for (var i = 0; i <= 16; i++)
        {
            var angle = start + (double)i / 16 * sweep;
            points.Add((oval.X + oval.Width / 2 + oval.Width / 2 * Math.Cos(angle * Math.PI / 180),
                oval.Y + oval.Height / 2 + oval.Height / 2 * Math.Sin(angle * Math.PI / 180)));
        }
        var first = new Point((float)points[0].Item1, (float)points[0].Item2);
        var path = new PathGeometry().MoveTo(first).ArcTo(oval, start, sweep);
        points.Insert(0, (first.X, first.Y));
        using var actual = Context(w, h);
        using var expected = Context(w, h);
        var color = new Color(137, 193, 71, 128);
        actual.DrawPath(path, Pen.FromColor(color, 2));
        PaintExpected(expected, UnionSamples(w, h, [points.ToArray()], 2), w, h, color);
        Assert.Equal(expected.GetBitmap().Pixels, actual.GetBitmap().Pixels);
    }

    [Theory]
    [InlineData(620, 420)]
    [InlineData(66000, 6)]
    public void CoverageTilesPreserveOriginalSampleSequenceAndClearReusedBuffers(int w, int h)
    {
        Point[] points = [new(.1f, .3f), new(w - 2.25f, h - 2.125f), new(w / 2f, 1.3f)];
        using var actual = Context(w, h);
        using var expected = Context(w, h);
        var color = new Color(137, 193, 71, 128);
        actual.DrawPath(Path(points), Pen.FromColor(color, 1.75f));
        PaintExpected(expected, UnionSamples(w, h, [Doubles(points)], 1.75f), w, h, color);
        Assert.Equal(expected.GetBitmap().Pixels, actual.GetBitmap().Pixels);
        actual.Clear(Color.Transparent);
        expected.Clear(Color.Transparent);
        Point[] small = [new(2, 2), new(8, 4), new(3, 4)];
        actual.DrawPath(Path(small), Pen.FromColor(color, .75f));
        PaintExpected(expected, UnionSamples(w, h, [Doubles(small)], .75f), w, h, color);
        Assert.Equal(expected.GetBitmap().Pixels, actual.GetBitmap().Pixels);
    }

    [Fact]
    public void MultiSegmentStrokeHonorsPaddedRows()
    {
        var surface = new PaddedSurface();
        using var actual = new RenderContext(surface, new Size(surface.Width, surface.Height), 1);
        using var expected = Context(surface.Width, surface.Height);
        actual.Clear(Color.Transparent);
        Point[] points = [new(2, 2), new(25, 20), new(3, 21), new(26, 3)];
        var color = new Color(137, 193, 71, 128);
        actual.DrawPath(Path(points), Pen.FromColor(color, 2));
        PaintExpected(expected, UnionSamples(surface.Width, surface.Height, [Doubles(points)], 2), surface.Width, surface.Height, color);
        using var capture = actual.CaptureBitmap();
        Assert.Equal(expected.GetBitmap().Pixels, capture.Pixels);
        foreach (var row in surface.Rows)
            Assert.All(row.Skip(surface.Width * 4), value => Assert.Equal((byte)0xA7, value));
    }

    private static RenderContext Context(int width, int height)
    {
        var context = new RenderContext(new Bitmap(width, height), 1);
        context.Clear(Color.Transparent);
        return context;
    }

    private static PathGeometry Path(Point[] points)
    {
        var path = new PathGeometry().MoveTo(points[0]);
        foreach (var point in points.Skip(1)) path.LineTo(point);
        return path;
    }

    private static (double x, double y)[] Doubles(Point[] points) => points.Select(p => ((double)p.X, (double)p.Y)).ToArray();
    private static IEnumerable<byte> Alphas(Bitmap bitmap) => Enumerable.Range(0, bitmap.Width * bitmap.Height).Select(i => bitmap.Pixels[i * 4 + 3]);

    private static void PaintExpected(RenderContext context, ushort[] mask, int width, int height, Color color)
    {
        // Draw one fully covered single-segment pixel through the unchanged old path,
        // so expected source-over/opacity quantization comes from the original blender.
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var count = BitOperations.PopCount((uint)mask[y * width + x]);
            if (count == 0) continue;
            var alpha = (byte)(color.A * count / 16);
            context.DrawPath(Path([new(x, y + .5f), new(x + 1, y + .5f)]),
                Pen.FromColor(new Color(color.R, color.G, color.B, alpha), 1));
        }
    }

    private static ushort[] UnionSamples(int width, int height, (double x, double y)[][] figures, float strokeWidth, Rect? clip = null)
    {
        var mask = new ushort[width * height];
        foreach (var points in figures)
        for (var i = 0; i < points.Length - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var dx = (float)(b.x - a.x);
            var dy = (float)(b.y - a.y);
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < .0001 || strokeWidth <= 0) continue;
            Rect? rect = Math.Abs(dx) < .01
                ? new Rect((float)a.x - strokeWidth / 2, (float)Math.Min(a.y, b.y), strokeWidth, Math.Max(1, Math.Abs(dy)))
                : Math.Abs(dy) < .01
                    ? new Rect((float)Math.Min(a.x, b.x), (float)a.y - strokeWidth / 2, Math.Max(1, Math.Abs(dx)), strokeWidth)
                    : null;
            var radius = MathF.Max(.5f, strokeWidth / 2);
            var minX = Math.Max(0, rect is { } r0 ? (int)MathF.Floor(r0.Left) : (int)Math.Floor(Math.Min(a.x, b.x) - radius - 1));
            var minY = Math.Max(0, rect is { } r1 ? (int)MathF.Floor(r1.Top) : (int)Math.Floor(Math.Min(a.y, b.y) - radius - 1));
            var maxX = Math.Min(width - 1, rect is { } r2 ? (int)MathF.Ceiling(r2.Right) : (int)Math.Ceiling(Math.Max(a.x, b.x) + radius + 1));
            var maxY = Math.Min(height - 1, rect is { } r3 ? (int)MathF.Ceiling(r3.Bottom) : (int)Math.Ceiling(Math.Max(a.y, b.y) + radius + 1));
            if (clip is { } c)
            {
                minX = Math.Max(minX, (int)MathF.Floor(c.Left));
                minY = Math.Max(minY, (int)MathF.Floor(c.Top));
                maxX = Math.Min(maxX, (int)MathF.Ceiling(c.Right) - 1);
                maxY = Math.Min(maxY, (int)MathF.Ceiling(c.Bottom) - 1);
            }
            for (var y = minY; y <= maxY; y++)
            {
                var sampleX = new Vector4(minX - (float)a.x) + new Vector4(.125f, .375f, .625f, .875f);
                for (var x = minX; x <= maxX; x++)
                {
                    for (var sy = 0; sy < 4; sy++)
                    {
                        if (rect is { } axis)
                        {
                            for (var sx = 0; sx < 4; sx++)
                            {
                                var px = x + (sx + .5f) / 4;
                                var py = y + (sy + .5f) / 4;
                                if (px >= axis.Left && px < axis.Right && py >= axis.Top && py < axis.Bottom)
                                    mask[y * width + x] |= (ushort)(1 << (sy * 4 + sx));
                            }
                        }
                        else
                        {
                            // This is the original DrawLine sample arithmetic, before union.
                            var sampleY = new Vector4(y + (sy + .5f) / 4 - (float)a.y);
                            var t = Vector4.Clamp((sampleX * new Vector4(dx) + sampleY * new Vector4(dy)) * new Vector4(1f / lengthSquared), Vector4.Zero, Vector4.One);
                            var distanceX = sampleX - t * new Vector4(dx);
                            var distanceY = sampleY - t * new Vector4(dy);
                            var squared = distanceX * distanceX + distanceY * distanceY;
                            if (squared.X <= radius * radius) mask[y * width + x] |= (ushort)(1 << (sy * 4));
                            if (squared.Y <= radius * radius) mask[y * width + x] |= (ushort)(2 << (sy * 4));
                            if (squared.Z <= radius * radius) mask[y * width + x] |= (ushort)(4 << (sy * 4));
                            if (squared.W <= radius * radius) mask[y * width + x] |= (ushort)(8 << (sy * 4));
                        }
                    }
                    sampleX += Vector4.One;
                }
            }
        }
        return mask;
    }

    private sealed class PaddedSurface : ISoftwareRenderSurface
    {
        public int Width => 32;
        public int Height => 28;
        public int Stride => Width * 4 + 16;
        public byte[][] Rows { get; } = Enumerable.Range(0, 28).Select(_ => Enumerable.Repeat((byte)0xA7, 32 * 4 + 16).ToArray()).ToArray();
        public Span<byte> GetRowSpan(int y) => Rows[y];
        public void Resize(int width, int height) => throw new NotSupportedException();
        public void Present(IReadOnlyList<Rect>? dirtyRects) { }
        public void Dispose() { }
    }
}
