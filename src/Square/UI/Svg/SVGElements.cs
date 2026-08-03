using System.Globalization;
using System.Numerics;
using Square.Graphics;
using Square.Graphics.Svg;

namespace Square.UI.Svg;

/// <summary>SVG 根元素（对齐浏览器 <c>&lt;svg&gt;</c>），承载独立 <see cref="SVGDocument"/>。</summary>
public sealed class SVGSVGElement : SVGElement, Document.IEmbeddedDocumentRoot
{
    /// <summary>构造根元素并创建附属文档。</summary>
    public SVGSVGElement() => SvgDocument = new SVGDocument(this);

    /// <summary>根元素拥有的 SVG 文档。</summary>
    public SVGDocument SvgDocument { get; }
    Document? Document.IEmbeddedDocumentRoot.EmbeddedDocument => SvgDocument;
    /// <inheritdoc />
    public override string TagName => "svg";
    /// <inheritdoc />
    public override bool HasCustomMeasure => true;

    /// <summary>ViewBox 属性（对齐 SVG <c>viewBox</c>）。</summary>
    public string ViewBox { get => Value("ViewBox"); set => SetProperty("ViewBox", value); }

    /// <inheritdoc />
    public override Size Measure(Size availableSize)
    {
        var viewBox = SvgValues.ParseViewBox(ViewBox);
        var width = SvgValues.Number(this, "Width", viewBox?.Width ?? 300f);
        var height = SvgValues.Number(this, "Height", viewBox?.Height ?? 150f);
        return new Size(MathF.Max(0, width), MathF.Max(0, height));
    }

    /// <inheritdoc />
    public override void Paint(IRenderContext context)
    {
        if (Geometry.IsEmpty) return;
        var viewBox = SvgValues.ParseViewBox(ViewBox) ?? new Rect(0, 0,
            SvgValues.Number(this, "Width", Geometry.Width), SvgValues.Number(this, "Height", Geometry.Height));
        if (viewBox.Width <= 0 || viewBox.Height <= 0) return;

        var scale = MathF.Min(Geometry.Width / viewBox.Width, Geometry.Height / viewBox.Height);
        var x = Geometry.X + (Geometry.Width - viewBox.Width * scale) / 2f;
        var y = Geometry.Y + (Geometry.Height - viewBox.Height * scale) / 2f;
        var matrix = Matrix3x2.CreateTranslation(-viewBox.X, -viewBox.Y) *
                     Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(x, y);

        var rootPaint = SvgPaint.Resolve(this, SvgPaint.Default);
        var rootTransform = SvgValues.Transform(GetProperty<object>("Transform")?.ToString());
        context.PushClip(Geometry);
        context.PushTransform(matrix);
        if (!rootTransform.IsIdentity) context.PushTransform(rootTransform);
        SvgRenderer.DrawChildren(context, this, rootPaint);
        if (!rootTransform.IsIdentity) context.PopTransform();
        context.PopTransform();
        context.PopClip();
    }

    private string Value(string name) => GetProperty<object>(name)?.ToString() ?? "";
}

