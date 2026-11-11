using System.Numerics;
using System.Runtime.CompilerServices;
using Square.Backends;
using Square.Graphics;
using Xunit;

namespace Square.Backends.Tests;

public sealed class SoftwareCoverageCacheTests
{
    private static SoftwareCoverageState State => new(Matrix3x2.Identity, 1.25f, false, 64, 56, true, new Rect(2, 3, 48, 40));
    private static PathGeometry Path() => new PathGeometry().MoveTo(new Point(2, 3)).LineTo(new Point(20, 4))
        .ArcTo(new Rect(4, 6, 14, 12), 15, 140).Close();
    private static SoftwareCoverageMask Mask() => new([new SoftwareCoveragePixel(3, 1, 4), new SoftwareCoveragePixel(1, 1, 16), new SoftwareCoveragePixel(3, 1, 8)]);

    [Fact]
    public void MaskCopiesPixelsAndBuilderPreservesOrderAndRepeatedLocations()
    {
        SoftwareCoveragePixel[] input = [new(3, 1, 4), new(1, 1, 16), new(3, 1, 8)];
        var expected = input.ToArray();
        var mask = new SoftwareCoverageMask(input);
        input[0] = new SoftwareCoveragePixel(99, 99, 1);
        Assert.Equal(expected, mask.Pixels.ToArray());
        var builder = new SoftwareCoverageBuilder(3);
        foreach (var p in expected) builder.Record(p.X, p.Y, p.Covered);
        var result = builder.Finish();
        Assert.NotNull(result);
        Assert.Equal(expected, result.Pixels.ToArray());
        builder.Record(10, 10, 16);
        Assert.Equal(expected, builder.Finish()!.Pixels.ToArray());
    }

    [Fact]
    public void BuilderRejectsIncompleteOversizedCoverageAndSupportsEmptyMasks()
    {
        var builder = new SoftwareCoverageBuilder(2);
        builder.Record(0, 0, 1); builder.Record(1, 0, 16); builder.Record(2, 0, 8);
        Assert.Null(builder.Finish());
        Assert.Null(builder.Finish());
        var empty = new SoftwareCoverageBuilder().Finish();
        Assert.NotNull(empty);
        Assert.True(empty.Pixels.IsEmpty);
        var path = Path(); var state = State;
        var cache = new SoftwareCoverageCache();
        cache.Add(path, in state, empty);
        Assert.True(cache.TryGet(path, in state, out var hit));
        Assert.True(hit.Pixels.IsEmpty);
    }

    [Fact]
    public void EveryStateComponentAndRawSignedZeroParticipatesInTheKey()
    {
        var path = Path(); var state = State;
        var cache = new SoftwareCoverageCache();
        cache.Add(path, in state, Mask());
        SoftwareCoverageState[] changed =
        [
            state with { Transform = new Matrix3x2(1.25f, 0, 0, 1, 0, 0) },
            state with { Transform = new Matrix3x2(1, .125f, 0, 1, 0, 0) },
            state with { Transform = new Matrix3x2(1, 0, .125f, 1, 0, 0) },
            state with { Transform = new Matrix3x2(1, 0, 0, 1.25f, 0, 0) },
            state with { Transform = Matrix3x2.CreateTranslation(.25f, 0) },
            state with { Transform = Matrix3x2.CreateTranslation(0, .25f) },
            state with { Width = 2 }, state with { Fill = true },
            state with { CanvasWidth = 65 }, state with { CanvasHeight = 57 }, state with { HasClip = false },
            state with { ClipBounds = new Rect(2.25f, 3, 48, 40) },
            state with { ClipBounds = new Rect(2, 3.25f, 48, 40) },
            state with { ClipBounds = new Rect(2, 3, 48.25f, 40) },
            state with { ClipBounds = new Rect(2, 3, 48, 40.25f) }
        ];
        foreach (var value in changed) Assert.False(cache.TryGet(path, in value, out _));
        Assert.True(cache.TryGet(path, in state, out _));
        var positive = state with { Width = 0, ClipBounds = new Rect(0, 3, 48, 40) };
        cache.Add(path, in positive, Mask());
        var minusZero = BitConverter.Int32BitsToSingle(int.MinValue);
        var negativeWidth = positive with { Width = minusZero };
        var negativeMatrix = positive with { Transform = new Matrix3x2(1, minusZero, 0, 1, 0, 0) };
        var negativeClip = positive with { ClipBounds = new Rect(minusZero, 3, 48, 40) };
        Assert.False(cache.TryGet(path, in negativeWidth, out _));
        Assert.False(cache.TryGet(path, in negativeMatrix, out _));
        Assert.False(cache.TryGet(path, in negativeClip, out _));
    }

