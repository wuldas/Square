using System.Numerics;
using AndroidColor = global::Android.Graphics.Color;
using AndroidBitmap = global::Android.Graphics.Bitmap;
using AndroidGraphicsCanvas = global::Android.Graphics.Canvas;
using AndroidDashPathEffect = global::Android.Graphics.DashPathEffect;
using AndroidLinearGradient = global::Android.Graphics.LinearGradient;
using AndroidMatrix = global::Android.Graphics.Matrix;
using AndroidPaint = global::Android.Graphics.Paint;
using AndroidPath = global::Android.Graphics.Path;
using AndroidPicture = global::Android.Graphics.Picture;
using AndroidPorterDuff = global::Android.Graphics.PorterDuff;
using AndroidRadialGradient = global::Android.Graphics.RadialGradient;
using AndroidRect = global::Android.Graphics.Rect;
using AndroidRectF = global::Android.Graphics.RectF;
using AndroidShader = global::Android.Graphics.Shader;
using AndroidTypeface = global::Android.Graphics.Typeface;
using AndroidPaintFlags = global::Android.Graphics.PaintFlags;
using AndroidTypefaceStyle = global::Android.Graphics.TypefaceStyle;
using SquareBitmap = Square.Graphics.Bitmap;
using SquareColor = Square.Graphics.Color;
using SquareImage = Square.Graphics.Image;
using SquarePath = Square.Graphics.PathGeometry;
using SquareRect = Square.Graphics.Rect;
using SquareFontStyle = Square.Graphics.FontStyle;
using Square.Graphics;
using SquareFontWeight = Square.Graphics.FontWeight;

namespace Square.Backends.AndroidCanvas;

/// <summary>供 Android View 在真实 Canvas 上绘制已提交帧的上下文。</summary>
public interface IAndroidCanvasRenderContext
{
    /// <summary>将最近一次提交的帧绘制到 Android Canvas。</summary>
    void Draw(AndroidGraphicsCanvas canvas);
}

/// <summary>将 Square 绘制命令录制为 Android Picture 并由 View Canvas 直接呈现。</summary>
internal sealed class AndroidCanvasRenderContext : IRenderContext, IDpiResizableRenderContext, IAndroidCanvasRenderContext
{
    private readonly Dictionary<SquareBitmap, CachedBitmap> _imageCache = [];
    private AndroidGraphicsCanvas? _recordingCanvas;
    private AndroidPicture? _picture;
    private Size _canvasSize;
    private float _dpiScale;
    private int _saveDepth;
    private bool _recording;
    private bool _disposed;

    public AndroidCanvasRenderContext(Size canvasSize, float dpiScale)
        => Resize(canvasSize, dpiScale);

    public Size CanvasSize => _canvasSize;
    public float DpiScale => _dpiScale;
    public bool SupportsPartialRendering => false;

    public void PushTransform(Matrix3x2 matrix)
    {
        EnsureRecording();
        _recordingCanvas!.Save();
        using var androidMatrix = ToAndroidMatrix(matrix);
        _recordingCanvas.Concat(androidMatrix);
        _saveDepth++;
    }

    public void PopTransform()
    {
        EnsureRecording();
        if (_saveDepth == 0) return;
        _recordingCanvas!.Restore();
        _saveDepth--;
    }

    public void PushClip(SquareRect rect)
    {
        EnsureRecording();
        _recordingCanvas!.Save();
        _recordingCanvas.ClipRect(ToRectF(rect));
        _saveDepth++;
    }

    public void PushClip(Geometry geometry)
    {
        EnsureRecording();
        _recordingCanvas!.Save();
        using var path = ToPath(geometry);
        _recordingCanvas.ClipPath(path);
        _saveDepth++;
    }

    public void PopClip()
    {
        EnsureRecording();
        if (_saveDepth == 0) return;
        _recordingCanvas!.Restore();
        _saveDepth--;
    }

    public void FillRect(SquareRect rect, Brush brush)
    {
        EnsureRecording();
        using var paint = CreatePaint(brush, AndroidPaint.Style.Fill!);
        _recordingCanvas!.DrawRect(ToRectF(rect), paint);
    }