/// <summary>SVG 分组元素（对齐 <c>&lt;g&gt;</c>）。</summary>
public sealed class SVGGElement : SVGElement
{
    /// <inheritdoc />
    public override string TagName => "g";
}
/// <summary>SVG 路径元素（对齐 <c>&lt;path&gt;</c>）。</summary>
public sealed class SVGPathElement : SVGGeometryElement
{
    /// <inheritdoc />
    public override string TagName => "path";
    /// <inheritdoc />
    protected override Geometry? CreateGeometry() => SvgPathParser.Parse(Value("Data"));
}
/// <summary>SVG 矩形元素（对齐 <c>&lt;rect&gt;</c>）。</summary>
public sealed class SVGRectElement : SVGGeometryElement
{
    /// <inheritdoc />
    public override string TagName => "rect";
    /// <inheritdoc />
    protected override Geometry? CreateGeometry()
    {
        var width = Number("Width"); var height = Number("Height");
        if (width <= 0 || height <= 0) return null;
        var rect = new Rect(Number("X"), Number("Y"), width, height);
        var rx = Number("RadiusX"); var ry = Number("RadiusY", rx);
        return rx > 0 || ry > 0 ? new RoundedRectGeometry(rect, rx, ry) : new RectGeometry(rect);
    }
}
/// <summary>SVG 圆形元素（对齐 <c>&lt;circle&gt;</c>）。</summary>
public sealed class SVGCircleElement : SVGGeometryElement
{
    /// <inheritdoc />
    public override string TagName => "circle";
    /// <inheritdoc />
    protected override Geometry? CreateGeometry()
    {
        var radius = Number("Radius");
        return radius > 0 ? new EllipseGeometry(new Point(Number("CenterX"), Number("CenterY")), radius, radius) : null;
    }
}
/// <summary>SVG 椭圆元素（对齐 <c>&lt;ellipse&gt;</c>）。</summary>
public sealed class SVGEllipseElement : SVGGeometryElement
{
    /// <inheritdoc />
    public override string TagName => "ellipse";
    /// <inheritdoc />
    protected override Geometry? CreateGeometry()
    {
        var rx = Number("RadiusX"); var ry = Number("RadiusY");
        return rx > 0 && ry > 0 ? new EllipseGeometry(new Point(Number("CenterX"), Number("CenterY")), rx, ry) : null;
    }
}
/// <summary>SVG 直线元素（对齐 <c>&lt;line&gt;</c>）。</summary>
public sealed class SVGLineElement : SVGGeometryElement
{
    /// <inheritdoc />
    public override string TagName => "line";
    /// <inheritdoc />
    protected override Geometry CreateGeometry() => PathGeometry.Create()
        .MoveTo(new Point(Number("X1"), Number("Y1"))).LineTo(new Point(Number("X2"), Number("Y2")));
}
/// <summary>SVG 折线元素（对齐 <c>&lt;polyline&gt;</c>）。</summary>
public sealed class SVGPolylineElement : SVGGeometryElement
{
    /// <inheritdoc />
    public override string TagName => "polyline";
    /// <inheritdoc />
    protected override Geometry? CreateGeometry() => CreatePoints(close: false);
}
/// <summary>SVG 多边形元素（对齐 <c>&lt;polygon&gt;</c>）。</summary>
public sealed class SVGPolygonElement : SVGGeometryElement
{
    /// <inheritdoc />
    public override string TagName => "polygon";
    /// <inheritdoc />
    protected override Geometry? CreateGeometry() => CreatePoints(close: true);
}

/// <summary>SVG 几何元素基类（对齐 <c>SVGGeometryElement</c>）。</summary>
public abstract class SVGGeometryElement : SVGElement
{
    internal void Draw(IRenderContext context, SvgPaint paint)
    {
        var geometry = CreateGeometry();
        if (geometry == null) return;
        if (paint.Fill is Color fill && fill.A > 0) context.FillGeometry(geometry, Brush.FromColor(fill));
        if (paint.Stroke is Color stroke && stroke.A > 0 && paint.StrokeWidth > 0)
            context.DrawGeometry(geometry, Pen.FromColor(stroke, paint.StrokeWidth));
    }

    /// <summary>创建几何对象。</summary>
    protected abstract Geometry? CreateGeometry();
    /// <summary>读取数值属性。</summary>
    protected float Number(string name, float fallback = 0) => SvgValues.Number(this, name, fallback);
    /// <summary>读取字符串属性。</summary>
    protected string Value(string name) => GetProperty<object>(name)?.ToString() ?? "";
    /// <summary>根据 Points 属性构建折线/多边形几何。</summary>
    protected Geometry? CreatePoints(bool close)
    {
        var values = SvgValues.NumberList(Value("Points"));
        if (values.Count < 4) return null;
        var path = PathGeometry.Create().MoveTo(new Point(values[0], values[1]));
        for (var i = 2; i + 1 < values.Count; i += 2) path.LineTo(new Point(values[i], values[i + 1]));
        return close ? path.Close() : path;
    }
}

internal static class SvgRenderer
{
    public static void DrawChildren(IRenderContext context, SVGElement parent, SvgPaint inherited)
    {
        foreach (var child in parent.Children.OfType<SVGElement>())
        {
            var paint = SvgPaint.Resolve(child, inherited);
            var transform = SvgValues.Transform(child.GetProperty<object>("Transform")?.ToString());
            if (!transform.IsIdentity) context.PushTransform(transform);
            if (child is SVGGeometryElement geometry) geometry.Draw(context, paint);
            DrawChildren(context, child, paint);
            if (!transform.IsIdentity) context.PopTransform();
        }
    }
}

internal readonly record struct SvgPaint(Color? Fill, Color? Stroke, float StrokeWidth, float Opacity)
{
    public static SvgPaint Default => new(Color.Black, null, 1f, 1f);

    public static SvgPaint Resolve(SVGElement element, SvgPaint inherited)
    {
        var localOpacity = Math.Clamp(SvgValues.StyleNumber(element, "opacity", "Opacity", 1f), 0, 1);
        var opacity = inherited.Opacity * localOpacity;
        var fillText = SvgValues.StyleValue(element, "fill", "Fill");
        var strokeText = SvgValues.StyleValue(element, "stroke", "Stroke");
        var fill = fillText == null ? SvgValues.Opacity(inherited.Fill, localOpacity) : SvgValues.Color(fillText, opacity);
        var stroke = strokeText == null ? SvgValues.Opacity(inherited.Stroke, localOpacity) : SvgValues.Color(strokeText, opacity);
        fill = SvgValues.Opacity(fill, SvgValues.StyleNumber(element, "fill-opacity", "FillOpacity", 1f));
        stroke = SvgValues.Opacity(stroke, SvgValues.StyleNumber(element, "stroke-opacity", "StrokeOpacity", 1f));
        var width = SvgValues.StyleNumber(element, "stroke-width", "StrokeWidth", inherited.StrokeWidth);
        return new SvgPaint(fill, stroke, width, opacity);
    }
}

