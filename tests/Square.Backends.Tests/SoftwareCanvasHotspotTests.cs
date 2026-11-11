using System.Numerics;
using Square.Backends;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Tests;

public class SoftwareCanvasHotspotTests
{
    private const int Width = 48;
    private const int Height = 40;
    private static readonly Rect Canvas = new(0, 0, Width, Height);
    private static readonly Rect OversizedRect = new(-512, -512, 1024, 1024);

    public static IEnumerable<object[]> PolygonCases()
    {
        for (var shape = 0; shape < 5; shape++)
            for (var transform = 0; transform < 7; transform++)
                yield return [shape, transform];
    }

    [Theory]
    [MemberData(nameof(PolygonCases))]
    public void PolygonClipMatchesPerEdgeEvenOddPredicate(int shape, int transformIndex)
    {
        var points = Polygon(shape);
        var transform = Transform(transformIndex);
        var bounds = Bounds(points, transform);
        Assert.True(Matrix3x2.Invert(transform, out var inverse));
        using var context = CreateContext();
        context.Clear(Color.Black);
        context.PushTransform(transform);
        context.PushClip(Path(points));
        context.PopTransform();
        context.FillRect(Canvas, new SolidColorBrush(Color.White));

        AssertMask(context, bounds, (x, y) => Inside(points, ToPoint(inverse, x, y)));
    }

    [Fact]
    public void NestedPolygonEllipseAndRectClipsKeepTheirIntersectionAndRestoreOnPop()
    {
        var outer = Polygon(0);
        var inner = Polygon(1);
        var ellipse = new EllipseGeometry(new Point(21, 19), 17, 15);
        var rect = new Rect(6.25f, 4.5f, 29.5f, 25.25f);
        using var context = CreateContext();
        context.Clear(Color.Black);
        context.PushClip(Path(outer));
        context.PushClip(ellipse);
        context.PushClip(Path(inner));
        context.PushClip(rect);
        var bounds = Rect.Intersect(Rect.Intersect(Bounds(outer, Matrix3x2.Identity),
            new Rect(4, 4, 34, 30)), Rect.Intersect(Bounds(inner, Matrix3x2.Identity), rect));
        context.FillRect(Canvas, new SolidColorBrush(Color.White));
        AssertMask(context, bounds, (x, y) => Inside(outer, new Point(x, y))
            && Inside(inner, new Point(x, y)) && InEllipse(ellipse, x, y));

        context.PopClip();
        context.PopClip();
        context.PopClip();
        context.Clear(Color.Black);
        context.FillRect(Canvas, new SolidColorBrush(Color.White));
        AssertMask(context, Bounds(outer, Matrix3x2.Identity), (x, y) => Inside(outer, new Point(x, y)));
    }

    [Fact]
    public void PathClipSnapshotsCommandsButEllipseClipStillObservesLiveGeometry()
    {
        var points = Polygon(1);
        var path = Path(points);
        var ellipse = new EllipseGeometry(new Point(23, 19), 17, 15);
        using var context = CreateContext();
        context.Clear(Color.Black);
        context.PushClip(path);
        path.LineTo(new Point(45, 38));
        context.PushClip(ellipse);
        var snapshotBounds = Rect.Intersect(Bounds(points, Matrix3x2.Identity), new Rect(6, 4, 34, 30));
        ellipse.Center = new Point(26, 20);
        ellipse.RadiusX = 8;
        context.FillRect(Canvas, new SolidColorBrush(Color.White));
        AssertMask(context, snapshotBounds, (x, y) => Inside(points, new Point(x, y)) && InEllipse(ellipse, x, y));

        context.PopClip();
        context.PopClip();
        context.PushClip(path);
        context.Clear(Color.Black);
        context.FillRect(Canvas, new SolidColorBrush(Color.White));
        // Close() appends the first vertex before the subsequent LineTo command.
        Point[] appended = [.. points, points[0], new Point(45, 38)];
        AssertMask(context, Bounds(appended, Matrix3x2.Identity), (x, y) => Inside(appended, new Point(x, y)));
    }

