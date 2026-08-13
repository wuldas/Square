namespace Square.Graphics;

/// <summary>几何图形基类。</summary>
public abstract class Geometry { }

/// <summary>圆角矩形单个角的椭圆半径。</summary>
public readonly record struct CornerRadius(float X, float Y)
{
    /// <summary>兼容圆形圆角的半径。</summary>
    public CornerRadius(float radius) : this(radius, radius) { }
}

/// <summary>矩形几何。</summary>
public sealed class RectGeometry : Geometry
{
    /// <summary>矩形。</summary>
    public Rect Rect { get; set; }
    /// <summary>构造矩形几何。</summary>
    public RectGeometry(Rect rect) { Rect = rect; }
}

/// <summary>圆角矩形几何。</summary>
public sealed class RoundedRectGeometry : Geometry
{
    /// <summary>外接矩形。</summary>
    public Rect Rect { get; set; }
    /// <summary>X 方向圆角半径。</summary>
    public float RadiusX { get; set; }
    /// <summary>Y 方向圆角半径。</summary>
    public float RadiusY { get; set; }

    /// <summary>左上角圆角半径。</summary>
    public CornerRadius TopLeft { get; set; }
    /// <summary>右上角圆角半径。</summary>
    public CornerRadius TopRight { get; set; }
    /// <summary>右下角圆角半径。</summary>
    public CornerRadius BottomRight { get; set; }
    /// <summary>左下角圆角半径。</summary>
    public CornerRadius BottomLeft { get; set; }

    /// <summary>是否四角使用相同的圆角。</summary>
    public bool IsUniform => TopLeft == TopRight && TopLeft == BottomRight && TopLeft == BottomLeft;

    /// <summary>构造圆角矩形几何。</summary>
    public RoundedRectGeometry(Rect rect, float radiusX, float radiusY)
    {
        Rect = rect;
        RadiusX = radiusX;
        RadiusY = radiusY;
        TopLeft = TopRight = BottomRight = BottomLeft = new CornerRadius(radiusX, radiusY);
    }

    /// <summary>构造支持四角独立椭圆半径的圆角矩形。</summary>
    public RoundedRectGeometry(
        Rect rect,
        CornerRadius topLeft,
        CornerRadius topRight,
        CornerRadius bottomRight,
        CornerRadius bottomLeft)
    {
        Rect = rect;
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
        RadiusX = topLeft.X;
        RadiusY = topLeft.Y;
    }

    /// <summary>将四角圆角转换为闭合路径。</summary>
    public PathGeometry ToPath()
    {
        var path = PathGeometry.Create();
        var left = Rect.Left;
        var top = Rect.Top;
        var right = Rect.Right;
        var bottom = Rect.Bottom;
        path.MoveTo(new Point(left + TopLeft.X, top));
        path.LineTo(new Point(right - TopRight.X, top));
        AddCorner(path, right - 2 * TopRight.X, top, TopRight, -90);
        path.LineTo(new Point(right, bottom - BottomRight.Y));
        AddCorner(path, right - 2 * BottomRight.X, bottom - 2 * BottomRight.Y, BottomRight, 0);
        path.LineTo(new Point(left + BottomLeft.X, bottom));
        AddCorner(path, left, bottom - 2 * BottomLeft.Y, BottomLeft, 90);
        path.LineTo(new Point(left, top + TopLeft.Y));
        AddCorner(path, left, top, TopLeft, 180);
        path.Close();
        return path;
    }

    private static void AddCorner(PathGeometry path, float x, float y, CornerRadius radius, float startAngle)
    {
        if (radius.X <= 0 || radius.Y <= 0) return;
        path.ArcTo(new Rect(x, y, radius.X * 2, radius.Y * 2), startAngle, 90);
    }
}

/// <summary>椭圆几何。</summary>
public sealed class EllipseGeometry : Geometry
{
    /// <summary>圆心。</summary>
    public Point Center { get; set; }
    /// <summary>X 方向半径。</summary>
    public float RadiusX { get; set; }
    /// <summary>Y 方向半径。</summary>
    public float RadiusY { get; set; }

    /// <summary>构造椭圆几何。</summary>
    public EllipseGeometry(Point center, float radiusX, float radiusY)
    {
        Center = center; RadiusX = radiusX; RadiusY = radiusY;
    }
}