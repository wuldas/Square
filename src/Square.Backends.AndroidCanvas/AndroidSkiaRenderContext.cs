using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;
using Square.Graphics;
using SquareBitmap = Square.Graphics.Bitmap;
using SquareColor = Square.Graphics.Color;
using SquareImage = Square.Graphics.Image;
using SquarePath = Square.Graphics.PathGeometry;
using SquareRect = Square.Graphics.Rect;
using SquareFontStyle = Square.Graphics.FontStyle;
using SquareFontWeight = Square.Graphics.FontWeight;

namespace Square.Backends.AndroidCanvas;

/// <summary>供 Android SKCanvasView 在 Skia surface 上绘制已提交帧的上下文。</summary>
public interface IAndroidSkiaRenderContext
{
    /// <summary>将最近一次提交的帧绘制到 Skia Canvas。</summary>
    void Draw(SKCanvas canvas);
}

/// <summary>使用 SKPicture 录制并直接绘制到 Android Skia surface。</summary>
internal sealed class AndroidSkiaRenderContext : IRenderContext, IDpiResizableRenderContext, IAndroidSkiaRenderContext
{
    private readonly Dictionary<SquareBitmap, CachedBitmap> _imageCache = [];
    private SKPictureRecorder? _recorder;
    private SKCanvas? _recordingCanvas;
    private SKPicture? _picture;
    private Size _canvasSize;
    private float _dpiScale;
    private int _saveDepth;
    private bool _recording;
    private bool _disposed;

    public AndroidSkiaRenderContext(Size canvasSize, float dpiScale)
        => Resize(canvasSize, dpiScale);

    public Size CanvasSize => _canvasSize;
    public float DpiScale => _dpiScale;
    public bool SupportsPartialRendering => false;

    public void PushTransform(Matrix3x2 matrix)
    {
        EnsureRecording();
        _recordingCanvas!.Save();
        _recordingCanvas.Concat(ToSkMatrix(matrix));
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
        _recordingCanvas.ClipRect(ToSkRect(rect), SKClipOperation.Intersect, false);
        _saveDepth++;
    }

    public void PushClip(Geometry geometry)
    {
        EnsureRecording();
        _recordingCanvas!.Save();
        using var path = ToPath(geometry);
        _recordingCanvas.ClipPath(path, SKClipOperation.Intersect, true);
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
        using var paint = CreatePaint(brush, SKPaintStyle.Fill);
        _recordingCanvas!.DrawRect(ToSkRect(rect), paint);
    }

    public void DrawRect(SquareRect rect, Pen pen)
    {
        EnsureRecording();
        using var paint = CreateStrokePaint(pen);
        _recordingCanvas!.DrawRect(ToSkRect(rect), paint);
    }

    public void FillPath(SquarePath path, Brush brush)
    {
        EnsureRecording();
        using var skPath = ToPath(path);
        using var paint = CreatePaint(brush, SKPaintStyle.Fill);
        _recordingCanvas!.DrawPath(skPath, paint);
    }

    public void DrawPath(SquarePath path, Pen pen)
    {
        EnsureRecording();
        using var skPath = ToPath(path);
        using var paint = CreateStrokePaint(pen);
        _recordingCanvas!.DrawPath(skPath, paint);
    }

