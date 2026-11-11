using System.Numerics;
using LibTessDotNet;
using Square.Graphics;
using Square.Rendering.Tessellation;

namespace Square.Backends.Vulkan;

/// <summary>Immutable device-space clip. Integral rectangles use the caller's scissor.</summary>
internal sealed class VulkanClipRegion
{
    private readonly Triangle[]? _mask;
    private static readonly VulkanClipRegion Empty = new(Rect.Empty, []);

    public Rect Bounds { get; }
    public bool HasMask => _mask is not null;
    public long EstimatedBytes => 64L + (_mask?.LongLength ?? 0) * 24;

    private VulkanClipRegion(Rect bounds, Triangle[]? mask)
    {
        Bounds = bounds;
        _mask = mask;
    }

    public static VulkanClipRegion CreateCanvas(Rect bounds)
    {
        if (!ValidRect(bounds)) return Empty;
        return new VulkanClipRegion(bounds, Integral(bounds) ? null : RectangleTriangles(bounds));
    }

    public VulkanClipRegion Intersect(Rect rect, Matrix3x2 transform)
    {
        if (Bounds.IsEmpty || !ValidRect(rect) || !ValidTransform(transform)) return Empty;
        Point[] points =
        [
            Transform(new Point(rect.Left, rect.Top), transform),
            Transform(new Point(rect.Right, rect.Top), transform),
            Transform(new Point(rect.Right, rect.Bottom), transform),
            Transform(new Point(rect.Left, rect.Bottom), transform)
        ];
        if (points.Any(p => !Finite(p))) return Empty;
        var shapeBounds = PointBounds(points);
        var bounds = Rect.Intersect(Bounds, shapeBounds);
        if (!ValidRect(bounds)) return Empty;
        var axisAligned = transform.M12 == 0 && transform.M21 == 0
            || transform.M11 == 0 && transform.M22 == 0;
        if (axisAligned && Integral(shapeBounds) && _mask is null)
            return new VulkanClipRegion(bounds, null);
        return IntersectMesh(Triangulate([points.ToList()]));
    }

