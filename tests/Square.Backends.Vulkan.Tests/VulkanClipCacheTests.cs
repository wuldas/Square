using System.Numerics;
using System.Runtime.InteropServices;
using Square.Graphics;
using Square.Rendering.Tessellation;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

public sealed class VulkanClipCacheTests
{
    private static readonly Rect Canvas = new(0, 0, 64, 64);
    private static readonly uint[] Indices = [0, 1, 2, 0, 2, 3];

    [Fact]
    public void MeshOwnsSnapshotsAndComparesRawVertexBits()
    {
        var vertices = Quad();
        vertices[0].X = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00001));
        vertices[0].U = -0f;
        var indices = Indices.ToArray();
        var mesh = new VulkanCachedMesh(vertices, indices);
        Assert.True(mesh.Matches(vertices, indices));
        vertices[0].X = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00002));
        Assert.False(mesh.Matches(vertices, indices));
        vertices[0] = mesh.Vertices[0];
        vertices[0].U = 0f;
        Assert.False(mesh.Matches(vertices, indices));
        vertices[0] = mesh.Vertices[0];
        indices[0] = 2;
        Assert.False(mesh.Matches(vertices, indices));
        Assert.Equal(0u, mesh.Indices[0]);
        Assert.True(mesh.ByteSize >= vertices.Length * 20L + indices.Length * 4L);
    }

    [Fact]
    public void IdenticalShapeValuesHitWithoutKeepingMutableGeometry()
    {
        var cache = new VulkanClipCache();
        var parent = VulkanClipRegion.CreateCanvas(Canvas);
        var a = Path(new(2, 2), new(44, 2), new(44, 12), new(12, 12), new(12, 44), new(2, 44));
        var b = Path(new(2, 2), new(44, 2), new(44, 12), new(12, 12), new(12, 44), new(2, 44));
        var first = cache.GetClip(parent, a, Matrix3x2.Identity);
        Assert.Same(first, cache.GetClip(parent, b, Matrix3x2.Identity));
        Assert.Equal(1, cache.ClipBuildCount);
        Assert.Equal(1, cache.ClipHitCount);
        Assert.True(Fresh(parent.Intersect(a, Matrix3x2.Identity)).Matches(Fresh(first).Vertices, Fresh(first).Indices));
    }

    [Fact]
    public void SameCountPathReplacementAndAppendCannotReuseAStaleClip()
    {
        var cache = new VulkanClipCache();
        var parent = VulkanClipRegion.CreateCanvas(Canvas);
        var path = Path(new(2, 2), new(30, 2), new(30, 30));
        var first = cache.GetClip(parent, path, Matrix3x2.Identity);
        var saved = Fresh(first);
        ((List<PathCommand>)path.Commands)[1] = new LineToCmd(new Point(2, 30));
        var second = cache.GetClip(parent, path, Matrix3x2.Identity);
        Assert.NotSame(first, second);
        Assert.False(saved.Matches(Fresh(second).Vertices, Fresh(second).Indices));
        Assert.True(saved.Matches(Fresh(first).Vertices, Fresh(first).Indices));
        path.MoveTo(new Point(40, 40)).LineTo(new Point(60, 40)).LineTo(new Point(40, 60)).Close();
        var third = cache.GetClip(parent, path, Matrix3x2.Identity);
        Assert.NotSame(second, third);
        Assert.True(Fresh(parent.Intersect(path, Matrix3x2.Identity)).Matches(Fresh(third).Vertices, Fresh(third).Indices));
        Assert.Equal(3, cache.ClipBuildCount);
    }

    [Fact]
    public void ParentIdentitySeparatesMasksEvenWhenBoundsAreEqual()
    {
        var cache = new VulkanClipCache();
        var root = VulkanClipRegion.CreateCanvas(Canvas);
        var a = root.Intersect(Path(new(0, 0), new(32, 0), new(32, 8), new(8, 8), new(8, 32), new(0, 32)), Matrix3x2.Identity);
        var b = root.Intersect(Path(new(0, 0), new(32, 0), new(32, 32), new(24, 32), new(24, 8), new(0, 8)), Matrix3x2.Identity);
        Assert.Equal(a.Bounds, b.Bounds);
        var first = cache.GetClip(a, Canvas, Matrix3x2.Identity);
        var second = cache.GetClip(b, Canvas, Matrix3x2.Identity);
        Assert.NotSame(first, second);
        Assert.False(Fresh(first).Matches(Fresh(second).Vertices, Fresh(second).Indices));
        Assert.Equal(2, cache.ClipBuildCount);
    }

    [Fact]
    public void EllipseRoundedCornersAndEveryTransformFieldInvalidate()
    {
        var cache = new VulkanClipCache();
        var root = VulkanClipRegion.CreateCanvas(Canvas);
        var ellipse = new EllipseGeometry(new Point(24, 24), 12, 8);
        var a = cache.GetClip(root, ellipse, Matrix3x2.Identity);
        ellipse.RadiusY = 9;
        var b = cache.GetClip(root, ellipse, Matrix3x2.Identity);
        Assert.NotSame(a, b);
        ellipse.Center = new Point(25, 24);
        Assert.NotSame(b, cache.GetClip(root, ellipse, Matrix3x2.Identity));
        var rounded = new RoundedRectGeometry(new Rect(4, 4, 40, 40), 8, 8);
        var c = cache.GetClip(root, rounded, Matrix3x2.Identity);
        rounded.TopRight = new CornerRadius(2, 3);
        var d = cache.GetClip(root, rounded, Matrix3x2.Identity);
        Assert.NotSame(c, d);
        rounded.RadiusX = 5; // Also snapshot mutable fields unused by today's ToPath implementation.
        Assert.NotSame(d, cache.GetClip(root, rounded, Matrix3x2.Identity));
        var rectangle = new Rect(1, 2, 12, 15);
        Matrix3x2[] transforms = [Matrix3x2.Identity, new(2, 0, 0, 1, 0, 0), new(1, .1f, 0, 1, 0, 0),
            new(1, 0, .1f, 1, 0, 0), new(1, 0, 0, 2, 0, 0), new(1, 0, 0, 1, 2, 0), new(1, 0, 0, 1, 0, 2)];
        var regions = transforms.Select(matrix => cache.GetClip(root, rectangle, matrix)).ToArray();
        Assert.Equal(regions.Length, regions.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public void MeshHitsAndMutationsPreserveFreshColorUvAndIndexBytes()
    {
        var cache = new VulkanClipCache();
        var clip = cache.GetClip(VulkanClipRegion.CreateCanvas(Canvas), new Rect(3.25f, 4.5f, 31, 26), Matrix3x2.Identity);
        var vertices = Quad();
        var first = cache.Clip(clip, vertices, Indices);
        Assert.Same(first, cache.Clip(clip, vertices.ToArray(), Indices.ToArray()));
        var saved = new VulkanCachedMesh(first.Vertices, first.Indices);
        vertices[0].U = .125f;
        vertices[1].Color ^= 0x100u;
        var second = cache.Clip(clip, vertices, Indices);
        Assert.NotSame(first, second);
        Assert.True(Fresh(clip, vertices).Matches(second.Vertices, second.Indices));
        Assert.True(saved.Matches(first.Vertices, first.Indices));
        var indices = Indices.ToArray();
        (indices[0], indices[1]) = (indices[1], indices[0]);
        var third = cache.Clip(clip, vertices, indices);
        Assert.NotSame(second, third);
        Assert.True(Fresh(clip, vertices, indices).Matches(third.Vertices, third.Indices));
        Assert.Equal(3, cache.MeshBuildCount);
        Assert.Equal(1, cache.MeshHitCount);
    }

    [Fact]
    public void ClipLruKeepsRecentlyTouchedEntry()
    {
        var cache = new VulkanClipCache(maxClipEntries: 2);
        var root = VulkanClipRegion.CreateCanvas(Canvas);
        var a = new Rect(1, 1, 10, 10);
        var b = new Rect(2, 2, 10, 10);
        var first = cache.GetClip(root, a, Matrix3x2.Identity);
        var evicted = cache.GetClip(root, b, Matrix3x2.Identity);
        Assert.Same(first, cache.GetClip(root, a, Matrix3x2.Identity));
        cache.GetClip(root, new Rect(3, 3, 10, 10), Matrix3x2.Identity);
        Assert.Same(first, cache.GetClip(root, a, Matrix3x2.Identity));
        Assert.NotSame(evicted, cache.GetClip(root, b, Matrix3x2.Identity));
        Assert.Equal(2, cache.ClipCount);
    }

    [Fact]
    public void MeshLruKeepsRecentlyTouchedEntry()
    {
        var cache = new VulkanClipCache(maxMeshEntries: 2, maxMeshBytes: 4096);
        var root = VulkanClipRegion.CreateCanvas(Canvas);
        var a = Quad();
        var b = Quad(); b[0].Color++;
        var c = Quad(); c[0].Color += 2;
        var first = cache.Clip(root, a, Indices);
        var evicted = cache.Clip(root, b, Indices);
        Assert.Same(first, cache.Clip(root, a, Indices));
        cache.Clip(root, c, Indices);
        Assert.Same(first, cache.Clip(root, a, Indices));
        Assert.NotSame(evicted, cache.Clip(root, b, Indices));
        Assert.Equal(2, cache.MeshCount);
    }

    [Fact]
    public void OversizedItemsAreBuiltButNotRetained()
    {
        var cache = new VulkanClipCache(maxClipBytes: 1, maxMeshBytes: 1);
        var root = VulkanClipRegion.CreateCanvas(Canvas);
        var shape = new EllipseGeometry(new Point(20, 20), 15, 10);
        var clip = cache.GetClip(root, shape, Matrix3x2.Identity);
        var result = cache.Clip(clip, Quad(), Indices);
        Assert.True(Fresh(clip).Matches(result.Vertices, result.Indices));
        cache.GetClip(root, shape, Matrix3x2.Identity);
        cache.Clip(clip, Quad(), Indices);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
        Assert.Equal(4, cache.BuildCount);
        Assert.Equal(0, cache.HitCount);
    }

    [Fact]
    public void BudgetAccountsForParentsRetainedAfterTheirEntriesAreEvicted()
    {
        const long clipBudget = 16 * 1024;
        const long meshBudget = 32 * 1024;
        var cache = new VulkanClipCache(maxClipEntries: 1, maxClipBytes: clipBudget, maxMeshBytes: meshBudget);
        var root = VulkanClipRegion.CreateCanvas(Canvas);
        var parent = cache.GetClip(root, new EllipseGeometry(new Point(30, 30), 25, 22), Matrix3x2.Identity);
        var child = cache.GetClip(parent, new Rect(10, 10, 40, 40), Matrix3x2.Identity);
        Assert.Equal(1, cache.ClipCount);
        Assert.True(cache.ClipBytes >= parent.EstimatedBytes + child.EstimatedBytes);
        cache.Clip(child, Quad(), Indices);
        Assert.InRange(cache.ClipBytes, 1, clipBudget);
        Assert.InRange(cache.MeshBytes, 1, meshBudget);
        for (var i = 0; i < 30; i++)
        {
            cache.GetClip(child, new Rect(10+i*.125f, 10, 30, 30), Matrix3x2.Identity);
            Assert.InRange(cache.ClipBytes, 0, clipBudget);
            Assert.InRange(cache.MeshBytes, 0, meshBudget);
        }
    }

    [Fact]
    public void ClearReleasesEverythingAndKeepsCumulativeCounters()
    {
        var cache = new VulkanClipCache();
        var root = VulkanClipRegion.CreateCanvas(Canvas);
        var region = cache.GetClip(root, new Rect(1, 1, 20, 20), Matrix3x2.Identity);
        cache.Clip(region, Quad(), Indices);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
        Assert.Equal(2, cache.BuildCount);
        Assert.Equal(0, cache.HitCount);
        Assert.NotSame(region, cache.GetClip(root, new Rect(1, 1, 20, 20), Matrix3x2.Identity));
    }

    private static VulkanCachedMesh Fresh(VulkanClipRegion clip, Vertex2D[]? vertices = null, uint[]? indices = null)
    {
        var output = new List<Vertex2D>();
        var triangles = new List<uint>();
        clip.Clip(vertices ?? Quad(), indices ?? Indices, output, triangles);
        return new VulkanCachedMesh(CollectionsMarshal.AsSpan(output), CollectionsMarshal.AsSpan(triangles));
    }

    private static Vertex2D[] Quad() => [new(0, 0, 0, 0, 0x80402010), new(64, 0, 1, 0, 0x90703020),
        new(64, 64, 1, 1, 0xa0905030), new(0, 64, 0, 1, 0xb0c07040)];

    private static PathGeometry Path(params Point[] points)
    {
        var path = new PathGeometry().MoveTo(points[0]);
        foreach (var point in points.Skip(1)) path.LineTo(point);
        return path.Close();
    }
}