internal static class SvgValues
{
    public static float Number(Element element, string property, float fallback = 0)
    {
        var value = element.GetProperty<object>(property);
        return value switch
        {
            byte number => number, short number => number, int number => number, long number => number,
            float number => number, double number => (float)number, decimal number => (float)number,
            _ => Parse(value?.ToString(), fallback)
        };
    }

    public static float StyleNumber(SVGElement element, string style, string property, float fallback)
    {
        var value = element.Style.Get(style) ?? "";
        return string.IsNullOrWhiteSpace(value) ? Number(element, property, fallback) : Parse(value, fallback);
    }

    public static string? StyleValue(SVGElement element, string style, string property)
    {
        var value = element.Style.Get(style) ?? "";
        if (!string.IsNullOrWhiteSpace(value)) return value;
        return element.GetProperty<object>(property)?.ToString();
    }

    public static Rect? ParseViewBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var numbers = NumberList(value);
        return numbers.Count == 4 && numbers[2] > 0 && numbers[3] > 0
            ? new Rect(numbers[0], numbers[1], numbers[2], numbers[3]) : null;
    }

    public static List<float> NumberList(string? value)
    {
        var numbers = new List<float>();
        if (string.IsNullOrWhiteSpace(value)) return numbers;
        var index = 0;
        while (SvgPathParser.TryReadNumber(value, ref index, out var number)) numbers.Add(number);
        return numbers;
    }

    public static Matrix3x2 Transform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Matrix3x2.Identity;
        var result = Matrix3x2.Identity; var index = 0;
        while (index < value.Length)
        {
            while (index < value.Length && (char.IsWhiteSpace(value[index]) || value[index] == ',')) index++;
            var start = index; while (index < value.Length && char.IsLetter(value[index])) index++;
            if (start == index) break;
            var name = value[start..index].ToLowerInvariant();
            while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
            if (index >= value.Length || value[index++] != '(') break;
            var end = value.IndexOf(')', index); if (end < 0) break;
            var args = NumberList(value[index..end]); index = end + 1;
            var matrix = name switch
            {
                "translate" when args.Count >= 1 => Matrix3x2.CreateTranslation(args[0], args.Count > 1 ? args[1] : 0),
                "scale" when args.Count >= 1 => Matrix3x2.CreateScale(args[0], args.Count > 1 ? args[1] : args[0]),
                "rotate" when args.Count >= 1 => Matrix3x2.CreateRotation(args[0] * MathF.PI / 180f,
                    args.Count >= 3 ? new Vector2(args[1], args[2]) : Vector2.Zero),
                "matrix" when args.Count >= 6 => new Matrix3x2(args[0], args[1], args[2], args[3], args[4], args[5]),
                _ => Matrix3x2.Identity
            };
            result = matrix * result;
        }
        return result;
    }

    public static Color? Color(string value, float opacity)
    {
        value = value.Trim();
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        var color = value.ToLowerInvariant() switch
        {
            "black" => Graphics.Color.Black, "white" => Graphics.Color.White, "red" => Graphics.Color.Red,
            "green" => Graphics.Color.Green, "blue" => Graphics.Color.Blue, "transparent" => Graphics.Color.Transparent,
            _ when value.StartsWith('#') => Hex(value), _ => Graphics.Color.Black
        };
        return Opacity(color, opacity);
    }

    public static Color? Opacity(Color? color, float opacity) => color is Color value
        ? new Color(value.R, value.G, value.B, (byte)Math.Clamp(MathF.Round(value.A * opacity), 0, 255)) : null;

    private static Color Hex(string value)
    {
        var text = value[1..];
        if (text.Length == 4) return new Color(Nibble(text[0]), Nibble(text[1]), Nibble(text[2]), Nibble(text[3]));
        if (text.Length == 8) return new Color(Convert.ToByte(text[0..2], 16), Convert.ToByte(text[2..4], 16),
            Convert.ToByte(text[4..6], 16), Convert.ToByte(text[6..8], 16));
        return Graphics.Color.Parse(value);
    }

    private static byte Nibble(char value) => (byte)(Convert.ToByte(value.ToString(), 16) * 17);
    private static float Parse(string? value, float fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = value.Trim(); if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : fallback;
    }
}