    public VulkanClipRegion Intersect(Geometry geometry, Matrix3x2 transform)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry is RectGeometry rect) return Intersect(rect.Rect, transform);
        if (Bounds.IsEmpty || !ValidTransform(transform)) return Empty;
        List<List<Point>> contours;
        switch (geometry)
        {
            case PathGeometry path:
                contours = PathTessellator.FlattenPath(path, transform);
                break;
            case RoundedRectGeometry rounded:
                if (!ValidRect(rounded.Rect)) return Empty;
                contours = PathTessellator.FlattenPath(rounded.ToPath(), transform);
                break;
            case EllipseGeometry ellipse:
            {
                var center = ellipse.Center;
                var rx = ellipse.RadiusX;
                var ry = ellipse.RadiusY;
                if (!Finite(center) || !float.IsFinite(rx) || !float.IsFinite(ry) || rx <= 0 || ry <= 0) return Empty;
                var count = Math.Max(32, PathTessellator.GetCurveSegmentCount(rx, ry, MathF.Tau, transform));
                var contour = new List<Point>(count);
                for (var i = 0; i < count; i++)
                {
                    var angle = MathF.Tau * i / count;
                    contour.Add(new Point(center.X + rx * MathF.Cos(angle), center.Y + ry * MathF.Sin(angle)));
                }
                contours = [contour];
                break;
            }
            default:
                throw new NotSupportedException($"Vulkan geometry clipping does not support '{geometry.GetType().Name}'.");
        }
        // FlattenPath uses the matrix only to select curve resolution. Store transformed
        // value copies, so later path/geometry mutations cannot change a pushed clip.
        foreach (var contour in contours)
        for (var i = 0; i < contour.Count; i++)
        {
            var point = Transform(contour[i], transform);
            if (!Finite(point)) return Empty;
            contour[i] = point;
        }
        return IntersectMesh(Triangulate(contours));
    }

    /// <summary>Appends clipped triangles; callers keep applying Bounds as the scissor.</summary>
    public void Clip(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices,
        List<Vertex2D> outputVertices, List<uint> outputIndices)
    {
        if (Bounds.IsEmpty || indices.IsEmpty) return;
        if (indices.Length % 3 != 0) throw new ArgumentException("Triangle indices must be a multiple of three.", nameof(indices));
        if (_mask is null)
        {
            var offset = (uint)outputVertices.Count;
            foreach (var vertex in vertices) outputVertices.Add(vertex);
            foreach (var index in indices) outputIndices.Add(offset + index);
            return;
        }
        Span<DPoint> polygon = stackalloc DPoint[15];
        for (var i = 0; i < indices.Length; i += 3)
        {
            var a = vertices[(int)indices[i]];
            var b = vertices[(int)indices[i + 1]];
            var c = vertices[(int)indices[i + 2]];
            var source = new Triangle(new Point(a.X, a.Y), new Point(b.X, b.Y), new Point(c.X, c.Y));
            var area = Orient(source.A, source.B, source.C);
            if (!source.IsFinite || area == 0 || !Overlaps(source.Bounds, Bounds)) continue;
            foreach (var triangle in _mask)
            {
                if (!Overlaps(source.Bounds, triangle.Bounds)) continue;
                var count = ConvexIntersection(source, triangle, polygon);
                if (count < 3) continue;
                var offset = (uint)outputVertices.Count;
                for (var p = 0; p < count; p++)
                    outputVertices.Add(Interpolate(a, b, c, polygon[p], area));
                for (var p = 1; p < count - 1; p++)
                {
                    var first = outputVertices[(int)offset];
                    var second = outputVertices[(int)offset + p];
                    var third = outputVertices[(int)offset + p + 1];
                    // Float vertex storage can collapse an otherwise valid tiny polygon.
                    if (Orient(new Point(first.X, first.Y), new Point(second.X, second.Y), new Point(third.X, third.Y)) == 0) continue;
                    outputIndices.Add(offset);
                    outputIndices.Add(offset + (uint)(area > 0 ? p : p + 1));
                    outputIndices.Add(offset + (uint)(area > 0 ? p + 1 : p));
                }
            }
        }
    }

    private VulkanClipRegion IntersectMesh(Triangle[] shape)
    {
        if (shape.Length == 0) return Empty;
        var parent = _mask ?? RectangleTriangles(Bounds);
        // Both inputs are disjoint, consistently oriented triangle meshes. Winding >=2
        // is their intersection; tessellating once also removes internal shared edges.
        var tess = new Tess();
        AddTriangles(tess, parent);
        AddTriangles(tess, shape);
        tess.Tessellate(WindingRule.AbsGeqTwo, ElementType.Polygons, 3);
        var result = ReadTriangles(tess);
        if (result.Length == 0) return Empty;
        var bounds = result[0].Bounds;
        for (var i = 1; i < result.Length; i++) bounds = UnionBounds(bounds, result[i].Bounds);
        return new VulkanClipRegion(bounds, result);
    }

    private static Triangle[] Triangulate(List<List<Point>> contours)
        => contours.Any(contour => contour.Count >= 3) ? ReadTriangles(PathTessellator.Triangulate(contours)) : [];

    private static void AddTriangles(Tess tess, Triangle[] triangles)
    {
        foreach (var triangle in triangles)
        {
            ContourVertex[] vertices =
            [
                new() { Position = new Vec3(triangle.A.X, triangle.A.Y, 0) },
                new() { Position = new Vec3(triangle.B.X, triangle.B.Y, 0) },
                new() { Position = new Vec3(triangle.C.X, triangle.C.Y, 0) }
            ];
            tess.AddContour(vertices, ContourOrientation.Original);
        }
    }

    private static Triangle[] ReadTriangles(Tess tess)
    {
        var triangles = new List<Triangle>(tess.ElementCount);
        for (var i = 0; i < tess.ElementCount; i++)
        {
            var ia = tess.Elements[i * 3];
            var ib = tess.Elements[i * 3 + 1];
            var ic = tess.Elements[i * 3 + 2];
            if (ia < 0 || ib < 0 || ic < 0) continue;
            var a = tess.Vertices[ia].Position;
            var b = tess.Vertices[ib].Position;
            var c = tess.Vertices[ic].Position;
            var triangle = new Triangle(new Point(a.X, a.Y), new Point(b.X, b.Y), new Point(c.X, c.Y));
            var area = Orient(triangle.A, triangle.B, triangle.C);
            if (!triangle.IsFinite || area == 0) continue;
            if (area < 0) triangle = new Triangle(triangle.A, triangle.C, triangle.B);
            triangles.Add(triangle);
        }
        return triangles.ToArray();
    }

    private static Triangle[] RectangleTriangles(Rect rect) =>
    [
        new(new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom)),
        new(new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Bottom), new Point(rect.Left, rect.Bottom))
    ];

    private static int ConvexIntersection(Triangle a, Triangle b, Span<DPoint> output)
    {
        Span<DPoint> candidates = stackalloc DPoint[15];
        var count = 0;
        for (var i = 0; i < 3; i++)
        {
            var p = new DPoint(a[i].X, a[i].Y);
            if (Inside(p, b)) candidates[count++] = p;
            p = new DPoint(b[i].X, b[i].Y);
            if (Inside(p, a)) candidates[count++] = p;
        }
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
            if (SegmentIntersection(new DPoint(a[i].X, a[i].Y), new DPoint(a[(i + 1) % 3].X, a[(i + 1) % 3].Y),
                new DPoint(b[j].X, b[j].Y), new DPoint(b[(j + 1) % 3].X, b[(j + 1) % 3].Y), out var point))
                candidates[count++] = point;
        // A canonical convex hull removes duplicate corners and collinear fan triangles.
        for (var i = 1; i < count; i++)
        {
            var point = candidates[i];
            var j = i;
            while (j > 0 && Compare(candidates[j - 1], point) > 0)
            {
                candidates[j] = candidates[j - 1];
                j--;
            }
            candidates[j] = point;
        }
        var unique = 0;
        for (var i = 0; i < count; i++)
            if (unique == 0 || candidates[i] != candidates[unique - 1]) candidates[unique++] = candidates[i];
        if (unique < 3) return 0;
        Span<DPoint> hull = stackalloc DPoint[30];
        var n = 0;
        for (var i = 0; i < unique; i++)
        {
            while (n >= 2 && Orient(hull[n - 2], hull[n - 1], candidates[i]) <= 0) n--;
            hull[n++] = candidates[i];
        }
        var lower = n;
        for (var i = unique - 2; i >= 0; i--)
        {
            while (n > lower && Orient(hull[n - 2], hull[n - 1], candidates[i]) <= 0) n--;
            hull[n++] = candidates[i];
        }
        n--;
        if (n < 3) return 0;
        hull[..n].CopyTo(output);
        return n;
    }

    private static bool SegmentIntersection(DPoint p, DPoint p1, DPoint q, DPoint q1, out DPoint result)
    {
        result = default;
        // Sorting endpoints and line pairs makes a shared-edge crossing bit-identical
        // regardless of which adjacent mask/source triangle requests the intersection.
        if (Compare(p, p1) > 0) (p, p1) = (p1, p);
        if (Compare(q, q1) > 0) (q, q1) = (q1, q);
        if (Compare(p, q) > 0 || Compare(p, q) == 0 && Compare(p1, q1) > 0)
            (p, p1, q, q1) = (q, q1, p, p1);
        var rx = p1.X - p.X;
        var ry = p1.Y - p.Y;
        var sx = q1.X - q.X;
        var sy = q1.Y - q.Y;
        var denominator = rx * sy - ry * sx;
        if (denominator == 0) return false;
        var dx = q.X - p.X;
        var dy = q.Y - p.Y;
        var t = (dx * sy - dy * sx) / denominator;
        var u = (dx * ry - dy * rx) / denominator;
        if (t < 0 || t > 1 || u < 0 || u > 1) return false;
        result = t == 0 ? p : t == 1 ? p1 : u == 0 ? q : u == 1 ? q1 : new DPoint(p.X + t * rx, p.Y + t * ry);
        return true;
    }

    private static Vertex2D Interpolate(Vertex2D a, Vertex2D b, Vertex2D c, DPoint point, double area)
    {
        var x = (float)point.X;
        var y = (float)point.Y;
        // Evaluate attributes once in the original source triangle, after positional
        // quantization. Shared output positions therefore also get identical attributes.
        var px = (double)x - a.X;
        var py = (double)y - a.Y;
        var w1 = (px * ((double)c.Y - a.Y) - py * ((double)c.X - a.X)) / area;
        var w2 = (((double)b.X - a.X) * py - ((double)b.Y - a.Y) * px) / area;
        var color = a.Color;
        if (a.Color != b.Color || a.Color != c.Color)
        {
            color = 0;
            for (var shift = 0; shift < 32; shift += 8)
            {
                var channel = (double)((a.Color >> shift) & 255);
                var value = channel + w1 * (((b.Color >> shift) & 255) - channel) + w2 * (((c.Color >> shift) & 255) - channel);
                color |= (uint)Math.Clamp((int)Math.Round(value), 0, 255) << shift;
            }
        }
        return new Vertex2D(x, y,
            (float)(a.U + w1 * ((double)b.U - a.U) + w2 * ((double)c.U - a.U)),
            (float)(a.V + w1 * ((double)b.V - a.V) + w2 * ((double)c.V - a.V)), color);
    }

    private static bool Inside(DPoint p, Triangle triangle)
    {
        var a = new DPoint(triangle.A.X, triangle.A.Y);
        var b = new DPoint(triangle.B.X, triangle.B.Y);
        var c = new DPoint(triangle.C.X, triangle.C.Y);
        return Orient(a, b, c) > 0
            ? Orient(a, b, p) >= 0 && Orient(b, c, p) >= 0 && Orient(c, a, p) >= 0
            : Orient(a, b, p) <= 0 && Orient(b, c, p) <= 0 && Orient(c, a, p) <= 0;
    }

    private static int Compare(DPoint a, DPoint b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y);
    private static double Orient(DPoint a, DPoint b, DPoint c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static double Orient(Point a, Point b, Point c) => ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);
    private static Point Transform(Point p, Matrix3x2 m) => new(p.X * m.M11 + p.Y * m.M21 + m.M31, p.X * m.M12 + p.Y * m.M22 + m.M32);
    private static bool Finite(Point p) => float.IsFinite(p.X) && float.IsFinite(p.Y);
    private static bool Integral(Rect rect) => rect.Left == MathF.Truncate(rect.Left) && rect.Top == MathF.Truncate(rect.Top)
        && rect.Right == MathF.Truncate(rect.Right) && rect.Bottom == MathF.Truncate(rect.Bottom);
    private static bool ValidRect(Rect r) => !r.IsEmpty && float.IsFinite(r.X) && float.IsFinite(r.Y) && float.IsFinite(r.Right) && float.IsFinite(r.Bottom);
    private static bool ValidTransform(Matrix3x2 m) => float.IsFinite(m.M11) && float.IsFinite(m.M12)
        && float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M31) && float.IsFinite(m.M32)
        && (double)m.M11 * m.M22 - (double)m.M12 * m.M21 != 0;
    private static bool Overlaps(Rect a, Rect b) => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
    private static Rect UnionBounds(Rect a, Rect b)
    {
        var x = Math.Min(a.Left, b.Left);
        var y = Math.Min(a.Top, b.Top);
        return new Rect(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }
    private static Rect PointBounds(Point[] points)
    {
        var x = points.Min(p => p.X);
        var y = points.Min(p => p.Y);
        return new Rect(x, y, points.Max(p => p.X) - x, points.Max(p => p.Y) - y);
    }

    private readonly record struct DPoint(double X, double Y);
    private readonly record struct Triangle(Point A, Point B, Point C)
    {
        public Point this[int index] => index == 0 ? A : index == 1 ? B : C;
        public bool IsFinite => Finite(A) && Finite(B) && Finite(C);
        public Rect Bounds => new(Math.Min(A.X, Math.Min(B.X, C.X)), Math.Min(A.Y, Math.Min(B.Y, C.Y)),
            Math.Max(A.X, Math.Max(B.X, C.X)) - Math.Min(A.X, Math.Min(B.X, C.X)),
            Math.Max(A.Y, Math.Max(B.Y, C.Y)) - Math.Min(A.Y, Math.Min(B.Y, C.Y)));
    }
}
