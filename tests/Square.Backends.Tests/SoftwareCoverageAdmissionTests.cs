using System.Numerics;
using System.Runtime.CompilerServices;
using Square.Backends;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Tests;

public sealed class SoftwareCoverageAdmissionTests
{
    private static SoftwareCoverageState State => new(Matrix3x2.Identity, 0, true, 40, 36, false, Rect.Empty);
    private static PathGeometry Path() => new PathGeometry().MoveTo(new Point(3, 3))
        .LineTo(new Point(30, 3)).LineTo(new Point(3, 30)).Close();
    private static SoftwareCoverageMask Mask() => new([new SoftwareCoveragePixel(4, 4, 16)]);

    [Fact]
    public void FirstUseDoesNotBuildMaskSecondRecordsThirdHitsAndPaintRemainsIndependent()
    {
        using var context = new RenderContext(new Bitmap(40, 36), 1f);
        var path = Path();
        context.FillPath(path, new SolidColorBrush(new Color(10, 30, 70, 128)));
        Assert.Equal(0, context.CoverageBuildCount);
        Assert.Equal(0, context.CoverageCacheCount);
        Assert.Equal(1, context.CoverageProbationCount);
        Assert.Equal((byte)128, context.GetBitmap().GetPixel(5, 5)[3]);
        context.FillPath(path, new SolidColorBrush(new Color(80, 90, 110, 128)));
        Assert.Equal(1, context.CoverageBuildCount);
        Assert.Equal(1, context.CoverageCacheCount);
        Assert.Equal(0, context.CoverageProbationCount);
        Assert.Equal(0, context.CoverageHitCount);
        Assert.Equal((byte)191, context.GetBitmap().GetPixel(5, 5)[3]);
        context.FillPath(path, new SolidColorBrush(new Color(190, 130, 20, 128)));
        Assert.Equal(1, context.CoverageHitCount);
        Assert.Equal((byte)223, context.GetBitmap().GetPixel(5, 5)[3]);
    }

    [Fact]
    public void SameCountMutationBeforeAdmissionAndAfterCachingAlwaysUsesCurrentCommands()
    {
        using var actual = new RenderContext(new Bitmap(40, 36), 1f);
        using var expected = new RenderContext(new Bitmap(40, 36), 1f);
        var path = Path();
        var commands = Assert.IsType<List<PathCommand>>(path.Commands);
        var brush = new SolidColorBrush(new Color(79, 137, 211, 128));
        for (var draw = 0; draw < 7; draw++)
        {
            if (draw == 1) commands[1] = new LineToCmd(new Point(12, 3)); // Before the second draw records.
            if (draw == 3) commands[2] = new LineToCmd(new Point(3, 12)); // Invalidates an existing same-count snapshot.
            actual.Clear(Color.Transparent); expected.Clear(Color.Transparent);
            actual.FillPath(path, brush);
            var fresh = new PathGeometry();
            Assert.IsType<List<PathCommand>>(fresh.Commands).AddRange(commands);
            expected.FillPath(fresh, brush); // Different owner every time: independent uncached coverage.
            Assert.Equal(expected.GetBitmap().Pixels, actual.GetBitmap().Pixels);
        }
        Assert.True(actual.CoverageHitCount >= 2);
    }

    [Fact]
    public void ProbationEvictionAndCombinedEntryByteBudgetsRemainBounded()
    {
        var cache = new SoftwareCoverageCache(2, 2048);
        var state = State;
        var a = Path(); var b = Path(); var c = Path();
        Assert.False(cache.ShouldRecord(a, in state));
        Assert.False(cache.ShouldRecord(b, in state));
        Assert.False(cache.ShouldRecord(c, in state));
        Assert.Equal(2, cache.ProbationCount);
        Assert.False(cache.ShouldRecord(a, in state)); // a was evicted; identity alone cannot bypass probation.
        Assert.True(cache.ShouldRecord(c, in state));
        cache.Add(c, in state, Mask());
        Assert.True(cache.TryGet(c, in state, out _));
        Assert.InRange(cache.Count + cache.ProbationCount, 0, 2);
        Assert.InRange(cache.Bytes + cache.ProbationBytes, 0, 2048);
        var changed = state with { Transform = Matrix3x2.CreateTranslation(1f / 16, 0) };
        Assert.False(cache.ShouldRecord(c, in changed));
        Assert.True(cache.ShouldRecord(c, in changed));
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.ProbationCount);
        Assert.Equal(0, cache.Bytes + cache.ProbationBytes);
    }

    [Theory]
    [InlineData(0, 1048576L)]
    [InlineData(16, 0L)]
    [InlineData(16, 511L)]
    public void DisabledCacheDoesNotRecordOrRetainProbation(int entries, long bytes)
    {
        var cache = new SoftwareCoverageCache(entries, bytes);
        var path = Path(); var state = State;
        for (var use = 0; use < 4; use++) Assert.False(cache.ShouldRecord(path, in state));
        Assert.Equal(0, cache.ProbationCount);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes + cache.ProbationBytes);
    }

    [Fact]
    public void ProbationKeepsWeakOwnersAndSeparatesOwnerAndEveryRenderState()
    {
        var cache = new SoftwareCoverageCache();
        var state = State; var path = Path();
        Assert.False(cache.ShouldRecord(path, in state));
        Assert.False(cache.ShouldRecord(Path(), in state));
        var changed = state with { Fill = false, Width = 2 };
        Assert.False(cache.ShouldRecord(path, in changed));
        Assert.True(cache.ShouldRecord(path, in state));
        Assert.True(cache.ShouldRecord(path, in changed));
        var weak = AddTransient(cache);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(weak.IsAlive);
        GC.KeepAlive(path);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddTransient(SoftwareCoverageCache cache)
    {
        var path = Path(); var state = State;
        Assert.False(cache.ShouldRecord(path, in state));
        return new WeakReference(path);
    }
}
