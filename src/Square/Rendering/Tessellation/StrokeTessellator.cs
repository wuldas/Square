using Square.Graphics;

namespace Square.Rendering.Tessellation;

internal static class StrokeTessellator
{
    private const float Epsilon = 0.0001f;

    public static void Append(
        IReadOnlyList<Point> contour,
        float halfWidth,
        float feather,
        StrokeStyle? style,
        uint color,
        float u0,
        float v0,
        float u1,
        float v1,
        Func<Point, Point> transform,
        List<Vertex2D> vertices,
        List<uint> indices)
    {
        if (halfWidth <= 0 || contour.Count < 2) return;

        var points = NormalizeContour(contour, out var closed);
        if (points.Count < (closed ? 3 : 2)) return;

        style ??= new StrokeStyle();
        if (TryNormalizeDashPattern(style.DashArray, out var dashPattern))
        {
            var solidStyle = new StrokeStyle
            {
                Cap = style.Cap,
                Join = style.Join,
                MiterLimit = style.MiterLimit
            };
            foreach (var dash in SplitDashes(points, closed, dashPattern, style.DashOffset))
            {
                Append(dash, halfWidth, feather, solidStyle, color, u0, v0, u1, v1,
                    transform, vertices, indices);
            }
            return;
        }

        var solidHalfWidth = Math.Max(0, halfWidth - feather / 2f);
        var transparentHalfWidth = halfWidth + feather / 2f;
        var transparent = color & 0x00FFFFFFu;
        var segmentCount = closed ? points.Count : points.Count - 1;
        var tangents = new Point[segmentCount];
        var normals = new Point[segmentCount];

        for (var i = 0; i < segmentCount; i++)
        {
            var start = points[i];
            var end = points[(i + 1) % points.Count];
            if (!TryGetDirection(start, end, out var tangent, out var normal)) continue;
            tangents[i] = tangent;
            normals[i] = normal;

            if (!closed && style.Cap == LineCap.Square)
            {
                if (i == 0) start = Add(start, Scale(tangent, -solidHalfWidth));
                if (i == segmentCount - 1) end = Add(end, Scale(tangent, solidHalfWidth));
            }

            AppendSegment(start, end, normal, solidHalfWidth, transparentHalfWidth,
                color, transparent, u0, v0, u1, v1, transform, vertices, indices);
        }

        var joinStart = closed ? 0 : 1;
        var joinEnd = closed ? points.Count : points.Count - 1;
        for (var i = joinStart; i < joinEnd; i++)
        {
            var previousSegment = (i - 1 + segmentCount) % segmentCount;
            var nextSegment = i % segmentCount;
            if (LengthSquared(tangents[previousSegment]) < Epsilon || LengthSquared(tangents[nextSegment]) < Epsilon)
                continue;

            AppendJoin(points[i % points.Count], tangents[previousSegment], normals[previousSegment],
                tangents[nextSegment], normals[nextSegment], solidHalfWidth, transparentHalfWidth,
                style.Join, style.MiterLimit, color, transparent, u0, v0, transform, vertices, indices);
        }

        if (closed) return;

        var firstTangent = tangents[0];
        var lastTangent = tangents[^1];
        if (LengthSquared(firstTangent) >= Epsilon)
        {
            var center = style.Cap == LineCap.Square
                ? Add(points[0], Scale(firstTangent, -solidHalfWidth))
                : points[0];
            AppendCap(center, Scale(firstTangent, -1), normals[0], solidHalfWidth, transparentHalfWidth,
                style.Cap, color, transparent, u0, v0, transform, vertices, indices);
        }
        if (LengthSquared(lastTangent) >= Epsilon)
        {
            var center = style.Cap == LineCap.Square
                ? Add(points[^1], Scale(lastTangent, solidHalfWidth))
                : points[^1];
            AppendCap(center, lastTangent, normals[^1], solidHalfWidth, transparentHalfWidth,
                style.Cap, color, transparent, u0, v0, transform, vertices, indices);
        }
    }

