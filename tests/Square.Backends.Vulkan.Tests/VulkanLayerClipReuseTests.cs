using System.Numerics;
using System.Runtime.InteropServices;
using Square.Graphics;
using Square.Rendering.Tessellation;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

public sealed class VulkanLayerClipReuseTests
{
    private static readonly uint[] Indices = [0, 1, 2, 0, 2, 3];
    private static readonly Vertex2D[] Quad = [new(0, 0, 0, 0, 0xA05090D0), new(96, 0, 1, 0, 0xA05090D0),
        new(96, 96, 1, 1, 0xA05090D0), new(0, 96, 0, 1, 0xA05090D0)];

    [Fact]
    public void StableLayerRootsReuseClipAndClippedMeshAcrossFrames()
    {
        var cache = new VulkanClipCache();
        var bounds = new Rect(8, 12, 64, 64);
        var path = Triangle(false);
        var root = cache.GetCanvasClip(bounds);
        var clip = cache.GetClip(root, path, Matrix3x2.CreateTranslation(12, 16));
        var mesh = cache.Clip(clip, Quad, Indices);
        var builds = cache.BuildCount;
        for (var frame = 0; frame < 10; frame++)
        {
            var nextRoot = cache.GetCanvasClip(new Rect(8, 12, 64, 64));
            var nextClip = cache.GetClip(nextRoot, path, Matrix3x2.CreateTranslation(12, 16));
            Assert.Same(root, nextRoot);
            Assert.Same(clip, nextClip);
            Assert.Same(mesh, cache.Clip(nextClip, Quad, Indices));
        }
        Assert.Equal(builds, cache.BuildCount);
        Assert.Equal(30, cache.HitCount);
        Assert.True(Fresh(clip).Matches(mesh.Vertices, mesh.Indices));
    }

    [Fact]
    public void EvictedRootLeavesAnExistingChildSafeAndAccounted()
    {
        var cache = new VulkanClipCache(maxClipEntries: 2);
        var bounds = new Rect(0, 0, 96, 96);
        var root = cache.GetCanvasClip(bounds);
        var path = Triangle(false);
        var child = cache.GetClip(root, path, Matrix3x2.Identity);
        var saved = Fresh(child);
        cache.GetCanvasClip(new Rect(4, 4, 64, 64)); // Evicts root, not child.
        Assert.Same(child, cache.GetClip(root, path, Matrix3x2.Identity));
        var replacement = cache.GetCanvasClip(bounds); // Evicts the other canvas.
        Assert.NotSame(root, replacement);
        Assert.Same(child, cache.GetClip(root, path, Matrix3x2.Identity));
        Assert.True(saved.Matches(Fresh(child).Vertices, Fresh(child).Indices));
        Assert.True(cache.ClipBytes >= root.EstimatedBytes + child.EstimatedBytes + replacement.EstimatedBytes);
    }

    [Fact]
    public void CanvasRootsShareClipBudgetAndClear()
    {
        const long budget = 1500;
        var cache = new VulkanClipCache(maxClipEntries: 2, maxClipBytes: budget);
        var a = cache.GetCanvasClip(new Rect(0, 0, 64, 64));
        var b = cache.GetCanvasClip(new Rect(4, 4, 64, 64));
        Assert.Same(a, cache.GetCanvasClip(a.Bounds));
        cache.GetCanvasClip(new Rect(8, 8, 64, 64));
        Assert.Same(a, cache.GetCanvasClip(a.Bounds));
        Assert.NotSame(b, cache.GetCanvasClip(b.Bounds));
        Assert.InRange(cache.ClipCount, 0, 2);
        Assert.InRange(cache.ClipBytes, 0, budget);
        var builds = cache.BuildCount;
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
        Assert.Equal(builds, cache.BuildCount);
        var disabled = new VulkanClipCache(maxClipBytes: 1);
        Assert.NotSame(disabled.GetCanvasClip(a.Bounds), disabled.GetCanvasClip(a.Bounds));
        Assert.Equal(0, disabled.Count);
        // A fractional canvas is a geometric mask and never interned as a no-mask root.
        var fractional = new Rect(.25f, .5f, 64, 64);
        Assert.True(cache.GetCanvasClip(fractional).HasMask);
        Assert.NotSame(cache.GetCanvasClip(fractional), cache.GetCanvasClip(fractional));
    }

    [Fact]
    public void EqualBoundsMaskedParentsRemainDistinct()
    {
        var cache = new VulkanClipCache();
        var canvas = cache.GetCanvasClip(new Rect(0, 0, 96, 96));
        var a = canvas.Intersect(Triangle(false), Matrix3x2.Identity);
        var b = canvas.Intersect(Triangle(true), Matrix3x2.Identity);
        Assert.Equal(a.Bounds, b.Bounds);
        var first = cache.GetClip(a, new Rect(0, 0, 32, 32), Matrix3x2.Identity);
        var second = cache.GetClip(b, new Rect(0, 0, 32, 32), Matrix3x2.Identity);
        Assert.NotSame(first, second);
        var meshA = cache.Clip(first, Quad, Indices);
        var meshB = cache.Clip(second, Quad, Indices);
        Assert.False(meshA.Matches(meshB.Vertices, meshB.Indices));
        Assert.True(Fresh(first).Matches(meshA.Vertices, meshA.Indices));
        Assert.True(Fresh(second).Matches(meshB.Vertices, meshB.Indices));
    }

