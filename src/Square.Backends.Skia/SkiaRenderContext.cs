using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Square.Graphics;
using Bitmap = Square.Graphics.Bitmap;
using Color = Square.Graphics.Color;
using Image = Square.Graphics.Image;
using Path = Square.Graphics.PathGeometry;

namespace Square.Backends.Skia;

internal sealed class SkiaRenderContext : IRenderContext, IDpiResizableRenderContext, IRenderBitmapSource
{
    private readonly PresentFrameHandler? _presentFrame;
    private readonly Dictionary<Bitmap, CachedBitmap> _imageCache = [];
    private SKBitmap _framebuffer = null!;
    private SKCanvas _canvas = null!;
    private Bitmap _presentBitmap = null!;
    private Size _canvasSize;
    private float _dpiScale;
    private int _layerDepth;
    private bool _disposed;

    public SkiaRenderContext(Size canvasSize, float dpiScale, PresentFrameHandler? presentFrame)
    {
        _presentFrame = presentFrame;
        Resize(canvasSize, dpiScale);
    }

    public Size CanvasSize => _canvasSize;
    public float DpiScale => _dpiScale;
    public bool SupportsPartialRendering => true;

    public void PushTransform(Matrix3x2 matrix)
    {
        ThrowIfDisposed();
        _canvas.Save();
        _canvas.Concat(ToSkMatrix(matrix));
    }

    public void PopTransform()
    {
        ThrowIfDisposed();
        _canvas.Restore();
    }

    public void PushClip(Rect rect)
    {
        ThrowIfDisposed();
        _canvas.Save();
        _canvas.ClipRect(ToSkRect(rect), SKClipOperation.Intersect, antialias: false);
    }

    public void PushClip(Geometry geometry)
    {
        ThrowIfDisposed();
        _canvas.Save();
        using var path = CreatePath(geometry);
        _canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
    }

    public void PopClip()
    {
        ThrowIfDisposed();
        _canvas.Restore();
    }

    public void FillRect(Rect rect, Brush brush)
    {
        ThrowIfDisposed();
        using var paint = CreatePaint(brush, SKPaintStyle.Fill);
        _canvas.DrawRect(ToSkRect(rect), paint);
    }

    public void DrawRect(Rect rect, Pen pen)
    {
        ThrowIfDisposed();
        using var paint = CreateStrokePaint(pen);
        _canvas.DrawRect(ToSkRect(rect), paint);
    }

    public void FillPath(Path path, Brush brush)
    {
        ThrowIfDisposed();
        using var skPath = CreatePath(path);
        using var paint = CreatePaint(brush, SKPaintStyle.Fill);
        _canvas.DrawPath(skPath, paint);
    }

    public void DrawPath(Path path, Pen pen)
    {
        ThrowIfDisposed();
        using var skPath = CreatePath(path);
        using var paint = CreateStrokePaint(pen);
        _canvas.DrawPath(skPath, paint);
    }

    public void FillGeometry(Geometry geometry, Brush brush)
    {
        ThrowIfDisposed();
        using var paint = CreatePaint(brush, SKPaintStyle.Fill);
        DrawGeometry(geometry, paint);
    }

    public void DrawGeometry(Geometry geometry, Pen pen)
    {
        ThrowIfDisposed();
        using var paint = CreateStrokePaint(pen);
        DrawGeometry(geometry, paint);
    }