    private static List<Point> NormalizeContour(IReadOnlyList<Point> contour, out bool closed)
    {
        var points = new List<Point>(contour.Count);
        foreach (var point in contour)
        {
            if (points.Count == 0 || DistanceSquared(points[^1], point) > Epsilon)
                points.Add(point);
        }

        closed = points.Count > 2 && DistanceSquared(points[0], points[^1]) <= Epsilon;
        if (closed) points.RemoveAt(points.Count - 1);
        return points;
    }

    internal static List<List<Point>> SplitDashes(
        IReadOnlyList<Point> contour,
        bool closed,
        IReadOnlyList<float> pattern,
        float offset)
    {
        if (contour.Count < 2 || pattern.Count == 0) return [];

        var patternLength = 0f;
        for (var i = 0; i < pattern.Count; i++) patternLength += pattern[i];
        if (patternLength <= Epsilon || !float.IsFinite(patternLength)) return [];

        var phase = float.IsFinite(offset) ? PositiveModulo(offset, patternLength) : 0f;
        var patternIndex = 0;
        var patternRemaining = pattern[0];
        var drawing = true;
        AdvancePastEmptyPatternEntries(pattern, ref patternIndex, ref patternRemaining, ref drawing);
        while (phase > Epsilon)
        {
            if (phase < patternRemaining - Epsilon)
            {
                patternRemaining -= phase;
                phase = 0;
            }
            else
            {
                phase -= patternRemaining;
                AdvancePattern(pattern, ref patternIndex, ref patternRemaining, ref drawing);
            }
        }

        var startsDrawing = drawing;
        var lastChunkWasDrawing = false;
        var dashes = new List<List<Point>>();
        List<Point>? current = drawing ? [contour[0]] : null;
        var segmentCount = closed ? contour.Count : contour.Count - 1;

        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var start = contour[segmentIndex];
            var end = contour[(segmentIndex + 1) % contour.Count];
            var delta = Subtract(end, start);
            var segmentLength = MathF.Sqrt(LengthSquared(delta));
            if (segmentLength <= Epsilon) continue;

            var consumed = 0f;
            while (consumed < segmentLength - Epsilon)
            {
                var chunkLength = Math.Min(segmentLength - consumed, patternRemaining);
                if (chunkLength <= Epsilon)
                {
                    AdvancePattern(pattern, ref patternIndex, ref patternRemaining, ref drawing);
                    if (drawing && current == null)
                        current = [Interpolate(start, end, consumed / segmentLength)];
                    continue;
                }

                consumed += chunkLength;
                var point = Interpolate(start, end, Math.Min(1f, consumed / segmentLength));
                lastChunkWasDrawing = drawing;
                if (drawing)
                {
                    current ??= [Interpolate(start, end, (consumed - chunkLength) / segmentLength)];
                    AddDistinct(current, point);
                }

                patternRemaining -= chunkLength;
                if (patternRemaining > Epsilon) continue;

                var wasDrawing = drawing;
                AdvancePattern(pattern, ref patternIndex, ref patternRemaining, ref drawing);
                if (wasDrawing && !drawing)
                {
                    AddDash(dashes, current);
                    current = null;
                }
                else if (!wasDrawing && drawing)
                {
                    current = [point];
                }
            }

            if (drawing && current != null) AddDistinct(current, end);
        }

        AddDash(dashes, current);
        if (closed && startsDrawing && lastChunkWasDrawing && dashes.Count > 1)
        {
            var first = dashes[0];
            var last = dashes[^1];
            foreach (var point in first.Skip(1)) AddDistinct(last, point);
            dashes.RemoveAt(dashes.Count - 1);
            dashes[0] = last;
        }

