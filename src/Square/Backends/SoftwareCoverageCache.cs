using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Square.Graphics;

namespace Square.Backends;

internal readonly record struct SoftwareCoverageState(Matrix3x2 Transform, float Width, bool Fill,
    int CanvasWidth, int CanvasHeight, bool HasClip, Rect ClipBounds);

internal readonly record struct SoftwareCoveragePixel(int X, int Y, byte Covered);

/// <summary>Ordered raw 4x4 coverage calls, independent of paint and visibility predicates.</summary>
internal sealed class SoftwareCoverageMask
{
    private readonly SoftwareCoveragePixel[] _pixels;
    public ReadOnlySpan<SoftwareCoveragePixel> Pixels => _pixels;
    public long ByteSize => 64L + (long)_pixels.Length * 16;

    public SoftwareCoverageMask(ReadOnlySpan<SoftwareCoveragePixel> pixels) => _pixels = pixels.ToArray();
}

/// <summary>An over-budget recording is abandoned rather than returned as an incomplete mask.</summary>
internal sealed class SoftwareCoverageBuilder
{
    private readonly int _maxPixels;
    private List<SoftwareCoveragePixel>? _pixels = [];
    private bool _finished;
    private SoftwareCoverageMask? _result;

    public SoftwareCoverageBuilder(int maxPixels = 65536)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxPixels);
        _maxPixels = maxPixels;
    }

    public void Record(int x, int y, int covered)
    {
        if (_finished || _pixels is null || covered < 1 || covered > 16) return;
        if (_pixels.Count >= _maxPixels)
        {
            _pixels = null; // Release the potentially large backing array immediately.
            return;
        }
        _pixels.Add(new SoftwareCoveragePixel(x, y, (byte)covered));
    }

    public SoftwareCoverageMask? Finish()
    {
        if (_finished) return _result;
        _finished = true;
        if (_pixels is not null) _result = new SoftwareCoverageMask(CollectionsMarshal.AsSpan(_pixels));
        _pixels = null;
        return _result;
    }
}

/// <summary>Bounded LRU of original coverage, using weak path identity and immutable command values.</summary>
internal sealed class SoftwareCoverageCache
{
    internal const int MaxSnapshotCommands = 16384;
    private const long EntryOverhead = 512;
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly Dictionary<Key, List<LinkedListNode<Entry>>> _entries = [];
    private readonly LinkedList<Entry> _lru = [];
    private readonly Dictionary<Key, List<LinkedListNode<Probation>>> _probation = [];
    private readonly LinkedList<Probation> _probationLru = [];

    public SoftwareCoverageCache(int maxEntries = 16384, long maxBytes = 64L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
    }

    public int Count => _lru.Count;
    public int ProbationCount => _probationLru.Count;
    public long ProbationBytes => (long)_probationLru.Count * EntryOverhead;
    public long Bytes { get; private set; }
    public long BuildCount { get; private set; }
    public long HitCount { get; private set; }

    public bool TryGet(PathGeometry path, in SoftwareCoverageState state, out SoftwareCoverageMask mask)
    {
        ArgumentNullException.ThrowIfNull(path);
        var key = Key.Create(path, state);
        var removed = false;
        if (_entries.TryGetValue(key, out var bucket))
        {
            for (var index = bucket.Count - 1; index >= 0; index--)
            {
                var node = bucket[index];
                if (!node.Value.Owner.TryGetTarget(out var owner))
                {
                    Remove(node);
                    removed = true;
                    continue;
                }
                if (!ReferenceEquals(owner, path)) continue; // Runtime identity hashes may collide.
                if (!Matches(path.Commands, node.Value.Commands))
                {
                    Remove(node);
                    removed = true;
                    continue;
                }
                _lru.Remove(node);
                _lru.AddLast(node);
                HitCount++;
                mask = node.Value.Mask;
                if (removed) TrimIndex();
                return true;
            }
        }
        if (removed) TrimIndex();
        mask = null!;
        return false;
    }

