using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Square.Graphics;
using Square.Text.Glyph;

namespace Square.Backends;

internal sealed class RenderContext : IRenderContext, IDpiResizableRenderContext, IRenderBitmapSource
{
    public bool SupportsPartialRendering => true;
    private const int CoverageSampleGrid = 4;
    private const int CoverageSampleCount = CoverageSampleGrid * CoverageSampleGrid;
    private const uint AlphaMask = 0xFF000000u;

    private readonly ISoftwareRenderSurface _surface;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private int _bitmapStride;
    private bool _hasClip;
    private bool _hasGeometryClip;
    private float _clipLeft, _clipTop, _clipRight, _clipBottom;
    private Func<float, float, bool>? _clipContains;
    private Size _canvasSize;
    private float _dpiScale;
    private readonly Stack<ClipRegion> _clipStack = new();
    private readonly Stack<Matrix3x2> _transformStack = new();
    private readonly Stack<LayerState> _layerStack = new();
    private readonly LayerBufferPool _layerBufferPool = new();
    private readonly byte[] _coverageAlphaLookup = new byte[CoverageSampleCount + 1];
    private int _coverageAlphaSource = -1;
    private float _coverageAlphaOpacity = -1f;
    private readonly byte[] _byteCoverageAlphaLookup = new byte[256];
    private int _byteCoverageAlphaSource = -1;
    private float _byteCoverageAlphaOpacity = -1f;
    private Rect[] _scaledDirtyRects = [];
    private Matrix3x2 _currentTransform;
    private float _currentOpacity = 1f;
    private readonly SystemGlyphRasterizer _glyphRasterizer = SystemGlyphRasterizer.Shared;

    public Size CanvasSize => _canvasSize;
    public float DpiScale => _dpiScale;
    internal int RetainedLayerBufferBytes => _layerBufferPool.RetainedBytes;
    internal int RetainedLayerBufferCount => _layerBufferPool.RetainedBufferCount;
    internal int LayerBufferAllocationCount => _layerBufferPool.AllocationCount;
    internal int LayerBufferReuseCount => _layerBufferPool.ReuseCount;
    internal int RoundedRectStrokeFastPathCount { get; private set; }

    internal RenderContext(Bitmap bitmap, float dpiScale, PresentFrameHandler? presentFrame = null)
        : this(
            bitmap,
            new Size(bitmap.Width / NormalizeDpiScale(dpiScale), bitmap.Height / NormalizeDpiScale(dpiScale)),
            dpiScale,
            presentFrame)
    {
    }

    internal RenderContext(Bitmap bitmap, Size canvasSize, float dpiScale, PresentFrameHandler? presentFrame = null)
        : this(new BitmapSoftwareRenderSurface(bitmap, presentFrame), canvasSize, dpiScale)
    {
    }