    [Theory]
    [InlineData("append")]
    [InlineData("move")]
    [InlineData("line")]
    [InlineData("arc")]
    [InlineData("close")]
    public void FullCommandSnapshotInvalidatesAppendsAndSameCountReplacement(string mutation)
    {
        var cache = new SoftwareCoverageCache(); var path = Path(); var state = State;
        var mask = Mask(); var before = mask.Pixels.ToArray();
        cache.Add(path, in state, mask);
        Assert.True(cache.TryGet(path, in state, out _));
        var commands = Assert.IsType<List<PathCommand>>(path.Commands);
        switch (mutation)
        {
            case "append": path.LineTo(new Point(24, 18)); break;
            case "move": commands[0] = new MoveToCmd(new Point(3, 3)); break;
            case "line": commands[1] = new LineToCmd(new Point(19, 4)); break;
            case "arc": commands[2] = new ArcToCmd(new Rect(4, 6, 14, 12), 15, 141); break;
            case "close": commands[3] = new LineToCmd(new Point(2, 3)); break;
        }
        Assert.False(cache.TryGet(path, in state, out _));
        cache.Add(path, in state, Mask());
        Assert.True(cache.TryGet(path, in state, out _));
        Assert.Equal(before, mask.Pixels.ToArray());
    }

    [Fact]
    public void WeakOwnerIdentityDoesNotReuseEqualButDifferentPathObjects()
    {
        var cache = new SoftwareCoverageCache(); var path = Path(); var state = State;
        cache.Add(path, in state, Mask());
        Assert.False(cache.TryGet(Path(), in state, out _));
        var weak = AddTransient(cache);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(weak.IsAlive);
        GC.KeepAlive(path);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddTransient(SoftwareCoverageCache cache)
    {
        var path = Path(); var state = State;
        cache.Add(path, in state, Mask());
        return new WeakReference(path);
    }

    [Fact]
    public void LruBudgetAndClearRemainBoundedAndCountersCumulative()
    {
        var state = State; var mask = Mask();
        var a = Path(); var b = Path(); var c = Path();
        var cache = new SoftwareCoverageCache(2, 1024 * 1024);
        cache.Add(a, in state, mask); cache.Add(b, in state, mask);
        Assert.True(cache.TryGet(a, in state, out _));
        cache.Add(c, in state, mask);
        Assert.False(cache.TryGet(b, in state, out _));
        Assert.True(cache.TryGet(a, in state, out _));
        Assert.True(cache.TryGet(c, in state, out _));
        Assert.Equal(2, cache.Count);
        var probe = new SoftwareCoverageCache();
        probe.Add(a, in state, mask);
        Assert.True(probe.Bytes > mask.ByteSize);
        var limit = probe.Bytes;
        var bounded = new SoftwareCoverageCache(100, limit);
        bounded.Add(a, in state, mask); bounded.Add(b, in state, mask); bounded.Add(c, in state, mask);
        Assert.InRange(bounded.Bytes, 0, limit);
        Assert.Equal(1, bounded.Count);
        var builds = cache.BuildCount; var hits = cache.HitCount;
        cache.Clear();
        Assert.Equal(0, cache.Count); Assert.Equal(0, cache.Bytes);
        Assert.Equal(builds, cache.BuildCount); Assert.Equal(hits, cache.HitCount);
        Assert.False(cache.TryGet(a, in state, out _));
    }

    [Theory]
    [InlineData(0, 1048576L)]
    [InlineData(20, 0L)]
    [InlineData(20, 1L)]
    public void DisabledOrOversizedEntriesAreNotRetained(int entries, long bytes)
    {
        var cache = new SoftwareCoverageCache(entries, bytes); var path = Path(); var state = State;
        cache.Add(path, in state, Mask());
        Assert.False(cache.TryGet(path, in state, out _));
        Assert.Equal(0, cache.Count); Assert.Equal(0, cache.Bytes);
    }

    [Fact]
    public void UnknownAndOversizedCommandSnapshotsAreNotRetained()
    {
        var cache = new SoftwareCoverageCache(); var state = State; var unknown = Path();
        Assert.IsType<List<PathCommand>>(unknown.Commands).Add(new UnknownCommand());
        cache.Add(unknown, in state, Mask());
        Assert.False(cache.TryGet(unknown, in state, out _));
        var huge = new PathGeometry().MoveTo(new Point(0, 0));
        for (var i = 0; i < 16384; i++) huge.LineTo(new Point(i, 1));
        cache.Add(huge, in state, Mask());
        Assert.False(cache.TryGet(huge, in state, out _));
        Assert.Equal(0, cache.Count); Assert.Equal(0, cache.Bytes);
    }
    private sealed record UnknownCommand : PathCommand;
}
