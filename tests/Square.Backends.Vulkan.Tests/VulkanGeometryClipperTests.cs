using System.Numerics;
using Square.Graphics;
using Square.Rendering.Tessellation;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

public class VulkanGeometryClipperTests
{
    private const uint Tint = 0x80402010;
    private static readonly Rect Canvas = new(0, 0, 64, 64);

    [Fact]
    public void IntegralAxisAlignedRectUsesScissorAndPreservesAllVertexBits()
    {
        var region = VulkanClipRegion.CreateCanvas(Canvas)
            .Intersect(new Rect(3, 4, 20, 17), Matrix3x2.CreateScale(2, 1));
        Assert.False(region.HasMask);
        Assert.Equal(new Rect(6, 4, 40, 17), region.Bounds);
        var input = Quad(Canvas);
        var output = new List<Vertex2D> { new(-1, -1, 0, 0, 0) };
        var indices = new List<uint>();
        region.Clip(input, [0, 1, 2, 0, 2, 3], output, indices);
        Assert.Equal(5, output.Count);
        Assert.Equal(new uint[] { 1, 2, 3, 1, 3, 4 }, indices);
        for (var i = 0; i < input.Length; i++) Assert.Equal(input[i], output[i + 1]);
    }

    [Theory]
    [InlineData(4.25f, 5.5f, 20.5f, 12.25f)]
    [InlineData(.125f, .375f, .25f, .125f)]
    public void FractionalRectangleProducesExactMaskWithoutDoubleCoverage(float x, float y, float w, float h)
    {
        var rect = new Rect(x, y, w, h);
        var region = VulkanClipRegion.CreateCanvas(Canvas).Intersect(rect, Matrix3x2.Identity);
        Assert.True(region.HasMask);
        var mesh = ClipQuad(region);
        AssertArea(w * (double)h, mesh);
        Assert.Equal(1, Coverage(mesh, x + w / 2.0, y + h / 2.0));
        Assert.Equal(0, Coverage(mesh, x - .01, y + h / 2.0));
        AssertNoOverdraw(mesh);
    }

