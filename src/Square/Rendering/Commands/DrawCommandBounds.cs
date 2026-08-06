using Square.Graphics;
using System.Numerics;

namespace Square.Rendering.Commands;

internal static class DrawCommandBounds
{
    /// <summary>计算命令列表在变换与裁剪后的可视边界，可选择在无内容时回退到布局边界。</summary>
    public static Rect Calculate(
        IReadOnlyList<DrawCommand> commands,
        Rect fallbackBounds,
        bool fallbackWhenEmpty = true)
    {
        var bounds = Rect.Empty;
        var hasBounds = false;
        var transforms = new Stack<Matrix3x2>();
        var currentTransform = Matrix3x2.Identity;
        var clips = new Stack<Rect>();

        foreach (var command in commands)
        {
            switch (command)
            {
                case PushTransformCommand push:
                    transforms.Push(currentTransform);
                    currentTransform = push.Matrix * currentTransform;
                    continue;
                case PopTransformCommand:
                    currentTransform = transforms.Count > 0 ? transforms.Pop() : Matrix3x2.Identity;
                    continue;
                case PushClipCommand push:
                    PushClip(clips, push.Rect);
                    continue;
                case PushGeometryClipCommand push:
                    PushClip(clips, GetGeometryBounds(push.Geometry));
                    continue;
                case PopClipCommand:
                    if (clips.Count > 0) clips.Pop();
                    continue;
            }

            var commandBounds = ClipBounds(TransformBounds(GetBounds(command, fallbackBounds), currentTransform), clips);
            if (commandBounds.IsEmpty) continue;
            bounds = hasBounds ? Rect.Union(bounds, commandBounds) : commandBounds;
            hasBounds = true;
        }

        return hasBounds ? bounds : fallbackWhenEmpty ? fallbackBounds : Rect.Empty;
    }

    private static Rect GetBounds(DrawCommand command, Rect fallbackBounds) => command switch
    {
        FillRectCommand fill => fill.Rect,
        DrawRectCommand draw => draw.Rect.Inflate(Math.Max(1f, draw.Pen.Width) / 2f, Math.Max(1f, draw.Pen.Width) / 2f),
        DrawTextCommand text => TextMetrics.MeasureInkBounds(text.Text, text.Origin),
        DrawImageCommand image => image.Dest,
        FillGeometryCommand fill => GetGeometryBounds(fill.Geometry),
        DrawGeometryCommand draw => GetGeometryBounds(draw.Geometry).Inflate(Math.Max(1f, draw.Pen.Width) / 2f, Math.Max(1f, draw.Pen.Width) / 2f),
        PushLayerCommand layer => layer.Bounds,
        ClearCommand => fallbackBounds,
        FillPathCommand fill => GetPathBounds(fill.Path),
        DrawPathCommand draw => GetPathBounds(draw.Path).Inflate(Math.Max(1f, draw.Pen.Width) / 2f, Math.Max(1f, draw.Pen.Width) / 2f),
        _ => Rect.Empty
    };

    private static Rect GetGeometryBounds(Geometry geometry) => geometry switch
    {
        RectGeometry rect => rect.Rect,
        RoundedRectGeometry rounded => rounded.Rect,
        EllipseGeometry ellipse => new Rect(
            ellipse.Center.X - ellipse.RadiusX,
            ellipse.Center.Y - ellipse.RadiusY,
            ellipse.RadiusX * 2,
            ellipse.RadiusY * 2),
        PathGeometry path => GetPathBounds(path),
        _ => Rect.Empty
    };

    private static Rect GetPathBounds(PathGeometry path)
    {
        var bounds = Rect.Empty;
        var hasBounds = false;

        foreach (var command in path.Commands)
        {
            var commandBounds = command switch
            {
                MoveToCmd move => new Rect(move.Point.X, move.Point.Y, 1, 1),
                LineToCmd line => new Rect(line.Point.X, line.Point.Y, 1, 1),
                ArcToCmd arc => arc.Oval,
                _ => Rect.Empty
            };
            if (commandBounds.IsEmpty) continue;
            bounds = hasBounds ? Rect.Union(bounds, commandBounds) : commandBounds;
            hasBounds = true;
        }

        return hasBounds ? bounds : Rect.Empty;
    }

    private static void PushClip(Stack<Rect> clips, Rect clip)
    {
        if (clips.Count > 0)
            clip = Rect.Intersect(clips.Peek(), clip);
        clips.Push(clip);
    }

    private static Rect ClipBounds(Rect bounds, Stack<Rect> clips)
    {
        if (bounds.IsEmpty || clips.Count == 0) return bounds;
        return Rect.Intersect(bounds, clips.Peek());
    }

    private static Rect TransformBounds(Rect bounds, Matrix3x2 transform)
    {
        if (bounds.IsEmpty || transform.IsIdentity) return bounds;

        var p1 = Vector2.Transform(new Vector2(bounds.Left, bounds.Top), transform);
        var p2 = Vector2.Transform(new Vector2(bounds.Right, bounds.Top), transform);
        var p3 = Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), transform);
        var p4 = Vector2.Transform(new Vector2(bounds.Left, bounds.Bottom), transform);
        var left = MathF.Min(MathF.Min(p1.X, p2.X), MathF.Min(p3.X, p4.X));
        var top = MathF.Min(MathF.Min(p1.Y, p2.Y), MathF.Min(p3.Y, p4.Y));
        var right = MathF.Max(MathF.Max(p1.X, p2.X), MathF.Max(p3.X, p4.X));
        var bottom = MathF.Max(MathF.Max(p1.Y, p2.Y), MathF.Max(p3.Y, p4.Y));
        return new Rect(left, top, right - left, bottom - top);
    }
}