    [Fact]
    public void NestedLayerRootsDoNotCaptureTheParentMask()
    {
        var cache = new VulkanClipCache();
        var canvas = cache.GetCanvasClip(new Rect(0, 0, 96, 96));
        var parentA = canvas.Intersect(Triangle(false), Matrix3x2.Identity);
        var parentB = canvas.Intersect(Triangle(true), Matrix3x2.Identity);
        // PushLayer creates a fresh isolated canvas from bounds; its parent's mask is
        // applied only on PopLayer. Equal bounds may safely share the isolated canvas.
        var layerA = cache.GetCanvasClip(parentA.Bounds);
        var layerB = cache.GetCanvasClip(parentB.Bounds);
        Assert.Same(layerA, layerB);
        Assert.False(layerA.HasMask);
        var nested = cache.GetClip(layerA, new Rect(4.25f, 4.5f, 24, 24), Matrix3x2.Identity);
        Assert.Same(nested, cache.GetClip(layerB, new Rect(4.25f, 4.5f, 24, 24), Matrix3x2.Identity));
        var compositeA = cache.Clip(parentA.Intersect(layerA.Bounds, Matrix3x2.Identity), Quad, Indices);
        var compositeB = cache.Clip(parentB.Intersect(layerB.Bounds, Matrix3x2.Identity), Quad, Indices);
        Assert.False(compositeA.Matches(compositeB.Vertices, compositeB.Indices));
    }

    [Fact]
    public void FractionalLayerCompositeReusesOnlyItsExactParent()
    {
        var cache = new VulkanClipCache();
        var root = cache.GetCanvasClip(new Rect(0, 0, 96, 96));
        var bounds = new Rect(4.25f, 5.5f, 24, 24);
        var composite = cache.GetClip(root, bounds, Matrix3x2.Identity);
        Assert.True(composite.HasMask);
        Assert.Same(composite, cache.GetClip(root, bounds, Matrix3x2.Identity));
        var parent = root.Intersect(Triangle(false), Matrix3x2.Identity);
        var masked = cache.GetClip(parent, bounds, Matrix3x2.Identity);
        Assert.NotSame(composite, masked);
        Assert.True(Fresh(root.Intersect(bounds, Matrix3x2.Identity)).Matches(Fresh(composite).Vertices, Fresh(composite).Indices));
        Assert.True(Fresh(parent.Intersect(bounds, Matrix3x2.Identity)).Matches(Fresh(masked).Vertices, Fresh(masked).Indices));
    }

    [Fact]
    public void MutableCommandsAndNonCommutingTransformsInvalidate()
    {
        var cache = new VulkanClipCache();
        var root = cache.GetCanvasClip(new Rect(0, 0, 96, 96));
        var path = Triangle(false);
        var translation = Matrix3x2.CreateTranslation(8, 12);
        var scale = Matrix3x2.CreateScale(1.5f, .75f);
        var a = cache.GetClip(root, path, translation * scale);
        var b = cache.GetClip(root, path, scale * translation);
        Assert.NotSame(a, b);
        Assert.True(Fresh(root.Intersect(path, translation * scale)).Matches(Fresh(a).Vertices, Fresh(a).Indices));
        ((List<PathCommand>)path.Commands)[1] = new LineToCmd(new Point(28, 4));
        var changed = cache.GetClip(root, path, translation * scale);
        Assert.NotSame(a, changed);
        Assert.True(Fresh(root.Intersect(path, translation * scale)).Matches(Fresh(changed).Vertices, Fresh(changed).Indices));
        path.MoveTo(new Point(40, 40)).LineTo(new Point(52, 40)).LineTo(new Point(40, 52)).Close();
        Assert.NotSame(changed, cache.GetClip(root, path, translation * scale));
    }

    private static PathGeometry Triangle(bool inverse) => inverse
        ? new PathGeometry().MoveTo(new Point(32, 32)).LineTo(new Point(0, 32)).LineTo(new Point(32, 0)).Close()
        : new PathGeometry().MoveTo(new Point(0, 0)).LineTo(new Point(32, 0)).LineTo(new Point(0, 32)).Close();

    private static VulkanCachedMesh Fresh(VulkanClipRegion clip)
    {
        var vertices = new List<Vertex2D>();
        var indices = new List<uint>();
        clip.Clip(Quad, Indices, vertices, indices);
        return new VulkanCachedMesh(CollectionsMarshal.AsSpan(vertices), CollectionsMarshal.AsSpan(indices));
    }
}