    public void DrawRect(SquareRect rect, Pen pen)
    {
        EnsureRecording();
        using var paint = CreateStrokePaint(pen);
        _recordingCanvas!.DrawRect(ToRectF(rect), paint);
    }

    public void FillPath(SquarePath path, Brush brush)
    {
        EnsureRecording();
        using var androidPath = ToPath(path);
        using var paint = CreatePaint(brush, AndroidPaint.Style.Fill!);
        _recordingCanvas!.DrawPath(androidPath, paint);
    }

    public void DrawPath(SquarePath path, Pen pen)
    {
        EnsureRecording();
        using var androidPath = ToPath(path);
        using var paint = CreateStrokePaint(pen);
        _recordingCanvas!.DrawPath(androidPath, paint);
    }

    public void FillGeometry(Geometry geometry, Brush brush)
    {
        EnsureRecording();
        using var paint = CreatePaint(brush, AndroidPaint.Style.Fill!);
        DrawGeometry(geometry, paint);
    }

    public void DrawGeometry(Geometry geometry, Pen pen)
    {
        EnsureRecording();
        using var paint = CreateStrokePaint(pen);
        DrawGeometry(geometry, paint);
    }

    public void DrawText(TextLayout text, Square.Graphics.Point origin, Brush brush)
    {
        EnsureRecording();
        if (string.IsNullOrEmpty(text.Text) || brush is not SolidColorBrush solid) return;

        using var paint = CreateTextPaint(text.Font, solid.Color);
        var lines = TextWrapping.Wrap(text.Text, text.MaxSize.Width, (_, rune) =>
            TextLayout.MeasureRuneAdvance(rune, text.Font), text.WrappingOptions);
        var lineHeight = TextMetrics.GetLineHeight(text.Font, text.LineHeight);
        var baselineOffset = TextMetrics.GetBaselineOffset(text.Font, lineHeight);
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var indent = text.GetLineIndent(lineIndex);
            var x = origin.X + indent + GetTextAlignmentOffset(text, line.Width + indent);
            var baseline = origin.Y + lineIndex * lineHeight + baselineOffset;
            foreach (var visualRune in text.EnumerateVisualRunes(line))
            {
                _recordingCanvas!.DrawText(visualRune.Glyph.ToString(), x, baseline, paint);
                x += visualRune.Advance;
            }
        }

