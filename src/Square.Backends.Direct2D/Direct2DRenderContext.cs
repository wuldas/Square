using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using DirectN;
using DirectN.Extensions;
using DirectN.Extensions.Com;
using Square.Graphics;
using Square.Text.Glyph;
using Bitmap = Square.Graphics.Bitmap;
using Color = Square.Graphics.Color;
using Image = Square.Graphics.Image;
using Path = Square.Graphics.PathGeometry;

namespace Square.Backends.Direct2D;

[SupportedOSPlatform("windows6.1")]
internal sealed unsafe class Direct2DRenderContext : IRenderContext, IDpiResizableRenderContext
{
    private readonly IComObject<ID2D1Factory> _factory;
    private readonly IntPtr _windowHandle;
    private readonly bool _vsync;
    private readonly Action? _requestRender;
    private readonly SystemGlyphRasterizer _glyphRasterizer = new(cacheGlyphs: false);
    private readonly Dictionary<Bitmap, CachedBitmap> _imageCache = [];
    private readonly Dictionary<GlyphCacheKey, CachedGlyph> _glyphCache = [];
    private readonly Stack<Matrix3x2> _transformStack = [];
    private readonly Stack<NativeState> _nativeStateStack = [];
    private IComObject<ID2D1HwndRenderTarget>? _target;
    private Matrix3x2 _currentTransform = Matrix3x2.Identity;
    private Size _canvasSize;
    private float _dpiScale;
    private bool _drawing;
    private bool _disposed;

