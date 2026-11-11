using System.Numerics;
using System.Runtime.InteropServices;
using Square.Graphics;
using Square.Rendering.Tessellation;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

public sealed class VulkanGeometryCacheTests
{
    private const uint Tint = 0xA37F391B;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HitsReturnTheExactFreshMeshWithoutBuildingAgain(bool stroke)
    {
        var cache = new VulkanGeometryCache();
        var path = Path();
        var pen = new Pen(Brush.FromColor(Color.White), 3);
        var transform = new Matrix3x2(1.25f, .125f, -.25f, 1.5f, 7, -3);
        var first = Get(cache, path, transform, pen, Tint, stroke);
        AssertMesh(Fresh(path, transform, pen, Tint, stroke), first);
        var builds = cache.BuildCount;
        var hits = cache.HitCount;
        var again = Get(cache, path, transform, pen, Tint, stroke);
        AssertMesh(Capture(first), again);
        Assert.Equal(builds, cache.BuildCount);
        Assert.Equal(hits + 1, cache.HitCount);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.Bytes >= first.ByteSize);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppendingCommandsInvalidatesAndKeepsEarlierMeshesImmutable(bool stroke)
    {
        var cache = new VulkanGeometryCache();
        var path = Path();
        var pen = Pen.FromColor(Color.White, 3);
        var first = Get(cache, path, Matrix3x2.Identity, pen, Tint, stroke);
        var snapshot = Capture(first);
        var builds = cache.BuildCount;
        path.LineTo(new Point(-4, 12));
        var changed = Get(cache, path, Matrix3x2.Identity, pen, Tint, stroke);
        Assert.Equal(builds + 1, cache.BuildCount);
        AssertMesh(Fresh(path, Matrix3x2.Identity, pen, Tint, stroke), changed);
        Assert.NotEqual(snapshot.Vertices, Capture(changed).Vertices);
        AssertMesh(snapshot, first);
    }

    public static IEnumerable<object[]> CommandMutations()
    {
        for (var index = 0; index < 4; index++)
        {
            yield return [false, index];
            yield return [true, index];
        }
    }

    [Theory]
    [MemberData(nameof(CommandMutations))]
    public void SameCountBackingListReplacementInvalidatesEveryCommandKind(bool stroke, int index)
    {
        var cache = new VulkanGeometryCache();
        var path = Path();
        var pen = Pen.FromColor(Color.White, 3);
        var first = Get(cache, path, Matrix3x2.Identity, pen, Tint, stroke);
        var snapshot = Capture(first);
        var commands = Assert.IsType<List<PathCommand>>(path.Commands);
        var count = commands.Count;
        commands[index] = index switch
        {
            0 => new MoveToCmd(new Point(8, 22)),
            1 => new LineToCmd(new Point(20, 10)),
            2 => new ArcToCmd(new Rect(14, 8, 24, 24), -90, 120),
            _ => new CloseCmd()
        };
        var builds = cache.BuildCount;
        var changed = Get(cache, path, Matrix3x2.Identity, pen, Tint, stroke);
        Assert.Equal(count, commands.Count);
        Assert.Equal(builds + 1, cache.BuildCount);
        AssertMesh(Fresh(path, Matrix3x2.Identity, pen, Tint, stroke), changed);
        AssertMesh(snapshot, first);
    }

