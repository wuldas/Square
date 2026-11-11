using System.Numerics;
using System.Reflection;
using Square.Backends;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Tests;

public sealed class SoftwareScanlineCoverageTests
{
    private const int Width = 40, Height = 36;
    private delegate void CoverageBlend(RenderContext context, int x, int y, Color color, int covered, int total);
    private static readonly CoverageBlend Blend = typeof(RenderContext)
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(method => method.Name == "BlendPixelCoverage" && method.GetParameters().Length == 5)
        .CreateDelegate<CoverageBlend>();

    public static IEnumerable<object[]> Cases()
    {
        for (var shape = 0; shape < 7; shape++)
        for (var transform = 0; transform < 6; transform++)
        for (var translation = 0; translation < 16; translation++)
            yield return [shape, transform, translation];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void LineContourMatchesOriginalFourByFourPredicateThroughTransformClipAndRepeatedAlpha(
        int shape, int transformIndex, int translation)
    {
        var points = Points(shape);
        var path = new PathGeometry().MoveTo(points[0]);
        foreach (var point in points.Skip(1)) path.LineTo(point);
        path.Close();
        var transform = Transform(transformIndex) * Matrix3x2.CreateTranslation(translation / 16f, (15 - translation) / 16f);
        var transformed = points.Select(p => Vector2.Transform(new Vector2(p.X, p.Y), transform))
            .Select(p => ((double)p.X, (double)p.Y)).ToList();
        transformed.Add(transformed[0]);
        using var actual = new RenderContext(new Bitmap(Width, Height), 1f);
        using var expected = new RenderContext(new Bitmap(Width, Height), 1f);
        var ellipse = new EllipseGeometry(new Point(20, 18), 18, 16);
        foreach (var context in new[] { actual, expected })
        {
            context.Clear(new Color(23, 61, 113, shape % 2 == 0 ? (byte)0 : (byte)91));
            if (transformIndex % 3 == 0) context.PushClip(new Rect(2.25f, 1.75f, 34.5f, 31.5f));
            else if (transformIndex % 3 == 1) context.PushClip(ellipse);
            else context.PushClip(new PathGeometry().MoveTo(new Point(2, 2)).LineTo(new Point(38, 4)).LineTo(new Point(4, 34)).Close());
            if (shape % 3 == 0) context.PushLayer(new Rect(3, 2, 33, 31), .63f);
        }
        actual.PushTransform(transform);
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var color = new Color(137, 53, 219, repeat == 0 ? (byte)255 : repeat == 1 ? (byte)128 : (byte)79);
            actual.FillPath(path, new SolidColorBrush(color));
            ReferenceCoverage(expected, transformed, color);
            Assert.Equal(expected.GetBitmap().Pixels, actual.GetBitmap().Pixels);
            if (repeat == 0) Assert.True(actual.PolygonScanlineFillCount > 0);
            if (repeat == 1) { ellipse.Center = new Point(18, 20); ellipse.RadiusX = 13; }
        }
        Assert.True(actual.CoverageHitCount > 0); // Third use replays the same ordered coverage with new paint/live clip.
        if (shape % 3 == 0)
        {
            actual.PopLayer(); expected.PopLayer();
            Assert.Equal(expected.GetBitmap().Pixels, actual.GetBitmap().Pixels);
        }
    }

    [Fact]
    public void ArcMultipleContoursAndNonFiniteInputDoNotEnterScanlineFastPath()
    {
        PathGeometry[] paths =
        [
            new PathGeometry().MoveTo(new Point(5, 5)).ArcTo(new Rect(5, 5, 20, 20), 0, 270).Close(),
            new PathGeometry().MoveTo(new Point(2, 2)).LineTo(new Point(30, 2)).LineTo(new Point(2, 30)).Close()
                .MoveTo(new Point(10, 10)).LineTo(new Point(20, 10)).LineTo(new Point(10, 20)).Close(),
            new PathGeometry().MoveTo(new Point(2, 2)).LineTo(new Point(float.NaN, 20)).LineTo(new Point(20, 2)).Close()
        ];
        foreach (var path in paths)
        {
            using var context = new RenderContext(new Bitmap(Width, Height), 1f);
            context.FillPath(path, new SolidColorBrush(Color.White));
            Assert.Equal(0, context.PolygonScanlineFillCount);
        }
    }

    private static void ReferenceCoverage(RenderContext target, List<(double X, double Y)> points, Color color)
    {
        // Frozen original per-sample even/odd predicate. Only existing pixel blending is shared,
        // so this isolates coverage/boundary equivalence from unrelated blend rounding policies.
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var covered = 0;
            for (var sy = 0; sy < 4; sy++)
            for (var sx = 0; sx < 4; sx++)
            {
                var px = x + (sx + .5) / 4;
                var py = y + (sy + .5) / 4;
                var inside = false;
                for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                {
                    var (xi, yi) = points[i];
                    var (xj, yj) = points[j];
                    if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi) inside = !inside;
                }
                if (inside) covered++;
            }
            Blend(target, x, y, color, covered, 16);
        }
    }

    private static Matrix3x2 Transform(int index) => index switch
    {
        1 => Matrix3x2.CreateScale(-1, 1) * Matrix3x2.CreateTranslation(38, 0),
        2 => Matrix3x2.CreateRotation(.23f, new Vector2(20, 18)),
        3 => new Matrix3x2(.875f, .125f, -.25f, 1.125f, 3, -2),
        4 => Matrix3x2.CreateScale(.75f, -1.25f) * Matrix3x2.CreateTranslation(4, 35),
        5 => new Matrix3x2(1, .00001f, 0, 1, 1, -1),
        _ => Matrix3x2.Identity
    };

    private static Point[] Points(int shape) => shape switch
    {
        0 => [new(8, 2), new(26, 2), new(34, 10), new(34, 24), new(26, 32), new(8, 32), new(2, 24), new(2, 10)],
        1 => [new(2.125f, 2.125f), new(35.875f, 2.125f), new(35.875f, 30.875f), new(25.125f, 30.875f), new(25.125f, 10.125f), new(12.875f, 10.125f), new(12.875f, 30.875f), new(2.125f, 30.875f)],
        2 => [new(3, 3), new(35, 31), new(3, 31), new(35, 3)],
        3 => [new(3, 4), new(20, 4), new(20, 4), new(35, 4), new(35, 30), new(18, 18), new(3, 30), new(3, 4)],
        4 => [new(-20, 5.5f), new(35, .5f), new(60, 42.5f), new(.5f, 39.5f)],
        5 => [new(10.125f, 3.125f), new(10.1875f, 3.125f), new(25.1875f, 30.875f), new(25.125f, 30.875f)],
        _ => [new(10.125f, 10.125f), new(20.125f, 10.125f), new(20.125f, 10.125f), new(10.125f, 10.125f)]
    };
}
