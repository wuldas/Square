using System.Numerics;
using System.Runtime.InteropServices;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Vulkan.Tests;

public sealed class VulkanBrushCacheTests
{
    [Fact]
    public void EqualContentAcrossObjectsReusesTheImmutableSnapshot()
    {
        var cache = new VulkanBrushCache();
        var first = cache.Get(Linear(Color.Red), Matrix3x2.Identity);
        var equivalent = cache.Get(Linear(Color.Red), Matrix3x2.Identity);
        Assert.Same(first, equivalent);
        Assert.Equal(1, cache.Count);
        Assert.Equal(1, cache.BuildCount);
        Assert.Equal(1, cache.HitCount);
        AssertBytesEqual(VulkanBrushMaterial.Create(Linear(Color.Red), Matrix3x2.Identity), first);
    }

    [Fact]
    public void MutableStopsAndSameCountReplacementNeverChangeAnEarlierDraw()
    {
        var cache = new VulkanBrushCache();
        var brush = Linear(Color.Red);
        var original = cache.Get(brush, Matrix3x2.Identity);
        var originalBytes = original.Stops.ToArray();
        brush.Stops[0].Color = Color.Green;
        var colorChanged = cache.Get(brush, Matrix3x2.Identity);
        Assert.NotSame(original, colorChanged);
        AssertBytesEqual(VulkanBrushMaterial.Create(brush, Matrix3x2.Identity), colorChanged);
        brush.Stops[0].Offset = 0.25f;
        Assert.NotSame(colorChanged, cache.Get(brush, Matrix3x2.Identity));
        brush.Stops[1] = new GradientStop(0.75f, Color.Blue);
        AssertBytesEqual(VulkanBrushMaterial.Create(brush, Matrix3x2.Identity), cache.Get(brush, Matrix3x2.Identity));
        brush.Stops = [new GradientStop(0, Color.Red), new GradientStop(1, Color.Black)];
        Assert.Same(original, cache.Get(brush, Matrix3x2.Identity));
        Assert.Equal(originalBytes, original.Stops.ToArray());
    }

    [Fact]
    public void CoordinatesTransformRadiusAndSpreadUseCurrentValues()
    {
        var cache = new VulkanBrushCache();
        var brush = Linear(Color.Red);
        var first = cache.Get(brush, Matrix3x2.Identity);
        brush.Start = new Point(1, 3);
        var moved = cache.Get(brush, Matrix3x2.Identity);
        Assert.NotSame(first, moved);
        brush.End = new Point(20, 30);
        Assert.NotSame(moved, cache.Get(brush, Matrix3x2.Identity));
        var transform = Matrix3x2.CreateScale(1.5f, -2) * Matrix3x2.CreateRotation(0.3f) * Matrix3x2.CreateTranslation(11, 17);
        AssertBytesEqual(VulkanBrushMaterial.Create(brush, transform), cache.Get(brush, transform));
        var radial = new RadialGradientBrush(new Point(3, 5), 6, brush.Stops);
        var before = cache.Get(radial, transform);
        radial.Radius = 12;
        Assert.NotSame(before, cache.Get(radial, transform));
        radial.Center = new Point(7, 8);
        radial.SpreadMethod = GradientSpreadMethod.Reflect;
        var reflected = cache.Get(radial, transform);
        radial.SpreadMethod = GradientSpreadMethod.Repeat;
        Assert.NotSame(reflected, cache.Get(radial, transform));
        AssertBytesEqual(VulkanBrushMaterial.Create(radial, transform), cache.Get(radial, transform));
    }

    [Fact]
    public void UnsortedDuplicateOrderAndEmptyOrDegenerateGradientsRemainExact()
    {
        var cache = new VulkanBrushCache();
        var brush = new LinearGradientBrush(Point.Zero, new Point(0, 10),
            new GradientStop(.8f, Color.Blue), new GradientStop(.4f, Color.Red), new GradientStop(.4f, Color.Green));
        var ordered = cache.Get(brush, Matrix3x2.Identity);
        (brush.Stops[1], brush.Stops[2]) = (brush.Stops[2], brush.Stops[1]);
        var reversed = cache.Get(brush, Matrix3x2.Identity);
        Assert.NotSame(ordered, reversed);
        AssertBytesEqual(VulkanBrushMaterial.Create(brush, Matrix3x2.Identity), reversed);
        brush.End = Point.Zero;
        AssertBytesEqual(VulkanBrushMaterial.Create(brush, Matrix3x2.Identity), cache.Get(brush, Matrix3x2.Identity));
        brush.Stops = [];
        AssertBytesEqual(VulkanBrushMaterial.Create(brush, Matrix3x2.CreateScale(0)), cache.Get(brush, Matrix3x2.CreateScale(0)));
        var radial = new RadialGradientBrush(new Point(1, 2), 0, new GradientStop(0, Color.Blue));
        AssertBytesEqual(VulkanBrushMaterial.Create(radial, Matrix3x2.Identity), cache.Get(radial, Matrix3x2.Identity));
    }