        foreach (var decoration in text.GetDecorationRects(origin))
            _recordingCanvas!.DrawRect(ToRectF(decoration), paint);
    }

    public void DrawImage(SquareImage image, SquareRect dest, SquareRect? source = null)
    {
        EnsureRecording();
        if (image is not SquareBitmap bitmap || bitmap.IsDisposed || dest.IsEmpty) return;
        var cached = GetCachedBitmap(bitmap);
        var sourceRect = source ?? new SquareRect(0, 0, bitmap.Width, bitmap.Height);
        using var paint = new AndroidPaint(AndroidPaintFlags.AntiAlias | AndroidPaintFlags.FilterBitmap);
        _recordingCanvas!.DrawBitmap(cached.Bitmap, ToAndroidRect(sourceRect), ToRectF(dest), paint);
    }

    public void PushLayer(SquareRect bounds, float opacity)
    {
        EnsureRecording();
        using var paint = new AndroidPaint
        {
            Alpha = (int)MathF.Round(Math.Clamp(opacity, 0f, 1f) * 255f)
        };
        _recordingCanvas!.SaveLayer(ToRectF(bounds), paint);
        _saveDepth++;
    }

    public void PopLayer()
    {
        EnsureRecording();
        if (_saveDepth == 0) return;
        _recordingCanvas!.Restore();
        _saveDepth--;
    }

    public void Clear(SquareColor color)
    {
        EnsureRecording();
        _recordingCanvas!.DrawColor(ToAndroidColor(color), AndroidPorterDuff.Mode.Src!);
    }

    public void Clear(SquareColor color, SquareRect rect)
    {
        EnsureRecording();
        _recordingCanvas!.Save();
        try
        {
            _recordingCanvas.ClipRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
            Clear(color);
        }
        finally
        {
            _recordingCanvas.Restore();
        }
    }

    public void Flush() => EndRecording();

    public void Present() => Present(null);

    public void Present(IReadOnlyList<SquareRect>? dirtyRects)
    {
        if (dirtyRects is { Count: 0 }) return;
        EndRecording();
        // The host invalidates the View after the application frame; do not request
        // another application frame from this commit.
    }

    public void Resize(Size canvasSize) => Resize(canvasSize, _dpiScale);

    public void Resize(Size canvasSize, float dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EndRecording();
        _picture?.Dispose();
        _picture = null;
        _canvasSize = canvasSize;
        _dpiScale = float.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1f;
    }

    public void Draw(AndroidGraphicsCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (_disposed || _picture == null) return;
        canvas.DrawPicture(_picture);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        EndRecording();
        _picture?.Dispose();
        _picture = null;
        foreach (var cached in _imageCache.Values) cached.Bitmap.Dispose();
        _imageCache.Clear();
    }

    private void EnsureRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recording) return;
        _picture?.Dispose();
        _picture = new AndroidPicture();
        _recordingCanvas = _picture.BeginRecording(
            Math.Max(1, (int)MathF.Ceiling(_canvasSize.Width)),
            Math.Max(1, (int)MathF.Ceiling(_canvasSize.Height)));
        _recording = true;
        _saveDepth = 0;
    }

    private void EndRecording()
    {
        if (!_recording) return;
        while (_saveDepth > 0)
        {
            _recordingCanvas!.Restore();
            _saveDepth--;
        }
        _picture!.EndRecording();
        _recordingCanvas = null;
        _recording = false;
    }

    private void DrawGeometry(Geometry geometry, AndroidPaint paint)
    {
        switch (geometry)
        {
            case RectGeometry rect:
                _recordingCanvas!.DrawRect(ToRectF(rect.Rect), paint);
                break;
            case RoundedRectGeometry rounded:
                using (var path = ToPath(rounded)) _recordingCanvas!.DrawPath(path, paint);
                break;
            case EllipseGeometry ellipse:
                _recordingCanvas!.DrawOval(new AndroidRectF(
                    ellipse.Center.X - ellipse.RadiusX,
                    ellipse.Center.Y - ellipse.RadiusY,
                    ellipse.Center.X + ellipse.RadiusX,
                    ellipse.Center.Y + ellipse.RadiusY), paint);
                break;
            case SquarePath path:
                using (var androidPath = ToPath(path)) _recordingCanvas!.DrawPath(androidPath, paint);
                break;
            default:
                throw new NotSupportedException($"Android Canvas does not support geometry type '{geometry.GetType().Name}'.");
        }
    }

    private static AndroidPaint CreatePaint(Brush brush, AndroidPaint.Style style)
    {
        var paint = new AndroidPaint(AndroidPaintFlags.AntiAlias);
        paint.SetStyle(style!);
        switch (brush)
        {
            case SolidColorBrush solid:
                paint.Color = ToAndroidColor(solid.Color);
                break;
            case LinearGradientBrush linear:
                ApplyGradient(paint, linear.Stops, linear.SpreadMethod,
                    (colors, positions, tileMode) => new AndroidLinearGradient(
                        linear.Start.X, linear.Start.Y, linear.End.X, linear.End.Y,
                        colors, positions, tileMode));
                break;
            case RadialGradientBrush radial:
                ApplyGradient(paint, radial.Stops, radial.SpreadMethod,
                    (colors, positions, tileMode) => new AndroidRadialGradient(
                        radial.Center.X, radial.Center.Y, Math.Max(0, radial.Radius),
                        colors, positions, tileMode));
                break;
            default:
                paint.Dispose();
                throw new NotSupportedException($"Android Canvas does not support brush type '{brush.GetType().Name}'.");
        }
        return paint;
    }

    private static void ApplyGradient(
        AndroidPaint paint,
        IReadOnlyList<GradientStop> stops,
        GradientSpreadMethod spreadMethod,
        Func<int[], float[], AndroidShader.TileMode, AndroidShader> createShader)
    {
        if (stops.Count == 0)
        {
            paint.Color = AndroidColor.Transparent;
            return;
        }
        var ordered = stops.OrderBy(stop => stop.Offset).ToArray();
        var colors = ordered.Select(stop => ToAndroidArgb(stop.Color)).ToArray();
        var positions = ordered.Select(stop => Math.Clamp(stop.Offset, 0f, 1f)).ToArray();
        paint.SetShader(createShader(colors, positions, spreadMethod switch
        {
            GradientSpreadMethod.Reflect => AndroidShader.TileMode.Mirror!,
            GradientSpreadMethod.Repeat => AndroidShader.TileMode.Repeat!,
            _ => AndroidShader.TileMode.Clamp!
        }));
    }

    private static AndroidPaint CreateStrokePaint(Pen pen)
    {
        var paint = CreatePaint(pen.Brush, AndroidPaint.Style.Stroke!);
        paint.StrokeWidth = Math.Max(0, pen.Width);
        if (pen.StrokeStyle is not { } stroke) return paint;
        paint.StrokeCap = stroke.Cap switch
        {
            LineCap.Round => AndroidPaint.Cap.Round,
            LineCap.Square => AndroidPaint.Cap.Square,
            _ => AndroidPaint.Cap.Butt
        };
        paint.StrokeJoin = stroke.Join switch
        {
            LineJoin.Round => AndroidPaint.Join.Round,
            LineJoin.Bevel => AndroidPaint.Join.Bevel,
            _ => AndroidPaint.Join.Miter
        };
        paint.StrokeMiter = Math.Max(0, stroke.MiterLimit);
        if (stroke.DashArray is { Length: > 0 } dashes && dashes.All(value => value > 0 && float.IsFinite(value)))
            paint.SetPathEffect(new AndroidDashPathEffect(dashes, stroke.DashOffset));
        return paint;
    }

    private static AndroidPaint CreateTextPaint(Font font, SquareColor color)
    {
        var style = AndroidTypefaceStyle.Normal;
        if (font.Weight >= SquareFontWeight.Bold) style |= AndroidTypefaceStyle.Bold;
        if (font.Style is SquareFontStyle.Italic or SquareFontStyle.Oblique) style |= AndroidTypefaceStyle.Italic;
        var paint = new AndroidPaint(AndroidPaintFlags.AntiAlias | AndroidPaintFlags.SubpixelText)
        {
            Color = ToAndroidColor(color),
            TextSize = Math.Max(1f, font.Size)
        };
        paint.SetTypeface(AndroidTypeface.Create(font.Family, style));
        return paint;
    }

    private CachedBitmap GetCachedBitmap(SquareBitmap bitmap)
    {
        if (_imageCache.TryGetValue(bitmap, out var cached) && cached.Version == bitmap.ContentVersion)
            return cached;
        cached?.Bitmap.Dispose();
        var native = AndroidBitmap.CreateBitmap(bitmap.Width, bitmap.Height, AndroidBitmap.Config.Argb8888!);
        var pixels = new int[checked(bitmap.Width * bitmap.Height)];
        for (var y = 0; y < bitmap.Height; y++)
        {
            var sourceOffset = y * bitmap.Stride;
            var destinationOffset = y * bitmap.Width;
            for (var x = 0; x < bitmap.Width; x++, destinationOffset++, sourceOffset += 4)
            {
                var blue = bitmap.Pixels[sourceOffset];
                var green = bitmap.Pixels[sourceOffset + 1];
                var red = bitmap.Pixels[sourceOffset + 2];
                var alpha = bitmap.Pixels[sourceOffset + 3];
                pixels[destinationOffset] = blue | green << 8 | red << 16 | alpha << 24;
            }
        }
        native.SetPixels(pixels, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);
        native.SetPremultiplied(true);
        cached = new CachedBitmap(bitmap.ContentVersion, native);
        _imageCache[bitmap] = cached;
        return cached;
    }

    private static AndroidPath ToPath(Geometry geometry)
    {
        if (geometry is RoundedRectGeometry rounded && rounded.IsUniform)
        {
            var path = new AndroidPath();
            path.AddRoundRect(ToRectF(rounded.Rect), rounded.RadiusX, rounded.RadiusY, AndroidPath.Direction.Cw!);
            return path;
        }
        if (geometry is SquarePath squarePath) return ToPath(squarePath);
        if (geometry is RoundedRectGeometry custom) return ToPath(custom.ToPath());
        var result = new AndroidPath();
        switch (geometry)
        {
            case RectGeometry rect:
                result.AddRect(ToRectF(rect.Rect), AndroidPath.Direction.Cw!);
                break;
            case EllipseGeometry ellipse:
                result.AddOval(new AndroidRectF(
                    ellipse.Center.X - ellipse.RadiusX,
                    ellipse.Center.Y - ellipse.RadiusY,
                    ellipse.Center.X + ellipse.RadiusX,
                    ellipse.Center.Y + ellipse.RadiusY), AndroidPath.Direction.Cw!);
                break;
            default:
                throw new NotSupportedException($"Android Canvas does not support geometry type '{geometry.GetType().Name}'.");
        }
        return result;
    }

    private static AndroidPath ToPath(SquarePath path)
    {
        var result = new AndroidPath();
        foreach (var command in path.Commands)
        {
            switch (command)
            {
                case MoveToCmd move:
                    result.MoveTo(move.Point.X, move.Point.Y);
                    break;
                case LineToCmd line:
                    result.LineTo(line.Point.X, line.Point.Y);
                    break;
                case ArcToCmd arc:
                    result.ArcTo(arc.Oval.Left, arc.Oval.Top, arc.Oval.Right, arc.Oval.Bottom,
                        arc.StartAngle, arc.SweepAngle, forceMoveTo: false);
                    break;
                case CloseCmd:
                    result.Close();
                    break;
            }
        }
        return result;
    }

    private static AndroidMatrix ToAndroidMatrix(Matrix3x2 matrix)
    {
        var result = new AndroidMatrix();
        result.SetValues([
            matrix.M11, matrix.M21, matrix.M31,
            matrix.M12, matrix.M22, matrix.M32,
            0, 0, 1]);
        return result;
    }

    private static AndroidRectF ToRectF(SquareRect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
    private static AndroidRect ToAndroidRect(SquareRect rect) => new(
        (int)MathF.Floor(rect.Left), (int)MathF.Floor(rect.Top),
        (int)MathF.Ceiling(rect.Right), (int)MathF.Ceiling(rect.Bottom));
    private static AndroidColor ToAndroidColor(SquareColor color) => AndroidColor.Argb(color.A, color.R, color.G, color.B);
    private static int ToAndroidArgb(SquareColor color) =>
        AndroidColor.Argb(color.A, color.R, color.G, color.B).ToArgb();

    private static float GetTextAlignmentOffset(TextLayout text, float lineWidth)
    {
        if (!float.IsFinite(text.MaxSize.Width) || text.MaxSize.Width <= lineWidth) return 0;
        return text.Alignment switch
        {
            TextAlignment.Center => (text.MaxSize.Width - lineWidth) / 2f,
            TextAlignment.Right => text.MaxSize.Width - lineWidth,
            _ => 0
        };
    }

    private sealed record CachedBitmap(long Version, AndroidBitmap Bitmap);
}

/// <summary>注册 Android Canvas 后端。</summary>
public static class AndroidCanvasRegistration
{
    /// <summary>注册名为 AndroidCanvas 的后端。</summary>
    public static void Register() => RenderBackendRegistry.Register(new AndroidCanvasBackendFactory());
}

internal sealed class AndroidCanvasBackendFactory : IRenderBackendFactory
{
    public string Name => "AndroidCanvas";

    public IRenderContext CreateContext(RenderContextCreateInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new AndroidCanvasRenderContext(info.CanvasSize, info.DpiScale);
    }
}
