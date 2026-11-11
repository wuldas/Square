using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Square.Graphics;
using Square.Rendering.Tessellation;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

public sealed class VulkanPaintIndependentGeometryTests
{
    [Fact]
    public void ChangingFillColorsReusesGeometryAndOnlyTintsPrivateCopies()
    {
        var path = Shape();
        var transform = Matrix3x2.CreateScale(1.25f, .75f) * Matrix3x2.CreateTranslation(3, 5);
        var cache = new VulkanGeometryCache();
        var white = cache.GetFillGeometry(path, transform);
        var saved = new VulkanCachedMesh(white.Vertices, white.Indices);
        uint[] colors = [0, 0x01020304, 0x80402010, 0xFE90A0B0, uint.MaxValue];
        foreach (var color in colors)
        {
            var again = cache.GetFillGeometry(path, transform);
            Assert.Same(white, again);
            var privateCopy = again.Vertices.ToArray();
            VulkanBatchRenderer.ApplyFillColor(privateCopy, color);
            var legacy = new VulkanGeometryCache().GetFill(path, transform, color);
            Assert.True(legacy.Matches(privateCopy, again.Indices));
            Assert.True(saved.Matches(white.Vertices, white.Indices));
        }
        Assert.Equal(1, cache.BuildCount);
        Assert.Equal(5, cache.HitCount);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ClippingWhiteThenTintingMatchesLegacyColoredClippingByteForByte()
    {
        var path = Shape();
        var transform = new Matrix3x2(1, .125f, -.25f, 1, 7, 5);
        var white = new VulkanGeometryCache().GetFillGeometry(path, transform);
        var clip = VulkanClipRegion.CreateCanvas(new Rect(0, 0, 96, 96))
            .Intersect(new EllipseGeometry(new Point(30, 30), 24, 18), Matrix3x2.CreateRotation(.1f));
        var clipCache = new VulkanClipCache();
        var clippedWhite = clipCache.Clip(clip, white.Vertices, white.Indices);
        var original = new VulkanCachedMesh(clippedWhite.Vertices, clippedWhite.Indices);
        foreach (var color in new uint[] { 0, 0x010000FF, 0x80402010, 0xFEDCB123, uint.MaxValue })
        {
            var colored = new VulkanGeometryCache().GetFill(path, transform, color);
            var legacy = FreshClip(clip, colored);
            Assert.Same(clippedWhite, clipCache.Clip(clip, white.Vertices, white.Indices));
            var appended = clippedWhite.Vertices.ToArray();
            VulkanBatchRenderer.ApplyFillColor(appended, color);
            Assert.True(legacy.Matches(appended, clippedWhite.Indices));
            Assert.True(original.Matches(clippedWhite.Vertices, clippedWhite.Indices));
        }
        Assert.Equal(1, clipCache.MeshBuildCount);
    }

    [Fact]
    public void TintTouchesOnlyTheNewAppendRange()
    {
        var white = new VulkanGeometryCache().GetFillGeometry(Shape(), Matrix3x2.Identity);
        var buffer = new Vertex2D[white.Vertices.Length + 2];
        buffer[0] = new Vertex2D(0, 0, 0, 0, 0x12345678);
        buffer[^1] = new Vertex2D(0, 0, 0, 0, 0x87654321);
        white.Vertices.CopyTo(buffer.AsSpan(1));
        VulkanBatchRenderer.ApplyFillColor(buffer.AsSpan(1, white.Vertices.Length), 0x80402010);
        Assert.Equal(0x12345678u, buffer[0].Color);
        Assert.Equal(0x87654321u, buffer[^1].Color);
        foreach (var vertex in white.Vertices) Assert.Equal(uint.MaxValue, vertex.Color);
    }

    [Fact]
    public void CommandsAndNonCommutingTransformsStillInvalidateWhileStrokeKeepsFeather()
    {
        var cache = new VulkanGeometryCache();
        var path = Shape();
        var a = Matrix3x2.CreateTranslation(4, 7) * Matrix3x2.CreateScale(1.5f, .75f);
        var b = Matrix3x2.CreateScale(1.5f, .75f) * Matrix3x2.CreateTranslation(4, 7);
        var first = cache.GetFillGeometry(path, a);
        Assert.NotSame(first, cache.GetFillGeometry(path, b));
        ((List<PathCommand>)path.Commands)[1] = new LineToCmd(new Point(44, 6));
        var changed = cache.GetFillGeometry(path, a);
        Assert.NotSame(first, changed);
        path.LineTo(new Point(5, 22));
        Assert.NotSame(changed, cache.GetFillGeometry(path, a));
        var pen = Pen.FromColor(Color.White, 3);
        var stroke = cache.GetStroke(path, a, pen, 0x80402010);
        var builds = cache.BuildCount;
        Assert.NotSame(stroke, cache.GetStroke(path, a, pen, 0x70402010));
        Assert.Equal(builds + 1, cache.BuildCount);
        Assert.Contains(stroke.Vertices.ToArray(), vertex => (vertex.Color >> 24) == 0);
    }

    [Fact]
    public void PaintIndependentFillsUseTheExistingEntryAndByteBudget()
    {
        var probe = new VulkanGeometryCache();
        probe.GetFillGeometry(Shape(), Matrix3x2.Identity);
        var budget = probe.Bytes + 32;
        var cache = new VulkanGeometryCache(maxEntries: 100, maxBytes: budget);
        for (var index = 0; index < 20; index++)
        {
            cache.GetFillGeometry(Shape(index * 60), Matrix3x2.Identity);
            Assert.InRange(cache.Bytes, 0, budget);
            Assert.InRange(cache.Count, 0, 1);
        }
        cache.Clear();
        Assert.Equal(0, cache.Count); Assert.Equal(0, cache.Bytes);
        var disabled = new VulkanGeometryCache(maxBytes: 1);
        disabled.GetFillGeometry(Shape(), Matrix3x2.Identity);
        Assert.Equal(0, disabled.Count); Assert.Equal(0, disabled.Bytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait("Category", "RealVulkanGpu")]
    public void CachedColoredPathsMatchDirectRectanglesWithClipsLayersAndOrderedAlpha(int samples)
    {
        if (!VulkanOptimizationGpuScope.Enabled) return;
        using var environment = new VulkanOptimizationGpuScope(samples);
        using var fixture = new VulkanOptimizationGpuFixture();
        var context = fixture.Context;
        Assert.Equal((uint)samples, context.ColorSampleCount);
        var rects = new[] { new Rect(8, 8, 60, 40), new Rect(32, 24, 60, 40), new Rect(8, 78, 56, 48), new Rect(82, 78, 48, 48) };
        var paths = rects.Select(Rectangle).ToArray();
        var gradient = new LinearGradientBrush(new Point(82, 78), new Point(130, 126),
            new GradientStop(0, Color.Red), new GradientStop(1, Color.Blue));
        var cache = (VulkanGeometryCache)typeof(VulkanRenderContext).GetField("_geometryCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        var clips = (VulkanClipCache)typeof(VulkanRenderContext).GetField("_clipCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        Bitmap Draw(bool cached, int phase)
        {
            return fixture.Draw(() =>
            {
                context.PushTransform(Matrix3x2.CreateScale(1, .875f) * Matrix3x2.CreateTranslation(2, 3));
                void Fill(int index, Brush brush)
                {
                    if (cached) context.FillPath(paths[index], brush);
                    else context.FillRect(rects[index], brush);
                }
                var alpha = new byte[] { 0, 128, 255, 1 };
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    Fill(0, Brush.FromColor(new Color((byte)(40 + phase * 20), 120, 220, alpha[(phase + repeat) % alpha.Length])));
                    Fill(1, Brush.FromColor(new Color(210, (byte)(50 + phase * 20), 60, 160)));
                }
                context.PushClip(new EllipseGeometry(new Point(36, 100), 28, 22));
                Fill(2, Brush.FromColor(new Color(40, 220, 90, 128)));
                context.PopClip();
                context.PushLayer(new Rect(78, 74, 56, 56), .6f);
                Fill(3, gradient); Fill(3, Brush.FromColor(new Color(200, 180, 30, 80)));
                context.PopLayer();
                context.PopTransform();
            });
        }
        using (var reference = Draw(false, 0))
        using (var first = Draw(true, 0)) Assert.Equal(reference.Pixels, first.Pixels);
        var builds = cache.BuildCount;
        for (var phase = 1; phase < 4; phase++)
        {
            gradient.Stops[0].Color = new Color(30, (byte)(80 + phase * 20), 190, 255);
            using var reference = Draw(false, phase);
            var clippedBuilds = clips.MeshBuildCount;
            using var warm = Draw(true, phase);
            Assert.Equal(reference.Pixels, warm.Pixels);
            Assert.Equal(builds, cache.BuildCount);
            Assert.Equal(clippedBuilds, clips.MeshBuildCount);
        }
    }

    private static PathGeometry Shape(float offset = 0) => new PathGeometry().MoveTo(new Point(offset + 4, 4))
        .LineTo(new Point(offset + 52, 4)).LineTo(new Point(offset + 52, 18)).LineTo(new Point(offset + 24, 18))
        .LineTo(new Point(offset + 24, 48)).LineTo(new Point(offset + 4, 48)).Close();
    private static PathGeometry Rectangle(Rect rect) => new PathGeometry().MoveTo(new Point(rect.Left, rect.Top))
        .LineTo(new Point(rect.Right, rect.Top)).LineTo(new Point(rect.Right, rect.Bottom)).LineTo(new Point(rect.Left, rect.Bottom)).Close();
    private static VulkanCachedMesh FreshClip(VulkanClipRegion clip, VulkanCachedMesh mesh)
    {
        var vertices = new List<Vertex2D>(); var indices = new List<uint>();
        clip.Clip(mesh.Vertices, mesh.Indices, vertices, indices);
        return new VulkanCachedMesh(CollectionsMarshal.AsSpan(vertices), CollectionsMarshal.AsSpan(indices));
    }
}
