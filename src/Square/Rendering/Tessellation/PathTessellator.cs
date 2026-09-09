using System.Numerics;
using Square.Graphics;

namespace Square.Rendering.Tessellation;

/// <summary>共享路径展平、曲线误差估计和 EvenOdd 三角化辅助。</summary>
internal static class PathTessellator
{
    public static List<List<Point>> FlattenPath(PathGeometry path, Matrix3x2 physicalTransform)
    {
        var contours = new List<List<Point>>();
        var current = new List<Point>();
        Point first = default;

        foreach (var command in path.Commands)
        {
            switch (command)
            {
                case MoveToCmd move:
                    if (current.Count > 0) contours.Add(current);
                    current = [move.Point];
                    first = move.Point;
                    break;
                case LineToCmd line:
                    current.Add(line.Point);
                    break;
                case ArcToCmd arc:
                    FlattenArc(current, arc, physicalTransform);
                    break;
                case CloseCmd:
                    if (current.Count > 0)
                    {
                        if (current[^1] != first) current.Add(first);
                        contours.Add(current);
                        current = [];
                    }
                    break;
            }
        }

        if (current.Count > 0) contours.Add(current);
        return contours;
    }

    public static int GetCurveSegmentCount(
        float radiusX,
        float radiusY,
        float sweepRadians,
        Matrix3x2 physicalTransform)
    {
        const float maxSagitta = 0.2f;
        const int minFullCircleSegments = 32;
        const int maxFullCircleSegments = 256;

        var xScale = MathF.Sqrt(
            physicalTransform.M11 * physicalTransform.M11 +
            physicalTransform.M12 * physicalTransform.M12);
        var yScale = MathF.Sqrt(
            physicalTransform.M21 * physicalTransform.M21 +
            physicalTransform.M22 * physicalTransform.M22);
        var physicalRadius = MathF.Max(MathF.Abs(radiusX) * xScale, MathF.Abs(radiusY) * yScale);
        var sweep = Math.Clamp(MathF.Abs(sweepRadians), 0, MathF.Tau);
        if (physicalRadius <= maxSagitta || sweep <= float.Epsilon) return 1;

        var segmentAngle = 2f * MathF.Acos(Math.Clamp(1f - maxSagitta / physicalRadius, -1f, 1f));
        var adaptive = segmentAngle > float.Epsilon
            ? (int)MathF.Ceiling(sweep / segmentAngle)
            : maxFullCircleSegments;
        var minimum = Math.Max(1, (int)MathF.Ceiling(minFullCircleSegments * sweep / MathF.Tau));
        var maximum = Math.Max(minimum, (int)MathF.Ceiling(maxFullCircleSegments * sweep / MathF.Tau));
        return Math.Clamp(adaptive, minimum, maximum);
    }

    public static float GetLogicalFeatherWidth(Matrix3x2 physicalTransform)
    {
        var xScale = MathF.Sqrt(
            physicalTransform.M11 * physicalTransform.M11 +
            physicalTransform.M12 * physicalTransform.M12);
        var yScale = MathF.Sqrt(
            physicalTransform.M21 * physicalTransform.M21 +
            physicalTransform.M22 * physicalTransform.M22);
        return 1f / MathF.Max(0.001f, MathF.Max(xScale, yScale));
    }

    public static LibTessDotNet.Tess Triangulate(IReadOnlyList<List<Point>> contours)
    {
        var tess = new LibTessDotNet.Tess();
        foreach (var contour in contours)
        {
            if (contour.Count < 3) continue;
            var points = new LibTessDotNet.ContourVertex[contour.Count];
            for (var i = 0; i < contour.Count; i++)
            {
                points[i] = new LibTessDotNet.ContourVertex
                {
                    Position = new LibTessDotNet.Vec3 { X = contour[i].X, Y = contour[i].Y, Z = 0 }
                };
            }
            tess.AddContour(points, LibTessDotNet.ContourOrientation.Original);
        }

        tess.Tessellate(LibTessDotNet.WindingRule.EvenOdd, LibTessDotNet.ElementType.Polygons, 3);
        return tess;
    }

    private static void FlattenArc(List<Point> contour, ArcToCmd arc, Matrix3x2 physicalTransform)
    {
        var cx = arc.Oval.X + arc.Oval.Width / 2;
        var cy = arc.Oval.Y + arc.Oval.Height / 2;
        var rx = arc.Oval.Width / 2;
        var ry = arc.Oval.Height / 2;
        var startRadians = arc.StartAngle * MathF.PI / 180f;
        var sweepRadians = arc.SweepAngle * MathF.PI / 180f;
        var segments = GetCurveSegmentCount(rx, ry, MathF.Abs(sweepRadians), physicalTransform);

        for (var i = 1; i <= segments; i++)
        {
            var angle = startRadians + sweepRadians * i / segments;
            contour.Add(new Point(cx + rx * MathF.Cos(angle), cy + ry * MathF.Sin(angle)));
        }
    }
}