    [Fact]
    public void PenWidthCapJoinMiterDashAndOffsetMutationsInvalidateIndependently()
    {
        var cache = new VulkanGeometryCache();
        var path = new PathGeometry().MoveTo(new Point(4, 26)).LineTo(new Point(26, 26)).LineTo(new Point(14, 4));
        var style = new StrokeStyle { DashArray = [3, 2] };
        var pen = new Pen(Brush.FromColor(Color.White), 3, style);
        var previous = cache.GetStroke(path, Matrix3x2.Identity, pen, Tint);
        void Check(Action mutate)
        {
            var old = Capture(previous);
            var builds = cache.BuildCount;
            mutate();
            var next = cache.GetStroke(path, Matrix3x2.Identity, pen, Tint);
            Assert.Equal(builds + 1, cache.BuildCount);
            AssertMesh(Fresh(path, Matrix3x2.Identity, pen, Tint, true), next);
            AssertMesh(old, previous);
            previous = next;
        }
        Check(() => pen.Width = 6);
        Check(() => style.Cap = LineCap.Round);
        Check(() => style.Cap = LineCap.Square);
        Check(() => style.Join = LineJoin.Bevel);
        Check(() => style.Join = LineJoin.Round);
        Check(() => { style.Join = LineJoin.Miter; style.MiterLimit = 1; });
        Check(() => style.MiterLimit = 4);
        Check(() => style.DashArray![0] = 1.25f);
        Check(() => style.DashOffset = .75f);
        Check(() => style.DashArray = [2, 1, 3]);
        Check(() => style.DashArray![1] = 1.5f);
        var hits = cache.HitCount;
        var buildsBeforeEqualCopy = cache.BuildCount;
        style.DashArray = style.DashArray!.ToArray();
        AssertMesh(Capture(previous), cache.GetStroke(path, Matrix3x2.Identity, pen, Tint));
        Assert.Equal(buildsBeforeEqualCopy, cache.BuildCount);
        Assert.Equal(hits + 1, cache.HitCount);
        Check(() => pen.StrokeStyle = null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransformAndDpiChangesUseTheirOwnExactMeshes(bool stroke)
    {
        var cache = new VulkanGeometryCache();
        var path = Path();
        var pen = Pen.FromColor(Color.White, 2.5f);
        Matrix3x2[] transforms =
        [
            Matrix3x2.Identity,
            Matrix3x2.CreateScale(1.5f),
            Matrix3x2.CreateScale(2),
            Matrix3x2.CreateTranslation(.25f, -2.5f),
            Matrix3x2.CreateRotation(.19f, new Vector2(20, 18)),
            new(1, .125f, .25f, 1, 3, 4)
        ];
        foreach (var transform in transforms)
        {
            var builds = cache.BuildCount;
            AssertMesh(Fresh(path, transform, pen, Tint, stroke), Get(cache, path, transform, pen, Tint, stroke));
            Assert.Equal(builds + 1, cache.BuildCount);
        }
        var before = cache.BuildCount;
        foreach (var transform in transforms)
            AssertMesh(Fresh(path, transform, pen, Tint, stroke), Get(cache, path, transform, pen, Tint, stroke));
        Assert.Equal(before, cache.BuildCount);
        Assert.Equal((long)transforms.Length, cache.HitCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackedTintMutationDoesNotRecolorAnEarlierSnapshot(bool stroke)
    {
        var cache = new VulkanGeometryCache();
        var path = Path();
        var brush = new SolidColorBrush(new Color(13, 57, 191, 91));
        var pen = new Pen(brush, 3);
        var first = Get(cache, path, Matrix3x2.Identity, pen, Pack(brush.Color), stroke);
        var old = Capture(first);
        brush.Color = new Color(229, 31, 83, 173);
        var builds = cache.BuildCount;
        var next = Get(cache, path, Matrix3x2.Identity, pen, Pack(brush.Color), stroke);
        Assert.Equal(builds + 1, cache.BuildCount);
        AssertMesh(Fresh(path, Matrix3x2.Identity, pen, Pack(brush.Color), stroke), next);
        Assert.NotEqual(old.Vertices, Capture(next).Vertices);
        AssertMesh(old, first);
    }

    [Fact]
    public void FillAndStrokeOfTheSamePathNeverShareTheWrongPayload()
    {
        var cache = new VulkanGeometryCache();
        var path = Path();
        var pen = Pen.FromColor(Color.White, 3);
        AssertMesh(Fresh(path, Matrix3x2.Identity, pen, Tint, false), cache.GetFill(path, Matrix3x2.Identity, Tint));
        AssertMesh(Fresh(path, Matrix3x2.Identity, pen, Tint, true), cache.GetStroke(path, Matrix3x2.Identity, pen, Tint));
        Assert.Equal(2, cache.BuildCount);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void LruEvictsTheLeastRecentlyUsedMeshAndPreservesHeldSnapshots()
    {
        var cache = new VulkanGeometryCache(maxEntries: 2, maxBytes: 1024 * 1024);
        var a = Path(0); var b = Path(50); var c = Path(100);
        var first = cache.GetFill(a, Matrix3x2.Identity, Tint);
        var old = Capture(first);
        cache.GetFill(b, Matrix3x2.Identity, Tint);
        cache.GetFill(a, Matrix3x2.Identity, Tint); // a is now most recently used.
        cache.GetFill(c, Matrix3x2.Identity, Tint);
        Assert.Equal(2, cache.Count);
        var builds = cache.BuildCount;
        cache.GetFill(a, Matrix3x2.Identity, Tint);
        Assert.Equal(builds, cache.BuildCount);
        cache.GetFill(b, Matrix3x2.Identity, Tint);
        Assert.Equal(builds + 1, cache.BuildCount);
        Assert.Equal(2, cache.Count);
        AssertMesh(old, first);
    }

    [Fact]
    public void ByteBudgetIncludesKeysAndEvictsBeforeExceedingItsLimit()
    {
        var probe = new VulkanGeometryCache();
        var pen = Pen.FromColor(Color.White, 3);
        var mesh = probe.GetStroke(Path(), Matrix3x2.Identity, pen, Tint);
        Assert.True(probe.Bytes > mesh.ByteSize);
        var budget = probe.Bytes + 32;
        var cache = new VulkanGeometryCache(maxEntries: 100, maxBytes: budget);
        for (var i = 0; i < 12; i++)
        {
            cache.GetStroke(Path(i * 50), Matrix3x2.Identity, pen, Tint);
            Assert.InRange(cache.Bytes, 0, budget);
            Assert.InRange(cache.Count, 0, 1);
        }
    }

    [Theory]
    [InlineData(0, 1048576L)]
    [InlineData(100, 0L)]
    [InlineData(100, 1L)]
    public void DisabledOrOversizedEntriesStillReturnCorrectUncachedMeshes(int maxEntries, long maxBytes)
    {
        var cache = new VulkanGeometryCache(maxEntries, maxBytes);
        var path = Path();
        var pen = Pen.FromColor(Color.White, 3);
        var expected = Fresh(path, Matrix3x2.Identity, pen, Tint, true);
        AssertMesh(expected, cache.GetStroke(path, Matrix3x2.Identity, pen, Tint));
        AssertMesh(expected, cache.GetStroke(path, Matrix3x2.Identity, pen, Tint));
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
        Assert.Equal(2, cache.BuildCount);
        Assert.Equal(0, cache.HitCount);
    }

    [Fact]
    public void ClearDropsRetentionButNotCumulativeCountersOrEarlierMeshContents()
    {
        var cache = new VulkanGeometryCache();
        var path = Path();
        var first = cache.GetFill(path, Matrix3x2.Identity, Tint);
        var old = Capture(first);
        cache.GetFill(path, Matrix3x2.Identity, Tint);
        var builds = cache.BuildCount;
        var hits = cache.HitCount;
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
        Assert.Equal(builds, cache.BuildCount);
        Assert.Equal(hits, cache.HitCount);
        cache.GetFill(Path(150), Matrix3x2.CreateScale(2), 0xFFFFFFFF);
        Assert.Equal(builds + 1, cache.BuildCount);
        AssertMesh(old, first);
    }

    private static PathGeometry Path(float offset = 0) => new PathGeometry()
        .MoveTo(new Point(offset + 4, 24)).LineTo(new Point(offset + 24, 6))
        .ArcTo(new Rect(offset + 12, 6, 24, 24), -90, 180).LineTo(new Point(offset + 4, 36));
    private static uint Pack(Color color) => (uint)(color.R | color.G << 8 | color.B << 16 | color.A << 24);
    private static VulkanCachedMesh Get(VulkanGeometryCache cache, PathGeometry path, Matrix3x2 transform, Pen pen, uint color, bool stroke)
        => stroke ? cache.GetStroke(path, transform, pen, color) : cache.GetFill(path, transform, color);
    private static Snapshot Capture(VulkanCachedMesh mesh) => new(MemoryMarshal.AsBytes(mesh.Vertices).ToArray(), mesh.Indices.ToArray());
    private static void AssertMesh(Snapshot expected, VulkanCachedMesh actual)
    {
        Assert.Equal(expected.Vertices, MemoryMarshal.AsBytes(actual.Vertices).ToArray());
        Assert.Equal(expected.Indices, actual.Indices.ToArray());
        Assert.True(actual.ByteSize >= expected.Vertices.LongLength + expected.Indices.LongLength * sizeof(uint));
    }

    private static Snapshot Fresh(PathGeometry path, Matrix3x2 transform, Pen pen, uint color, bool stroke)
    {
        // Reproduce the pre-cache renderer through the shared tessellators, not a
        // second cache lookup; compare positions, UVs and RGBA bytes without tolerance.
        var vertices = new List<Vertex2D>();
        var indices = new List<uint>();
        var contours = PathTessellator.FlattenPath(path, transform);
        var (u0, v0, u1, v1) = VulkanTextureAtlas.WhitePixelUV;
        Point Transform(Point p) => new(p.X * transform.M11 + p.Y * transform.M21 + transform.M31,
            p.X * transform.M12 + p.Y * transform.M22 + transform.M32);
        if (stroke)
        {
            foreach (var contour in contours)
                if (contour.Count >= 2)
                    StrokeTessellator.Append(contour, pen.Width / 2, PathTessellator.GetLogicalFeatherWidth(transform),
                        pen.StrokeStyle, color, u0, v0, u1, v1, Transform, vertices, indices);
        }
        else if (contours.Count != 0)
        {
            var tess = PathTessellator.Triangulate(contours);
            for (var i = 0; i < tess.ElementCount * 3; i++)
            {
                var p = tess.Vertices[tess.Elements[i]].Position;
                var point = Transform(new Point(p.X, p.Y));
                vertices.Add(new Vertex2D(point.X, point.Y, u0, v0, color));
                indices.Add((uint)i);
            }
        }
        return new Snapshot(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(vertices)).ToArray(), indices.ToArray());
    }

    private sealed record Snapshot(byte[] Vertices, uint[] Indices);
}