    [Fact]
    public void ConcavePathRejectsTheNotchInsteadOfUsingItsBounds()
    {
        Point[] polygon = [new(2, 2), new(26, 2), new(26, 8), new(10, 8), new(10, 25), new(2, 25)];
        var region = VulkanClipRegion.CreateCanvas(Canvas).Intersect(Path(polygon), Matrix3x2.Identity);
        var mesh = ClipQuad(region);
        Assert.True(region.HasMask);
        Assert.Equal(0, Coverage(mesh, 20, 18));
        Assert.Equal(1, Coverage(mesh, 5, 18));
        AssertArea(280, mesh);
        AssertSamples(mesh, (x, y) => Inside(polygon, x, y));
        AssertNoOverdraw(mesh);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvenOddContoursKeepHolesRegardlessOfContourDirection(bool reverseHole)
    {
        var outer = RectanglePoints(new Rect(2, 2, 28, 28));
        var hole = RectanglePoints(new Rect(10, 10, 12, 12));
        if (reverseHole) Array.Reverse(hole);
        var path = Path(outer);
        AddContour(path, hole);
        var region = VulkanClipRegion.CreateCanvas(Canvas).Intersect(path, Matrix3x2.Identity);
        var mesh = ClipQuad(region);
        Assert.Equal(1, Coverage(mesh, 6, 6));
        Assert.Equal(0, Coverage(mesh, 16, 16));
        AssertArea(640, mesh);
        AssertSamples(mesh, (x, y) => Inside(outer, x, y) != Inside(hole, x, y));
        AssertNoOverdraw(mesh);
    }

    [Fact]
    public void SelfIntersectingPathUsesEvenOddAreaOnce()
    {
        Point[] points = [new(4, 4), new(28, 28), new(4, 28), new(28, 4)];
        var region = VulkanClipRegion.CreateCanvas(Canvas).Intersect(Path(points), Matrix3x2.Identity);
        var mesh = ClipQuad(region);
        AssertArea(288, mesh);
        AssertSamples(mesh, (x, y) => Inside(points, x, y));
        AssertNoOverdraw(mesh);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedRotatedClipsIntersectExactlyAndKeepParentSnapshot(bool reflected)
    {
        Point[] polygon = [new(2, 2), new(35, 2), new(35, 11), new(15, 11), new(15, 34), new(2, 34)];
        var a = new Matrix3x2(1.2f, .25f, -.15f, .9f, 7, 3);
        if (reflected) a *= Matrix3x2.CreateScale(-1, 1) * Matrix3x2.CreateTranslation(64, 0);
        var b = Matrix3x2.CreateRotation(-.2f, new Vector2(24, 22));
        var rect = new Rect(5.25f, 4.5f, 28.5f, 31.25f);
        var parent = VulkanClipRegion.CreateCanvas(Canvas).Intersect(Path(polygon), a);
        var parentMesh = ClipQuad(parent);
        var child = parent.Intersect(rect, b).Intersect(new Rect(8, 7, 40, 39), Matrix3x2.Identity);
        Assert.True(child.HasMask);
        Assert.True(Matrix3x2.Invert(a, out var inverseA));
        Assert.True(Matrix3x2.Invert(b, out var inverseB));
        var mesh = ClipQuad(child);
        AssertSamples(mesh, (x, y) =>
        {
            var pa = Vector2.Transform(new Vector2((float)x, (float)y), inverseA);
            var pb = Vector2.Transform(new Vector2((float)x, (float)y), inverseB);
            return Inside(polygon, pa.X, pa.Y) && InRect(rect, pb.X, pb.Y)
                && InRect(new Rect(8, 7, 40, 39), x, y);
        });
        AssertNoOverdraw(mesh);
        Assert.Equal(parentMesh.Vertices.ToArray(), ClipQuad(parent).Vertices.ToArray());
        Assert.Equal(parentMesh.Indices.ToArray(), ClipQuad(parent).Indices.ToArray());
    }

    [Fact]
    public void RepeatedIntersectionDoesNotDuplicateSemitransparentTriangles()
    {
        var path = Path(RectanglePoints(new Rect(2.25f, 3.5f, 30, 27)));
        AddContour(path, RectanglePoints(new Rect(10, 10, 10, 10)));
        var region = VulkanClipRegion.CreateCanvas(Canvas);
        for (var i = 0; i < 12; i++) region = region.Intersect(path, Matrix3x2.Identity);
        var mesh = ClipQuad(region);
        AssertArea(710, mesh);
        Assert.All(mesh.Vertices, v => Assert.Equal(Tint, v.Color));
        AssertNoOverdraw(mesh);
        AssertSamples(mesh, (x, y) => InRect(new Rect(2.25f, 3.5f, 30, 27), x, y)
            && !InRect(new Rect(10, 10, 10, 10), x, y));
    }

    [Fact]
    public void MutablePathEllipseAndRoundedGeometryAreSnapshotted()
    {
        var path = Path(RectanglePoints(new Rect(4, 4, 20, 20)));
        var parent = VulkanClipRegion.CreateCanvas(Canvas).Intersect(path, Matrix3x2.Identity);
        path.MoveTo(new Point(30, 30)).LineTo(new Point(60, 30)).LineTo(new Point(30, 60)).Close();
        AssertArea(400, ClipQuad(parent));
        Assert.True(Area(ClipQuad(VulkanClipRegion.CreateCanvas(Canvas).Intersect(path, Matrix3x2.Identity))) > 400);

        var ellipse = new EllipseGeometry(new Point(32, 32), 15, 9);
        var oval = VulkanClipRegion.CreateCanvas(Canvas).Intersect(ellipse, Matrix3x2.Identity);
        var ovalMesh = ClipQuad(oval);
        ellipse.Center = new Point(5, 5);
        ellipse.RadiusX = 1;
        Assert.Equal(ovalMesh.Vertices.ToArray(), ClipQuad(oval).Vertices.ToArray());
        Assert.Equal(1, Coverage(ovalMesh, 32, 32));
        Assert.Equal(0, Coverage(ovalMesh, 17.1, 23.1));

        var rounded = new RoundedRectGeometry(new Rect(2, 2, 28, 24),
            new CornerRadius(8), new CornerRadius(0), new CornerRadius(6), new CornerRadius(3));
        var roundedRegion = VulkanClipRegion.CreateCanvas(Canvas).Intersect(rounded, Matrix3x2.Identity);
        rounded.TopLeft = rounded.BottomRight = new CornerRadius(0);
        var roundedMesh = ClipQuad(roundedRegion);
        Assert.Equal(0, Coverage(roundedMesh, 3, 3));
        Assert.Equal(1, Coverage(roundedMesh, 29, 3));
        Assert.Equal(0, Coverage(roundedMesh, 29, 25));
        AssertNoOverdraw(roundedMesh);
    }

    [Fact]
    public void TinyEllipseIsNotLostToOneSegmentCurveResolution()
    {
        var region = VulkanClipRegion.CreateCanvas(Canvas)
            .Intersect(new EllipseGeometry(new Point(.375f, .125f), .1f, .1f), Matrix3x2.Identity);
        var mesh = ClipQuad(region);
        Assert.True(region.HasMask);
        Assert.True(Area(mesh) > .02);
        Assert.Equal(1, Coverage(mesh, .375, .125));
        Assert.Equal(0, Coverage(mesh, .6, .125));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClippingPreservesSourceWindingAndInterpolatesUvAndRgba(bool clockwise)
    {
        Vertex2D[] source =
        [
            new(0, 0, 0, 0, 64u << 24 | 17u << 16),
            new(32, 0, 1, 0, 128u << 24 | 17u << 16 | 255),
            new(0, 32, 0, 1, 255u << 24 | 17u << 16 | 255u << 8)
        ];
        var region = VulkanClipRegion.CreateCanvas(Canvas).Intersect(new Rect(4.25f, 3.5f, 12.5f, 10.25f), Matrix3x2.Identity);
        var vertices = new List<Vertex2D>();
        var indices = new List<uint>();
        region.Clip(source, clockwise ? [0, 2, 1] : [0, 1, 2], vertices, indices);
        Assert.NotEmpty(indices);
        foreach (var v in vertices)
        {
            Assert.InRange(Math.Abs(v.U - v.X / 32), 0, 1e-6);
            Assert.InRange(Math.Abs(v.V - v.Y / 32), 0, 1e-6);
            Assert.Equal((uint)Math.Round(255 * v.X / 32.0), v.Color & 255);
            Assert.Equal((uint)Math.Round(255 * v.Y / 32.0), v.Color >> 8 & 255);
            Assert.Equal(17u, v.Color >> 16 & 255);
            Assert.Equal((uint)Math.Round(64 + 64 * v.X / 32.0 + 191 * v.Y / 32.0), v.Color >> 24);
        }
        for (var i = 0; i < indices.Count; i += 3)
            Assert.Equal(clockwise, SignedArea(vertices[(int)indices[i]], vertices[(int)indices[i + 1]], vertices[(int)indices[i + 2]]) < 0);
        AssertArea(12.5 * 10.25, new Mesh(vertices, indices));
        AssertNoOverdraw(new Mesh(vertices, indices));
    }

    [Fact]
    public void FractionalCanvasAndReflectedDpiTransformStayWithinBothMasks()
    {
        var canvas = new Rect(.25f, .5f, 48.5f, 40.25f);
        var transform = Matrix3x2.CreateScale(-1.5f, 2) * Matrix3x2.CreateTranslation(53, 1);
        var logical = new Rect(4.25f, 3.5f, 20, 12);
        var region = VulkanClipRegion.CreateCanvas(canvas).Intersect(logical, transform);
        var mesh = ClipQuad(region);
        Assert.True(Matrix3x2.Invert(transform, out var inverse));
        AssertSamples(mesh, (x, y) =>
        {
            var point = Vector2.Transform(new Vector2((float)x, (float)y), inverse);
            return InRect(canvas, x, y) && InRect(logical, point.X, point.Y);
        });
        AssertNoOverdraw(mesh);
    }

    [Fact]
    public void EmptyDisjointAndSingularStatesEmitNothing()
    {
        var root = VulkanClipRegion.CreateCanvas(Canvas);
        VulkanClipRegion[] empty =
        [
            VulkanClipRegion.CreateCanvas(Rect.Empty),
            root.Intersect(Rect.Empty, Matrix3x2.Identity),
            root.Intersect(new Rect(80, 80, 10, 10), Matrix3x2.Identity),
            root.Intersect(new Rect(2, 2, 20, 20), Matrix3x2.CreateScale(0, 1)),
            root.Intersect(new EllipseGeometry(new Point(10, 10), 0, 3), Matrix3x2.Identity),
            root.Intersect(new PathGeometry(), Matrix3x2.Identity)
        ];
        foreach (var region in empty)
        {
            Assert.True(region.Bounds.IsEmpty);
            Assert.Empty(ClipQuad(region).Indices);
        }
        Assert.False(root.HasMask);
        Assert.Equal(Canvas, root.Bounds);
    }

    [Fact]
    public void AdjacentClippedCellsShareEdgesWithoutDuplicateRasterCoverage()
    {
        Point[] polygon = [new(3.15f, 4.35f), new(51.7f, 7.1f), new(43.25f, 53.6f), new(11.4f, 43.9f), new(21.8f, 24.3f)];
        var region = VulkanClipRegion.CreateCanvas(Canvas).Intersect(Path(polygon), Matrix3x2.Identity)
            .Intersect(new Rect(6.125f, 5.375f, 43.25f, 45.75f), Matrix3x2.CreateRotation(.07f, new Vector2(25, 24)));
        var mesh = ClipQuad(region);
        AssertNoOverdraw(mesh);
        for (var i = 0; i < mesh.Indices.Count; i += 3)
        for (var edge = 0; edge < 3; edge++)
        {
            var a = mesh.Vertices[(int)mesh.Indices[i + edge]];
            var b = mesh.Vertices[(int)mesh.Indices[i + (edge + 1) % 3]];
            foreach (var t in new[] { .25, .5, .75 })
                Assert.InRange(Coverage(mesh, a.X + ((double)b.X - a.X) * t, a.Y + ((double)b.Y - a.Y) * t), 0, 1);
        }
    }

    private static Mesh ClipQuad(VulkanClipRegion region)
    {
        var vertices = new List<Vertex2D>();
        var indices = new List<uint>();
        region.Clip(Quad(Canvas), [0, 1, 2, 0, 2, 3], vertices, indices);
        return new Mesh(vertices, indices);
    }

    private static Vertex2D[] Quad(Rect rect) =>
    [
        new(rect.Left, rect.Top, 0, 0, Tint), new(rect.Right, rect.Top, 1, 0, Tint),
        new(rect.Right, rect.Bottom, 1, 1, Tint), new(rect.Left, rect.Bottom, 0, 1, Tint)
    ];

    private static Point[] RectanglePoints(Rect r) => [new(r.Left, r.Top), new(r.Right, r.Top), new(r.Right, r.Bottom), new(r.Left, r.Bottom)];
    private static PathGeometry Path(Point[] points) { var path = new PathGeometry(); AddContour(path, points); return path; }
    private static void AddContour(PathGeometry path, Point[] points)
    {
        path.MoveTo(points[0]);
        foreach (var point in points.Skip(1)) path.LineTo(point);
        path.Close();
    }
    private static bool InRect(Rect r, double x, double y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    private static bool Inside(Point[] points, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            var a = points[i];
            var b = points[j];
            if ((a.Y > y) != (b.Y > y) && x < (b.X - (double)a.X) * (y - a.Y) / (b.Y - (double)a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
    private static double SignedArea(Vertex2D a, Vertex2D b, Vertex2D c) => ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);
    private static double Area(Mesh mesh)
    {
        var sum = 0d;
        for (var i = 0; i < mesh.Indices.Count; i += 3)
            sum += Math.Abs(SignedArea(mesh.Vertices[(int)mesh.Indices[i]], mesh.Vertices[(int)mesh.Indices[i + 1]], mesh.Vertices[(int)mesh.Indices[i + 2]])) / 2;
        return sum;
    }
    private static void AssertArea(double expected, Mesh mesh) => Assert.InRange(Area(mesh), expected - 1e-4, expected + 1e-4);
    private static void AssertSamples(Mesh mesh, Func<double, double, bool> contains)
    {
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
        {
            var px = x + .271;
            var py = y + .619;
            Assert.Equal(contains(px, py) ? 1 : 0, Coverage(mesh, px, py));
        }
    }
    private static void AssertNoOverdraw(Mesh mesh)
    {
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
            Assert.InRange(Coverage(mesh, x + .375, y + .125), 0, 1);
    }
    private static int Coverage(Mesh mesh, double x, double y)
    {
        var count = 0;
        for (var i = 0; i < mesh.Indices.Count; i += 3)
        {
            var a = mesh.Vertices[(int)mesh.Indices[i]];
            var b = mesh.Vertices[(int)mesh.Indices[i + 1]];
            var c = mesh.Vertices[(int)mesh.Indices[i + 2]];
            if (SignedArea(a, b, c) < 0) (b, c) = (c, b);
            if (Edge(a, b, x, y) && Edge(b, c, x, y) && Edge(c, a, x, y)) count++;
        }
        return count;
    }
    private static bool Edge(Vertex2D a, Vertex2D b, double x, double y)
    {
        var value = ((double)b.X - a.X) * (y - a.Y) - ((double)b.Y - a.Y) * (x - a.X);
        return value > 0 || value == 0 && (b.Y < a.Y || b.Y == a.Y && b.X > a.X);
    }
    private sealed record Mesh(List<Vertex2D> Vertices, List<uint> Indices);
}