    [Fact]
    public void PolygonScanlineRefreshesWhenPaintVisitsRowsOutOfOrder()
    {
        var points = Polygon(0);
        using var context = CreateContext();
        context.Clear(Color.Black);
        context.PushClip(Path(points));
        for (var x = 0; x < Width; x++)
            for (var y = Height - 1; y >= 0; y--)
                context.FillRect(new Rect(x, y, 1, 1), new SolidColorBrush(Color.White));
        AssertMask(context, Bounds(points, Matrix3x2.Identity), (x, y) => Inside(points, new Point(x, y)));
    }

    [Theory]
    [InlineData(0, GradientSpreadMethod.Pad, 0)]
    [InlineData(1, GradientSpreadMethod.Repeat, 87)]
    [InlineData(2, GradientSpreadMethod.Reflect, 255)]
    [InlineData(3, GradientSpreadMethod.Pad, 87)]
    [InlineData(4, GradientSpreadMethod.Reflect, 0)]
    [InlineData(5, GradientSpreadMethod.Repeat, 255)]
    [InlineData(6, GradientSpreadMethod.Pad, 87)]
    public void GradientRowsMatchGenericPerPixelSamplerThroughMutationClipsAndLayers(
        int transformIndex, GradientSpreadMethod spread, byte backgroundAlpha)
    {
        using var optimized = CreateContext();
        using var reference = CreateContext();
        var background = new Color(31, 83, 127, backgroundAlpha);
        optimized.Clear(background);
        reference.Clear(background);
        var stops = new[]
        {
            new GradientStop(.75f, new Color(213, 47, 91, 129)),
            new GradientStop(0, new Color(17, 159, 51, 3)),
            new GradientStop(.75f, new Color(51, 61, 239, 223)),
            new GradientStop(1, new Color(247, 197, 29, 251))
        };
        var brush = new LinearGradientBrush(new Point(6, -4), new Point(6, 27), stops) { SpreadMethod = spread };
        for (var stage = 0; stage < 7; stage++)
        {
            switch (stage)
            {
                case 1:
                    stops[0].Offset = .25f;
                    stops[1].Color = new Color(191, 33, 71, 139);
                    brush.Start = new Point(-3, 8);
                    brush.End = new Point(-3, -11);
                    break;
                case 2:
                    brush.Stops = [new GradientStop(0, Color.Blue), new GradientStop(1, new Color(97, 53, 23, 73))];
                    brush.SpreadMethod = GradientSpreadMethod.Repeat;
                    break;
                case 3:
                    brush.End = new Point(15, 21); // Nonvertical fallback.
                    break;
                case 4:
                    brush.Stops = [];
                    brush.End = brush.Start;
                    break;
                case 5:
                    brush.Stops = [new GradientStop(.4f, new Color(123, 57, 229, 107))];
                    break;
                case 6:
                    brush.Stops = [new GradientStop(.5f, Color.Red), new GradientStop(.5f, Color.Blue)];
                    brush.Start = new Point(0, 0);
                    brush.End = new Point(0, 1);
                    break;
            }
            DrawGradient(optimized, brush, Transform(transformIndex), genericSampler: false, layer: stage % 2 == 0);
            DrawGradient(reference, brush, Transform(transformIndex), genericSampler: true, layer: stage % 2 == 0);
            Assert.Equal(reference.GetBitmap().Pixels, optimized.GetBitmap().Pixels);
        }
    }

    public static IEnumerable<object[]> SpanCases()
    {
        for (var shape = 0; shape < 5; shape++)
            for (var transform = 0; transform < 7; transform++)
                foreach (byte alpha in new byte[] { 0, 79, 255 })
                    yield return [shape, transform, alpha];
    }

