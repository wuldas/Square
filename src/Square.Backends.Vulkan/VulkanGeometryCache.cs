using System.Numerics;
using System.Runtime.CompilerServices;
using Square.Graphics;
using Square.Rendering.Tessellation;

namespace Square.Backends.Vulkan;

/// <summary>Bounded, immutable CPU meshes; GPU frame owners still copy every draw in its original order.</summary>
internal sealed class VulkanGeometryCache
{
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _lru = [];
    public int Count => _entries.Count;
    public long Bytes { get; private set; }
    public long BuildCount { get; private set; }
    public long HitCount { get; private set; }

    public VulkanGeometryCache(int maxEntries = 16384, long maxBytes = 64L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
    }

    public VulkanCachedMesh GetStroke(PathGeometry path, Matrix3x2 transform, Pen pen, uint packedColor)
        => Get(path, transform, pen, packedColor);

    public VulkanCachedMesh GetFill(PathGeometry path, Matrix3x2 transform, uint packedColor)
        => Get(path, transform, null, packedColor);

    // Fill meshes contain no stroke feather: append-time paint can replace opaque white.
    public VulkanCachedMesh GetFillGeometry(PathGeometry path, Matrix3x2 transform)
        => Get(path, transform, null, uint.MaxValue);

    public bool TryGetPrimitiveStroke(Rect rect, float rx, float ry, bool rounded,
        Matrix3x2 transform, Pen pen, uint packedColor, out VulkanCachedMesh mesh)
    {
        var key = CreateKey(null, transform, pen, packedColor, PrimitiveKey.Create(rect, rx, ry, rounded));
        return TryGetCached(key, null, pen.StrokeStyle?.DashArray, out mesh);
    }

    public VulkanCachedMesh BuildPrimitiveStroke(PathGeometry generated, Rect rect, float rx, float ry, bool rounded,
        Matrix3x2 transform, Pen pen, uint packedColor)
    {
        var key = CreateKey(null, transform, pen, packedColor, PrimitiveKey.Create(rect, rx, ry, rounded));
        var dashes = pen.StrokeStyle?.DashArray;
        BuildCount++;
        var mesh = Build(generated, transform, pen, packedColor);
        Store(key, null, dashes, mesh);
        return mesh;
    }

    private VulkanCachedMesh Get(PathGeometry path, Matrix3x2 transform, Pen? pen, uint packedColor)
    {
        ArgumentNullException.ThrowIfNull(path);
        var dashes = pen?.StrokeStyle?.DashArray;
        var key = CreateKey(path, transform, pen, packedColor);
        if (TryGetCached(key, path, dashes, out var mesh)) return mesh;
        BuildCount++;
        mesh = Build(path, transform, pen, packedColor);
        Store(key, path, dashes, mesh);
        return mesh;
    }

    private static Key CreateKey(PathGeometry? path, Matrix3x2 transform, Pen? pen, uint packedColor,
        PrimitiveKey primitive = default)
    {
        var style = pen?.StrokeStyle;
        var dashes = style?.DashArray;
        var dashHash = new HashCode();
        if (dashes is not null) foreach (var dash in dashes) dashHash.Add(dash);
        return new Key(path is null ? 0 : RuntimeHelpers.GetHashCode(path), path?.Commands.Count ?? 0,
            transform, pen is null, pen?.Width ?? 0, packedColor,
            style?.Cap ?? LineCap.Butt, style?.Join ?? LineJoin.Miter,
            style?.MiterLimit ?? 10, style?.DashOffset ?? 0, dashes?.Length ?? 0,
            dashes is { Length: > 0 } ? dashHash.ToHashCode() : 0, primitive);
    }