    [Fact]
    public void RealHashCollisionStillComparesAllStopBits()
    {
        var cache = new VulkanBrushCache(maxEntries: 2);
        var positive = new LinearGradientBrush(Point.Zero, new Point(1, 0), new GradientStop(0f, Color.Red));
        var negative = new LinearGradientBrush(Point.Zero, new Point(1, 0), new GradientStop(BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)), Color.Red));
        var a = cache.Get(positive, Matrix3x2.Identity);
        var b = cache.Get(negative, Matrix3x2.Identity);
        Assert.Equal(a.GetHashCode(), b.GetHashCode()); // Float equality hashes +0/-0 alike; GPU snapshots preserve their bits.
        Assert.NotSame(a, b);
        Assert.Equal(0, BitConverter.SingleToInt32Bits(a.Stops[0].Offset));
        Assert.Equal(unchecked((int)0x80000000), BitConverter.SingleToInt32Bits(b.Stops[0].Offset));
        cache.Get(Linear(Color.Blue), Matrix3x2.Identity); // Evict the non-head collision entry.
        Assert.Same(b, cache.Get(negative, Matrix3x2.Identity));
        Assert.NotSame(a, cache.Get(positive, Matrix3x2.Identity));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void NonfiniteValuesKeepTheirOriginalStoredBitsAndDoNotPoisonHits()
    {
        var cache = new VulkanBrushCache();
        var brush = new RadialGradientBrush(new Point(float.NaN, float.PositiveInfinity), 2,
            new GradientStop(BitConverter.Int32BitsToSingle(0x7fc00001), Color.Red),
            new GradientStop(float.NegativeInfinity, Color.Blue));
        var first = cache.Get(brush, Matrix3x2.Identity);
        AssertBytesEqual(VulkanBrushMaterial.Create(brush, Matrix3x2.Identity), first);
        Assert.Same(first, cache.Get(brush, Matrix3x2.Identity));
        brush.Stops[0].Offset = BitConverter.Int32BitsToSingle(0x7fc12345);
        var changedPayload = cache.Get(brush, Matrix3x2.Identity);
        Assert.NotSame(first, changedPayload);
        Assert.Equal(0x7fc00001, BitConverter.SingleToInt32Bits(first.Stops[0].Offset));
        Assert.Equal(0x7fc12345, BitConverter.SingleToInt32Bits(changedPayload.Stops[0].Offset));
        brush.Radius = float.PositiveInfinity;
        AssertBytesEqual(VulkanBrushMaterial.Create(brush, Matrix3x2.Identity), cache.Get(brush, Matrix3x2.Identity));
    }

    [Fact]
    public void LayerOpacityIsImmutableAndKeepsFloatPrecision()
    {
        var cache = new VulkanBrushCache();
        const float opacity = .001234567f;
        var first = cache.GetLayer(opacity);
        Assert.Same(first, cache.GetLayer(opacity));
        Assert.Equal(opacity, first.RowX.W);
        Assert.NotSame(first, cache.GetLayer(.001234568f));
        Assert.Same(cache.GetLayer(1), cache.GetLayer(float.NaN));
        Assert.Same(cache.GetLayer(1), cache.GetLayer(float.PositiveInfinity));
        Assert.Same(cache.GetLayer(0), cache.GetLayer(float.NegativeInfinity));
        AssertBytesEqual(VulkanBrushMaterial.Layer(opacity), first);
    }

    [Fact]
    public void LruEntryBudgetByteBudgetOversizeAndClearReleaseRetainedSnapshots()
    {
        var cache = new VulkanBrushCache(maxEntries: 2, maxBytes: 700);
        var red = cache.Get(Linear(Color.Red), Matrix3x2.Identity);
        var blue = cache.Get(Linear(Color.Blue), Matrix3x2.Identity);
        Assert.Same(red, cache.Get(Linear(Color.Red), Matrix3x2.Identity));
        cache.Get(Linear(Color.Green), Matrix3x2.Identity);
        Assert.Same(red, cache.Get(Linear(Color.Red), Matrix3x2.Identity));
        Assert.NotSame(blue, cache.Get(Linear(Color.Blue), Matrix3x2.Identity));
        Assert.Equal(2, cache.Count);
        Assert.InRange(cache.Bytes, 1, 700);
        var oversized = new LinearGradientBrush(Point.Zero, new Point(0, 10),
            Enumerable.Range(0, 100).Select(i => new GradientStop(i / 99f, Color.Red)).ToArray());
        var firstLarge = cache.Get(oversized, Matrix3x2.Identity);
        Assert.NotSame(firstLarge, cache.Get(oversized, Matrix3x2.Identity));
        Assert.Equal(2, cache.Count);
        Assert.InRange(cache.Bytes, 1, 700);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
        Assert.NotSame(red, cache.Get(Linear(Color.Red), Matrix3x2.Identity));
        var disabled = new VulkanBrushCache(0, 0);
        Assert.NotSame(disabled.GetLayer(.5f), disabled.GetLayer(.5f));
        Assert.Equal(0, disabled.Count);
        Assert.Equal(0, disabled.Bytes);
    }

    [Fact]
    public void WarmContentHitsAllocateNoStopArraysOrMaterials()
    {
        var cache = new VulkanBrushCache();
        var first = Linear(Color.Red);
        var second = Linear(Color.Red);
        for (var i = 0; i < 256; i++) { cache.Get(first, Matrix3x2.Identity); cache.GetLayer(.5f); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            cache.Get((i & 1) == 0 ? first : second, Matrix3x2.Identity);
            cache.GetLayer(.5f);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, bytes);
        Assert.Equal(2, cache.BuildCount);
    }

    private static LinearGradientBrush Linear(Color color) => new(Point.Zero, new Point(0, 10), new GradientStop(0, color), new GradientStop(1, Color.Black));
    private static void AssertBytesEqual(VulkanBrushMaterial expected, VulkanBrushMaterial actual)
    {
        var a = expected.ToPushConstants(17, new Vector2(3, 7));
        var b = actual.ToPushConstants(17, new Vector2(3, 7));
        Assert.True(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref a, 1)).SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref b, 1))));
        Assert.True(MemoryMarshal.AsBytes(expected.Stops).SequenceEqual(MemoryMarshal.AsBytes(actual.Stops)));
    }
}