    public void DrawText(TextLayout text, Point origin, Brush brush)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(text.Text) || brush is not SolidColorBrush solid)
            return;

        var lines = TextWrapping.Wrap(text.Text, text.MaxSize.Width, (offset, rune) =>
            TextLayout.MeasureRuneAdvance(rune, text.Font), text.WrappingOptions);
        var lineHeight = TextMetrics.GetLineHeight(text.Font, text.LineHeight);
        var baselineOffset = TextMetrics.GetBaselineOffset(text.Font, lineHeight);
        using var paint = CreatePaint(solid, SKPaintStyle.Fill);
        paint.IsDither = true;

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var indent = text.GetLineIndent(lineIndex);
            var x = origin.X + indent + GetTextAlignmentOffset(text, line.Width + indent);
            var baseline = origin.Y + lineIndex * lineHeight + baselineOffset;
            foreach (var visualRune in text.EnumerateVisualRunes(line))
            {
                var rune = visualRune.Glyph;
                using var skFont = SkiaRegistration.TextMetricsProvider.CreateFont(text.Font, rune.Value);
                _canvas.DrawText(
                    rune.ToString(),
                    SnapToPhysicalPixel(x),
                    SnapToPhysicalPixel(baseline),
                    SKTextAlign.Left,
                    skFont,
                    paint);
                x += visualRune.Advance;
            }
        }

        using var decorationPaint = CreatePaint(solid, SKPaintStyle.Fill);
        foreach (var rect in text.GetDecorationRects(origin))
            _canvas.DrawRect(ToSkRect(rect), decorationPaint);
    }

    public void DrawImage(Image image, Rect dest, Rect? source = null)
    {
        ThrowIfDisposed();
        if (image is not Bitmap bitmap || bitmap.IsDisposed || dest.IsEmpty) return;
        var cached = GetCachedBitmap(bitmap);
        var sourceRect = source ?? new Rect(0, 0, bitmap.Width, bitmap.Height);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White
        };
        _canvas.DrawBitmap(
            cached.Bitmap,
            ToSkRect(sourceRect),
            ToSkRect(dest),
            new SKSamplingOptions(SKFilterMode.Linear),
            paint);
    }

    public void PushLayer(Rect bounds, float opacity)
    {
        ThrowIfDisposed();
        var alpha = (byte)Math.Clamp((int)MathF.Round(Math.Clamp(opacity, 0f, 1f) * 255f), 0, 255);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
        _canvas.SaveLayer(ToSkRect(bounds), paint);
        _layerDepth++;
    }

    public void PopLayer()
    {
        ThrowIfDisposed();
        if (_layerDepth == 0) return;
        _canvas.Restore();
        _layerDepth--;
    }

    public void Clear(Color color)
    {
        ThrowIfDisposed();
        _canvas.Clear(ToSkColor(color));
    }

    public void Clear(Color color, Rect rect)
    {
        ThrowIfDisposed();
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
            Color = ToSkColor(color)
        };
        _canvas.DrawRect(ToSkRect(rect), paint);
    }

    public void Flush()
    {
        ThrowIfDisposed();
        _canvas.Flush();
    }

    public void Present() => Present(null);

    public void Present(IReadOnlyList<Rect>? dirtyRects)
    {
        ThrowIfDisposed();
        if (dirtyRects is { Count: 0 }) return;
        var physicalDirtyRects = ScaleDirtyRects(dirtyRects);
        CopyFramebufferTo(_presentBitmap, physicalDirtyRects);
        _presentBitmap.MarkDirty();
        _presentFrame?.Invoke(_presentBitmap, physicalDirtyRects);
    }

    public void Resize(Size canvasSize)
        => Resize(canvasSize, _dpiScale);

    public void Resize(Size canvasSize, float dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        dpiScale = float.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1f;
        var width = Math.Max(1, (int)MathF.Ceiling(canvasSize.Width * dpiScale));
        var height = Math.Max(1, (int)MathF.Ceiling(canvasSize.Height * dpiScale));
        _canvasSize = canvasSize;
        _dpiScale = dpiScale;
        _layerDepth = 0;

        _canvas?.Dispose();
        _framebuffer?.Dispose();
        _presentBitmap?.Dispose();

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _framebuffer = new SKBitmap(info);
        _canvas = new SKCanvas(_framebuffer);
        _canvas.Scale(dpiScale);
        _presentBitmap = new Bitmap(width, height);
    }

    public Bitmap CaptureBitmap()
    {
        ThrowIfDisposed();
        var bitmap = new Bitmap(_framebuffer.Width, _framebuffer.Height);
        CopyFramebufferTo(bitmap, null);
        bitmap.MarkDirty();
        return bitmap;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvas.Dispose();
        _framebuffer.Dispose();
        _presentBitmap.Dispose();
        foreach (var cached in _imageCache.Values) cached.Bitmap.Dispose();
        _imageCache.Clear();
    }

    private void DrawGeometry(Geometry geometry, SKPaint paint)
    {
        switch (geometry)
        {
            case RectGeometry rect:
                _canvas.DrawRect(ToSkRect(rect.Rect), paint);
                break;
            case RoundedRectGeometry rounded:
                _canvas.DrawRoundRect(ToSkRect(rounded.Rect), rounded.RadiusX, rounded.RadiusY, paint);
                break;
            case EllipseGeometry ellipse:
                _canvas.DrawOval(new SKRect(
                    ellipse.Center.X - ellipse.RadiusX,
                    ellipse.Center.Y - ellipse.RadiusY,
                    ellipse.Center.X + ellipse.RadiusX,
                    ellipse.Center.Y + ellipse.RadiusY), paint);
                break;
            case Path path:
                using (var skPath = CreatePath(path))
                    _canvas.DrawPath(skPath, paint);
                break;
            default:
                throw new NotSupportedException($"Skia rendering does not support geometry type '{geometry.GetType().Name}'.");
        }
    }

    private SKPaint CreateStrokePaint(Pen pen)
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

    private SKPaint CreatePaint(Brush brush, SKPaintStyle style)
    {
        var paint = new SKPaint { Style = style, IsAntialias = true };
        switch (brush)
        {
            case SolidColorBrush solid:
                paint.Color = ToSkColor(solid.Color);
                break;
            case LinearGradientBrush linear:
                ApplyGradient(paint, linear.Stops, linear.SpreadMethod,
                    (colors, positions, tileMode) => SKShader.CreateLinearGradient(
                        ToSkPoint(linear.Start), ToSkPoint(linear.End), colors, positions, tileMode));
                break;
            case RadialGradientBrush radial:
                ApplyGradient(paint, radial.Stops, radial.SpreadMethod,
                    (colors, positions, tileMode) => SKShader.CreateRadialGradient(
                        ToSkPoint(radial.Center), Math.Max(0, radial.Radius), colors, positions, tileMode));
                break;
            default:
                paint.Dispose();
                throw new NotSupportedException($"Skia rendering does not support brush type '{brush.GetType().Name}'.");
        }
        return paint;
    }

    private void ApplyGradient(
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

    private CachedBitmap GetCachedBitmap(Bitmap bitmap)
    {
        if (_imageCache.TryGetValue(bitmap, out var cached) && cached.Version == bitmap.ContentVersion)
            return cached;
        cached?.Bitmap.Dispose();
        var skBitmap = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        Marshal.Copy(bitmap.Pixels, 0, skBitmap.GetPixels(), bitmap.Pixels.Length);
        cached = new CachedBitmap(bitmap.ContentVersion, skBitmap);
        _imageCache[bitmap] = cached;
        return cached;
    }

    private static SKPath CreatePath(Geometry geometry)
    {
        if (geometry is Path path) return CreatePath(path);
        using var builder = new SKPathBuilder();
        switch (geometry)
        {
            case RectGeometry rect:
                builder.AddRect(ToSkRect(rect.Rect));
                break;
            case RoundedRectGeometry rounded:
                builder.AddRoundRect(ToSkRect(rounded.Rect), rounded.RadiusX, rounded.RadiusY);
                break;
            case EllipseGeometry ellipse:
                builder.AddOval(new SKRect(
                    ellipse.Center.X - ellipse.RadiusX,
                    ellipse.Center.Y - ellipse.RadiusY,
                    ellipse.Center.X + ellipse.RadiusX,
                    ellipse.Center.Y + ellipse.RadiusY));
                break;
            default:
                throw new NotSupportedException($"Skia rendering does not support geometry type '{geometry.GetType().Name}'.");
        }
        return builder.Detach();
    }

    private static SKPath CreatePath(Path path)
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
                    builder.ArcTo(ToSkRect(arc.Oval), arc.StartAngle, arc.SweepAngle, forceMoveTo: false);
                    break;
                case CloseCmd:
                    builder.Close();
                    break;
            }
        }
        return builder.Detach();
    }

    private void CopyFramebufferTo(Bitmap bitmap, IReadOnlyList<Rect>? dirtyRects)
    {
        if (dirtyRects is { Count: > 0 })
        {
            foreach (var dirtyRect in dirtyRects)
                CopyFramebufferRectTo(bitmap, dirtyRect);
            return;
        }

        var byteCount = checked(_framebuffer.RowBytes * _framebuffer.Height);
        if (_framebuffer.RowBytes == bitmap.Stride)
        {
            Marshal.Copy(_framebuffer.GetPixels(), bitmap.Pixels, 0, Math.Min(byteCount, bitmap.Pixels.Length));
            return;
        }
        for (var row = 0; row < _framebuffer.Height; row++)
            Marshal.Copy(IntPtr.Add(_framebuffer.GetPixels(), row * _framebuffer.RowBytes), bitmap.Pixels, row * bitmap.Stride, bitmap.Stride);
    }

    private void CopyFramebufferRectTo(Bitmap bitmap, Rect rect)
    {
        var left = Math.Clamp((int)MathF.Floor(rect.Left), 0, bitmap.Width);
        var top = Math.Clamp((int)MathF.Floor(rect.Top), 0, bitmap.Height);
        var right = Math.Clamp((int)MathF.Ceiling(rect.Right), left, bitmap.Width);
        var bottom = Math.Clamp((int)MathF.Ceiling(rect.Bottom), top, bitmap.Height);
        if (right <= left || bottom <= top) return;

        var bytesPerRow = (right - left) * 4;
        for (var row = top; row < bottom; row++)
        {
            var source = IntPtr.Add(_framebuffer.GetPixels(), row * _framebuffer.RowBytes + left * 4);
            Marshal.Copy(source, bitmap.Pixels, row * bitmap.Stride + left * 4, bytesPerRow);
        }
    }

    private Rect[] _scaledDirtyRects = [];

    private IReadOnlyList<Rect>? ScaleDirtyRects(IReadOnlyList<Rect>? dirtyRects)
    {
        if (dirtyRects == null) return null;
        if (_scaledDirtyRects.Length < dirtyRects.Count)
            _scaledDirtyRects = new Rect[Math.Max(dirtyRects.Count, _scaledDirtyRects.Length * 2)];

        for (var i = 0; i < dirtyRects.Count; i++)
        {
            var rect = dirtyRects[i];
            _scaledDirtyRects[i] = new Rect(
                MathF.Floor(rect.Left * _dpiScale),
                MathF.Floor(rect.Top * _dpiScale),
                MathF.Ceiling(rect.Right * _dpiScale) - MathF.Floor(rect.Left * _dpiScale),
                MathF.Ceiling(rect.Bottom * _dpiScale) - MathF.Floor(rect.Top * _dpiScale));
        }

        return new ArraySegment<Rect>(_scaledDirtyRects, 0, dirtyRects.Count);
    }

    private static SKColor ToSkColor(Color color)
        => new(color.R, color.G, color.B, color.A);

    private static SKRect ToSkRect(Rect rect)
        => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static SKPoint ToSkPoint(Point point)
        => new(point.X, point.Y);

    private float SnapToPhysicalPixel(float value)
        => MathF.Round(value * _dpiScale) / _dpiScale;

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

    private static SKMatrix ToSkMatrix(Matrix3x2 matrix)
        => new(
            matrix.M11, matrix.M21, matrix.M31,
            matrix.M12, matrix.M22, matrix.M32,
            0, 0, 1);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record CachedBitmap(long Version, SKBitmap Bitmap);
}