    private bool TryGetCached(Key key, PathGeometry? path, float[]? dashes, out VulkanCachedMesh mesh)
    {
        if (_entries.TryGetValue(key, out var node))
        {
            // Public mutable paths still require identity AND every command value.
            // Primitive entries use complete shape values and never retain generated paths.
            if ((key.Primitive.Kind != 0 || path is not null && node.Value.Owner is { } weak &&
                weak.TryGetTarget(out var owner) && ReferenceEquals(owner, path) &&
                MatchesCommands(node.Value.Commands, path.Commands)) &&
                MatchesDashes(node.Value.Dashes, dashes))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                HitCount++;
                mesh = node.Value.Mesh;
                return true;
            }
            Remove(node);
        }
        mesh = null!;
        return false;
    }

    private void Store(Key key, PathGeometry? path, float[]? dashes, VulkanCachedMesh mesh)
    {
        if (path is not null)
            foreach (var command in path.Commands)
                if (command is not (MoveToCmd or LineToCmd or ArcToCmd or CloseCmd)) return;
        // Include the larger value key, dictionary/LRU bookkeeping, retained immutable
        // commands and dash snapshot. Both entry kinds share this one count/byte budget.
        var bytes = checked(mesh.ByteSize + 640L + (path?.Commands.Count ?? 0) * 64L + (dashes?.Length ?? 0) * 4L);
        if (_maxEntries == 0 || bytes > _maxBytes) return;
        if (_entries.TryGetValue(key, out var previous)) Remove(previous);
        var evicted = false;
        while (_entries.Count >= _maxEntries || Bytes > _maxBytes - bytes)
        {
            Remove(_lru.Last!);
            evicted = true;
        }
        if (evicted) _entries.TrimExcess();
        var entry = new Entry(key, path is null ? null : new WeakReference<PathGeometry>(path),
            path is null ? [] : path.Commands.ToArray(), dashes is { Length: > 0 } ? dashes.ToArray() : [], mesh, bytes);
        var node = _lru.AddFirst(entry);
        _entries.Add(key, node);
        Bytes += bytes;
    }

    private VulkanCachedMesh Build(PathGeometry path, Matrix3x2 transform, Pen? pen, uint color)
    {
        var _vertices = new List<Vertex2D>();
        var _indices = new List<uint>();
        var contours = PathTessellator.FlattenPath(path, transform);
        var (u0, v0, u1, v1) = VulkanTextureAtlas.WhitePixelUV;
        if (pen is null)
        {
            if (contours.Count != 0)
            {
                var tess = PathTessellator.Triangulate(contours);
                var count = tess.ElementCount * 3;
                for (var i = 0; i < count; i++)
                {
                    var vertex = tess.Vertices[tess.Elements[i]].Position;
                    var point = Transform(new Point(vertex.X, vertex.Y), transform);
                    _vertices.Add(new Vertex2D(point.X, point.Y, u0, v0, color));
                    _indices.Add((uint)i);
                }
            }
        }
        else
        {
            foreach (var contour in contours)
            {
                if (contour.Count < 2) continue;
                StrokeTessellator.Append(contour, pen.Width / 2f,
                    PathTessellator.GetLogicalFeatherWidth(transform), pen.StrokeStyle,
                    color, u0, v0, u1, v1, point => Transform(point, transform), _vertices, _indices);
            }
        }
        return new VulkanCachedMesh(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices),
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_indices));
    }

    private static Point Transform(Point point, Matrix3x2 transform)
    {
        var x = point.X * transform.M11 + point.Y * transform.M21 + transform.M31;
        var y = point.X * transform.M12 + point.Y * transform.M22 + transform.M32;
        return new Point(x, y);
    }

    private static bool MatchesCommands(PathCommand[] snapshot, IReadOnlyList<PathCommand> commands)
    {
        if (snapshot.Length != commands.Count) return false;
        for (var i = 0; i < snapshot.Length; i++)
            if (!Equals(snapshot[i], commands[i])) return false;
        return true;
    }

    private static bool MatchesDashes(float[] snapshot, float[]? dashes)
        => snapshot.AsSpan().SequenceEqual(dashes is null ? ReadOnlySpan<float>.Empty : dashes);

    private void Remove(LinkedListNode<Entry> node)
    {
        _entries.Remove(node.Value.Key);
        _lru.Remove(node);
        Bytes -= node.Value.Bytes;
    }

    public void Clear()
    {
        _entries.Clear();
        _entries.TrimExcess();
        _lru.Clear();
        Bytes = 0;
    }

    private readonly record struct PrimitiveKey(int Kind, int X, int Y, int Width, int Height, int RadiusX, int RadiusY)
    {
        public static PrimitiveKey Create(Rect rect, float rx, float ry, bool rounded) => new(rounded ? 2 : 1,
            BitConverter.SingleToInt32Bits(rect.X), BitConverter.SingleToInt32Bits(rect.Y),
            BitConverter.SingleToInt32Bits(rect.Width), BitConverter.SingleToInt32Bits(rect.Height),
            BitConverter.SingleToInt32Bits(rx), BitConverter.SingleToInt32Bits(ry));
    }

    private readonly record struct Key(int PathIdentity, int Commands, Matrix3x2 Transform,
        bool Fill, float Width, uint Color, LineCap Cap, LineJoin Join, float MiterLimit,
        float DashOffset, int DashCount, int DashHash, PrimitiveKey Primitive);
    private sealed record Entry(Key Key, WeakReference<PathGeometry>? Owner, PathCommand[] Commands, float[] Dashes, VulkanCachedMesh Mesh, long Bytes);
}