    // Probation only decides whether to build current coverage; it never reuses pixels or
    // command values. Add snapshots current Commands, and every actual hit still calls Matches.
    public bool ShouldRecord(PathGeometry path, in SoftwareCoverageState state)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_maxEntries == 0 || _maxBytes < EntryOverhead || path.Commands.Count > MaxSnapshotCommands) return false;
        var key = Key.Create(path, state);
        if (RemoveProbation(key, path)) return true;
        EvictFor(EntryOverhead);
        var node = _probationLru.AddLast(new Probation(key, new WeakReference<PathGeometry>(path)));
        if (!_probation.TryGetValue(key, out var bucket)) _probation.Add(key, bucket = []);
        bucket.Add(node);
        return false;
    }

    public void Add(PathGeometry path, in SoftwareCoverageState state, SoftwareCoverageMask mask)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(mask);
        BuildCount++;
        if (_maxEntries == 0 || path.Commands.Count > MaxSnapshotCommands) return;
        // Known command records are sealed, with immutable value fields. Copying their
        // references snapshots the full command list without retaining the mutable path.
        for (var index = 0; index < path.Commands.Count; index++)
            if (path.Commands[index] is not (MoveToCmd or LineToCmd or ArcToCmd or CloseCmd)) return;
        var bytes = EntryOverhead + 32L + (long)path.Commands.Count * 72 + mask.ByteSize;
        if (bytes > _maxBytes) return;
        var key = Key.Create(path, state);
        if (_entries.TryGetValue(key, out var existing))
            for (var index = existing.Count - 1; index >= 0; index--)
            {
                var node = existing[index];
                if (!node.Value.Owner.TryGetTarget(out var owner) || ReferenceEquals(owner, path)) Remove(node);
            }
        RemoveProbation(key, path);
        EvictFor(bytes);
        var snapshot = path.Commands.ToArray();
        var entry = new Entry(key, new WeakReference<PathGeometry>(path), snapshot, mask, bytes);
        var added = _lru.AddLast(entry);
        if (!_entries.TryGetValue(key, out var bucket)) _entries.Add(key, bucket = []);
        bucket.Add(added);
        Bytes += bytes;
        TrimIndex();
    }

    /// <summary>Release retained snapshots/masks; cumulative diagnostic counters remain.</summary>
    public void Clear()
    {
        _entries.Clear();
        _entries.TrimExcess();
        _lru.Clear();
        _probation.Clear();
        _probation.TrimExcess();
        _probationLru.Clear();
        Bytes = 0;
    }

    private void EvictFor(long bytes)
    {
        while (_lru.Count + _probationLru.Count >= _maxEntries || Bytes + ProbationBytes > _maxBytes - bytes)
        {
            if (_probationLru.First is { } probation) Remove(probation);
            else Remove(_lru.First!);
        }
    }

    private bool RemoveProbation(Key key, PathGeometry path)
    {
        var found = false;
        if (_probation.TryGetValue(key, out var bucket))
            for (var index = bucket.Count - 1; index >= 0; index--)
            {
                var node = bucket[index];
                var alive = node.Value.Owner.TryGetTarget(out var owner);
                if (alive && !ReferenceEquals(owner, path)) continue;
                found |= alive;
                Remove(node);
            }
        return found;
    }

    private void Remove(LinkedListNode<Probation> node)
    {
        _probationLru.Remove(node);
        var bucket = _probation[node.Value.Key];
        bucket.Remove(node);
        if (bucket.Count == 0) _probation.Remove(node.Value.Key);
        else if (bucket.Count * 2 < bucket.Capacity) bucket.TrimExcess();
        if (_probation.Count * 2 < _probation.EnsureCapacity(0)) _probation.TrimExcess();
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _lru.Remove(node);
        var bucket = _entries[node.Value.Key];
        bucket.Remove(node);
        if (bucket.Count == 0) _entries.Remove(node.Value.Key);
        else if (bucket.Count * 2 < bucket.Capacity) bucket.TrimExcess();
        Bytes -= node.Value.Bytes;
    }

    private void TrimIndex()
    {
        if (_entries.Count * 2 < _entries.EnsureCapacity(0)) _entries.TrimExcess();
    }

    private static bool Matches(IReadOnlyList<PathCommand> current, PathCommand[] snapshot)
    {
        if (current.Count != snapshot.Length) return false;
        for (var index = 0; index < snapshot.Length; index++)
        {
            var same = (current[index], snapshot[index]) switch
            {
                (MoveToCmd a, MoveToCmd b) => Same(a.Point, b.Point),
                (LineToCmd a, LineToCmd b) => Same(a.Point, b.Point),
                (ArcToCmd a, ArcToCmd b) => Same(a.Oval, b.Oval) &&
                    Bits(a.StartAngle) == Bits(b.StartAngle) && Bits(a.SweepAngle) == Bits(b.SweepAngle),
                (CloseCmd, CloseCmd) => true,
                _ => false
            };
            if (!same) return false;
        }
        return true;
    }

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static bool Same(Point a, Point b) => Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y);
    private static bool Same(Rect a, Rect b) => Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y)
        && Bits(a.Width) == Bits(b.Width) && Bits(a.Height) == Bits(b.Height);

    private readonly record struct Key(int OwnerHash, int M11, int M12, int M21, int M22, int M31, int M32,
        int Width, bool Fill, int CanvasWidth, int CanvasHeight, bool HasClip,
        int ClipX, int ClipY, int ClipWidth, int ClipHeight)
    {
        public static Key Create(PathGeometry path, in SoftwareCoverageState state) => new(
            RuntimeHelpers.GetHashCode(path), Bits(state.Transform.M11), Bits(state.Transform.M12),
            Bits(state.Transform.M21), Bits(state.Transform.M22), Bits(state.Transform.M31), Bits(state.Transform.M32),
            Bits(state.Width), state.Fill, state.CanvasWidth, state.CanvasHeight, state.HasClip,
            Bits(state.ClipBounds.X), Bits(state.ClipBounds.Y), Bits(state.ClipBounds.Width), Bits(state.ClipBounds.Height));
    }

    private sealed record Probation(Key Key, WeakReference<PathGeometry> Owner);

    private sealed record Entry(Key Key, WeakReference<PathGeometry> Owner, PathCommand[] Commands,
        SoftwareCoverageMask Mask, long Bytes);
}