    [Theory]
    [MemberData(nameof(SpanCases))]
    public void SinglePathGradientSpansMatchPerPixelSamplingOnOpaqueAndTransparentTargets(
        int shape, int clipTransformIndex, byte backgroundAlpha)
    {
        using var optimized = CreateContext();
        using var reference = CreateContext();
        var background = new Color(67, 103, 191, backgroundAlpha);
        optimized.Clear(background);
        reference.Clear(background);
        optimized.FillRect(new Rect(15, 0, 6, Height), new SolidColorBrush(Color.White));
        reference.FillRect(new Rect(15, 0, 6, Height), new SolidColorBrush(Color.White));
        var path = Path(Polygon(shape));
        var clipTransform = Transform(clipTransformIndex);
        var gradientTransform = Transform(clipTransformIndex % 3);
        var brush = new LinearGradientBrush(new Point(7, 0), new Point(7, 40),
            new GradientStop(0, new Color(11, 37, 197, 0)),
            new GradientStop(.5f, new Color(79, 131, 217, 127)),
            new GradientStop(1, new Color(173, 59, 43, 255)));
        foreach (var context in new[] { optimized, reference })
        {
            context.PushTransform(clipTransform);
            context.PushClip(path);
            context.PopTransform();
            context.PushTransform(gradientTransform);
        }
        for (var repeat = 0; repeat < 3; repeat++)
        {
            optimized.FillRect(OversizedRect, brush);
            reference.FillGeometry(new RoundedRectGeometry(OversizedRect, 0, 0), brush);
            Assert.Equal(reference.GetBitmap().Pixels, optimized.GetBitmap().Pixels);
            brush.Start = new Point(7, 31);
            brush.End = new Point(7, -5);
            brush.Stops[1].Color = new Color(137, 179, 29, (byte)(repeat * 127));
            brush.SpreadMethod = (GradientSpreadMethod)repeat;
        }
    }

    [Fact]
    public void GradientRowReuseHandlesDpiAndPaddedSurfaceWithoutWritingPadding()
    {
        var optimizedSurface = new PaddedSurface();
        var referenceSurface = new PaddedSurface();
        using var optimized = new RenderContext(optimizedSurface, new Size(Width / 1.5f, Height / 1.5f), 1.5f);
        using var reference = new RenderContext(referenceSurface, new Size(Width / 1.5f, Height / 1.5f), 1.5f);
        optimized.Clear(new Color(13, 31, 57, 91));
        reference.Clear(new Color(13, 31, 57, 91));
        var brush = new LinearGradientBrush(new Point(2, 5), new Point(2, 17),
            new GradientStop(0, new Color(103, 57, 19, 73)), new GradientStop(1, new Color(217, 131, 47, 193)));
        DrawGradient(optimized, brush, Matrix3x2.CreateTranslation(2, -3), false, true);
        DrawGradient(reference, brush, Matrix3x2.CreateTranslation(2, -3), true, true);
        optimized.PushClip(Path(Polygon(0)));
        reference.PushClip(Path(Polygon(0)));
        optimized.FillRect(OversizedRect, brush);
        reference.FillGeometry(new RoundedRectGeometry(OversizedRect, 0, 0), brush);
        for (var y = 0; y < Height; y++)
        {
            Assert.Equal(referenceSurface.Rows[y], optimizedSurface.Rows[y]);
            Assert.All(optimizedSurface.Rows[y].Skip(Width * 4), value => Assert.Equal((byte)0xA7, value));
        }
    }

    private static void DrawGradient(RenderContext context, LinearGradientBrush brush, Matrix3x2 transform, bool genericSampler, bool layer)
    {
        context.PushClip(new RoundedRectGeometry(new Rect(1, 1, Width - 2, Height - 2), 5, 7));
        context.PushClip(Path(Polygon(0)));
        if (layer) context.PushLayer(new Rect(4, 3, 35, 31), .63f);
        context.PushTransform(transform);
        if (genericSampler)
            // This route keeps the unoptimized per-pixel SampleBrush/BlendPixel code.
            context.FillGeometry(new RoundedRectGeometry(OversizedRect, 0, 0), brush);
        else
            context.FillRect(OversizedRect, brush);
        context.PopTransform();
        if (layer) context.PopLayer();
        context.PopClip();
        context.PopClip();
    }

    private static RenderContext CreateContext() => new(new Bitmap(Width, Height), 1f);