    public Direct2DRenderContext(RenderContextCreateInfo info, Win32RenderTarget target)
    {
        _canvasSize = info.CanvasSize;
        _dpiScale = NormalizeDpiScale(info.DpiScale);
        _windowHandle = target.WindowHandle;
        _vsync = info.VSync;
        _requestRender = info.RequestRender;
        try
        {
            _factory = D2D1Functions.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, null);
            CreateTarget();
        }
        catch (Exception exception) when (exception is not Direct2DException)
        {
            _factory?.Dispose();
            throw new Direct2DException("Failed to create the Direct2D HWND render target.", exception);
        }
    }

    public Size CanvasSize => _canvasSize;
    public float DpiScale => _dpiScale;
    public bool SupportsPartialRendering => false;

    public void PushTransform(Matrix3x2 matrix)
    {
        EnsureDrawing();
        _transformStack.Push(_currentTransform);
        _currentTransform = matrix * _currentTransform;
        _target!.Object.SetTransform(ToMatrix(_currentTransform));
    }

    public void PopTransform()
    {
        EnsureDrawing();
        _currentTransform = _transformStack.Count > 0 ? _transformStack.Pop() : Matrix3x2.Identity;
        _target!.Object.SetTransform(ToMatrix(_currentTransform));
    }

    public void PushClip(Rect rect)
    {
        EnsureDrawing();
        _target!.Object.PushAxisAlignedClip(ToRect(rect), D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);
        _nativeStateStack.Push(new NativeState(NativeStateKind.RectangleClip));
    }

    public void PushClip(Geometry geometry)
    {
        EnsureDrawing();
        var mask = CreateGeometry(geometry);
        IComObject<ID2D1Layer>? layer = null;
        try
        {
            layer = CreateLayer();
            var parameters = new D2D1_LAYER_PARAMETERS
            {
                contentBounds = InfiniteRect,
                geometricMask = ComObject.ToComInstanceOfTypeNoAddRef<ID2D1Geometry>(mask.Object),
                maskAntialiasMode = D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
                maskTransform = IdentityMatrix,
                opacity = 1f,
                opacityBrush = IntPtr.Zero,
                layerOptions = D2D1_LAYER_OPTIONS.D2D1_LAYER_OPTIONS_NONE
            };
            _target!.Object.PushLayer(in parameters, layer.Object);
            _nativeStateStack.Push(new NativeState(NativeStateKind.GeometryClip, layer, mask));
        }
        catch
        {
            layer?.Dispose();
            mask.Dispose();
            throw;
        }
    }

    public void PopClip()
    {
        EnsureDrawing();
        if (_nativeStateStack.Count == 0) return;
        var state = _nativeStateStack.Peek();
        if (state.Kind == NativeStateKind.OpacityLayer)
            throw new InvalidOperationException("PopClip cannot pop an active opacity layer.");
        _nativeStateStack.Pop();
        if (state.Kind == NativeStateKind.GeometryClip)
            _target!.Object.PopLayer();
        else
            _target!.Object.PopAxisAlignedClip();
        state.Dispose();
    }

    public void FillRect(Rect rect, Brush brush)
    {
        EnsureDrawing();
        using var nativeBrush = CreateBrush(brush);
        _target!.Object.FillRectangle(ToRect(rect), nativeBrush.Object);
    }

    public void DrawRect(Rect rect, Pen pen)
    {
        EnsureDrawing();
        using var nativeBrush = CreateBrush(pen.Brush);
        using var stroke = CreateStrokeStyle(pen.StrokeStyle);
        _target!.Object.DrawRectangle(ToRect(rect), nativeBrush.Object, Math.Max(0, pen.Width), stroke?.Object!);
    }

    public void FillPath(Path path, Brush brush)
    {
        EnsureDrawing();
        using var geometry = CreatePath(path);
        using var nativeBrush = CreateBrush(brush);
        _target!.Object.FillGeometry(geometry.Object, nativeBrush.Object, null!);
    }

    public void DrawPath(Path path, Pen pen)
    {
        EnsureDrawing();
        using var geometry = CreatePath(path);
        using var nativeBrush = CreateBrush(pen.Brush);
        using var stroke = CreateStrokeStyle(pen.StrokeStyle);
        _target!.Object.DrawGeometry(
            geometry.Object, nativeBrush.Object, Math.Max(0, pen.Width), stroke?.Object!);
    }

    public void FillGeometry(Geometry geometry, Brush brush)
    {
        EnsureDrawing();
        using var nativeBrush = CreateBrush(brush);
        switch (geometry)
        {
            case RectGeometry rect:
                _target!.Object.FillRectangle(ToRect(rect.Rect), nativeBrush.Object);
                return;
            case RoundedRectGeometry rounded when rounded.IsUniform:
                _target!.Object.FillRoundedRectangle(ToRoundedRect(rounded), nativeBrush.Object);
                return;
            case EllipseGeometry ellipse:
                _target!.Object.FillEllipse(ToEllipse(ellipse), nativeBrush.Object);
                return;
            case RoundedRectGeometry rounded:
                using (var path = CreatePath(rounded.ToPath()))
                    _target!.Object.FillGeometry(path.Object, nativeBrush.Object, null!);
                return;
            case Path path:
                using (var nativePath = CreatePath(path))
                    _target!.Object.FillGeometry(nativePath.Object, nativeBrush.Object, null!);
                return;
            default:
                throw new NotSupportedException(
                    $"Direct2D rendering does not support geometry type '{geometry.GetType().Name}'.");
        }
    }

    public void DrawGeometry(Geometry geometry, Pen pen)
    {
        EnsureDrawing();
        using var nativeBrush = CreateBrush(pen.Brush);
        using var stroke = CreateStrokeStyle(pen.StrokeStyle);
        var width = Math.Max(0, pen.Width);
        switch (geometry)
        {
            case RectGeometry rect:
                _target!.Object.DrawRectangle(ToRect(rect.Rect), nativeBrush.Object, width, stroke?.Object!);
                return;
            case RoundedRectGeometry rounded when rounded.IsUniform:
                _target!.Object.DrawRoundedRectangle(
                    ToRoundedRect(rounded), nativeBrush.Object, width, stroke?.Object!);
                return;
            case EllipseGeometry ellipse:
                _target!.Object.DrawEllipse(ToEllipse(ellipse), nativeBrush.Object, width, stroke?.Object!);
                return;
            case RoundedRectGeometry rounded:
                using (var path = CreatePath(rounded.ToPath()))
                    _target!.Object.DrawGeometry(path.Object, nativeBrush.Object, width, stroke?.Object!);
                return;
            case Path path:
                using (var nativePath = CreatePath(path))
                    _target!.Object.DrawGeometry(nativePath.Object, nativeBrush.Object, width, stroke?.Object!);
                return;
            default:
                throw new NotSupportedException(
                    $"Direct2D rendering does not support geometry type '{geometry.GetType().Name}'.");
        }
    }

    public void DrawText(TextLayout text, Point origin, Brush brush)
    {
        EnsureDrawing();
        if (string.IsNullOrEmpty(text.Text)) return;
        using var nativeBrush = CreateBrush(brush);
        var lines = TextWrapping.Wrap(text.Text, text.MaxSize.Width, (offset, rune) =>
            TextLayout.MeasureRuneAdvance(rune, text.Font), text.WrappingOptions);
        var lineHeight = TextMetrics.GetLineHeight(text.Font, text.LineHeight);
        var baselineOffset = TextMetrics.GetBaselineOffset(text.Font, lineHeight);

        var antialiasMode = _target!.Object.GetAntialiasMode();
        _target.Object.SetAntialiasMode(D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);
        try
        {
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                var indent = text.GetLineIndent(lineIndex);
                var x = origin.X + indent + GetTextAlignmentOffset(text, line.Width + indent);
                var baseline = origin.Y + lineIndex * lineHeight + baselineOffset;
                foreach (var visualRune in text.EnumerateVisualRunes(line))
                {
                    var rune = visualRune.Glyph;
                    var glyph = GetOrCreateGlyph(text.Font, rune.IsBmp ? (char)rune.Value : '\ufffd');
                    if (glyph is { Width: > 0, Height: > 0 })
                    {
                        var glyphX = SnapTextCoordinate(x, _currentTransform.M31);
                        var glyphBaseline = SnapTextCoordinate(baseline, _currentTransform.M32);
                        var destination = new D2D_RECT_F(
                            glyphX + glyph.OffsetX,
                            glyphBaseline + glyph.OffsetY,
                            glyphX + glyph.OffsetX + glyph.Width,
                            glyphBaseline + glyph.OffsetY + glyph.Height);
                        var source = new D2D_RECT_F(0, 0, glyph.Width, glyph.Height);
                        _target.Object.FillOpacityMask(
                            glyph.Bitmap!.Object,
                            nativeBrush.Object,
                            D2D1_OPACITY_MASK_CONTENT.D2D1_OPACITY_MASK_CONTENT_TEXT_NATURAL,
                            (IntPtr)(&destination),
                            (IntPtr)(&source));
                    }
                    x += visualRune.Advance;
                }
            }
        }
        finally
        {
            _target.Object.SetAntialiasMode(antialiasMode);
        }

        foreach (var rect in text.GetDecorationRects(origin))
            _target!.Object.FillRectangle(ToRect(rect), nativeBrush.Object);
    }

    public void DrawImage(Image image, Rect dest, Rect? source = null)
    {
        EnsureDrawing();
        if (image is not Bitmap bitmap || bitmap.IsDisposed || dest.IsEmpty) return;
        var cached = GetOrCreateBitmap(bitmap);
        _target!.DrawBitmap(
            cached.Bitmap,
            1f,
            D2D1_BITMAP_INTERPOLATION_MODE.D2D1_BITMAP_INTERPOLATION_MODE_LINEAR,
            ToRect(dest),
            ToRect(source ?? new Rect(0, 0, bitmap.Width, bitmap.Height)));
    }

    public void PushLayer(Rect bounds, float opacity)
    {
        EnsureDrawing();
        var layer = CreateLayer();
        var parameters = new D2D1_LAYER_PARAMETERS
        {
            contentBounds = ToRect(bounds),
            geometricMask = IntPtr.Zero,
            maskAntialiasMode = D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
            maskTransform = IdentityMatrix,
            opacity = NormalizeOpacity(opacity),
            opacityBrush = IntPtr.Zero,
            layerOptions = D2D1_LAYER_OPTIONS.D2D1_LAYER_OPTIONS_NONE
        };
        try
        {
            _target!.Object.PushLayer(in parameters, layer.Object);
            _nativeStateStack.Push(new NativeState(NativeStateKind.OpacityLayer, layer));
        }
        catch
        {
            layer.Dispose();
            throw;
        }
    }

    public void PopLayer()
    {
        EnsureDrawing();
        if (_nativeStateStack.Count == 0) return;
        var state = _nativeStateStack.Peek();
        if (state.Kind != NativeStateKind.OpacityLayer)
            throw new InvalidOperationException("PopLayer cannot pop an active clip.");
        _nativeStateStack.Pop();
        _target!.Object.PopLayer();
        state.Dispose();
    }

    public void Clear(Color color)
    {
        EnsureDrawing();
        if (_nativeStateStack.Count > 0)
            throw new InvalidOperationException(
                "Full-frame Clear must be called before pushing Direct2D clips or layers.");
        _target!.Object.Clear(ToColor(color));
    }

    public void Clear(Color color, Rect rect)
    {
        EnsureDrawing();
        using var brush = CreateSolidBrush(color);
        _target!.Object.FillRectangle(ToRect(rect), brush.Object);
    }

    public void Flush()
    {
        ThrowIfDisposed();
        if (!_drawing || _target == null) return;
        if (_nativeStateStack.Any(static state =>
                state.Kind is NativeStateKind.GeometryClip or NativeStateKind.OpacityLayer))
            return;
        var result = _target.Object.Flush(IntPtr.Zero, IntPtr.Zero);
        HandleDrawResult(result, "Failed to flush Direct2D drawing commands.");
    }

    public void Present() => Present(null);

    public void Present(IReadOnlyList<Rect>? dirtyRects)
    {
        ThrowIfDisposed();
        if (dirtyRects is { Count: 0 }) return;
        if (!_drawing || _target == null) return;
        var result = _target!.Object.EndDraw(IntPtr.Zero, IntPtr.Zero);
        _drawing = false;
        HandleDrawResult(result, "Failed to present the Direct2D frame.");
    }

    public void Resize(Size canvasSize)
        => Resize(canvasSize, _dpiScale);

    public void Resize(Size canvasSize, float dpiScale)
    {
        ThrowIfDisposed();
        FinishPendingDraw();
        _canvasSize = canvasSize;
        _dpiScale = NormalizeDpiScale(dpiScale);
        ResetStacks();
        _glyphRasterizer.Clear();
        ClearGlyphCache();
        if (_target == null)
        {
            CreateTarget();
            return;
        }

        _target.Object.SetDpi(96f * _dpiScale);
        var result = _target.Object.Resize(GetPhysicalSize());
        HandleDrawResult(result, "Failed to resize the Direct2D HWND render target.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_drawing && _target != null)
        {
            _target.Object.EndDraw(IntPtr.Zero, IntPtr.Zero);
            _drawing = false;
        }
        ReleaseDeviceResources();
        _factory.Dispose();
        _glyphRasterizer.Clear();
    }

    private void EnsureDrawing()
    {
        ThrowIfDisposed();
        if (_target == null) CreateTarget();
        if (_drawing) return;
        PruneDisposedImages();
        _target!.Object.BeginDraw();
        _target.Object.SetTransform(ToMatrix(_currentTransform));
        _drawing = true;
    }

    private void CreateTarget()
    {
        var targetProperties = new D2D1_RENDER_TARGET_PROPERTIES
        {
            type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_DEFAULT,
            pixelFormat = new D2D1_PIXEL_FORMAT
            {
                format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_IGNORE
            },
            dpiX = 96f * _dpiScale,
            dpiY = 96f * _dpiScale,
            usage = D2D1_RENDER_TARGET_USAGE.D2D1_RENDER_TARGET_USAGE_NONE,
            minLevel = D2D1_FEATURE_LEVEL.D2D1_FEATURE_LEVEL_DEFAULT
        };
        var windowProperties = new D2D1_HWND_RENDER_TARGET_PROPERTIES
        {
            hwnd = new HWND(_windowHandle),
            pixelSize = GetPhysicalSize(),
            presentOptions = _vsync
                ? D2D1_PRESENT_OPTIONS.D2D1_PRESENT_OPTIONS_NONE
                : D2D1_PRESENT_OPTIONS.D2D1_PRESENT_OPTIONS_IMMEDIATELY
        };
        _target = _factory.CreateHwndRenderTarget(windowProperties, targetProperties);
        _target.Object.SetAntialiasMode(D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        _target.Object.SetTransform(ToMatrix(_currentTransform));
    }

    private NativeBrush CreateBrush(Brush brush)
    {
        switch (brush)
        {
            case SolidColorBrush solid:
                return CreateSolidBrush(solid.Color);
            case LinearGradientBrush linear:
            {
                if (linear.Stops.Length == 0)
                    return CreateSolidBrush(Color.Transparent);
                using var stops = CreateGradientStops(linear.Stops, linear.SpreadMethod);
                var properties = new D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES
                {
                    startPoint = ToPoint(linear.Start),
                    endPoint = ToPoint(linear.End)
                };
                var native = _target!.CreateLinearGradientBrush(properties, stops, null);
                return new NativeBrush(native, native.Object);
            }
            case RadialGradientBrush radial:
            {
                if (radial.Stops.Length == 0)
                    return CreateSolidBrush(Color.Transparent);
                using var stops = CreateGradientStops(radial.Stops, radial.SpreadMethod);
                var properties = new D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES
                {
                    center = ToPoint(radial.Center),
                    gradientOriginOffset = new D2D_POINT_2F(0, 0),
                    radiusX = Math.Max(0.001f, radial.Radius),
                    radiusY = Math.Max(0.001f, radial.Radius)
                };
                var native = _target!.CreateRadialGradientBrush(properties, stops, null);
                return new NativeBrush(native, native.Object);
            }
            default:
                throw new NotSupportedException(
                    $"Direct2D rendering does not support brush type '{brush.GetType().Name}'.");
        }
    }

    private NativeBrush CreateSolidBrush(Color color)
    {
        var native = _target!.CreateSolidColorBrush(ToColor(color), null);
        return new NativeBrush(native, native.Object);
    }

    private IComObject<ID2D1GradientStopCollection> CreateGradientStops(
        IReadOnlyList<GradientStop> stops,
        GradientSpreadMethod spreadMethod)
    {
        var nativeStops = stops
            .OrderBy(stop => stop.Offset)
            .Select(stop => new D2D1_GRADIENT_STOP
            {
                position = Math.Clamp(stop.Offset, 0f, 1f),
                color = ToColor(stop.Color)
            });
        return _target!.CreateGradientStopCollection(
            nativeStops,
            D2D1_GAMMA.D2D1_GAMMA_2_2,
            spreadMethod switch
            {
                GradientSpreadMethod.Reflect => D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_MIRROR,
                GradientSpreadMethod.Repeat => D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_WRAP,
                _ => D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_CLAMP
            });
    }

    private IComObject<ID2D1StrokeStyle>? CreateStrokeStyle(StrokeStyle? style)
    {
        if (style == null) return null;
        var dashes = style.DashArray is { Length: > 0 } values &&
                     values.All(value => value > 0 && float.IsFinite(value))
            ? values
            : [];
        var properties = new D2D1_STROKE_STYLE_PROPERTIES
        {
            startCap = ToCap(style.Cap),
            endCap = ToCap(style.Cap),
            dashCap = ToCap(style.Cap),
            lineJoin = style.Join switch
            {
                LineJoin.Round => D2D1_LINE_JOIN.D2D1_LINE_JOIN_ROUND,
                LineJoin.Bevel => D2D1_LINE_JOIN.D2D1_LINE_JOIN_BEVEL,
                _ => D2D1_LINE_JOIN.D2D1_LINE_JOIN_MITER
            },
            miterLimit = Math.Max(1, style.MiterLimit),
            dashStyle = dashes.Length > 0
                ? D2D1_DASH_STYLE.D2D1_DASH_STYLE_CUSTOM
                : D2D1_DASH_STYLE.D2D1_DASH_STYLE_SOLID,
            dashOffset = style.DashOffset
        };
        return _factory.CreateStrokeStyle(properties, dashes);
    }

    private IComObject<ID2D1Layer> CreateLayer()
    {
        var result = _target!.Object.CreateLayer(IntPtr.Zero, out var layer);
        result.ThrowOnError();
        return new ComObject<ID2D1Layer>(layer);
    }

    private NativeGeometry CreateGeometry(Geometry geometry)
    {
        return geometry switch
        {
            RectGeometry rect => CreateRectangleGeometry(rect.Rect),
            RoundedRectGeometry rounded when rounded.IsUniform => CreateRoundedRectangleGeometry(rounded),
            RoundedRectGeometry rounded => CreatePath(rounded.ToPath()),
            EllipseGeometry ellipse => CreateEllipseGeometry(ellipse),
            Path path => CreatePath(path),
            _ => throw new NotSupportedException(
                $"Direct2D rendering does not support geometry type '{geometry.GetType().Name}'.")
        };
    }

    private NativeGeometry CreateRectangleGeometry(Rect rect)
    {
        var native = _factory.CreateRectangleGeometry(ToRect(rect));
        return new NativeGeometry(native, native.Object);
    }

    private NativeGeometry CreateRoundedRectangleGeometry(RoundedRectGeometry rounded)
    {
        var native = _factory.CreateRoundedRectangleGeometry(ToRoundedRect(rounded));
        return new NativeGeometry(native, native.Object);
    }

    private NativeGeometry CreateEllipseGeometry(EllipseGeometry ellipse)
    {
        var native = _factory.CreateEllipseGeometry(ToEllipse(ellipse));
        return new NativeGeometry(native, native.Object);
    }

    private NativeGeometry CreatePath(Path path)
    {
        var geometry = _factory.CreatePathGeometry();
        try
        {
            using var sink = geometry.Open<ID2D1GeometrySink>();
            sink.Object.SetFillMode(D2D1_FILL_MODE.D2D1_FILL_MODE_WINDING);
            var figureOpen = false;
            var current = new Point();
            foreach (var command in path.Commands)
            {
                switch (command)
                {
                    case MoveToCmd move:
                        if (figureOpen)
                            sink.Object.EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_OPEN);
                        sink.Object.BeginFigure(ToPoint(move.Point), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
                        current = move.Point;
                        figureOpen = true;
                        break;
                    case LineToCmd line:
                        if (!figureOpen)
                        {
                            sink.Object.BeginFigure(ToPoint(line.Point), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
                            figureOpen = true;
                        }
                        else
                        {
                            sink.Object.AddLine(ToPoint(line.Point));
                        }
                        current = line.Point;
                        break;
                    case ArcToCmd arc:
                        AddArc(sink.Object, arc, ref figureOpen, ref current);
                        break;
                    case CloseCmd when figureOpen:
                        sink.Object.EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED);
                        figureOpen = false;
                        break;
                }
            }
            if (figureOpen)
                sink.Object.EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_OPEN);
            sink.Object.Close().ThrowOnError();
            return new NativeGeometry(geometry, geometry.Object);
        }
        catch
        {
            geometry.Dispose();
            throw;
        }
    }

    private static void AddArc(ID2D1GeometrySink sink, ArcToCmd arc, ref bool figureOpen, ref Point current)
    {
        var radiusX = Math.Max(0, arc.Oval.Width / 2f);
        var radiusY = Math.Max(0, arc.Oval.Height / 2f);
        var center = new Point(arc.Oval.X + radiusX, arc.Oval.Y + radiusY);
        var start = PointOnEllipse(center, radiusX, radiusY, arc.StartAngle);
        if (!figureOpen)
        {
            sink.BeginFigure(ToPoint(start), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
            figureOpen = true;
        }
        else if (!AreClose(current, start))
        {
            sink.AddLine(ToPoint(start));
        }

        if (radiusX <= 0 || radiusY <= 0 || !float.IsFinite(arc.SweepAngle) || arc.SweepAngle == 0)
        {
            current = start;
            return;
        }

        var remaining = arc.SweepAngle;
        var angle = arc.StartAngle;
        while (MathF.Abs(remaining) > 0.001f)
        {
            var sweep = Math.Clamp(remaining, -180f, 180f);
            angle += sweep;
            var end = PointOnEllipse(center, radiusX, radiusY, angle);
            sink.AddArc(
                ToPoint(end),
                new D2D_SIZE_F(radiusX, radiusY),
                0,
                sweep > 0
                    ? D2D1_SWEEP_DIRECTION.D2D1_SWEEP_DIRECTION_CLOCKWISE
                    : D2D1_SWEEP_DIRECTION.D2D1_SWEEP_DIRECTION_COUNTER_CLOCKWISE,
                D2D1_ARC_SIZE.D2D1_ARC_SIZE_SMALL);
            current = end;
            remaining -= sweep;
        }
    }

    private CachedBitmap GetOrCreateBitmap(Bitmap bitmap)
    {
        if (_imageCache.TryGetValue(bitmap, out var cached) && cached.Version == bitmap.ContentVersion)
            return cached;
        cached?.Dispose();
        var premultiplied = Premultiply(bitmap.Pixels);
        fixed (byte* pixels = premultiplied)
        {
            var properties = new D2D1_BITMAP_PROPERTIES
            {
                pixelFormat = new D2D1_PIXEL_FORMAT
                {
                    format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                    alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED
                },
                dpiX = 96,
                dpiY = 96
            };
            var native = _target!.CreateBitmap(
                new D2D_SIZE_U(bitmap.Width, bitmap.Height),
                (IntPtr)pixels,
                (uint)bitmap.Stride,
                properties);
            cached = new CachedBitmap(bitmap.ContentVersion, native);
            _imageCache[bitmap] = cached;
            return cached;
        }
    }

    private CachedGlyph? GetOrCreateGlyph(Font font, char character)
    {
        var physicalSize = font.Size * _dpiScale;
        var key = new GlyphCacheKey(font.Family, physicalSize, font.Weight, font.Style, character);
        if (_glyphCache.TryGetValue(key, out var cached)) return cached;
        var rasterized = _glyphRasterizer.Rasterize(font.WithSize(physicalSize), character);
        if (rasterized == null) return null;
        if (rasterized.Width == 0 || rasterized.Height == 0)
        {
            cached = new CachedGlyph(null, 0, 0,
                rasterized.OffsetX / _dpiScale, rasterized.OffsetY / _dpiScale);
            _glyphCache[key] = cached;
            return cached;
        }

        fixed (byte* coverage = rasterized.Coverage)
        {
            var properties = new D2D1_BITMAP_PROPERTIES
            {
                pixelFormat = new D2D1_PIXEL_FORMAT
                {
                    format = DXGI_FORMAT.DXGI_FORMAT_A8_UNORM,
                    alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED
                },
                dpiX = 96f * _dpiScale,
                dpiY = 96f * _dpiScale
            };
            var bitmap = _target!.CreateBitmap(
                new D2D_SIZE_U(rasterized.Width, rasterized.Height),
                (IntPtr)coverage,
                (uint)rasterized.Stride,
                properties);
            cached = new CachedGlyph(
                bitmap,
                rasterized.Width / _dpiScale,
                rasterized.Height / _dpiScale,
                rasterized.OffsetX / _dpiScale,
                rasterized.OffsetY / _dpiScale);
            _glyphCache[key] = cached;
            return cached;
        }
    }

    private void FinishPendingDraw()
    {
        if (!_drawing || _target == null) return;
        var result = _target.Object.EndDraw(IntPtr.Zero, IntPtr.Zero);
        _drawing = false;
        HandleDrawResult(result, "Failed to finish Direct2D drawing before resize.");
    }

    private void HandleDrawResult(HRESULT result, string message)
    {
        if (result == Constants.D2DERR_RECREATE_TARGET)
        {
            ReleaseDeviceResources();
            if (_requestRender == null)
                throw new Direct2DException(
                    "Direct2D target recreation requires RenderContextCreateInfo.RequestRender to replay the frame.");
            _requestRender();
            return;
        }
        try
        {
            result.ThrowOnError();
        }
        catch (Exception exception)
        {
            throw new Direct2DException(message, exception);
        }
    }

    private void ReleaseDeviceResources()
    {
        ResetStacks();
        foreach (var bitmap in _imageCache.Values)
            bitmap.Dispose();
        _imageCache.Clear();
        ClearGlyphCache();
        _target?.Dispose();
        _target = null;
        _drawing = false;
    }

    private void ClearGlyphCache()
    {
        foreach (var glyph in _glyphCache.Values)
            glyph.Dispose();
        _glyphCache.Clear();
    }

    private void ResetStacks()
    {
        _transformStack.Clear();
        foreach (var state in _nativeStateStack)
            state.Dispose();
        _nativeStateStack.Clear();
        _currentTransform = Matrix3x2.Identity;
    }

    private void PruneDisposedImages()
    {
        List<Bitmap>? disposed = null;
        foreach (var pair in _imageCache)
        {
            if (!pair.Key.IsDisposed) continue;
            disposed ??= [];
            disposed.Add(pair.Key);
            pair.Value.Dispose();
        }
        if (disposed == null) return;
        foreach (var bitmap in disposed)
            _imageCache.Remove(bitmap);
    }

    private D2D_SIZE_U GetPhysicalSize()
        => new(
            Math.Max(1, (int)MathF.Ceiling(_canvasSize.Width * _dpiScale)),
            Math.Max(1, (int)MathF.Ceiling(_canvasSize.Height * _dpiScale)));

    private static byte[] Premultiply(byte[] pixels)
    {
        var result = new byte[pixels.Length];
        for (var index = 0; index + 3 < pixels.Length; index += 4)
        {
            var alpha = pixels[index + 3];
            result[index] = (byte)(pixels[index] * alpha / 255);
            result[index + 1] = (byte)(pixels[index + 1] * alpha / 255);
            result[index + 2] = (byte)(pixels[index + 2] * alpha / 255);
            result[index + 3] = alpha;
        }
        return result;
    }

    private static float GetTextAlignmentOffset(TextLayout text, float lineWidth)
        => !float.IsFinite(text.MaxSize.Width) || text.MaxSize.Width <= lineWidth
            ? 0
            : text.Alignment switch
            {
                TextAlignment.Center => (text.MaxSize.Width - lineWidth) / 2f,
                TextAlignment.Right => text.MaxSize.Width - lineWidth,
                _ => 0f
            };

    private float SnapTextCoordinate(float value, float translation)
    {
        if (_currentTransform.M11 != 1f || _currentTransform.M22 != 1f ||
            _currentTransform.M12 != 0f || _currentTransform.M21 != 0f)
            return value;
        return MathF.Round((value + translation) * _dpiScale) / _dpiScale - translation;
    }

    private static D2D1_CAP_STYLE ToCap(LineCap cap)
        => cap switch
        {
            LineCap.Round => D2D1_CAP_STYLE.D2D1_CAP_STYLE_ROUND,
            LineCap.Square => D2D1_CAP_STYLE.D2D1_CAP_STYLE_SQUARE,
            _ => D2D1_CAP_STYLE.D2D1_CAP_STYLE_FLAT
        };

    private static D2D_RECT_F ToRect(Rect rect)
        => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static D2D_POINT_2F ToPoint(Point point)
        => new(point.X, point.Y);

    private static D2D1_ELLIPSE ToEllipse(EllipseGeometry ellipse)
        => new(ToPoint(ellipse.Center), Math.Max(0, ellipse.RadiusX), Math.Max(0, ellipse.RadiusY));

    private static D2D1_ROUNDED_RECT ToRoundedRect(RoundedRectGeometry rounded)
        => new()
        {
            rect = ToRect(rounded.Rect),
            radiusX = Math.Max(0, rounded.RadiusX),
            radiusY = Math.Max(0, rounded.RadiusY)
        };

    private static D3DCOLORVALUE ToColor(Color color)
        => new()
        {
            r = color.R / 255f,
            g = color.G / 255f,
            b = color.B / 255f,
            a = color.A / 255f
        };

    private static D2D_MATRIX_3X2_F ToMatrix(Matrix3x2 matrix)
        => new(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M31, matrix.M32);

    private static Point PointOnEllipse(Point center, float radiusX, float radiusY, float angle)
    {
        var radians = angle * MathF.PI / 180f;
        return new Point(center.X + MathF.Cos(radians) * radiusX, center.Y + MathF.Sin(radians) * radiusY);
    }

    private static bool AreClose(Point left, Point right)
        => MathF.Abs(left.X - right.X) < 0.001f && MathF.Abs(left.Y - right.Y) < 0.001f;

    private static float NormalizeDpiScale(float dpiScale)
        => float.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1f;

    private static float NormalizeOpacity(float opacity)
        => float.IsNaN(opacity) ? 1f : Math.Clamp(opacity, 0f, 1f);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private static readonly D2D_MATRIX_3X2_F IdentityMatrix = ToMatrix(Matrix3x2.Identity);
    private static readonly D2D_RECT_F InfiniteRect =
        new(-float.MaxValue / 4, -float.MaxValue / 4, float.MaxValue / 4, float.MaxValue / 4);

    private enum NativeStateKind
    {
        RectangleClip,
        GeometryClip,
        OpacityLayer
    }

    private sealed class NativeState(
        NativeStateKind kind,
        IComObject<ID2D1Layer>? layer = null,
        NativeGeometry? geometry = null) : IDisposable
    {
        public NativeStateKind Kind { get; } = kind;

        public void Dispose()
        {
            geometry?.Dispose();
            layer?.Dispose();
        }
    }

    private readonly record struct GlyphCacheKey(
        string Family, float Size, FontWeight Weight, FontStyle Style, char Character);

    private sealed class CachedBitmap(long version, IComObject<ID2D1Bitmap> bitmap) : IDisposable
    {
        public long Version { get; } = version;
        public IComObject<ID2D1Bitmap> Bitmap { get; } = bitmap;
        public void Dispose() => Bitmap.Dispose();
    }

    private sealed class CachedGlyph(
        IComObject<ID2D1Bitmap>? bitmap,
        float width,
        float height,
        float offsetX,
        float offsetY) : IDisposable
    {
        public IComObject<ID2D1Bitmap>? Bitmap { get; } = bitmap;
        public float Width { get; } = width;
        public float Height { get; } = height;
        public float OffsetX { get; } = offsetX;
        public float OffsetY { get; } = offsetY;
        public void Dispose() => Bitmap?.Dispose();
    }

    private sealed class NativeBrush(IComObject owner, ID2D1Brush value) : IDisposable
    {
        public ID2D1Brush Object { get; } = value;
        public void Dispose() => owner.Dispose();
    }

    private sealed class NativeGeometry(IComObject owner, ID2D1Geometry value) : IDisposable
    {
        public ID2D1Geometry Object { get; } = value;
        public void Dispose() => owner.Dispose();
    }
}