        return dashes;
    }

    private static bool TryNormalizeDashPattern(float[]? dashArray, out float[] pattern)
    {
        pattern = [];
        if (dashArray is not { Length: > 0 }) return false;

        var length = dashArray.Length % 2 == 0 ? dashArray.Length : dashArray.Length * 2;
        pattern = new float[length];
        var total = 0f;
        for (var i = 0; i < length; i++)
        {
            var value = dashArray[i % dashArray.Length];
            if (!float.IsFinite(value) || value < 0)
            {
                pattern = [];
                return false;
            }
            pattern[i] = value;
            total += value;
        }

        if (total > Epsilon && float.IsFinite(total)) return true;
        pattern = [];
        return false;
    }

    private static void AdvancePattern(
        IReadOnlyList<float> pattern,
        ref int patternIndex,
        ref float patternRemaining,
        ref bool drawing)
    {
        patternIndex = (patternIndex + 1) % pattern.Count;
        patternRemaining = pattern[patternIndex];
        drawing = !drawing;
        AdvancePastEmptyPatternEntries(pattern, ref patternIndex, ref patternRemaining, ref drawing);
    }

    private static void AdvancePastEmptyPatternEntries(
        IReadOnlyList<float> pattern,
        ref int patternIndex,
        ref float patternRemaining,
        ref bool drawing)
    {
        for (var i = 0; i < pattern.Count && patternRemaining <= Epsilon; i++)
        {
            patternIndex = (patternIndex + 1) % pattern.Count;
            patternRemaining = pattern[patternIndex];
            drawing = !drawing;
        }
    }

    private static void AddDash(List<List<Point>> dashes, List<Point>? dash)
    {
        if (dash is { Count: >= 2 } && DistanceSquared(dash[0], dash[^1]) > Epsilon)
            dashes.Add(dash);
        else if (dash is { Count: >= 3 })
            dashes.Add(dash);
    }

    private static void AddDistinct(List<Point> points, Point point)
    {
        if (points.Count == 0 || DistanceSquared(points[^1], point) > Epsilon)
            points.Add(point);
    }

    private static Point Interpolate(Point start, Point end, float amount)
        => new(start.X + (end.X - start.X) * amount, start.Y + (end.Y - start.Y) * amount);

    private static float PositiveModulo(float value, float modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static void AppendSegment(
        Point start,
        Point end,
        Point normal,
        float solidHalfWidth,
        float transparentHalfWidth,
        uint color,
        uint transparent,
        float u0,
        float v0,
        float u1,
        float v1,
        Func<Point, Point> transform,
        List<Vertex2D> vertices,
        List<uint> indices)
    {
        var solidNormal = Scale(normal, solidHalfWidth);
        var transparentNormal = Scale(normal, transparentHalfWidth);
        var baseIndex = (uint)vertices.Count;
        AddVertex(vertices, Add(start, transparentNormal), u0, v0, transparent, transform);
        AddVertex(vertices, Add(start, solidNormal), u0, v0, color, transform);
        AddVertex(vertices, Subtract(start, solidNormal), u0, v0, color, transform);
        AddVertex(vertices, Subtract(start, transparentNormal), u0, v0, transparent, transform);
        AddVertex(vertices, Add(end, transparentNormal), u1, v1, transparent, transform);
        AddVertex(vertices, Add(end, solidNormal), u1, v1, color, transform);
        AddVertex(vertices, Subtract(end, solidNormal), u1, v1, color, transform);
        AddVertex(vertices, Subtract(end, transparentNormal), u1, v1, transparent, transform);

        AddQuad(indices, baseIndex, baseIndex + 4, baseIndex + 5, baseIndex + 1);
        AddQuad(indices, baseIndex + 1, baseIndex + 5, baseIndex + 6, baseIndex + 2);
        AddQuad(indices, baseIndex + 2, baseIndex + 6, baseIndex + 7, baseIndex + 3);
    }

    private static void AppendJoin(
        Point center,
        Point previousTangent,
        Point previousNormal,
        Point nextTangent,
        Point nextNormal,
        float solidHalfWidth,
        float transparentHalfWidth,
        LineJoin join,
        float miterLimit,
        uint color,
        uint transparent,
        float u,
        float v,
        Func<Point, Point> transform,
        List<Vertex2D> vertices,
        List<uint> indices)
    {
        var cross = Cross(previousTangent, nextTangent);
        var dot = Dot(previousTangent, nextTangent);
        if (MathF.Abs(cross) <= Epsilon && dot > 0) return;

        var side = cross >= 0 ? -1f : 1f;
        var previousOffset = Scale(previousNormal, side);
        var nextOffset = Scale(nextNormal, side);
        var solid = new List<Point>();
        var outer = new List<Point>();

        if (join == LineJoin.Round)
        {
            AppendArcPoints(solid, center, previousOffset, nextOffset, solidHalfWidth, cross);
            AppendArcPoints(outer, center, previousOffset, nextOffset, transparentHalfWidth, cross);
        }
        else if (join == LineJoin.Miter && TryCreateMiter(center, previousTangent, previousOffset,
                     nextTangent, nextOffset, solidHalfWidth, transparentHalfWidth, miterLimit,
                     out var solidMiter, out var transparentMiter))
        {
            solid.Add(Add(center, Scale(previousOffset, solidHalfWidth)));
            solid.Add(solidMiter);
            solid.Add(Add(center, Scale(nextOffset, solidHalfWidth)));
            outer.Add(Add(center, Scale(previousOffset, transparentHalfWidth)));
            outer.Add(transparentMiter);
            outer.Add(Add(center, Scale(nextOffset, transparentHalfWidth)));
        }
        else
        {
            solid.Add(Add(center, Scale(previousOffset, solidHalfWidth)));
            solid.Add(Add(center, Scale(nextOffset, solidHalfWidth)));
            outer.Add(Add(center, Scale(previousOffset, transparentHalfWidth)));
            outer.Add(Add(center, Scale(nextOffset, transparentHalfWidth)));
        }

        AppendFanAndFeather(center, solid, outer, color, transparent, u, v, transform, vertices, indices);
    }

    private static bool TryCreateMiter(
        Point center,
        Point previousTangent,
        Point previousOffset,
        Point nextTangent,
        Point nextOffset,
        float solidHalfWidth,
        float transparentHalfWidth,
        float miterLimit,
        out Point solidMiter,
        out Point transparentMiter)
    {
        solidMiter = default;
        transparentMiter = default;
        if (!float.IsFinite(miterLimit) || miterLimit <= 0) return false;

        var first = Add(center, Scale(previousOffset, solidHalfWidth));
        var second = Add(center, Scale(nextOffset, solidHalfWidth));
        if (!TryIntersectLines(first, previousTangent, second, nextTangent, out solidMiter)) return false;
        if (DistanceSquared(center, solidMiter) > miterLimit * miterLimit * solidHalfWidth * solidHalfWidth)
            return false;

        first = Add(center, Scale(previousOffset, transparentHalfWidth));
        second = Add(center, Scale(nextOffset, transparentHalfWidth));
        return TryIntersectLines(first, previousTangent, second, nextTangent, out transparentMiter);
    }

    private static void AppendCap(
        Point center,
        Point outward,
        Point normal,
        float solidHalfWidth,
        float transparentHalfWidth,
        LineCap cap,
        uint color,
        uint transparent,
        float u,
        float v,
        Func<Point, Point> transform,
        List<Vertex2D> vertices,
        List<uint> indices)
    {
        if (cap == LineCap.Round)
        {
            var solid = new List<Point>();
            var outer = new List<Point>();
            var centerAngle = MathF.Atan2(outward.Y, outward.X);
            const int segmentCount = 12;
            for (var i = 0; i <= segmentCount; i++)
            {
                var angle = centerAngle - MathF.PI / 2f + MathF.PI * i / segmentCount;
                var direction = new Point(MathF.Cos(angle), MathF.Sin(angle));
                solid.Add(Add(center, Scale(direction, solidHalfWidth)));
                outer.Add(Add(center, Scale(direction, transparentHalfWidth)));
            }
            AppendFanAndFeather(center, solid, outer, color, transparent, u, v, transform, vertices, indices);
            return;
        }

        var solidNormal = Scale(normal, solidHalfWidth);
        var transparentNormal = Scale(normal, transparentHalfWidth);
        var outerOffset = Scale(outward, Math.Max(0, transparentHalfWidth - solidHalfWidth));
        var baseIndex = (uint)vertices.Count;
        AddVertex(vertices, Add(center, solidNormal), u, v, color, transform);
        AddVertex(vertices, Subtract(center, solidNormal), u, v, color, transform);
        AddVertex(vertices, Add(Add(center, outerOffset), transparentNormal), u, v, transparent, transform);
        AddVertex(vertices, Subtract(Add(center, outerOffset), transparentNormal), u, v, transparent, transform);
        AddQuad(indices, baseIndex, baseIndex + 2, baseIndex + 3, baseIndex + 1);
    }

    private static void AppendArcPoints(List<Point> points, Point center, Point from, Point to, float radius, float cross)
    {
        var start = MathF.Atan2(from.Y, from.X);
        var end = MathF.Atan2(to.Y, to.X);
        var sweep = end - start;
        if (cross >= 0)
        {
            while (sweep < 0) sweep += MathF.Tau;
        }
        else
        {
            while (sweep > 0) sweep -= MathF.Tau;
        }
        if (MathF.Abs(sweep) > MathF.PI) sweep += sweep > 0 ? -MathF.Tau : MathF.Tau;

        var segmentCount = Math.Max(2, (int)MathF.Ceiling(MathF.Abs(sweep) / (MathF.PI / 12f)));
        for (var i = 0; i <= segmentCount; i++)
        {
            var angle = start + sweep * i / segmentCount;
            points.Add(Add(center, new Point(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius)));
        }
    }

    private static void AppendFanAndFeather(
        Point center,
        IReadOnlyList<Point> solid,
        IReadOnlyList<Point> outer,
        uint color,
        uint transparent,
        float u,
        float v,
        Func<Point, Point> transform,
        List<Vertex2D> vertices,
        List<uint> indices)
    {
        if (solid.Count < 2 || solid.Count != outer.Count) return;

        var centerIndex = (uint)vertices.Count;
        AddVertex(vertices, center, u, v, color, transform);
        for (var i = 0; i < solid.Count; i++)
        {
            AddVertex(vertices, solid[i], u, v, color, transform);
            AddVertex(vertices, outer[i], u, v, transparent, transform);
        }

        for (var i = 0; i < solid.Count - 1; i++)
        {
            var solid0 = centerIndex + 1u + (uint)(i * 2);
            var outer0 = solid0 + 1;
            var solid1 = solid0 + 2;
            var outer1 = solid0 + 3;
            indices.Add(centerIndex); indices.Add(solid0); indices.Add(solid1);
            AddQuad(indices, solid0, outer0, outer1, solid1);
        }
    }

    private static bool TryGetDirection(Point start, Point end, out Point tangent, out Point normal)
    {
        var delta = Subtract(end, start);
        var length = MathF.Sqrt(LengthSquared(delta));
        if (length <= Epsilon)
        {
            tangent = default;
            normal = default;
            return false;
        }
        tangent = Scale(delta, 1f / length);
        normal = new Point(-tangent.Y, tangent.X);
        return true;
    }

    private static bool TryIntersectLines(Point first, Point firstDirection, Point second, Point secondDirection, out Point intersection)
    {
        var denominator = Cross(firstDirection, secondDirection);
        if (MathF.Abs(denominator) <= Epsilon)
        {
            intersection = default;
            return false;
        }
        var distance = Cross(Subtract(second, first), secondDirection) / denominator;
        intersection = Add(first, Scale(firstDirection, distance));
        return true;
    }

    private static void AddVertex(List<Vertex2D> vertices, Point point, float u, float v, uint color, Func<Point, Point> transform)
    {
        var transformed = transform(point);
        vertices.Add(new Vertex2D(transformed.X, transformed.Y, u, v, color));
    }

    private static void AddQuad(List<uint> indices, uint a, uint b, uint c, uint d)
    {
        indices.Add(a); indices.Add(b); indices.Add(c);
        indices.Add(a); indices.Add(c); indices.Add(d);
    }

    private static Point Add(Point left, Point right) => new(left.X + right.X, left.Y + right.Y);
    private static Point Subtract(Point left, Point right) => new(left.X - right.X, left.Y - right.Y);
    private static Point Scale(Point point, float value) => new(point.X * value, point.Y * value);
    private static float Dot(Point left, Point right) => left.X * right.X + left.Y * right.Y;
    private static float Cross(Point left, Point right) => left.X * right.Y - left.Y * right.X;
    private static float LengthSquared(Point point) => Dot(point, point);
    private static float DistanceSquared(Point left, Point right) => LengthSquared(Subtract(left, right));
}