    private static Point[] Polygon(int shape) => shape switch
    {
        0 => [new(2.5f, 2.5f), new(35.5f, 2.5f), new(35.5f, 31.5f), new(25.5f, 31.5f),
            new(25.5f, 11.5f), new(12.5f, 11.5f), new(12.5f, 31.5f), new(2.5f, 31.5f)],
        1 => [new(3.5f, 3.5f), new(35.5f, 29.5f), new(3.5f, 29.5f), new(35.5f, 3.5f)],
        2 => [new(4, 4), new(20, 4), new(20, 4), new(34, 4), new(34, 30), new(18, 18), new(4, 30), new(4, 4)],
        3 => [new(-20, 5.5f), new(40, .5f), new(60, 42.5f), new(.5f, 39.5f)],
        _ => [new(10.5f, 10.5f), new(20.5f, 10.5f), new(20.5f, 10.5f), new(10.5f, 10.5f)]
    };

    private static Matrix3x2 Transform(int index) => index switch
    {
        1 => Matrix3x2.CreateScale(1.25f, .75f) * Matrix3x2.CreateTranslation(-3, 6),
        2 => Matrix3x2.CreateScale(-1, 1) * Matrix3x2.CreateTranslation(Width, 0),
        3 => Matrix3x2.CreateRotation(.17f, new Vector2(24, 20)),
        4 => new Matrix3x2(1, 0, .35f, 1, 0, 0),
        5 => new Matrix3x2(1, .00001f, 0, 1, 2, -1),
        6 => Matrix3x2.CreateScale(.75f, -1.25f) * Matrix3x2.CreateTranslation(5, Height),
        _ => Matrix3x2.Identity
    };

    private static PathGeometry Path(Point[] points)
    {
        var path = new PathGeometry().MoveTo(points[0]);
        foreach (var point in points.Skip(1)) path.LineTo(point);
        return path.Close();
    }

    private static Rect Bounds(Point[] points, Matrix3x2 transform)
    {
        var transformed = points.Select(p => ToPoint(transform, p.X, p.Y)).ToArray();
        var left = transformed.Min(p => p.X);
        var top = transformed.Min(p => p.Y);
        return new Rect(left, top, transformed.Max(p => p.X) - left, transformed.Max(p => p.Y) - top);
    }

    private static Point ToPoint(Matrix3x2 matrix, float x, float y)
    {
        var value = Vector2.Transform(new Vector2(x, y), matrix);
        return new Point(value.X, value.Y);
    }

    private static bool Inside(Point[] points, Point point)
    {
        var inside = false;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            var a = points[i];
            var b = points[j];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static bool InEllipse(EllipseGeometry ellipse, float x, float y)
    {
        var dx = (x - ellipse.Center.X) / ellipse.RadiusX;
        var dy = (y - ellipse.Center.Y) / ellipse.RadiusY;
        return dx * dx + dy * dy <= 1;
    }

    private static void AssertMask(RenderContext context, Rect bounds, Func<float, float, bool> contains)
    {
        var expected = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var offset = (y * Width + x) * 4;
                expected[offset + 3] = 255;
                if (x < MathF.Ceiling(bounds.Left) || x >= MathF.Floor(bounds.Right)
                    || y < MathF.Ceiling(bounds.Top) || y >= MathF.Floor(bounds.Bottom)
                    || !contains(x + .5f, y + .5f)) continue;
                expected[offset] = expected[offset + 1] = expected[offset + 2] = 255;
            }
        Assert.Equal(expected, context.GetBitmap().Pixels);
    }

    private sealed class PaddedSurface : ISoftwareRenderSurface
    {
        public int Width => SoftwareCanvasHotspotTests.Width;
        public int Height => SoftwareCanvasHotspotTests.Height;
        public int Stride => Width * 4 + 16;
        internal byte[][] Rows { get; } = Enumerable.Range(0, SoftwareCanvasHotspotTests.Height)
            .Select(_ => Enumerable.Repeat((byte)0xA7, SoftwareCanvasHotspotTests.Width * 4 + 16).ToArray()).ToArray();
        public Span<byte> GetRowSpan(int y) => Rows[y];
        public void Resize(int width, int height) => throw new NotSupportedException();
        public void Present(IReadOnlyList<Rect>? dirtyRects) { }
        public void Dispose() { }
    }
}