    internal RenderContext(ISoftwareRenderSurface surface, Size canvasSize, float dpiScale)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
        _bitmapWidth = surface.Width;
        _bitmapHeight = surface.Height;
        _bitmapStride = surface.Stride;
        _canvasSize = canvasSize;
        _dpiScale = NormalizeDpiScale(dpiScale);
        _currentTransform = CreateDpiTransform(_dpiScale);
    }

    /// <summary>测试兼容：仅接收 Bitmap 的 Present 回调包装。</summary>
    internal RenderContext(Bitmap bitmap, float dpiScale, Action<Bitmap>? presentFrame)
        : this(bitmap, dpiScale, presentFrame == null
            ? null
            : (PresentFrameHandler)((frame, _) => presentFrame(frame)))
    {
    }

    public void PushTransform(Matrix3x2 matrix)
    {
        _transformStack.Push(_currentTransform);
        _currentTransform = matrix * _currentTransform;
    }

    public void PopTransform()
    {
        _currentTransform = _transformStack.Count > 0 ? _transformStack.Pop() : CreateDpiTransform(_dpiScale);
    }

    public void PushClip(Rect rect)
    {
        var bounds = TransformRect(rect);
        PushClipRegion(bounds, null, isRect: true);
        UpdateClipCache();
    }

    public void PushClip(Geometry geometry)
    {
        if (geometry is RectGeometry rect) { PushClip(rect.Rect); return; }
        if (!Matrix3x2.Invert(_currentTransform, out var inverse)) { PushClip(Rect.Empty); return; }
        Rect bounds;
        Func<float, float, bool> contains;
        switch (geometry)
        {
            case RoundedRectGeometry rounded:
                bounds = TransformRect(rounded.Rect);
                contains = (x, y) => ContainsRoundedRect(TransformPoint(inverse, x, y), rounded.Rect, rounded.RadiusX, rounded.RadiusY);
                break;
            case EllipseGeometry ellipse:
                bounds = TransformRect(new Rect(
                    ellipse.Center.X - ellipse.RadiusX, ellipse.Center.Y - ellipse.RadiusY,
                    ellipse.RadiusX * 2, ellipse.RadiusY * 2));
                contains = (x, y) => ContainsEllipse(TransformPoint(inverse, x, y), ellipse.Center, ellipse.RadiusX, ellipse.RadiusY);
                break;
            case PathGeometry path:
                var points = FlattenPath(path, Matrix3x2.Identity)
                    .Select(point => new Point((float)point.x, (float)point.y))
                    .ToArray();
                bounds = GetBounds(points, _currentTransform);
                contains = (x, y) => ContainsPolygon(points, TransformPoint(inverse, x, y));
                break;
            default:
                throw new NotSupportedException($"Software rendering does not support geometry clip type '{geometry.GetType().Name}'.");
        }
        PushClipRegion(bounds, contains, isRect: false);
        UpdateClipCache();
    }
    public void PopClip() { if (_clipStack.Count > 0) _clipStack.Pop(); UpdateClipCache(); }

    private void UpdateClipCache()
    {
        if (_clipStack.Count > 0)
        {
            _hasClip = true;
            var region = _clipStack.Peek();
            var clip = region.Bounds;
            _clipLeft = clip.X;
            _clipTop = clip.Y;
            _clipRight = clip.Right;
            _clipBottom = clip.Bottom;
            _hasGeometryClip = !region.IsRect;
            _clipContains = region.Contains;
        }
        else
        {
            _hasClip = false;
            _hasGeometryClip = false;
            _clipContains = null;
        }
    }

    public void Clear(Color color)
    {
        // BGRA packed fill — far cheaper than per-channel byte loop on full window
        var packed = PackBgra(color);
        if (_layerStack.Count == 0)
        {
            for (var y = 0; y < _bitmapHeight; y++)
                FillPackedBgra(MemoryMarshal.Cast<byte, uint>(_surface.GetRowSpan(y).Slice(0, _bitmapWidth * 4)), packed);
            return;
        }

        var layer = _layerStack.Peek();
        if (!layer.HasBuffer) return;
        for (var y = layer.Y; y < layer.Y + layer.Height; y++)
        {
            var row = _surface.GetRowSpan(y).Slice(layer.X * 4, layer.Width * 4);
            FillPackedBgra(MemoryMarshal.Cast<byte, uint>(row), packed);
        }
    }

    public void Clear(Color color, Rect rect)
    {
        var clipped = ClipRect(TransformRect(rect));
        if (clipped.IsEmpty) return;
        var left = MathF.Ceiling(clipped.Left);
        var top = MathF.Ceiling(clipped.Top);
        var right = MathF.Floor(clipped.Right);
        var bottom = MathF.Floor(clipped.Bottom);
        if (left >= right || top >= bottom) return;
        BlendRect(new Rect(left, top, right - left, bottom - top), color);
    }

    public void FillRect(Rect rect, Brush brush)
    {
        var inverse = Matrix3x2.Invert(_currentTransform, out var value) ? value : Matrix3x2.Identity;
        rect = TransformRect(rect);
        var clipped = ClipRect(rect);
        if (clipped.IsEmpty) return;
        if (brush is SolidColorBrush solid) BlendRect(clipped, solid.Color);
        else BlendBrush(clipped, brush, inverse);
    }

    public void DrawRect(Rect rect, Pen pen)
    {
        if (pen.Width <= 0) return;
        rect = TransformRect(rect);
        var w = (int)Math.Ceiling(pen.Width * _dpiScale);
        var color = (pen.Brush as SolidColorBrush)?.Color ?? Color.Black;

        BlendRect(new Rect(rect.X, rect.Y, rect.Width, w), color);
        BlendRect(new Rect(rect.X, rect.Bottom - w, rect.Width, w), color);
        BlendRect(new Rect(rect.X, rect.Y + w, w, rect.Height - w * 2), color);
        BlendRect(new Rect(rect.Right - w, rect.Y + w, w, rect.Height - w * 2), color);
    }

    public void FillGeometry(Geometry geometry, Brush brush)
    {
        if (brush is not SolidColorBrush sc)
        {
            var inverse = Matrix3x2.Invert(_currentTransform, out var value) ? value : Matrix3x2.Identity;
            switch (geometry)
            {
                case RectGeometry rect: FillRect(rect.Rect, brush); break;
                case RoundedRectGeometry rounded:
                    FillBrushShape(
                        TransformRect(rounded.Rect), brush, inverse,
                        point => ContainsRoundedRect(point, rounded.Rect, rounded.RadiusX, rounded.RadiusY));
                    break;
                case EllipseGeometry ellipse:
                    FillBrushShape(
                        TransformRect(new Rect(ellipse.Center.X - ellipse.RadiusX, ellipse.Center.Y - ellipse.RadiusY, ellipse.RadiusX * 2, ellipse.RadiusY * 2)),
                        brush, inverse,
                        point => ContainsEllipse(point, ellipse.Center, ellipse.RadiusX, ellipse.RadiusY));
                    break;
            }
            return;
        }
        switch (geometry)
        {
            case RectGeometry rg:
                FillRect(rg.Rect, brush);
                break;
            case RoundedRectGeometry rrg:
                FillRoundedRect(
                    TransformRect(rrg.Rect),
                    rrg.RadiusX * GetTransformScaleX(),
                    rrg.RadiusY * GetTransformScaleY(),
                    sc.Color);
                break;
            case EllipseGeometry eg:
                if (IsDpiOnlyTransform())
                    FillEllipse(TransformPoint(eg.Center), eg.RadiusX * _dpiScale, eg.RadiusY * _dpiScale, sc.Color);
                else
                    RasterizeTransformedEllipse(eg.Center, eg.RadiusX, eg.RadiusY, 0, sc.Color);
                break;
            case PathGeometry path:
                FillPath(path, brush);
                break;
        }
    }

    public void DrawGeometry(Geometry geometry, Pen pen)
    {
        switch (geometry)
        {
            case RectGeometry rg:
                DrawRect(rg.Rect, pen);
                break;
            case RoundedRectGeometry rrg:
                DrawRoundedRect(
                    TransformRect(rrg.Rect),
                    rrg.RadiusX * GetTransformScaleX(),
                    rrg.RadiusY * GetTransformScaleY(),
                    pen,
                    GetStrokeTransformScale());
                break;
            case EllipseGeometry eg:
                if (IsDpiOnlyTransform())
                    DrawEllipse(TransformPoint(eg.Center), eg.RadiusX * _dpiScale, eg.RadiusY * _dpiScale, pen);
                else
                    RasterizeTransformedEllipse(
                        eg.Center, eg.RadiusX, eg.RadiusY, pen.Width, (pen.Brush as SolidColorBrush)?.Color ?? Color.Black);
                break;
            case PathGeometry path:
                DrawPath(path, pen);
                break;
        }
    }

    public void FillPath(PathGeometry path, Brush brush)
    {
        if (brush is not SolidColorBrush sc) return;
        var points = FlattenPath(path, _currentTransform);
        if (points.Count < 3) return;
        FillPolygon(points, sc.Color);
    }

    public void DrawPath(PathGeometry path, Pen pen)
    {
        var color = (pen.Brush as SolidColorBrush)?.Color ?? Color.Black;
        var points = FlattenPath(path, _currentTransform);
        if (points.Count < 2) return;
        DrawPolyline(points, pen.Width * GetStrokeTransformScale(), color);
    }

    public void DrawText(TextLayout text, Point origin, Brush brush)
    {
        if (brush is not SolidColorBrush sc) return;
        if (string.IsNullOrEmpty(text.Text)) return;
        RenderText(text, TransformPoint(origin), sc.Color);
        RenderTextDecorations(text, TransformPoint(origin), sc.Color);
    }

    public void DrawImage(Image image, Rect dest, Rect? source = null)
    {
        if (image is not Bitmap src) return;
        var srcRect = source ?? new Rect(0, 0, src.Width, src.Height);
        BlendBitmap(src, srcRect, TransformRect(dest));
    }

    public void PushLayer(Rect bounds, float opacity)
    {
        var pixelBounds = GetLayerPixelBounds(TransformRect(bounds));
        if (pixelBounds.Width == 0 || pixelBounds.Height == 0)
        {
            _layerStack.Push(new LayerState([], 0, 0, 0, 0, NormalizeOpacity(opacity), false));
            return;
        }

        var stride = checked(pixelBounds.Width * 4);
        var length = checked(stride * pixelBounds.Height);
        var background = _layerBufferPool.Rent(length);
        try
        {
            for (var rowIndex = 0; rowIndex < pixelBounds.Height; rowIndex++)
            {
                var surfaceRow = _surface.GetRowSpan(pixelBounds.Y + rowIndex)
                    .Slice(pixelBounds.X * 4, stride);
                surfaceRow.CopyTo(background.AsSpan(rowIndex * stride, stride));
                surfaceRow.Clear();
            }
        }
        catch
        {
            _layerBufferPool.Return(background);
            throw;
        }

        _layerStack.Push(new LayerState(
            background,
            pixelBounds.X,
            pixelBounds.Y,
            pixelBounds.Width,
            pixelBounds.Height,
            NormalizeOpacity(opacity),
            true));
    }

    public void PopLayer()
    {
        if (_layerStack.Count == 0) return;

        var layer = _layerStack.Pop();
        if (!layer.HasBuffer) return;
        var stride = layer.Width * 4;
        try
        {
            for (var rowIndex = 0; rowIndex < layer.Height; rowIndex++)
            {
                var surfaceRow = _surface.GetRowSpan(layer.Y + rowIndex)
                    .Slice(layer.X * 4, stride);
                var background = layer.Background.AsSpan(rowIndex * stride, stride);
                CompositeLayerRow(surfaceRow, background, layer.Opacity);
                background.CopyTo(surfaceRow);
            }
        }
        finally
        {
            _layerBufferPool.Return(layer.Background);
        }
    }

    public void Flush() { }

    public void Present() => Present(null);

    public void Present(IReadOnlyList<Rect>? dirtyRects)
    {
        // 空列表 = 无区域需要上传
        if (dirtyRects is { Count: 0 }) return;
        _surface.Present(ScaleDirtyRects(dirtyRects));
    }

    private static uint PackBgra(Color color)
    {
        var pr = (byte)(color.R * color.A / 255);
        var pg = (byte)(color.G * color.A / 255);
        var pb = (byte)(color.B * color.A / 255);
        return (uint)(pb | (pg << 8) | (pr << 16) | (color.A << 24));
    }

    public void Resize(Size canvasSize)
        => Resize(canvasSize, _dpiScale);

    public void Resize(Size canvasSize, float dpiScale)
    {
        dpiScale = NormalizeDpiScale(dpiScale);
        var width = Math.Max(1, (int)MathF.Ceiling(canvasSize.Width * dpiScale));
        var height = Math.Max(1, (int)MathF.Ceiling(canvasSize.Height * dpiScale));

        _canvasSize = canvasSize;
        _dpiScale = dpiScale;
        _transformStack.Clear();
        ReleaseLayers(restoreBackground: true);
        _currentOpacity = 1f;
        _clipStack.Clear();
        UpdateClipCache();
        _currentTransform = CreateDpiTransform(dpiScale);
        if (_bitmapWidth == width && _bitmapHeight == height) return;

        _surface.Resize(width, height);
        _bitmapWidth = _surface.Width;
        _bitmapHeight = _surface.Height;
        _bitmapStride = _surface.Stride;
    }

    private static float NormalizeDpiScale(float dpiScale)
        => float.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1f;

    private static float NormalizeOpacity(float opacity)
        => float.IsNaN(opacity) ? 1f : Math.Clamp(opacity, 0f, 1f);

    private static Matrix3x2 CreateDpiTransform(float dpiScale)
        => Matrix3x2.CreateScale(dpiScale);

    private TextLayout ScaleTextLayout(TextLayout text)
    {
        if (_dpiScale == 1f) return text;
        return new TextLayout(text.Text, text.Font.WithSize(text.Font.Size * _dpiScale))
        {
            MaxSize = new Size(text.MaxSize.Width * _dpiScale, text.MaxSize.Height * _dpiScale),
            Alignment = text.Alignment,
            LineHeight = text.LineHeight,
            Direction = text.Direction,
            UnicodeBidi = text.UnicodeBidi,
            WhiteSpace = text.WhiteSpace,
            LetterSpacing = text.LetterSpacing * _dpiScale,
            WordSpacing = text.WordSpacing * _dpiScale,
            TextTransform = text.TextTransform,
            TextIndent = text.TextIndent * _dpiScale,
            CollapseNewlines = text.CollapseNewlines,
            TextDecorationLines = text.TextDecorationLines
        };
    }

    private IReadOnlyList<Rect>? ScaleDirtyRects(IReadOnlyList<Rect>? dirtyRects)
    {
        if (dirtyRects == null || _dpiScale == 1f) return dirtyRects;
        if (_scaledDirtyRects.Length < dirtyRects.Count)
            _scaledDirtyRects = new Rect[Math.Max(dirtyRects.Count, _scaledDirtyRects.Length * 2)];
        for (var i = 0; i < dirtyRects.Count; i++)
        {
            var rect = dirtyRects[i];
            _scaledDirtyRects[i] = new Rect(
                MathF.Floor(rect.X * _dpiScale),
                MathF.Floor(rect.Y * _dpiScale),
                MathF.Ceiling(rect.Right * _dpiScale) - MathF.Floor(rect.X * _dpiScale),
                MathF.Ceiling(rect.Bottom * _dpiScale) - MathF.Floor(rect.Y * _dpiScale));
        }
        return new ArraySegment<Rect>(_scaledDirtyRects, 0, dirtyRects.Count);
    }

    public void Dispose()
    {
        _clipStack.Clear();
        _transformStack.Clear();
        ReleaseLayers(restoreBackground: false);
        _layerBufferPool.Clear();
        _scaledDirtyRects = [];
        _surface.Dispose();
        _bitmapWidth = 0;
        _bitmapHeight = 0;
        _bitmapStride = 0;
    }

    internal Bitmap GetBitmap() => _surface is BitmapSoftwareRenderSurface bitmapSurface
        ? bitmapSurface.Bitmap
        : CaptureBitmap();

    public Bitmap CaptureBitmap()
    {
        var copy = new Bitmap(_bitmapWidth, _bitmapHeight);
        for (var y = 0; y < _bitmapHeight; y++)
            _surface.GetRowSpan(y).Slice(0, _bitmapWidth * 4).CopyTo(copy.GetRow(y));
        return copy;
    }

    // ── 核心：像素混合 ──

    private void BlendRect(Rect rect, Color color)
    {
        color = ApplyOpacity(color);
        var alpha = color.A;
        if (alpha == 0) return;

        var pr = (byte)(color.R * alpha / 255);
        var pg = (byte)(color.G * alpha / 255);
        var pb = (byte)(color.B * alpha / 255);

        var x0 = Math.Max(0, (int)Math.Round(rect.X));
        var y0 = Math.Max(0, (int)Math.Round(rect.Y));
        var x1 = Math.Min(_bitmapWidth, (int)Math.Round(rect.Right));
        var y1 = Math.Min(_bitmapHeight, (int)Math.Round(rect.Bottom));
        if (_hasClip)
        {
            x0 = Math.Max(x0, (int)Math.Ceiling(_clipLeft));
            y0 = Math.Max(y0, (int)Math.Ceiling(_clipTop));
            x1 = Math.Min(x1, (int)Math.Floor(_clipRight));
            y1 = Math.Min(y1, (int)Math.Floor(_clipBottom));
        }
        if (x0 >= x1 || y0 >= y1) return;

        if (alpha == 255 && (_clipStack.Count == 0 || _clipStack.Peek().IsRect))
        {
            // Opaque solid fill via uint row writes
            var packed = (uint)(pb | (pg << 8) | (pr << 16) | (255 << 24));
            var width = x1 - x0;
            for (var y = y0; y < y1; y++)
            {
                var row = MemoryMarshal.Cast<byte, uint>(_surface.GetRowSpan(y));
                FillPackedBgra(row.Slice(x0, width), packed);
            }
            return;
        }

        if (!_hasGeometryClip)
        {
            for (var y = y0; y < y1; y++)
            {
                var row = _surface.GetRowSpan(y).Slice(x0 * 4, (x1 - x0) * 4);
                BlendSolidRow(row, color.B, color.G, color.R, alpha);
            }
            return;
        }

        for (int y = y0; y < y1; y++)
        {
            var span = _surface.GetRowSpan(y);
            for (int x = x0; x < x1; x++)
            {
                if (!IsPointVisible(x + 0.5f, y + 0.5f)) continue;
                var idx = x * 4;
                var dstA = span[idx + 3];
                var outA = (byte)(alpha + (dstA * (255 - alpha) / 255));
                if (outA == 0) continue;
                span[idx] = (byte)((pb * 255 + span[idx] * dstA * (255 - alpha) / 255) / outA);
                span[idx + 1] = (byte)((pg * 255 + span[idx + 1] * dstA * (255 - alpha) / 255) / outA);
                span[idx + 2] = (byte)((pr * 255 + span[idx + 2] * dstA * (255 - alpha) / 255) / outA);
                span[idx + 3] = outA;
            }
        }
    }

    private void BlendRectCoverage(Rect rect, Color color)
    {
        if (rect.IsEmpty) return;
        var x0 = Math.Max(0, (int)MathF.Floor(rect.Left));
        var y0 = Math.Max(0, (int)MathF.Floor(rect.Top));
        var x1 = Math.Min(_bitmapWidth - 1, (int)MathF.Ceiling(rect.Right));
        var y1 = Math.Min(_bitmapHeight - 1, (int)MathF.Ceiling(rect.Bottom));
        if (_hasClip)
        {
            x0 = Math.Max(x0, (int)MathF.Floor(_clipLeft));
            y0 = Math.Max(y0, (int)MathF.Floor(_clipTop));
            x1 = Math.Min(x1, (int)MathF.Ceiling(_clipRight) - 1);
            y1 = Math.Min(y1, (int)MathF.Ceiling(_clipBottom) - 1);
        }
        if (x0 > x1 || y0 > y1) return;

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var covered = 0;
                for (var sy = 0; sy < CoverageSampleGrid; sy++)
                for (var sx = 0; sx < CoverageSampleGrid; sx++)
                {
                    var px = x + (sx + 0.5f) / CoverageSampleGrid;
                    var py = y + (sy + 0.5f) / CoverageSampleGrid;
                    if (px >= rect.Left && px < rect.Right && py >= rect.Top && py < rect.Bottom)
                        covered++;
                }

                BlendPixelCoverage(x, y, color, covered, CoverageSampleCount);
            }
        }
    }

    private static void FillPackedBgra(Span<uint> pixels, uint packed)
    {
        pixels.Fill(packed);
    }

    private void BlendPixel(int x, int y, Color color)
    {
        if ((uint)x >= (uint)_bitmapWidth || (uint)y >= (uint)_bitmapHeight) return;
        if (!IsPointVisible(x + 0.5f, y + 0.5f)) return;
        color = ApplyOpacity(color);
        BlendPixelCore(x, y, color.R, color.G, color.B, color.A);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BlendPixelCore(int x, int y, byte red, byte green, byte blue, byte alpha)
    {
        if (alpha == 0) return;
        var idx = x * 4;
        var row = _surface.GetRowSpan(y);
        var pr = (byte)(red * alpha / 255);
        var pg = (byte)(green * alpha / 255);
        var pb = (byte)(blue * alpha / 255);
        if (alpha == 255)
        {
            row[idx] = pb;
            row[idx + 1] = pg;
            row[idx + 2] = pr;
            row[idx + 3] = 255;
        }
        else
        {
            var dstA = row[idx + 3];
            var inverseAlpha = 255 - alpha;
            if (dstA == 255)
            {
                row[idx] = BlendOpaqueDestination(blue, alpha, row[idx], inverseAlpha);
                row[idx + 1] = BlendOpaqueDestination(green, alpha, row[idx + 1], inverseAlpha);
                row[idx + 2] = BlendOpaqueDestination(red, alpha, row[idx + 2], inverseAlpha);
                return;
            }
            var outA = (byte)(alpha + (dstA * inverseAlpha / 255));
            if (outA == 0) return;
            row[idx] = (byte)((pb * 255 + row[idx] * dstA * inverseAlpha / 255) / outA);
            row[idx + 1] = (byte)((pg * 255 + row[idx + 1] * dstA * inverseAlpha / 255) / outA);
            row[idx + 2] = (byte)((pr * 255 + row[idx + 2] * dstA * inverseAlpha / 255) / outA);
            row[idx + 3] = outA;
        }
    }

    private static void BlendSolidRow(Span<byte> row, byte blue, byte green, byte red, byte alpha)
    {
        var inverseAlpha = 255 - alpha;
        var offset = 0;
        for (; offset < row.Length; offset += 4)
        {
            var destinationAlpha = row[offset + 3];
            if (destinationAlpha == 255)
            {
                row[offset] = BlendOpaqueDestination(blue, alpha, row[offset], inverseAlpha);
                row[offset + 1] = BlendOpaqueDestination(green, alpha, row[offset + 1], inverseAlpha);
                row[offset + 2] = BlendOpaqueDestination(red, alpha, row[offset + 2], inverseAlpha);
                continue;
            }

            var outputAlpha = (byte)(alpha + destinationAlpha * inverseAlpha / 255);
            if (outputAlpha == 0) continue;
            row[offset] = (byte)((blue * alpha + row[offset] * destinationAlpha * inverseAlpha / 255) / outputAlpha);
            row[offset + 1] = (byte)((green * alpha + row[offset + 1] * destinationAlpha * inverseAlpha / 255) / outputAlpha);
            row[offset + 2] = (byte)((red * alpha + row[offset + 2] * destinationAlpha * inverseAlpha / 255) / outputAlpha);
            row[offset + 3] = outputAlpha;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte BlendOpaqueDestination(byte source, byte sourceAlpha, byte destination, int inverseAlpha)
        => (byte)((source * sourceAlpha + destination * inverseAlpha + 127) / 255);

    // ── 裁剪 ──

    private Rect ClipRect(Rect rect)
    {
        if (_hasClip) rect = Rect.Intersect(rect, _clipStack.Peek().Bounds);
        if (_layerStack.Count > 0)
        {
            var layer = _layerStack.Peek();
            rect = Rect.Intersect(rect, new Rect(layer.X, layer.Y, layer.Width, layer.Height));
        }
        return rect;
    }

    private void PushClipRegion(Rect bounds, Func<float, float, bool>? contains, bool isRect)
    {
        if (_clipStack.Count > 0)
        {
            var parent = _clipStack.Peek();
            bounds = Rect.Intersect(parent.Bounds, bounds);
            if (!parent.IsRect || !isRect)
            {
                var childContains = contains;
                contains = (x, y) => parent.ContainsPoint(x, y) &&
                    (childContains == null || childContains(x, y));
                isRect = false;
            }
        }
        _clipStack.Push(new ClipRegion(bounds, contains, isRect));
    }

    private bool IsPointVisible(float x, float y)
    {
        if (_layerStack.Count > 0)
        {
            var layer = _layerStack.Peek();
            if (x < layer.X || x >= layer.X + layer.Width || y < layer.Y || y >= layer.Y + layer.Height)
                return false;
        }
        if (!_hasClip) return true;
        if (x < _clipLeft || x >= _clipRight || y < _clipTop || y >= _clipBottom) return false;
        return !_hasGeometryClip || _clipContains?.Invoke(x, y) == true;
    }

    private void BlendBrush(Rect bounds, Brush brush, Matrix3x2 inverse)
        => FillBrushShape(bounds, brush, inverse, static _ => true);

    private void FillBrushShape(Rect bounds, Brush brush, Matrix3x2 inverse, Func<Point, bool> contains)
    {
        var clipped = ClipRect(bounds);
        var x0 = Math.Max(0, (int)MathF.Floor(clipped.Left));
        var y0 = Math.Max(0, (int)MathF.Floor(clipped.Top));
        var x1 = Math.Min(_bitmapWidth, (int)MathF.Ceiling(clipped.Right));
        var y1 = Math.Min(_bitmapHeight, (int)MathF.Ceiling(clipped.Bottom));
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var logical = TransformPoint(inverse, x + 0.5f, y + 0.5f);
                if (!contains(logical)) continue;
                BlendPixel(x, y, SampleBrush(brush, logical));
            }
        }
    }

    private static Color SampleBrush(Brush brush, Point point)
    {
        GradientStop[] stops;
        float offset;
        GradientSpreadMethod spread;
        switch (brush)
        {
            case LinearGradientBrush linear:
                var dx = linear.End.X - linear.Start.X;
                var dy = linear.End.Y - linear.Start.Y;
                var lengthSquared = dx * dx + dy * dy;
                offset = lengthSquared <= float.Epsilon
                    ? 0
                    : ((point.X - linear.Start.X) * dx + (point.Y - linear.Start.Y) * dy) / lengthSquared;
                stops = linear.Stops;
                spread = linear.SpreadMethod;
                break;
            case RadialGradientBrush radial:
                offset = radial.Radius <= 0
                    ? 0
                    : MathF.Sqrt(MathF.Pow(point.X - radial.Center.X, 2) + MathF.Pow(point.Y - radial.Center.Y, 2)) / radial.Radius;
                stops = radial.Stops;
                spread = radial.SpreadMethod;
                break;
            case SolidColorBrush solid:
                return solid.Color;
            default:
                return Color.Transparent;
        }
        if (stops.Length == 0) return Color.Transparent;
        offset = ApplySpread(offset, spread);
        GradientStop? minimum = null;
        GradientStop? maximum = null;
        GradientStop? lower = null;
        GradientStop? upper = null;
        foreach (var stop in stops)
        {
            if (minimum == null || stop.Offset < minimum.Offset) minimum = stop;
            if (maximum == null || stop.Offset >= maximum.Offset) maximum = stop;
            if (stop.Offset < offset && (lower == null || stop.Offset >= lower.Offset)) lower = stop;
            if (stop.Offset >= offset && (upper == null || stop.Offset < upper.Offset)) upper = stop;
        }
        if (offset <= minimum!.Offset) return minimum.Color;
        if (offset >= maximum!.Offset) return maximum.Color;
        lower ??= minimum;
        upper ??= maximum;
        var range = upper.Offset - lower.Offset;
        var amount = range <= float.Epsilon ? 0 : (offset - lower.Offset) / range;
        return Lerp(lower.Color, upper.Color, amount);
    }

    private static float ApplySpread(float offset, GradientSpreadMethod spread)
    {
        if (spread == GradientSpreadMethod.Repeat) return offset - MathF.Floor(offset);
        if (spread == GradientSpreadMethod.Reflect)
        {
            offset -= MathF.Floor(offset / 2f) * 2f;
            return offset <= 1f ? offset : 2f - offset;
        }
        return Math.Clamp(offset, 0, 1);
    }

    private static Color Lerp(Color start, Color end, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new Color(
            (byte)MathF.Round(start.R + (end.R - start.R) * amount),
            (byte)MathF.Round(start.G + (end.G - start.G) * amount),
            (byte)MathF.Round(start.B + (end.B - start.B) * amount),
            (byte)MathF.Round(start.A + (end.A - start.A) * amount));
    }

    private static bool ContainsRoundedRect(Point point, Rect rect, float radiusX, float radiusY)
    {
        if (!rect.Contains(point)) return false;
        radiusX = Math.Clamp(radiusX, 0, rect.Width / 2f);
        radiusY = Math.Clamp(radiusY, 0, rect.Height / 2f);
        if (radiusX <= 0 || radiusY <= 0) return true;
        var nearestX = Math.Clamp(point.X, rect.Left + radiusX, rect.Right - radiusX);
        var nearestY = Math.Clamp(point.Y, rect.Top + radiusY, rect.Bottom - radiusY);
        var dx = (point.X - nearestX) / radiusX;
        var dy = (point.Y - nearestY) / radiusY;
        return dx * dx + dy * dy <= 1;
    }

    private static bool ContainsEllipse(Point point, Point center, float radiusX, float radiusY)
    {
        if (radiusX <= 0 || radiusY <= 0) return false;
        var dx = (point.X - center.X) / radiusX;
        var dy = (point.Y - center.Y) / radiusY;
        return dx * dx + dy * dy <= 1;
    }

    private static bool ContainsPolygon(IReadOnlyList<Point> points, Point point)
    {
        var inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            var a = points[i];
            var b = points[j];
            if ((a.Y > point.Y) == (b.Y > point.Y)) continue;
            if (point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    private static Rect GetBounds(IReadOnlyList<Point> points, Matrix3x2 transform)
    {
        if (points.Count == 0) return Rect.Empty;
        var first = Vector2.Transform(new Vector2(points[0].X, points[0].Y), transform);
        var left = first.X;
        var top = first.Y;
        var right = first.X;
        var bottom = first.Y;
        for (var i = 1; i < points.Count; i++)
        {
            var point = Vector2.Transform(new Vector2(points[i].X, points[i].Y), transform);
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }
        return new Rect(left, top, right - left, bottom - top);
    }

    private static Point TransformPoint(Matrix3x2 matrix, float x, float y)
    {
        var transformed = Vector2.Transform(new Vector2(x, y), matrix);
        return new Point(transformed.X, transformed.Y);
    }

    private sealed record ClipRegion(Rect Bounds, Func<float, float, bool>? Contains, bool IsRect)
    {
        public bool ContainsPoint(float x, float y) =>
            x >= Bounds.Left && x < Bounds.Right && y >= Bounds.Top && y < Bounds.Bottom &&
            (IsRect || Contains?.Invoke(x, y) == true);
    }

    private Point TransformPoint(Point point)
    {
        if (_currentTransform.IsIdentity) return point;
        var transformed = Vector2.Transform(new Vector2(point.X, point.Y), _currentTransform);
        return new Point(transformed.X, transformed.Y);
    }

    private Rect TransformRect(Rect rect)
    {
        if (rect.IsEmpty || _currentTransform.IsIdentity) return rect;

        var p1 = Vector2.Transform(new Vector2(rect.Left, rect.Top), _currentTransform);
        var p2 = Vector2.Transform(new Vector2(rect.Right, rect.Top), _currentTransform);
        var p3 = Vector2.Transform(new Vector2(rect.Right, rect.Bottom), _currentTransform);
        var p4 = Vector2.Transform(new Vector2(rect.Left, rect.Bottom), _currentTransform);
        var left = MathF.Min(MathF.Min(p1.X, p2.X), MathF.Min(p3.X, p4.X));
        var top = MathF.Min(MathF.Min(p1.Y, p2.Y), MathF.Min(p3.Y, p4.Y));
        var right = MathF.Max(MathF.Max(p1.X, p2.X), MathF.Max(p3.X, p4.X));
        var bottom = MathF.Max(MathF.Max(p1.Y, p2.Y), MathF.Max(p3.Y, p4.Y));
        return new Rect(left, top, right - left, bottom - top);
    }

    private float GetTransformScaleX()
        => MathF.Sqrt(_currentTransform.M11 * _currentTransform.M11 + _currentTransform.M12 * _currentTransform.M12);

    private float GetTransformScaleY()
        => MathF.Sqrt(_currentTransform.M21 * _currentTransform.M21 + _currentTransform.M22 * _currentTransform.M22);

    private float GetStrokeTransformScale()
        => MathF.Max(GetTransformScaleX(), GetTransformScaleY());

    private bool IsDpiOnlyTransform()
    {
        const float tolerance = 0.0001f;
        return MathF.Abs(_currentTransform.M11 - _dpiScale) < tolerance &&
               MathF.Abs(_currentTransform.M22 - _dpiScale) < tolerance &&
               MathF.Abs(_currentTransform.M12) < tolerance &&
               MathF.Abs(_currentTransform.M21) < tolerance &&
               MathF.Abs(_currentTransform.M31) < tolerance &&
               MathF.Abs(_currentTransform.M32) < tolerance;
    }

    // ── 圆角矩形 ──

    private void FillRoundedRect(Rect rect, float rx, float ry, Color color)
    {
        RasterizeRoundedRect(rect, rx, ry, strokeWidth: 0, color);
    }

    private void DrawRoundedRect(Rect rect, float rx, float ry, Pen pen, float strokeScale)
    {
        var color = (pen.Brush as SolidColorBrush)?.Color ?? Color.Black;
        if (pen.Width <= 0) return;
        // 描边以几何边缘为中心（与 Skia/Vulkan 一致）：外侧膨胀半宽后按内侧描边光栅化。
        var width = pen.Width * strokeScale;
        var half = width / 2f;
        RasterizeRoundedRect(rect.Inflate(half, half), rx + half, ry + half, width, color);
    }

    private void RasterizeRoundedRect(Rect rect, float rx, float ry, float strokeWidth, Color color)
    {
        if (rect.IsEmpty) return;
        rx = Math.Clamp(rx, 0, rect.Width / 2f);
        ry = Math.Clamp(ry, 0, rect.Height / 2f);
        if (rx <= 0 || ry <= 0)
        {
            if (strokeWidth > 0) DrawRectPixels(rect, strokeWidth, color);
            else BlendRect(rect, color);
            return;
        }

        if (strokeWidth <= 0)
        {
            FillRoundedRectFast(rect, rx, ry, color);
            return;
        }

        DrawRoundedRectFast(rect, rx, ry, strokeWidth, color);
    }

    private void FillRoundedRectFast(Rect rect, float rx, float ry, Color color)
    {
        var left = rect.X;
        var top = rect.Y;
        var right = left + rect.Width;
        var bottom = top + rect.Height;
        var innerLeft = left + rx;
        var innerTop = top + ry;
        var innerRight = right - rx;
        var innerBottom = bottom - ry;
        var invRx2 = 1f / (rx * rx);
        var invRy2 = 1f / (ry * ry);
        var sampleInset = 0.5f / CoverageSampleGrid;
        var sampleMaxInset = 1f - sampleInset;

        var x0 = Math.Max(0, (int)MathF.Floor(left));
        var y0 = Math.Max(0, (int)MathF.Floor(top));
        var x1 = Math.Min(_bitmapWidth, (int)MathF.Ceiling(right));
        var y1 = Math.Min(_bitmapHeight, (int)MathF.Ceiling(bottom));
        if (_hasClip)
        {
            x0 = Math.Max(x0, (int)MathF.Floor(_clipLeft));
            y0 = Math.Max(y0, (int)MathF.Floor(_clipTop));
            x1 = Math.Min(x1, (int)MathF.Ceiling(_clipRight));
            y1 = Math.Min(y1, (int)MathF.Ceiling(_clipBottom));
        }
        if (x0 >= x1 || y0 >= y1) return;

        for (var y = y0; y < y1; y++)
        {
            var sampleTop = y + sampleInset;
            var sampleBottom = y + sampleMaxInset;
            var fullStart = x1;
            var fullEnd = x1;
            if (sampleTop >= top && sampleBottom <= bottom)
            {
                var inVerticalBody = sampleTop >= innerTop && sampleBottom <= innerBottom;
                var fullLeft = inVerticalBody ? left : innerLeft;
                var fullRight = inVerticalBody ? right : innerRight;
                fullStart = Math.Clamp((int)MathF.Ceiling(fullLeft - sampleInset), x0, x1);
                fullEnd = Math.Clamp((int)MathF.Floor(fullRight - sampleMaxInset) + 1, fullStart, x1);
            }

            for (var x = x0; x < x1; x++)
            {
                if (x == fullStart && fullStart < fullEnd)
                {
                    BlendRect(new Rect(fullStart, y, fullEnd - fullStart, 1), color);
                    x = fullEnd - 1;
                    continue;
                }

                var coverage = GetRoundedRectFeatherCoverage(x + 0.5f, y + 0.5f, rect, rx, ry);
                BlendPixelCoverage(x, y, color, coverage);
            }
        }
    }

    private static byte GetRoundedRectFeatherCoverage(float px, float py, Rect rect, float rx, float ry)
    {
        var edgeCoverage = MathF.Min(
            MathF.Min(px - rect.Left + 0.5f, rect.Right - px + 0.5f),
            MathF.Min(py - rect.Top + 0.5f, rect.Bottom - py + 0.5f));
        if (edgeCoverage <= 0) return 0;

        var innerLeft = rect.Left + rx;
        var innerRight = rect.Right - rx;
        var innerTop = rect.Top + ry;
        var innerBottom = rect.Bottom - ry;
        var inCorner = (px < innerLeft || px > innerRight) && (py < innerTop || py > innerBottom);
        if (!inCorner)
            return (byte)Math.Clamp((int)(edgeCoverage * 255f), 0, 255);

        var cx = px < innerLeft ? innerLeft : innerRight;
        var cy = py < innerTop ? innerTop : innerBottom;
        var dx = px - cx;
        var dy = py - cy;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance <= float.Epsilon) return 255;

        var ux = dx / distance;
        var uy = dy / distance;
        var radius = 1f / MathF.Sqrt(ux * ux / (rx * rx) + uy * uy / (ry * ry));
        return (byte)Math.Clamp((int)((radius + 0.5f - distance) * 255f), 0, 255);
    }

    private void DrawRoundedRectFast(Rect rect, float rx, float ry, float strokeWidth, Color color)
    {
        RoundedRectStrokeFastPathCount++;
        var width = Math.Min(strokeWidth, Math.Min(rect.Width, rect.Height) / 2f);
        BlendRectCoverage(new Rect(rect.X + rx, rect.Y, Math.Max(0, rect.Width - rx * 2), width), color);
        BlendRectCoverage(new Rect(rect.X + rx, rect.Bottom - width, Math.Max(0, rect.Width - rx * 2), width), color);
        BlendRectCoverage(new Rect(rect.X, rect.Y + ry, width, Math.Max(0, rect.Height - ry * 2)), color);
        BlendRectCoverage(new Rect(rect.Right - width, rect.Y + ry, width, Math.Max(0, rect.Height - ry * 2)), color);

        var hasInner = rect.Width > width * 2 && rect.Height > width * 2;
        var inner = hasInner ? rect.Inflate(-width, -width) : Rect.Empty;
        RasterizeRoundedRectCorners(rect, rx, ry, inner, Math.Max(0, rx - width), Math.Max(0, ry - width), hasInner, color);
    }

    private void RasterizeRoundedRectCorners(Rect rect, float rx, float ry, Rect inner, float innerRx, float innerRy, bool hasInner, Color color)
    {
        RasterizeRoundedRectCorner(new Rect(rect.X, rect.Y, rx, ry), rect, rx, ry, inner, innerRx, innerRy, hasInner, color);
        RasterizeRoundedRectCorner(new Rect(rect.Right - rx, rect.Y, rx, ry), rect, rx, ry, inner, innerRx, innerRy, hasInner, color);
        RasterizeRoundedRectCorner(new Rect(rect.X, rect.Bottom - ry, rx, ry), rect, rx, ry, inner, innerRx, innerRy, hasInner, color);
        RasterizeRoundedRectCorner(new Rect(rect.Right - rx, rect.Bottom - ry, rx, ry), rect, rx, ry, inner, innerRx, innerRy, hasInner, color);
    }

    private void RasterizeRoundedRectCorner(Rect cornerBounds, Rect rect, float rx, float ry, Rect inner, float innerRx, float innerRy, bool hasInner, Color color)
    {
        var x0 = Math.Max(0, (int)MathF.Floor(rect.X));
        x0 = Math.Max(x0, (int)MathF.Floor(cornerBounds.X));
        var y0 = Math.Max(0, (int)MathF.Floor(cornerBounds.Y));
        var x1 = Math.Min(_bitmapWidth - 1, (int)MathF.Ceiling(cornerBounds.Right));
        var y1 = Math.Min(_bitmapHeight - 1, (int)MathF.Ceiling(cornerBounds.Bottom));
        var outerCx = cornerBounds.X < rect.X + rx ? rect.X + rx : rect.Right - rx;
        var outerCy = cornerBounds.Y < rect.Y + ry ? rect.Y + ry : rect.Bottom - ry;
        var isLeft = cornerBounds.X < rect.X + rx;
        var isTop = cornerBounds.Y < rect.Y + ry;
        var outerInvRx2 = 1f / (rx * rx);
        var outerInvRy2 = 1f / (ry * ry);

        var innerCx = 0f;
        var innerCy = 0f;
        var innerInvRx2 = 0f;
        var innerInvRy2 = 0f;
        if (hasInner && innerRx > 0 && innerRy > 0)
        {
            innerCx = cornerBounds.X < rect.X + rx ? inner.X + innerRx : inner.Right - innerRx;
            innerCy = cornerBounds.Y < rect.Y + ry ? inner.Y + innerRy : inner.Bottom - innerRy;
            innerInvRx2 = 1f / (innerRx * innerRx);
            innerInvRy2 = 1f / (innerRy * innerRy);
        }

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var covered = 0;
                for (var sy = 0; sy < CoverageSampleGrid; sy++)
                {
                    for (var sx = 0; sx < CoverageSampleGrid; sx++)
                    {
                        var px = x + (sx + 0.5f) / CoverageSampleGrid;
                        var py = y + (sy + 0.5f) / CoverageSampleGrid;
                        if (isLeft ? px >= outerCx : px < outerCx) continue;
                        if (isTop ? py >= outerCy : py < outerCy) continue;
                        if (!IsInsideEllipse(px, py, outerCx, outerCy, outerInvRx2, outerInvRy2)) continue;
                        if (hasInner && IsInsideInnerRoundedRectCorner(px, py, inner, innerRx, innerRy, innerCx, innerCy, innerInvRx2, innerInvRy2)) continue;
                        covered++;
                    }
                }
                BlendPixelCoverage(x, y, color, covered, CoverageSampleCount);
            }
        }
    }

    private static bool IsInsideEllipse(float x, float y, float cx, float cy, float invRx2, float invRy2)
    {
        var dx = x - cx;
        var dy = y - cy;
        return dx * dx * invRx2 + dy * dy * invRy2 <= 1f;
    }

    private static bool IsInsideInnerRoundedRectCorner(
        float x, float y, Rect inner, float innerRx, float innerRy,
        float cornerCx, float cornerCy, float invRx2, float invRy2)
    {
        if (x < inner.X || x > inner.Right || y < inner.Y || y > inner.Bottom) return false;
        var inHorizontalBody = x >= inner.X + innerRx && x <= inner.Right - innerRx;
        var inVerticalBody = y >= inner.Y + innerRy && y <= inner.Bottom - innerRy;
        if (inHorizontalBody || inVerticalBody) return true;
        return IsInsideEllipse(x, y, cornerCx, cornerCy, invRx2, invRy2);
    }

    private void DrawRectPixels(Rect rect, float width, Color color)
    {
        var w = (int)Math.Ceiling(width);
        BlendRectCoverage(new Rect(rect.X, rect.Y, rect.Width, w), color);
        BlendRectCoverage(new Rect(rect.X, rect.Bottom - w, rect.Width, w), color);
        BlendRectCoverage(new Rect(rect.X, rect.Y + w, w, rect.Height - w * 2), color);
        BlendRectCoverage(new Rect(rect.Right - w, rect.Y + w, w, rect.Height - w * 2), color);
    }

    // ── 椭圆 ──

    private void FillEllipse(Point center, float rx, float ry, Color color)
    {
        if (rx <= 0 || ry <= 0) return;
        RasterizeEllipse(center, rx, ry, 0, color);
    }

    private void DrawEllipse(Point center, float rx, float ry, Pen pen)
    {
        var color = (pen.Brush as SolidColorBrush)?.Color ?? Color.Black;
        if (rx <= 0 || ry <= 0 || pen.Width <= 0) return;
        RasterizeEllipse(center, rx, ry, pen.Width * _dpiScale, color);
    }

    // ── 线段 ──

    private void DrawLine(double x0, double y0, double x1, double y1, float width, Color color)
    {
        var dx = (float)(x1 - x0);
        var dy = (float)(y1 - y0);
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 0.0001 || width <= 0) return;

        // Axis-aligned fast path
        if (Math.Abs(dx) < 0.01)
        {
            var x = (float)x0 - width / 2f;
            var top = (float)Math.Min(y0, y1);
            var h = (float)Math.Abs(dy);
            BlendRectCoverage(new Rect(x, top, width, Math.Max(1, h)), color);
            return;
        }
        if (Math.Abs(dy) < 0.01)
        {
            var y = (float)y0 - width / 2f;
            var left = (float)Math.Min(x0, x1);
            var w = (float)Math.Abs(dx);
            BlendRectCoverage(new Rect(left, y, Math.Max(1, w), width), color);
            return;
        }

        // Thick line: distance field with 4x4 samples.
        var radius = MathF.Max(0.5f, width / 2f);
        var radiusSquared = radius * radius;
        var inverseLengthSquared = 1f / lengthSquared;
        var startX = (float)x0;
        var startY = (float)y0;
        var sampleOffsets = new Vector4(0.125f, 0.375f, 0.625f, 0.875f);
        var lineDx = new Vector4(dx);
        var lineDy = new Vector4(dy);
        var inverseLength = new Vector4(inverseLengthSquared);
        var zero = Vector4.Zero;
        var one = Vector4.One;
        var minX = (int)Math.Floor(Math.Min(x0, x1) - radius - 1);
        var maxX = (int)Math.Ceiling(Math.Max(x0, x1) + radius + 1);
        var minY = (int)Math.Floor(Math.Min(y0, y1) - radius - 1);
        var maxY = (int)Math.Ceiling(Math.Max(y0, y1) + radius + 1);
        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        maxX = Math.Min(_bitmapWidth - 1, maxX);
        maxY = Math.Min(_bitmapHeight - 1, maxY);
        if (_hasClip)
        {
            minX = Math.Max(minX, (int)MathF.Floor(_clipLeft));
            minY = Math.Max(minY, (int)MathF.Floor(_clipTop));
            maxX = Math.Min(maxX, (int)MathF.Ceiling(_clipRight) - 1);
            maxY = Math.Min(maxY, (int)MathF.Ceiling(_clipBottom) - 1);
        }
        if (minX > maxX || minY > maxY) return;

        for (var y = minY; y <= maxY; y++)
        {
            var sampleXBase = new Vector4(minX - startX) + sampleOffsets;
            for (var x = minX; x <= maxX; x++)
            {
                var covered = 0;
                for (var sy = 0; sy < CoverageSampleGrid; sy++)
                {
                    var sampleY = y + (sy + 0.5f) / CoverageSampleGrid - startY;
                    var sampleYVector = new Vector4(sampleY);
                    var t = Vector4.Clamp(
                        (sampleXBase * lineDx + sampleYVector * lineDy) * inverseLength,
                        zero,
                        one);
                    var distanceX = sampleXBase - t * lineDx;
                    var distanceY = sampleYVector - t * lineDy;
                    var distanceSquared = distanceX * distanceX + distanceY * distanceY;
                    if (distanceSquared.X <= radiusSquared) covered++;
                    if (distanceSquared.Y <= radiusSquared) covered++;
                    if (distanceSquared.Z <= radiusSquared) covered++;
                    if (distanceSquared.W <= radiusSquared) covered++;
                }
                BlendPixelCoverage(x, y, color, covered, CoverageSampleCount);
                sampleXBase += Vector4.One;
            }
        }
    }

    private void RasterizeEllipse(Point center, float rx, float ry, float strokeWidth, Color color)
    {
        if (rx <= 0 || ry <= 0) return;

        // Solid fill: scanline (no supersampling) — dominant path for clock face
        if (strokeWidth <= 0)
        {
            FillEllipseScanline(center, rx, ry, color);
            return;
        }

        var halfStroke = strokeWidth / 2f;
        var outerRx = rx + halfStroke;
        var outerRy = ry + halfStroke;
        var innerRx = Math.Max(0, rx - halfStroke);
        var innerRy = Math.Max(0, ry - halfStroke);
        var x0 = Math.Max(0, (int)Math.Floor(center.X - outerRx - 1));
        var x1 = Math.Min(_bitmapWidth - 1, (int)Math.Ceiling(center.X + outerRx + 1));
        var y0 = Math.Max(0, (int)Math.Floor(center.Y - outerRy - 1));
        var y1 = Math.Min(_bitmapHeight - 1, (int)Math.Ceiling(center.Y + outerRy + 1));
        var hasInner = innerRx > 0 && innerRy > 0;
        var invOuterRx2 = 1f / (outerRx * outerRx);
        var invOuterRy2 = 1f / (outerRy * outerRy);
        var invInnerRx2 = hasInner ? 1f / (innerRx * innerRx) : 0f;
        var invInnerRy2 = hasInner ? 1f / (innerRy * innerRy) : 0f;

        for (var y = y0; y <= y1; y++)
        {
            var py0 = y + 0.125f - center.Y;
            var py1 = y + 0.375f - center.Y;
            var py2 = y + 0.625f - center.Y;
            var py3 = y + 0.875f - center.Y;
            var outerY0 = py0 * py0 * invOuterRy2;
            var outerY1 = py1 * py1 * invOuterRy2;
            var outerY2 = py2 * py2 * invOuterRy2;
            var outerY3 = py3 * py3 * invOuterRy2;
            if (outerY0 > 1f && outerY1 > 1f && outerY2 > 1f && outerY3 > 1f) continue;
            var rowExtent = 0f;
            if (outerY0 <= 1f) rowExtent = Math.Max(rowExtent, outerRx * MathF.Sqrt(1f - outerY0));
            if (outerY1 <= 1f) rowExtent = Math.Max(rowExtent, outerRx * MathF.Sqrt(1f - outerY1));
            if (outerY2 <= 1f) rowExtent = Math.Max(rowExtent, outerRx * MathF.Sqrt(1f - outerY2));
            if (outerY3 <= 1f) rowExtent = Math.Max(rowExtent, outerRx * MathF.Sqrt(1f - outerY3));
            var rowX0 = Math.Max(x0, (int)MathF.Floor(center.X - rowExtent - 1));
            var rowX1 = Math.Min(x1, (int)MathF.Ceiling(center.X + rowExtent + 1));
            var innerY0 = hasInner ? py0 * py0 * invInnerRy2 : 0f;
            var innerY1 = hasInner ? py1 * py1 * invInnerRy2 : 0f;
            var innerY2 = hasInner ? py2 * py2 * invInnerRy2 : 0f;
            var innerY3 = hasInner ? py3 * py3 * invInnerRy2 : 0f;

            for (var x = rowX0; x <= rowX1; x++)
            {
                var coverage = GetEllipseStrokeFeatherCoverage(
                    x + 0.5f - center.X,
                    y + 0.5f - center.Y,
                    outerRx,
                    outerRy,
                    innerRx,
                    innerRy,
                    hasInner);
                BlendPixelCoverage(x, y, color, coverage);
            }
        }
    }

    private void RasterizeTransformedEllipse(Point center, float rx, float ry, float strokeWidth, Color color)
    {
        if (rx <= 0 || ry <= 0) return;
        if (!Matrix3x2.Invert(_currentTransform, out var inverse)) return;

        var halfStroke = strokeWidth / 2f;
        var bounds = TransformRect(new Rect(
            center.X - rx - halfStroke,
            center.Y - ry - halfStroke,
            (rx + halfStroke) * 2,
            (ry + halfStroke) * 2));
        if (bounds.IsEmpty) return;

        var x0 = Math.Max(0, (int)MathF.Floor(bounds.Left - 1));
        var x1 = Math.Min(_bitmapWidth - 1, (int)MathF.Ceiling(bounds.Right + 1));
        var y0 = Math.Max(0, (int)MathF.Floor(bounds.Top - 1));
        var y1 = Math.Min(_bitmapHeight - 1, (int)MathF.Ceiling(bounds.Bottom + 1));

        var outerRx = rx + halfStroke;
        var outerRy = ry + halfStroke;
        var innerRx = rx - halfStroke;
        var innerRy = ry - halfStroke;
        var hasInner = strokeWidth > 0 && innerRx > 0 && innerRy > 0;
        var invOuterRx2 = 1f / (outerRx * outerRx);
        var invOuterRy2 = 1f / (outerRy * outerRy);
        var invInnerRx2 = hasInner ? 1f / (innerRx * innerRx) : 0f;
        var invInnerRy2 = hasInner ? 1f / (innerRy * innerRy) : 0f;
        var sampleInset = 0.5f / CoverageSampleGrid;

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                if (strokeWidth <= 0 && IsTransformedEllipsePixelFullyCovered(
                        x, y, sampleInset, inverse, center, invOuterRx2, invOuterRy2))
                {
                    BlendPixelCoverage(x, y, color, CoverageSampleCount, CoverageSampleCount);
                    continue;
                }

                var covered = 0;
                for (var sy = 0; sy < CoverageSampleGrid; sy++)
                {
                    for (var sx = 0; sx < CoverageSampleGrid; sx++)
                    {
                        var px = x + (sx + 0.5f) / CoverageSampleGrid;
                        var py = y + (sy + 0.5f) / CoverageSampleGrid;
                        var dx = px * inverse.M11 + py * inverse.M21 + inverse.M31 - center.X;
                        var dy = px * inverse.M12 + py * inverse.M22 + inverse.M32 - center.Y;
                        var insideOuter = dx * dx * invOuterRx2 + dy * dy * invOuterRy2 <= 1f;
                        var insideInner = hasInner && dx * dx * invInnerRx2 + dy * dy * invInnerRy2 < 1f;
                        if (insideOuter && !insideInner) covered++;
                    }
                }

                BlendPixelCoverage(x, y, color, covered, CoverageSampleCount);
            }
        }
    }

    private static bool IsTransformedEllipsePixelFullyCovered(
        int x, int y, float sampleInset, Matrix3x2 inverse, Point center, float invRx2, float invRy2)
    {
        return IsTransformedEllipseSampleInside(x + sampleInset, y + sampleInset, inverse, center, invRx2, invRy2) &&
               IsTransformedEllipseSampleInside(x + 1f - sampleInset, y + sampleInset, inverse, center, invRx2, invRy2) &&
               IsTransformedEllipseSampleInside(x + sampleInset, y + 1f - sampleInset, inverse, center, invRx2, invRy2) &&
               IsTransformedEllipseSampleInside(x + 1f - sampleInset, y + 1f - sampleInset, inverse, center, invRx2, invRy2);
    }

    private static bool IsTransformedEllipseSampleInside(
        float x, float y, Matrix3x2 inverse, Point center, float invRx2, float invRy2)
    {
        var dx = x * inverse.M11 + y * inverse.M21 + inverse.M31 - center.X;
        var dy = x * inverse.M12 + y * inverse.M22 + inverse.M32 - center.Y;
        return dx * dx * invRx2 + dy * dy * invRy2 <= 1f;
    }

    private void FillEllipseScanline(Point center, float rx, float ry, Color color)
    {
        var y0 = Math.Max(0, (int)Math.Floor(center.Y - ry - 1));
        var y1 = Math.Min(_bitmapHeight - 1, (int)Math.Ceiling(center.Y + ry + 1));
        var invRy2 = 1f / (ry * ry);
        if (float.IsInfinity(invRy2) || float.IsNaN(invRy2)) return;
        var sampleInset = 0.5f / CoverageSampleGrid;

        for (var y = y0; y <= y1; y++)
        {
            var maxExtent = 0f;
            var minExtent = float.MaxValue;
            for (var sy = 0; sy < CoverageSampleGrid; sy++)
            {
                var dy = y + (sy + 0.5f) / CoverageSampleGrid - center.Y;
                var normalizedY = dy * dy * invRy2;
                var extent = normalizedY <= 1f ? rx * MathF.Sqrt(1f - normalizedY) : 0f;
                maxExtent = Math.Max(maxExtent, extent);
                minExtent = Math.Min(minExtent, extent);
            }

            if (maxExtent <= 0) continue;

            var edgeStart = Math.Max(0, (int)MathF.Floor(center.X - maxExtent - 1));
            var edgeEnd = Math.Min(_bitmapWidth, (int)MathF.Ceiling(center.X + maxExtent + 1));
            var fillStart = Math.Clamp(
                (int)MathF.Ceiling(center.X - minExtent - sampleInset), edgeStart, edgeEnd);
            var fillEnd = Math.Clamp(
                (int)MathF.Floor(center.X + minExtent - (1f - sampleInset)) + 1, fillStart, edgeEnd);

            for (var x = edgeStart; x < fillStart; x++)
                BlendEllipseFeatherEdgePixel(center, rx, ry, color, x, y);

            if (fillStart < fillEnd)
                BlendRect(new Rect(fillStart, y, fillEnd - fillStart, 1), color);

            for (var x = fillEnd; x < edgeEnd; x++)
                BlendEllipseFeatherEdgePixel(center, rx, ry, color, x, y);
        }
    }

    private void BlendEllipseFeatherEdgePixel(Point center, float rx, float ry, Color color, int x, int y)
    {
        if (x < 0 || x >= _bitmapWidth || y < 0 || y >= _bitmapHeight) return;
        var coverage = GetEllipseFeatherCoverage(x + 0.5f - center.X, y + 0.5f - center.Y, rx, ry);
        BlendPixelCoverage(x, y, color, coverage);
    }

    private static byte GetEllipseStrokeFeatherCoverage(
        float dx, float dy, float outerRx, float outerRy, float innerRx, float innerRy, bool hasInner)
    {
        var outer = GetEllipseFeatherCoverage(dx, dy, outerRx, outerRy);
        if (!hasInner || outer == 0) return outer;

        var inner = 255 - GetEllipseFeatherCoverage(dx, dy, innerRx, innerRy);
        return (byte)Math.Min(outer, inner);
    }

    private static byte GetEllipseFeatherCoverage(float dx, float dy, float rx, float ry)
    {
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance <= float.Epsilon) return 255;

        var ux = dx / distance;
        var uy = dy / distance;
        var radius = 1f / MathF.Sqrt(ux * ux / (rx * rx) + uy * uy / (ry * ry));
        return (byte)Math.Clamp((int)((radius + 0.5f - distance) * 255f), 0, 255);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BlendPixelCoverage(int x, int y, Color color, int coveredSamples, int sampleCount)
    {
        if (coveredSamples <= 0) return;
        if ((uint)x >= (uint)_bitmapWidth || (uint)y >= (uint)_bitmapHeight) return;
        if (!IsPointVisible(x + 0.5f, y + 0.5f)) return;
        var alpha = sampleCount == CoverageSampleCount
            ? GetCoverageAlpha(color.A, coveredSamples)
            : ApplyOpacity((byte)Math.Clamp(
                (color.A * coveredSamples + sampleCount / 2) / sampleCount, 0, 255));
        BlendPixelCore(x, y, color.R, color.G, color.B, alpha);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BlendPixelCoverage(int x, int y, Color color, byte coverage)
    {
        if (coverage == 0) return;
        if ((uint)x >= (uint)_bitmapWidth || (uint)y >= (uint)_bitmapHeight) return;
        if (!IsPointVisible(x + 0.5f, y + 0.5f)) return;
        var alpha = coverage == 255 ? ApplyOpacity(color.A) : GetByteCoverageAlpha(color.A, coverage);
        BlendPixelCore(x, y, color.R, color.G, color.B, alpha);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte GetCoverageAlpha(byte sourceAlpha, int coveredSamples)
    {
        if (_coverageAlphaSource != sourceAlpha || _coverageAlphaOpacity != _currentOpacity)
        {
            _coverageAlphaSource = sourceAlpha;
            _coverageAlphaOpacity = _currentOpacity;
            for (var covered = 1; covered <= CoverageSampleCount; covered++)
            {
                var coverageAlpha = (byte)(sourceAlpha * covered / CoverageSampleCount);
                _coverageAlphaLookup[covered] = ApplyOpacity(coverageAlpha);
            }
        }
        return _coverageAlphaLookup[coveredSamples];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte GetByteCoverageAlpha(byte sourceAlpha, byte coverage)
    {
        if (_byteCoverageAlphaSource != sourceAlpha || _byteCoverageAlphaOpacity != _currentOpacity)
        {
            _byteCoverageAlphaSource = sourceAlpha;
            _byteCoverageAlphaOpacity = _currentOpacity;
            for (var value = 1; value < _byteCoverageAlphaLookup.Length; value++)
            {
                var coverageAlpha = (byte)(sourceAlpha * value / 255);
                _byteCoverageAlphaLookup[value] = ApplyOpacity(coverageAlpha);
            }
        }
        return _byteCoverageAlphaLookup[coverage];
    }

    private void DrawPolyline(List<(double x, double y)> points, float width, Color color)
    {
        for (int i = 0; i < points.Count - 1; i++)
            DrawLine(points[i].x, points[i].y, points[i + 1].x, points[i + 1].y, width, color);
    }

    // ── 多边形填充（扫描线） ──

    private void FillPolygon(List<(double x, double y)> points, Color color)
    {
        FillPolygonPoints(points, color);
    }

    private void FillPolygonPoints(List<(double x, double y)> points, Color color)
    {
        if (points.Count < 3) return;
        var minY = (int)Math.Floor(points.Min(p => p.y));
        var maxY = (int)Math.Ceiling(points.Max(p => p.y));
        var minX = (int)Math.Floor(points.Min(p => p.x));
        var maxX = (int)Math.Ceiling(points.Max(p => p.x));
        minX = Math.Max(0, minX - 1);
        minY = Math.Max(0, minY);
        maxX = Math.Min(_bitmapWidth - 1, maxX + 1);
        maxY = Math.Min(_bitmapHeight - 1, maxY);
        if (_hasClip)
        {
            minX = Math.Max(minX, (int)MathF.Floor(_clipLeft));
            minY = Math.Max(minY, (int)MathF.Floor(_clipTop));
            maxX = Math.Min(maxX, (int)MathF.Ceiling(_clipRight) - 1);
            maxY = Math.Min(maxY, (int)MathF.Ceiling(_clipBottom) - 1);
        }
        if (minX > maxX || minY > maxY) return;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var covered = 0;
                for (var sy = 0; sy < CoverageSampleGrid; sy++)
                for (var sx = 0; sx < CoverageSampleGrid; sx++)
                    if (IsPointInPolygon(points, x + (sx + 0.5) / CoverageSampleGrid, y + (sy + 0.5) / CoverageSampleGrid))
                        covered++;

                BlendPixelCoverage(x, y, color, covered, CoverageSampleCount);
            }
        }
    }

    private static bool IsPointInPolygon(List<(double x, double y)> points, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            var (xi, yi) = points[i];
            var (xj, yj) = points[j];
            if ((yi > y) == (yj > y)) continue;
            var intersectionX = (xj - xi) * (y - yi) / (yj - yi) + xi;
            if (x < intersectionX) inside = !inside;
        }
        return inside;
    }

    // ── 路径展开 ──

    private static List<(double x, double y)> FlattenPath(PathGeometry path, Matrix3x2 transform)
    {
        var points = new List<(double x, double y)>();
        double curX = 0, curY = 0;
        foreach (var cmd in path.Commands)
        {
            switch (cmd)
            {
                case MoveToCmd m:
                    curX = m.Point.X; curY = m.Point.Y;
                    points.Add((curX, curY));
                    break;
                case LineToCmd l:
                    curX = l.Point.X; curY = l.Point.Y;
                    points.Add((curX, curY));
                    break;
                case ArcToCmd a:
                    var steps = 16;
                    for (int i = 0; i <= steps; i++)
                    {
                        var t = (double)i / steps;
                        var angle = a.StartAngle + t * a.SweepAngle;
                        var x = a.Oval.X + a.Oval.Width / 2 + a.Oval.Width / 2 * Math.Cos(angle * Math.PI / 180);
                        var y = a.Oval.Y + a.Oval.Height / 2 + a.Oval.Height / 2 * Math.Sin(angle * Math.PI / 180);
                        points.Add((x, y));
                        curX = x; curY = y;
                    }
                    break;
                case CloseCmd:
                    if (points.Count > 0)
                        points.Add(points[0]);
                    break;
            }
        }
        if (!transform.IsIdentity)
        {
            for (var i = 0; i < points.Count; i++)
            {
                var point = Vector2.Transform(new Vector2((float)points[i].x, (float)points[i].y), transform);
                points[i] = (point.X, point.Y);
            }
        }
        return points;
    }

    // ── 位图混合 ──

    private void BlendBitmap(Bitmap src, Rect srcRect, Rect dest)
    {
        var dx0 = (int)Math.Round(dest.X);
        var dy0 = (int)Math.Round(dest.Y);
        var dw = (int)Math.Round(dest.Width);
        var dh = (int)Math.Round(dest.Height);
        var sx0 = (int)Math.Round(srcRect.X);
        var sy0 = (int)Math.Round(srcRect.Y);
        var sw = (int)Math.Round(srcRect.Width);
        var sh = (int)Math.Round(srcRect.Height);
        if (sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0) return;

        if (dw == sw && dh == sh && TryBlendBitmapUnscaled(src, sx0, sy0, sw, sh, dx0, dy0))
            return;

        var destBounds = ClipRect(new Rect(dx0, dy0, dw, dh));
        var startX = Math.Max(0, (int)MathF.Floor(destBounds.Left) - dx0);
        var startY = Math.Max(0, (int)MathF.Floor(destBounds.Top) - dy0);
        var endX = Math.Min(dw, (int)MathF.Ceiling(destBounds.Right) - dx0);
        var endY = Math.Min(dh, (int)MathF.Ceiling(destBounds.Bottom) - dy0);
        var needsPointClip = _hasClip && !_clipStack.Peek().IsRect;

        for (int dy = startY; dy < endY; dy++)
        {
            var sy = sy0 + dy * sh / dh;
            if (sy < 0 || sy >= src.Height) continue;
            var dstY = dy0 + dy;
            if (dstY < 0 || dstY >= _bitmapHeight) continue;
            var dstSpan = _surface.GetRowSpan(dstY);
            for (int dx = startX; dx < endX; dx++)
            {
                var sx = sx0 + dx * sw / dw;
                if (sx < 0 || sx >= src.Width) continue;
                var dstX = dx0 + dx;
                if (dstX < 0 || dstX >= _bitmapWidth) continue;
                if (needsPointClip && !IsPointVisible(dstX + 0.5f, dstY + 0.5f)) continue;
                var srcIdx = sy * src.Stride + sx * 4;
                var di = dstX * 4;
                var sa = ApplyOpacity(src.Pixels[srcIdx + 3]);
                if (sa == 0) continue;
                var sr = src.Pixels[srcIdx + 2];
                var sg = src.Pixels[srcIdx + 1];
                var sb = src.Pixels[srcIdx];
                if (sa == 255)
                {
                    dstSpan[di] = sb;
                    dstSpan[di + 1] = sg;
                    dstSpan[di + 2] = sr;
                    dstSpan[di + 3] = 255;
                }
                else
                {
                    var da = dstSpan[di + 3];
                    var outA = (byte)(sa + da * (255 - sa) / 255);
                    if (outA == 0) continue;
                    dstSpan[di] = (byte)((sb * 255 + dstSpan[di] * da * (255 - sa) / 255) / outA);
                    dstSpan[di + 1] = (byte)((sg * 255 + dstSpan[di + 1] * da * (255 - sa) / 255) / outA);
                    dstSpan[di + 2] = (byte)((sr * 255 + dstSpan[di + 2] * da * (255 - sa) / 255) / outA);
                    dstSpan[di + 3] = outA;
                }
            }
        }
    }

    private bool TryBlendBitmapUnscaled(Bitmap src, int sx0, int sy0, int width, int height, int dx0, int dy0)
    {
        if (_hasGeometryClip) return false;
        var copyX0 = Math.Max(0, Math.Max(-dx0, -sx0));
        var copyY0 = Math.Max(0, Math.Max(-dy0, -sy0));
        var copyX1 = Math.Min(width, Math.Min(_bitmapWidth - dx0, src.Width - sx0));
        var copyY1 = Math.Min(height, Math.Min(_bitmapHeight - dy0, src.Height - sy0));
        var clipped = ClipRect(new Rect(dx0, dy0, width, height));
        copyX0 = Math.Max(copyX0, (int)MathF.Floor(clipped.Left) - dx0);
        copyY0 = Math.Max(copyY0, (int)MathF.Floor(clipped.Top) - dy0);
        copyX1 = Math.Min(copyX1, (int)MathF.Ceiling(clipped.Right) - dx0);
        copyY1 = Math.Min(copyY1, (int)MathF.Ceiling(clipped.Bottom) - dy0);
        if (copyX0 >= copyX1 || copyY0 >= copyY1) return true;
        if (_currentOpacity < 1f) return false;

        var copyWidthBytes = (copyX1 - copyX0) * 4;
        for (var y = copyY0; y < copyY1; y++)
        {
            var srcOffset = (sy0 + y) * src.Stride + (sx0 + copyX0) * 4;
            var srcSpan = src.Pixels.AsSpan(srcOffset, copyWidthBytes);
            var dstSpan = _surface.GetRowSpan(dy0 + y).Slice((dx0 + copyX0) * 4, copyWidthBytes);

            if (IsOpaqueBgraRow(srcSpan))
                srcSpan.CopyTo(dstSpan);
            else
                BlendBitmapRow(srcSpan, dstSpan);
        }
        return true;
    }

    private static bool IsOpaqueBgraRow(ReadOnlySpan<byte> row)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(row);
        var offset = 0;
        if (Vector.IsHardwareAccelerated && pixels.Length >= Vector<uint>.Count)
        {
            var alphaMask = new Vector<uint>(AlphaMask);
            for (; offset <= pixels.Length - Vector<uint>.Count; offset += Vector<uint>.Count)
            {
                var values = new Vector<uint>(pixels.Slice(offset, Vector<uint>.Count));
                if (!Vector.EqualsAll(values & alphaMask, alphaMask)) return false;
            }
        }
        for (; offset < pixels.Length; offset++)
            if ((pixels[offset] & AlphaMask) != AlphaMask) return false;
        return true;
    }

    private Color ApplyOpacity(Color color)
    {
        if (_currentOpacity >= 1f) return color;
        return new Color(color.R, color.G, color.B, ApplyOpacity(color.A));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte ApplyOpacity(byte alpha) =>
        (byte)Math.Clamp((int)MathF.Round(alpha * _currentOpacity), 0, 255);

    private static void BlendBitmapRow(ReadOnlySpan<byte> srcSpan, Span<byte> dstSpan)
    {
        for (var i = 0; i < srcSpan.Length; i += 4)
        {
            var sa = srcSpan[i + 3];
            if (sa == 0) continue;
            var sb = srcSpan[i];
            var sg = srcSpan[i + 1];
            var sr = srcSpan[i + 2];
            if (sa == 255)
            {
                dstSpan[i] = sb;
                dstSpan[i + 1] = sg;
                dstSpan[i + 2] = sr;
                dstSpan[i + 3] = 255;
                continue;
            }

            var da = dstSpan[i + 3];
            var inverseAlpha = 255 - sa;
            var outA = (byte)(sa + da * inverseAlpha / 255);
            if (outA == 0) continue;
            dstSpan[i] = (byte)((sb * 255 + dstSpan[i] * da * inverseAlpha / 255) / outA);
            dstSpan[i + 1] = (byte)((sg * 255 + dstSpan[i + 1] * da * inverseAlpha / 255) / outA);
            dstSpan[i + 2] = (byte)((sr * 255 + dstSpan[i + 2] * da * inverseAlpha / 255) / outA);
            dstSpan[i + 3] = outA;
        }
    }

    private static void CompositeLayerRow(Span<byte> layer, Span<byte> background, float opacity)
    {
        for (var i = 0; i < layer.Length; i += 4)
        {
            var sourceAlpha = (byte)Math.Clamp((int)MathF.Round(layer[i + 3] * opacity), 0, 255);
            if (sourceAlpha == 0) continue;

            var destinationAlpha = background[i + 3];
            var inverseAlpha = 255 - sourceAlpha;
            var outputAlpha = (byte)(sourceAlpha + destinationAlpha * inverseAlpha / 255);
            if (outputAlpha == 0) continue;

            background[i] = (byte)((layer[i] * sourceAlpha + background[i] * destinationAlpha * inverseAlpha / 255) / outputAlpha);
            background[i + 1] = (byte)((layer[i + 1] * sourceAlpha + background[i + 1] * destinationAlpha * inverseAlpha / 255) / outputAlpha);
            background[i + 2] = (byte)((layer[i + 2] * sourceAlpha + background[i + 2] * destinationAlpha * inverseAlpha / 255) / outputAlpha);
            background[i + 3] = outputAlpha;
        }
    }

    private LayerPixelBounds GetLayerPixelBounds(Rect bounds)
    {
        if (_hasClip) bounds = Rect.Intersect(bounds, _clipStack.Peek().Bounds);
        if (_layerStack.Count > 0)
        {
            var parent = _layerStack.Peek();
            bounds = Rect.Intersect(bounds, new Rect(parent.X, parent.Y, parent.Width, parent.Height));
        }
        if (bounds.IsEmpty) return default;

        var left = Math.Clamp((int)MathF.Floor(bounds.Left), 0, _bitmapWidth);
        var top = Math.Clamp((int)MathF.Floor(bounds.Top), 0, _bitmapHeight);
        var right = Math.Clamp((int)MathF.Ceiling(bounds.Right), left, _bitmapWidth);
        var bottom = Math.Clamp((int)MathF.Ceiling(bounds.Bottom), top, _bitmapHeight);
        return new LayerPixelBounds(left, top, right - left, bottom - top);
    }

    private void ReleaseLayers(bool restoreBackground)
    {
        while (_layerStack.Count > 0)
        {
            var layer = _layerStack.Pop();
            if (!layer.HasBuffer) continue;
            if (restoreBackground)
            {
                var stride = layer.Width * 4;
                for (var rowIndex = 0; rowIndex < layer.Height; rowIndex++)
                {
                    layer.Background.AsSpan(rowIndex * stride, stride).CopyTo(
                        _surface.GetRowSpan(layer.Y + rowIndex).Slice(layer.X * 4, stride));
                }
            }
            _layerBufferPool.Return(layer.Background);
        }
    }

    private readonly record struct LayerPixelBounds(int X, int Y, int Width, int Height);
    private readonly record struct LayerState(
        byte[] Background,
        int X,
        int Y,
        int Width,
        int Height,
        float Opacity,
        bool HasBuffer);

    // ── 文字光栅化（简易字形） ──

    private void RenderText(TextLayout textLayout, Point origin, Color color)
    {
        if (_glyphRasterizer.IsAvailable)
        {
            RenderSystemText(textLayout, origin, color);
            return;
        }

        textLayout = ScaleTextLayout(textLayout);
        var fontSize = textLayout.Font.Size;
        var lineHeight = fontSize * textLayout.LineHeight;
        var pixelSize = Math.Max(1, (int)MathF.Ceiling(fontSize / 8f));
        var charWidth = pixelSize * 6;

        var lines = TextWrapping.Wrap(textLayout.Text, textLayout.MaxSize.Width,
            (_, _) => charWidth, textLayout.WrappingOptions);
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var indent = textLayout.GetLineIndent(lineIndex);
            var x = (int)Math.Round(origin.X + indent +
                GetTextAlignmentOffset(textLayout, line.Width + indent));
            var y = (int)Math.Round(origin.Y + lineIndex * lineHeight);
            foreach (var rune in textLayout.EnumerateVisualRunes(line))
            {
                if (rune.Rune.Value != ' ')
                    DrawGlyph(rune.Glyph.IsBmp ? (char)rune.Glyph.Value : '?', x, y, pixelSize, color);
                x += charWidth;
            }
        }
    }

    private void RenderSystemText(TextLayout textLayout, Point origin, Color color)
    {
        var lineHeight = TextMetrics.GetLineHeight(textLayout.Font, textLayout.LineHeight) * _dpiScale;
        var physicalFont = textLayout.Font.WithSize(textLayout.Font.Size * _dpiScale);
        var baselineOffset = TextMetrics.GetBaselineOffset(physicalFont, lineHeight);
        // Match measurement in logical coordinates; scale only glyph rasterization and placement.
        var lines = TextWrapping.Wrap(textLayout.Text, textLayout.MaxSize.Width, (_, rune) =>
            TextLayout.MeasureRuneAdvance(rune, textLayout.Font), textLayout.WrappingOptions);

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var indent = textLayout.GetLineIndent(lineIndex);
            var x = origin.X + (indent + GetTextAlignmentOffset(textLayout, line.Width + indent)) * _dpiScale;
            var y = origin.Y + lineIndex * lineHeight + baselineOffset;
            foreach (var visualRune in textLayout.EnumerateVisualRunes(line))
            {
                var rune = visualRune.Glyph;
                var advance = visualRune.Advance * _dpiScale;
                RasterizedGlyph? glyph = null;
                if (rune.IsBmp)
                    glyph = _glyphRasterizer.Rasterize(physicalFont, (char)rune.Value);
                if (glyph != null)
                {
                    var glyphX = (int)MathF.Round(x);
                    var glyphY = (int)MathF.Round(y);

                    for (var row = 0; row < glyph.Height; row++)
                    {
                        for (var column = 0; column < glyph.Width; column++)
                        {
                            var coverageIndex = row * glyph.Stride + column;
                            if (coverageIndex >= glyph.Coverage.Length) continue;
                            var coverage = glyph.Coverage[coverageIndex];
                            if (coverage == 0) continue;
                            var targetX = glyphX + glyph.OffsetX + column;
                            var targetY = glyphY + glyph.OffsetY + row;
                            if ((uint)targetX >= (uint)_bitmapWidth || (uint)targetY >= (uint)_bitmapHeight)
                                continue;
                            if (!IsPointVisible(targetX + 0.5f, targetY + 0.5f)) continue;
                            BlendPixelCore(
                                targetX,
                                targetY,
                                color.R,
                                color.G,
                                color.B,
                                GetByteCoverageAlpha(color.A, coverage));
                        }
                    }
                }
                x += advance;
            }
        }
    }

    private void RenderTextDecorations(TextLayout textLayout, Point origin, Color color)
    {
        var scaled = ScaleTextLayout(textLayout);
        foreach (var rect in scaled.GetDecorationRects(origin))
            BlendRect(rect, color);
    }

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

    private void DrawGlyph(char c, int x, int y, int pixelSize, Color color)
    {
        // 简易点阵字形：用字符码点生成 5x7 位图模式
        var pattern = GetGlyphPattern(c);
        var offsetX = x;
        var offsetY = y + pixelSize;

        for (int row = 0; row < 7; row++)
        {
            for (int col = 0; col < 5; col++)
            {
                if (IsFallbackGlyphPixelSet(pattern, row, col))
                {
                    for (int py = 0; py < pixelSize; py++)
                        for (int px = 0; px < pixelSize; px++)
                            BlendPixel(offsetX + col * pixelSize + px, offsetY + row * pixelSize + py, color);
                }
            }
        }
    }

    internal static bool IsFallbackGlyphPixelSet(char character, int row, int column) =>
        IsFallbackGlyphPixelSet(GetGlyphPattern(character), row, column);

    private static bool IsFallbackGlyphPixelSet(byte[] pattern, int row, int column)
    {
        if ((uint)row >= pattern.Length || column is < 0 or >= 5) return false;
        return (pattern[row] >> column & 1) != 0;
    }

    // 5x7 点阵字形表（ASCII 常用字符）
    private static readonly Dictionary<char, byte[]> GlyphPatterns = new()
    {
        ['A'] = new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
        ['B'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E },
        ['C'] = new byte[] { 0x0E, 0x11, 0x01, 0x01, 0x01, 0x11, 0x0E },
        ['D'] = new byte[] { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E },
        ['E'] = new byte[] { 0x1F, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1F },
        ['F'] = new byte[] { 0x1F, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x01 },
        ['G'] = new byte[] { 0x0E, 0x11, 0x01, 0x0D, 0x11, 0x11, 0x0E },
        ['H'] = new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
        ['I'] = new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E },
        ['J'] = new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C },
        ['K'] = new byte[] { 0x11, 0x11, 0x09, 0x07, 0x09, 0x11, 0x11 },
        ['L'] = new byte[] { 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x1F },
        ['M'] = new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 },
        ['N'] = new byte[] { 0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11 },
        ['O'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
        ['P'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x01, 0x01, 0x01 },
        ['Q'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x09, 0x16 },
        ['R'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x09, 0x11, 0x11 },
        ['S'] = new byte[] { 0x0E, 0x11, 0x01, 0x0E, 0x10, 0x11, 0x0E },
        ['T'] = new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 },
        ['U'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
        ['V'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 },
        ['W'] = new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x1B, 0x11 },
        ['X'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 },
        ['Y'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 },
        ['Z'] = new byte[] { 0x1F, 0x10, 0x08, 0x04, 0x02, 0x01, 0x1F },
    };

    private static byte[] GetGlyphPattern(char c)
    {
        var uc = char.ToUpperInvariant(c);
        return GlyphPatterns.TryGetValue(uc, out var p) ? p : new byte[] { 0x1F, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1F };
    }
}
