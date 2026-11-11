using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Square.Graphics;
using Square.Rendering.Tessellation;

namespace Square.Backends.Vulkan;

/// <summary>Value-validated, bounded LRU caches for immutable clips and clipped CPU meshes.</summary>
internal sealed class VulkanClipCache
{
    private const long EntryOverhead = 384;
    private readonly int _maxClipEntries;
    private readonly long _maxClipBytes;
    private readonly int _maxMeshEntries;
    private readonly long _maxMeshBytes;
    private readonly Dictionary<ulong, List<LinkedListNode<ClipEntry>>> _clips = [];
    private readonly LinkedList<ClipEntry> _clipLru = [];
    private readonly Dictionary<ulong, List<LinkedListNode<MeshEntry>>> _meshes = [];
    private readonly LinkedList<MeshEntry> _meshLru = [];

    public VulkanClipCache(int maxClipEntries = 2048, long maxClipBytes = 8L * 1024 * 1024,
        int maxMeshEntries = 4096, long maxMeshBytes = 16L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxClipEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxClipBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMeshEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMeshBytes);
        _maxClipEntries = maxClipEntries;
        _maxClipBytes = maxClipBytes;
        _maxMeshEntries = maxMeshEntries;
        _maxMeshBytes = maxMeshBytes;
    }

    public int Count => ClipCount + MeshCount;
    public int ClipCount => _clipLru.Count;
    public int MeshCount => _meshLru.Count;
    public long Bytes => ClipBytes + MeshBytes;
    public long ClipBytes { get; private set; }
    public long MeshBytes { get; private set; }
    public long ClipBuildCount { get; private set; }
    public long ClipHitCount { get; private set; }
    public long MeshBuildCount { get; private set; }
    public long MeshHitCount { get; private set; }
    public long BuildCount => ClipBuildCount + MeshBuildCount;
    public long HitCount => ClipHitCount + MeshHitCount;

    public VulkanClipRegion GetCanvasClip(Rect bounds)
    {
        Span<int> values = stackalloc int[5];
        var writer = new ShapeWriter(values);
        writer.Add(0); // Separate namespace from all child shape keys.
        writer.Add(bounds);
        var hash = Hash(values, 14695981039346656037ul);
        if (_clips.TryGetValue(hash, out var bucket))
            foreach (var node in bucket)
                if (node.Value.Parent is null && values.SequenceEqual(node.Value.Shape))
                {
                    _clipLru.Remove(node);
                    _clipLru.AddLast(node);
                    ClipHitCount++;
                    return node.Value.Result;
                }
        var result = VulkanClipRegion.CreateCanvas(bounds);
        ClipBuildCount++;
        // Fractional/invalid canvases retain their original mask behavior, uncached.
        if (result.HasMask) return result;
        var bytes = EntryOverhead + 24L + (long)values.Length * sizeof(int) + result.EstimatedBytes;
        if (_maxClipEntries == 0 || bytes > _maxClipBytes) return result;
        while (_clipLru.Count >= _maxClipEntries || ClipBytes > _maxClipBytes - bytes) RemoveOldestClip();
        var added = _clipLru.AddLast(new ClipEntry(hash, null, Matrix3x2.Identity, values.ToArray(), result, bytes));
        if (!_clips.TryGetValue(hash, out bucket)) _clips.Add(hash, bucket = []);
        bucket.Add(added);
        ClipBytes += bytes;
        return result;
    }

    public VulkanClipRegion GetClip(VulkanClipRegion parent, Rect rect, Matrix3x2 transform)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Span<int> values = stackalloc int[5];
        var writer = new ShapeWriter(values);
        writer.Add(1);
        writer.Add(rect);
        return GetClip(parent, values, transform, rect, null);
    }

    public VulkanClipRegion GetClip(VulkanClipRegion parent, Geometry geometry, Matrix3x2 transform)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(geometry);
        var count = ShapeLength(geometry);
        if (count < 0 || _maxClipEntries == 0 ||
            EntryOverhead + 24L + (long)count * sizeof(int) + parent.EstimatedBytes > _maxClipBytes)
        {
            var direct = parent.Intersect(geometry, transform);
            ClipBuildCount++;
            return direct;
        }
        int[]? rented = null;
        Span<int> values = count <= 512 ? stackalloc int[count] : (rented = ArrayPool<int>.Shared.Rent(count)).AsSpan(0, count);
        try
        {
            WriteShape(values, geometry);
            return GetClip(parent, values, transform, default, geometry);
        }
        finally
        {
            if (rented is not null) ArrayPool<int>.Shared.Return(rented);
        }
    }

    private VulkanClipRegion GetClip(VulkanClipRegion parent, ReadOnlySpan<int> values,
        Matrix3x2 transform, Rect rect, Geometry? geometry)
    {
        var hash = Hash(values, HashTransform(transform, Seed(parent)));
        if (_clips.TryGetValue(hash, out var bucket))
            foreach (var node in bucket)
                if (ReferenceEquals(node.Value.Parent, parent) && SameTransform(node.Value.Transform, transform) &&
                    values.SequenceEqual(node.Value.Shape))
                {
                    _clipLru.Remove(node);
                    _clipLru.AddLast(node);
                    ClipHitCount++;
                    return node.Value.Result;
                }
        var result = geometry is null ? parent.Intersect(rect, transform) : parent.Intersect(geometry, transform);
        ClipBuildCount++;
        // Regions hold no ancestor refs. Charge BOTH directly retained regions even when shared,
        // so evicting a parent's own entry cannot hide memory retained by a child key.
        var bytes = EntryOverhead + 24L + (long)values.Length * sizeof(int) + parent.EstimatedBytes + result.EstimatedBytes;
        if (_maxClipEntries == 0 || bytes > _maxClipBytes) return result;
        while (_clipLru.Count >= _maxClipEntries || ClipBytes > _maxClipBytes - bytes) RemoveOldestClip();
        var entry = new ClipEntry(hash, parent, transform, values.ToArray(), result, bytes);
        var added = _clipLru.AddLast(entry);
        if (!_clips.TryGetValue(hash, out bucket)) _clips.Add(hash, bucket = []);
        bucket.Add(added);
        ClipBytes += bytes;
        return result;
    }

    public VulkanCachedMesh Clip(VulkanClipRegion clip, ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var inputBytes = 96L + (long)vertices.Length * 20 + (long)indices.Length * sizeof(uint);
        var retainable = _maxMeshEntries > 0 && EntryOverhead + clip.EstimatedBytes + inputBytes <= _maxMeshBytes;
        var hash = 0ul;
        if (retainable)
        {
            hash = Hash(MemoryMarshal.Cast<Vertex2D, int>(vertices), Seed(clip));
            hash = Hash(MemoryMarshal.Cast<uint, int>(indices), hash);
            if (_meshes.TryGetValue(hash, out var bucket))
                foreach (var node in bucket)
                    if (ReferenceEquals(node.Value.Clip, clip) && node.Value.Input.Matches(vertices, indices))
                    {
                        _meshLru.Remove(node);
                        _meshLru.AddLast(node);
                        MeshHitCount++;
                        return node.Value.Result;
                    }
        }
        var clippedVertices = new List<Vertex2D>();
        var clippedIndices = new List<uint>();
        clip.Clip(vertices, indices, clippedVertices, clippedIndices);
        var result = new VulkanCachedMesh(CollectionsMarshal.AsSpan(clippedVertices), CollectionsMarshal.AsSpan(clippedIndices));
        MeshBuildCount++;
        var bytes = EntryOverhead + clip.EstimatedBytes + inputBytes + result.ByteSize;
        if (!retainable || bytes > _maxMeshBytes) return result;
        while (_meshLru.Count >= _maxMeshEntries || MeshBytes > _maxMeshBytes - bytes) RemoveOldestMesh();
        var input = new VulkanCachedMesh(vertices, indices);
        var entry = new MeshEntry(hash, clip, input, result, bytes);
        var added = _meshLru.AddLast(entry);
        if (!_meshes.TryGetValue(hash, out var entries)) _meshes.Add(hash, entries = []);
        entries.Add(added);
        MeshBytes += bytes;
        return result;
    }

    /// <summary>Releases retained entries; cumulative hit/build counters remain available.</summary>
    public void Clear()
    {
        _clips.Clear();
        _clips.TrimExcess();
        _clipLru.Clear();
        _meshes.Clear();
        _meshes.TrimExcess();
        _meshLru.Clear();
        ClipBytes = MeshBytes = 0;
    }

    private void RemoveOldestClip()
    {
        var node = _clipLru.First!;
        _clipLru.RemoveFirst();
        var bucket = _clips[node.Value.Hash];
        bucket.Remove(node);
        if (bucket.Count == 0) _clips.Remove(node.Value.Hash);
        else bucket.TrimExcess();
        _clips.TrimExcess();
        ClipBytes -= node.Value.Bytes;
    }

    private void RemoveOldestMesh()
    {
        var node = _meshLru.First!;
        _meshLru.RemoveFirst();
        var bucket = _meshes[node.Value.Hash];
        bucket.Remove(node);
        if (bucket.Count == 0) _meshes.Remove(node.Value.Hash);
        else bucket.TrimExcess();
        _meshes.TrimExcess();
        MeshBytes -= node.Value.Bytes;
    }

    private static int ShapeLength(Geometry geometry)
    {
        if (geometry is RectGeometry or EllipseGeometry) return 5;
        if (geometry is RoundedRectGeometry) return 15;
        if (geometry is not PathGeometry path) return -1;
        var length = 1;
        foreach (var command in path.Commands)
        {
            var size = command switch { MoveToCmd or LineToCmd => 3, ArcToCmd => 7, CloseCmd => 1, _ => -1 };
            if (size < 0) return -1; // Unknown extensions retain the existing uncached behavior.
            length = checked(length + size);
        }
        return length;
    }

    private static void WriteShape(Span<int> destination, Geometry geometry)
    {
        var writer = new ShapeWriter(destination);
        switch (geometry)
        {
            case RectGeometry rect:
                writer.Add(1); writer.Add(rect.Rect);
                break;
            case RoundedRectGeometry rounded:
                writer.Add(2); writer.Add(rounded.Rect);
                writer.Add(rounded.RadiusX); writer.Add(rounded.RadiusY);
                writer.Add(rounded.TopLeft); writer.Add(rounded.TopRight);
                writer.Add(rounded.BottomRight); writer.Add(rounded.BottomLeft);
                break;
            case EllipseGeometry ellipse:
                writer.Add(3); writer.Add(ellipse.Center);
                writer.Add(ellipse.RadiusX); writer.Add(ellipse.RadiusY);
                break;
            case PathGeometry path:
                writer.Add(4);
                foreach (var command in path.Commands)
                    switch (command)
                    {
                        case MoveToCmd move: writer.Add(1); writer.Add(move.Point); break;
                        case LineToCmd line: writer.Add(2); writer.Add(line.Point); break;
                        case ArcToCmd arc:
                            writer.Add(3); writer.Add(arc.Oval); writer.Add(arc.StartAngle); writer.Add(arc.SweepAngle); break;
                        case CloseCmd: writer.Add(4); break;
                    }
                break;
        }
    }

    private static ulong Seed(VulkanClipRegion clip) => Mix(14695981039346656037ul, RuntimeHelpers.GetHashCode(clip));
    private static ulong Mix(ulong hash, int value) => unchecked((hash ^ (uint)value) * 1099511628211ul);
    private static ulong Hash(ReadOnlySpan<int> values, ulong hash)
    {
        hash = Mix(hash, values.Length);
        foreach (var value in values) hash = Mix(hash, value);
        return hash;
    }
    private static ulong HashTransform(Matrix3x2 value, ulong hash)
    {
        hash = Mix(hash, BitConverter.SingleToInt32Bits(value.M11));
        hash = Mix(hash, BitConverter.SingleToInt32Bits(value.M12));
        hash = Mix(hash, BitConverter.SingleToInt32Bits(value.M21));
        hash = Mix(hash, BitConverter.SingleToInt32Bits(value.M22));
        hash = Mix(hash, BitConverter.SingleToInt32Bits(value.M31));
        return Mix(hash, BitConverter.SingleToInt32Bits(value.M32));
    }
    private static bool SameTransform(Matrix3x2 a, Matrix3x2 b)
        => BitConverter.SingleToInt32Bits(a.M11) == BitConverter.SingleToInt32Bits(b.M11)
        && BitConverter.SingleToInt32Bits(a.M12) == BitConverter.SingleToInt32Bits(b.M12)
        && BitConverter.SingleToInt32Bits(a.M21) == BitConverter.SingleToInt32Bits(b.M21)
        && BitConverter.SingleToInt32Bits(a.M22) == BitConverter.SingleToInt32Bits(b.M22)
        && BitConverter.SingleToInt32Bits(a.M31) == BitConverter.SingleToInt32Bits(b.M31)
        && BitConverter.SingleToInt32Bits(a.M32) == BitConverter.SingleToInt32Bits(b.M32);

    private ref struct ShapeWriter(Span<int> destination)
    {
        private readonly Span<int> _destination = destination;
        private int _offset;
        public void Add(int value) => _destination[_offset++] = value;
        public void Add(float value) => Add(BitConverter.SingleToInt32Bits(value));
        public void Add(Point value) { Add(value.X); Add(value.Y); }
        public void Add(Rect value) { Add(value.X); Add(value.Y); Add(value.Width); Add(value.Height); }
        public void Add(CornerRadius value) { Add(value.X); Add(value.Y); }
    }

    private sealed record ClipEntry(ulong Hash, VulkanClipRegion? Parent, Matrix3x2 Transform,
        int[] Shape, VulkanClipRegion Result, long Bytes);
    private sealed record MeshEntry(ulong Hash, VulkanClipRegion Clip, VulkanCachedMesh Input,
        VulkanCachedMesh Result, long Bytes);
}