    public void FillGeometry(Geometry geometry, Brush brush)
    {
        EnsureRecording();
        using var paint = CreatePaint(brush, SKPaintStyle.Fill);
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
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            Color = ToSkColor(solid.Color)
        };
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
                using var font = CreateFont(text.Font, visualRune.Glyph.Value);
                _recordingCanvas!.DrawText(visualRune.Glyph.ToString(), x, baseline, SKTextAlign.Left, font, paint);
                x += visualRune.Advance;
            }
        }
        foreach (var decoration in text.GetDecorationRects(origin))
            _recordingCanvas!.DrawRect(ToSkRect(decoration), paint);
    }

    public void DrawImage(SquareImage image, SquareRect dest, SquareRect? source = null)
    {
        EnsureRecording();
        if (image is not SquareBitmap bitmap || bitmap.IsDisposed || dest.IsEmpty) return;
        var cached = GetCachedBitmap(bitmap);
        var sourceRect = source ?? new SquareRect(0, 0, bitmap.Width, bitmap.Height);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);
        using var paint = new SKPaint { IsAntialias = true };
        _recordingCanvas!.DrawBitmap(cached.Bitmap, ToSkRect(sourceRect), ToSkRect(dest), sampling, paint);
    }

    public void PushLayer(SquareRect bounds, float opacity)
    {
        EnsureRecording();
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)MathF.Round(Math.Clamp(opacity, 0f, 1f) * 255f)) };
        _recordingCanvas!.SaveLayer(ToSkRect(bounds), paint);
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
        _recordingCanvas!.Clear(ToSkColor(color));
    }

    public void Clear(SquareColor color, SquareRect rect)
    {
        EnsureRecording();
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false, Color = ToSkColor(color) };
        _recordingCanvas!.DrawRect(ToSkRect(rect), paint);
    }

    public void Flush() => EndRecording();
    public void Present() => Present(null);
    public void Present(IReadOnlyList<SquareRect>? dirtyRects)
    {
        if (dirtyRects is { Count: 0 }) return;
        EndRecording();
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

    public void Draw(SKCanvas canvas)
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
        _picture = null;
        _recorder = new SKPictureRecorder();
        _recordingCanvas = _recorder.BeginRecording(new SKRect(
            0, 0, Math.Max(1, _canvasSize.Width), Math.Max(1, _canvasSize.Height)));
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
        _picture = _recorder!.EndRecording();
        _recordingCanvas = null;
        _recorder.Dispose();
        _recorder = null;
        _recording = false;
    }

    private void DrawGeometry(Geometry geometry, SKPaint paint)
    {
        switch (geometry)
        {
            case RectGeometry rect:
                _recordingCanvas!.DrawRect(ToSkRect(rect.Rect), paint);
                break;
            case RoundedRectGeometry rounded:
                using (var path = ToPath(rounded)) _recordingCanvas!.DrawPath(path, paint);
                break;
            case EllipseGeometry ellipse:
                _recordingCanvas!.DrawOval(new SKRect(
                    ellipse.Center.X - ellipse.RadiusX,
                    ellipse.Center.Y - ellipse.RadiusY,
                    ellipse.Center.X + ellipse.RadiusX,
                    ellipse.Center.Y + ellipse.RadiusY), paint);
                break;
            case SquarePath path:
                using (var skPath = ToPath(path)) _recordingCanvas!.DrawPath(skPath, paint);
                break;
            default:
                throw new NotSupportedException($"Android Skia does not support geometry type '{geometry.GetType().Name}'.");
        }
    }

    private static SKPaint CreatePaint(Brush brush, SKPaintStyle style)
    {
        var paint = new SKPaint { Style = style, IsAntialias = true };
        switch (brush)
        {
            case SolidColorBrush solid:
                paint.Color = ToSkColor(solid.Color);
                break;
            case LinearGradientBrush linear:
                ApplyGradient(paint, linear.Stops, linear.SpreadMethod,
                    (colors, positions, mode) => SKShader.CreateLinearGradient(
                        ToSkPoint(linear.Start), ToSkPoint(linear.End), colors, positions, mode));
                break;
            case RadialGradientBrush radial:
                ApplyGradient(paint, radial.Stops, radial.SpreadMethod,
                    (colors, positions, mode) => SKShader.CreateRadialGradient(
                        ToSkPoint(radial.Center), Math.Max(0, radial.Radius), colors, positions, mode));
                break;
            default:
                paint.Dispose();
                throw new NotSupportedException($"Android Skia does not support brush type '{brush.GetType().Name}'.");
        }
        return paint;
    }

    private static void ApplyGradient(
        SKPaint paint,
        IReadOnlyList<GradientStop> stops,
        GradientSpreadMethod spreadMethod,
        Func<SKColor[], float[], SKShaderTileMode, SKShader> createShader)
    {
        if (stops.Count == 0)
        {
            paint.Color = SKColors.Transparent;
            return;
        }
        var ordered = stops.OrderBy(stop => stop.Offset).ToArray();
        var colors = ordered.Select(stop => ToSkColor(stop.Color)).ToArray();
        var positions = ordered.Select(stop => Math.Clamp(stop.Offset, 0f, 1f)).ToArray();
        paint.Shader = createShader(colors, positions, spreadMethod switch
        {
            GradientSpreadMethod.Reflect => SKShaderTileMode.Mirror,
            GradientSpreadMethod.Repeat => SKShaderTileMode.Repeat,
            _ => SKShaderTileMode.Clamp
        });
    }

    private static SKPaint CreateStrokePaint(Pen pen)
    {
        var paint = CreatePaint(pen.Brush, SKPaintStyle.Stroke);
        paint.StrokeWidth = Math.Max(0, pen.Width);
        if (pen.StrokeStyle is not { } stroke) return paint;
        paint.StrokeCap = stroke.Cap switch
        {
            LineCap.Round => SKStrokeCap.Round,
            LineCap.Square => SKStrokeCap.Square,
            _ => SKStrokeCap.Butt
        };
        paint.StrokeJoin = stroke.Join switch
        {
            LineJoin.Round => SKStrokeJoin.Round,
            LineJoin.Bevel => SKStrokeJoin.Bevel,
            _ => SKStrokeJoin.Miter
        };
        paint.StrokeMiter = Math.Max(0, stroke.MiterLimit);
        if (stroke.DashArray is { Length: > 0 } dashes && dashes.All(value => value > 0 && float.IsFinite(value)))
            paint.PathEffect = SKPathEffect.CreateDash(dashes, stroke.DashOffset);
        return paint;
    }

    private static SKFont CreateFont(Font font, int codePoint)
    {
        var family = font.Family.ToLowerInvariant() switch
        {
            "sans-serif" or "system-ui" or "ui-sans-serif" => "sans-serif",
            "serif" or "ui-serif" => "serif",
            "monospace" or "ui-monospace" => "monospace",
            _ => font.Family
        };
        var style = new SKFontStyle((int)font.Weight, (int)SKFontStyleWidth.Normal,
            font.Style == SquareFontStyle.Normal ? SKFontStyleSlant.Upright : SKFontStyleSlant.Italic);
        var typeface = SKFontManager.Default.MatchCharacter(family, style, null, codePoint) ?? SKTypeface.Default;
        return new SKFont(typeface, Math.Max(1, font.Size))
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.Normal,
            Subpixel = true,
            LinearMetrics = true
        };
    }

    private CachedBitmap GetCachedBitmap(SquareBitmap bitmap)
    {
        if (_imageCache.TryGetValue(bitmap, out var cached) && cached.Version == bitmap.ContentVersion)
            return cached;
        cached?.Bitmap.Dispose();
        var native = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        Marshal.Copy(bitmap.Pixels, 0, native.GetPixels(), bitmap.Pixels.Length);
        cached = new CachedBitmap(bitmap.ContentVersion, native);
        _imageCache[bitmap] = cached;
        return cached;
    }

    private static SKPath ToPath(Geometry geometry)
    {
        using var builder = new SKPathBuilder();
        if (geometry is RoundedRectGeometry rounded)
        {
            if (rounded.IsUniform)
            {
                builder.AddRoundRect(ToSkRect(rounded.Rect), rounded.RadiusX, rounded.RadiusY);
                return builder.Detach();
            }
            return ToPath(rounded.ToPath());
        }
        if (geometry is SquarePath squarePath) return ToPath(squarePath);
        switch (geometry)
        {
            case RectGeometry rect:
                builder.AddRect(ToSkRect(rect.Rect));
                break;
            case EllipseGeometry ellipse:
                builder.AddOval(new SKRect(
                    ellipse.Center.X - ellipse.RadiusX,
                    ellipse.Center.Y - ellipse.RadiusY,
                    ellipse.Center.X + ellipse.RadiusX,
                    ellipse.Center.Y + ellipse.RadiusY));
                break;
            default:
                throw new NotSupportedException($"Android Skia does not support geometry type '{geometry.GetType().Name}'.");
        }
        return builder.Detach();
    }

    private static SKPath ToPath(SquarePath path)
    {
        using var builder = new SKPathBuilder();
        foreach (var command in path.Commands)
        {
            switch (command)
            {
                case MoveToCmd move:
                    builder.MoveTo(move.Point.X, move.Point.Y);
                    break;
                case LineToCmd line:
                    builder.LineTo(line.Point.X, line.Point.Y);
                    break;
                case ArcToCmd arc:
                    builder.ArcTo(ToSkRect(arc.Oval), arc.StartAngle, arc.SweepAngle, false);
                    break;
                case CloseCmd:
                    builder.Close();
                    break;
            }
        }
        return builder.Detach();
    }

    private static SKColor ToSkColor(SquareColor color) => new(color.R, color.G, color.B, color.A);
    private static SKRect ToSkRect(SquareRect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
    private static SKPoint ToSkPoint(Square.Graphics.Point point) => new(point.X, point.Y);
    private static SKMatrix ToSkMatrix(Matrix3x2 matrix) => new(
        matrix.M11, matrix.M21, matrix.M31,
        matrix.M12, matrix.M22, matrix.M32,
        0, 0, 1);

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

    private sealed record CachedBitmap(long Version, SKBitmap Bitmap);
}

/// <summary>注册 Android Skia surface 后端。</summary>
public static class AndroidSkiaRegistration
{
    /// <summary>注册名为 AndroidSkia 的后端。</summary>
    public static void Register() => RenderBackendRegistry.Register(new AndroidSkiaBackendFactory());
}

internal sealed class AndroidSkiaBackendFactory : IRenderBackendFactory
{
    public string Name => "AndroidSkia";

    public IRenderContext CreateContext(RenderContextCreateInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new AndroidSkiaRenderContext(info.CanvasSize, info.DpiScale);
    }
}
