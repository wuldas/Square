using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Godot;
using Square.Graphics;
using Square.Rendering.Tessellation;
using Square.Text.Glyph;
using GColor = Godot.Color;
using GImage = Godot.Image;
using GRect2 = Godot.Rect2;
using GRid = Godot.Rid;
using GTransform2D = Godot.Transform2D;
using GVector2 = Godot.Vector2;
using SBitmap = Square.Graphics.Bitmap;
using SColor = Square.Graphics.Color;
using SImage = Square.Graphics.Image;

using SFont = Square.Graphics.Font;
namespace Square.Backends.Godot;

/// <summary>通过 Godot RenderingServer 的透明 GPU Canvas 实现 Square 绘制。</summary>
public sealed class GodotRenderContext : IRenderContext, IDpiResizableRenderContext, IRenderBitmapSource
{
    private const string PaintShaderResource = "Square.Backends.Godot.Shaders.paint.gdshader";
    private const string CompositeShaderResource = "Square.Backends.Godot.Shaders.composite.gdshader";

    private readonly Control _target;
    private readonly GRid _parentViewport;
    private readonly GRid _rootViewport;
    private readonly GRid _rootCanvas;
    private readonly GRid _rootItem;
    private readonly GRid _compositeItem;
    private readonly GRid _paintShader;
    private readonly GRid _compositeShader;
    private readonly GRid _maskShader;
    private readonly GRid _compositeMaterial;
    private readonly List<GRid> _imageTextures = [];
    private readonly Dictionary<SBitmap, ImageCache> _imageCache = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, GradientCache> _gradientCache = new(StringComparer.Ordinal);
    private readonly Dictionary<GlyphKey, GlyphRecord> _glyphs = [];
    private readonly SystemGlyphRasterizer _glyphRasterizer = new(cacheGlyphs: false);
    private readonly byte[] _glyphAtlasPixels = new byte[GlyphAtlasWidth * GlyphAtlasHeight * 4];
    private readonly List<GRid> _glyphAtlasTextures = [];
    private readonly Stack<Matrix3x2> _transformStack = new();
    private readonly Stack<ClipFrame> _clipStack = new();
    private readonly Dictionary<(ClipMask Mask, GRid ParentViewport), MaskMaterialization> _maskCache = [];
    private readonly Stack<LayerCapture> _layerStack = new();
    private readonly CommandList _rootCommands = new();
    private readonly List<GRid> _committedItems = [];
    private readonly List<GRid> _committedCanvases = [];
    private readonly List<GRid> _committedViewports = [];
    private readonly List<GRid> _committedMaterials = [];
    private readonly List<GRid> _committedGradientTextures = [];

    private Matrix3x2 _currentTransform;
    private Rect _currentClip;
    private Size _canvasSize;
    private float _dpiScale;
    private int _physicalWidth;
    private int _physicalHeight;
    private bool _glyphAtlasDirty;
    private bool _frameDirty;
    private bool _disposed;
    private bool _hasPresentedFrame;
    private SColor _clearColor = SColor.Transparent;

    private const int GlyphAtlasWidth = 1024;
    private const int GlyphAtlasHeight = 1024;
    private int _glyphCursorX;
    private int _glyphCursorY;
    private int _glyphRowHeight;
    private int _glyphGeneration = -1;
    private float _glyphDpi = -1;
    private GRid _glyphAtlasTexture;

    private static readonly StringName BrushKindName = new("brush_kind");
    private static readonly StringName SolidColorName = new("solid_color");
    private static readonly StringName GradientStartName = new("gradient_start");
    private static readonly StringName GradientEndName = new("gradient_end");
    private static readonly StringName GradientCenterName = new("gradient_center");
    private static readonly StringName GradientRadiusName = new("gradient_radius");
    private static readonly StringName GradientSpreadName = new("gradient_spread");
    private static readonly StringName GradientStopsName = new("gradient_stops");
    private static readonly StringName GradientStopCountName = new("gradient_stop_count");
    private static readonly StringName ClipEnabledName = new("clip_enabled");
    private static readonly StringName ClipRectName = new("clip_rect");
    private static readonly StringName LayerOpacityName = new("layer_opacity");
    private static readonly StringName MaskEnabledName = new("mask_enabled");
    private static readonly StringName MaskTextureName = new("mask_texture");
    private static readonly StringName MaskRectName = new("mask_rect");
    private static readonly StringName MaskOriginName = new("mask_origin");
    private static readonly StringName CoverageEnabledName = new("coverage_enabled");
    private static readonly StringName CoverageTextureName = new("coverage_texture");
    private static readonly StringName CoverageRectName = new("coverage_rect");
    private ClipMask? _currentMask;
    /// <summary>创建绑定到已入树 Control 的 Godot GPU 上下文。</summary>
    public GodotRenderContext(Control target, RenderContextCreateInfo info)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        ArgumentNullException.ThrowIfNull(info);
        if (!target.IsInsideTree())
            throw new InvalidOperationException("GodotRenderContext requires the target Control to be inside the scene tree.");

        _canvasSize = info.CanvasSize;
        _dpiScale = NormalizeDpi(info.DpiScale);
        UpdatePhysicalSize();
        _currentTransform = Matrix3x2.CreateScale(_dpiScale);
        _currentClip = PhysicalBounds;

        _parentViewport = target.GetViewport().GetViewportRid();
        _paintShader = CreateShader(PaintShaderResource);
        _compositeShader = CreateShader(CompositeShaderResource);
        _maskShader = CreateShader("Square.Backends.Godot.Shaders.mask.gdshader");
        _rootViewport = RenderingServer.ViewportCreate();
        _rootCanvas = RenderingServer.CanvasCreate();
        _rootItem = RenderingServer.CanvasItemCreate();
        _compositeItem = RenderingServer.CanvasItemCreate();

        ConfigureViewport(_rootViewport, _physicalWidth, _physicalHeight, _parentViewport);
        RenderingServer.ViewportAttachCanvas(_rootViewport, _rootCanvas);
        RenderingServer.CanvasItemSetParent(_rootItem, _rootCanvas);
        RenderingServer.CanvasItemSetParent(_compositeItem, target.GetCanvasItem());
        _compositeMaterial = RenderingServer.MaterialCreate();
        RenderingServer.MaterialSetShader(_compositeMaterial, _compositeShader);
        RenderingServer.CanvasItemSetMaterial(_compositeItem, _compositeMaterial);
        RenderingServer.CanvasItemSetCustomRect(
            _compositeItem,
            true,
            new GRect2(0, 0, MathF.Max(0, _canvasSize.Width), MathF.Max(0, _canvasSize.Height)));
    }

    /// <inheritdoc />
    public Size CanvasSize => _canvasSize;

    /// <inheritdoc />
    public float DpiScale => _dpiScale;

    /// <inheritdoc />
    public bool SupportsPartialRendering => false;

    /// <inheritdoc />
    public void PushTransform(Matrix3x2 matrix)
    {
        EnsureNotDisposed();
        _transformStack.Push(_currentTransform);
        _currentTransform = matrix * _currentTransform;
    }

    /// <inheritdoc />
    public void PopTransform()
    {
        EnsureNotDisposed();
        if (_transformStack.Count > 0)
            _currentTransform = _transformStack.Pop();
    }

    /// <inheritdoc />
    public void PushClip(Rect rect)
    {
        EnsureNotDisposed();
        _clipStack.Push(new ClipFrame(_currentClip, _currentMask));
        _currentClip = Intersect(_currentClip, TransformRect(rect));
    }

    /// <inheritdoc />
    public void PushClip(Geometry geometry)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(geometry);
        _clipStack.Push(new ClipFrame(_currentClip, _currentMask));
        var bounds = Intersect(_currentClip, TransformRect(GetGeometryBounds(geometry)));
        _currentClip = bounds;
        if (!bounds.IsEmpty)
            _currentMask = new ClipMask(CreateClipTriangles(geometry), bounds, _currentMask);
    }

    /// <inheritdoc />
    public void PopClip()
    {
        EnsureNotDisposed();
        if (_clipStack.Count == 0) return;
        var frame = _clipStack.Pop();
        _currentClip = frame.Bounds;
        _currentMask = frame.Mask;
    }

    /// <inheritdoc />
    public void FillRect(Rect rect, Brush brush)
    {
        EnsureNotDisposed();
        if (rect.IsEmpty) return;
        AddTriangles(CreateQuad(rect), CreatePaint(brush));
    }

    /// <inheritdoc />
    public void DrawRect(Rect rect, Pen pen)
    {
        EnsureNotDisposed();
        if (rect.IsEmpty || pen.Width <= 0) return;
        DrawPath(PathGeometry.Create()
            .MoveTo(new Point(rect.X, rect.Y))
            .LineTo(new Point(rect.Right, rect.Y))
            .LineTo(new Point(rect.Right, rect.Bottom))
            .LineTo(new Point(rect.X, rect.Bottom))
            .Close(), pen);
    }

    /// <inheritdoc />
    public void FillPath(PathGeometry path, Brush brush)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(path);
        var contours = PathTessellator.FlattenPath(path, _currentTransform);
        var tessellation = PathTessellator.Triangulate(contours);
        if (tessellation.ElementCount == 0) return;

        var pointCount = tessellation.ElementCount * 3;
        var points = new GVector2[pointCount];
        var indices = new int[pointCount];
        for (var i = 0; i < pointCount; i++)
        {
            var vertex = tessellation.Vertices[tessellation.Elements[i]].Position;
            points[i] = new GVector2(vertex.X, vertex.Y);
            indices[i] = i;
        }
        AddTriangles(new TriangleData(points, indices, CreateWhiteColors(pointCount), null), CreatePaint(brush));
    }

    /// <inheritdoc />
    public void DrawPath(PathGeometry path, Pen pen)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(path);
        if (pen.Width <= 0) return;
        var contours = PathTessellator.FlattenPath(path, _currentTransform);
        if (contours.Count == 0) return;

        var vertices = new List<Vertex2D>();
        var indices = new List<uint>();
        foreach (var contour in contours)
        {
            if (contour.Count < 2) continue;
            StrokeTessellator.Append(
                contour,
                pen.Width / 2f,
                0,
                pen.StrokeStyle,
                0xFFFFFFFF,
                0,
                0,
                1,
                1,
                TransformPoint,
                vertices,
                indices);
        }

        if (vertices.Count == 0) return;
        var points = new GVector2[vertices.Count];
        var colors = new GColor[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            points[i] = new GVector2(vertices[i].X, vertices[i].Y);
            colors[i] = PackedColor(vertices[i].Color);
        }
        var triangleIndices = new int[indices.Count];
        for (var i = 0; i < indices.Count; i++) triangleIndices[i] = checked((int)indices[i]);
        if (_currentClip.IsEmpty) return;
        CurrentCommands.Commands.Add(RenderCommand.ForCoverage(
            new TriangleData(points, triangleIndices, colors, null),
            CreatePaint(pen.Brush),
            _currentClip,
            _currentMask));
        _frameDirty = true;
    }

    /// <inheritdoc />
    public void FillGeometry(Geometry geometry, Brush brush)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(geometry);
        switch (geometry)
        {
            case RectGeometry rect:
                FillRect(rect.Rect, brush);
                return;
            case RoundedRectGeometry rounded:
                FillPath(rounded.ToPath(), brush);
                return;
            case EllipseGeometry ellipse:
                FillPath(CreateEllipsePath(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY), brush);
                return;
            case PathGeometry path:
                FillPath(path, brush);
                return;
            default:
                throw new NotSupportedException($"Godot rendering does not support geometry {geometry.GetType().FullName}.");
        }
    }

    /// <inheritdoc />
    public void DrawGeometry(Geometry geometry, Pen pen)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(geometry);
        switch (geometry)
        {
            case RectGeometry rect:
                DrawRect(rect.Rect, pen);
                return;
            case RoundedRectGeometry rounded:
                DrawPath(rounded.ToPath(), pen);
                return;
            case EllipseGeometry ellipse:
                DrawPath(CreateEllipsePath(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, close: true), pen);
                return;
            case PathGeometry path:
                DrawPath(path, pen);
                return;
            default:
                throw new NotSupportedException($"Godot rendering does not support geometry {geometry.GetType().FullName}.");
        }
    }

    /// <inheritdoc />
    public void DrawText(TextLayout text, Point origin, Brush brush)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(text);
        var paint = CreatePaint(brush);
        var lineHeight = TextMetrics.GetLineHeight(text.Font, text.LineHeight);
        var baseline = TextMetrics.GetBaselineOffset(text.Font, lineHeight);
        var lines = TextWrapping.Wrap(
            text.Text,
            text.MaxSize.Width,
            (_, rune) => TextLayout.MeasureRuneAdvance(rune, text.Font),
            text.WrappingOptions);

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var x = text.GetLineOriginX(origin.X, lineIndex, line.Width);
            var y = origin.Y + lineIndex * lineHeight + baseline;
            foreach (var visualRune in text.EnumerateVisualRunes(line))
            {
                var character = visualRune.Rune.Value <= char.MaxValue
                    ? (char)visualRune.Rune.Value
                    : '\uFFFD';
                var glyph = GetGlyph(text.Font, character);
                if (glyph.Width > 0 && glyph.Height > 0)
                {
                    var glyphPaint = paint with { Kind = PaintKind.Glyph, Texture = _glyphAtlasTexture };
                    var rect = new Rect(
                        x + glyph.OffsetX,
                        y + glyph.OffsetY,
                        glyph.Width,
                        glyph.Height);
                    AddGlyphQuad(rect, glyph, glyphPaint);
                }
                x += visualRune.Advance;
            }
        }

        foreach (var decoration in text.GetDecorationRects(origin))
            AddTriangles(CreateQuad(decoration), paint);
    }

    /// <inheritdoc />
    public void DrawImage(SImage image, Rect dest, Rect? source = null)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(image);
        if (dest.IsEmpty || image.IsDisposed) return;
        switch (image)
        {
            case SBitmap bitmap:
                AddBitmapQuad(bitmap, dest, source);
                return;
            case VectorImage vector:
                vector.Draw(this, dest);
                return;
            default:
                throw new NotSupportedException($"Godot rendering does not support image {image.GetType().FullName}.");
        }
    }

    /// <inheritdoc />
    public void PushLayer(Rect bounds, float opacity)
    {
        EnsureNotDisposed();
        var physicalBounds = Intersect(TransformRect(bounds), _currentClip);
        _layerStack.Push(new LayerCapture(
            new CommandList { Bounds = physicalBounds, Opacity = NormalizeOpacity(opacity) },
            physicalBounds));
    }

    /// <inheritdoc />
    public void PopLayer()
    {
        EnsureNotDisposed();
        if (_layerStack.Count == 0) return;
        var layer = _layerStack.Pop();
        if (layer.List.Commands.Count == 0 || layer.Bounds.IsEmpty || layer.List.Opacity <= 0)
            return;
        CurrentCommands.Commands.Add(RenderCommand.ForLayer(layer.List, layer.Bounds));
        _frameDirty = true;
    }

    /// <inheritdoc />
    public void Clear(SColor color)
    {
        EnsureNotDisposed();
        CurrentCommands.Commands.Clear();
        CurrentCommands.ClearColor = color;
        CurrentCommands.HasClearColor = true;
        _frameDirty = true;
    }

    /// <inheritdoc />
    public void Clear(SColor color, Rect rect)
    {
        EnsureNotDisposed();
        if (rect.IsEmpty) return;
        AddTriangles(CreateQuad(rect), CreatePaint(new SolidColorBrush(color)));
    }

    /// <inheritdoc />
    public void Flush()
    {
        EnsureNotDisposed();
    }

    /// <inheritdoc />
    public void Present() => Present(null);

    /// <inheritdoc />
    public void Present(IReadOnlyList<Rect>? dirtyRects)
    {
        EnsureNotDisposed();
        if (dirtyRects is { Count: 0 } || !_frameDirty)
            return;
        if (_physicalWidth <= 0 || _physicalHeight <= 0)
            return;
        _maskCache.Clear();

        var nextItems = new List<GRid>();
        var nextCanvases = new List<GRid>();
        var nextViewports = new List<GRid>();
        var nextMaterials = new List<GRid>();
        var nextGradientTextures = new List<GRid>();
        try
        {
            RenderingServer.CanvasItemClear(_rootItem);
            if (_rootCommands.HasClearColor)
            {
                AddClearCommand(
                    _rootCommands,
                    new Rect(0, 0, _physicalWidth, _physicalHeight),
                    _rootCommands.ClearColor);
            }
            MaterializeList(
                _rootCommands,
                _rootViewport,
                _rootItem,
                Point.Zero,
                nextItems,
                nextCanvases,
                nextViewports,
                nextMaterials,
                nextGradientTextures);

            var rootTexture = RenderingServer.ViewportGetTexture(_rootViewport);
            RenderingServer.CanvasItemClear(_compositeItem);
            RenderingServer.CanvasItemAddTextureRectRegion(
                _compositeItem,
                new GRect2(0, 0, MathF.Max(0, _canvasSize.Width), MathF.Max(0, _canvasSize.Height)),
                rootTexture,
                new GRect2(0, 0, _physicalWidth, _physicalHeight),
                new GColor(1, 1, 1, 1));

            CommitFrame(nextItems, nextCanvases, nextViewports, nextMaterials, nextGradientTextures);
            _hasPresentedFrame = true;
            _frameDirty = false;
            _rootCommands.Commands.Clear();
            _rootCommands.HasClearColor = false;
            ActivateGraph(_rootViewport, _parentViewport, nextViewports);
        }
        catch
        {
            FreeResources(nextItems, nextCanvases, nextViewports, nextMaterials);
            throw;
        }
    }

    /// <inheritdoc />
    public void Resize(Size canvasSize)
        => Resize(canvasSize, _dpiScale);

    /// <inheritdoc />
    public void Resize(Size canvasSize, float dpiScale)
    {
        EnsureNotDisposed();
        _canvasSize = canvasSize;
        _dpiScale = NormalizeDpi(dpiScale);
        UpdatePhysicalSize();
        _currentTransform = Matrix3x2.CreateScale(_dpiScale);
        _currentClip = PhysicalBounds;
        _transformStack.Clear();
        _clipStack.Clear();
        _currentMask = null;
        _layerStack.Clear();
        _rootCommands.Commands.Clear();
        _rootCommands.HasClearColor = false;
        _frameDirty = false;
        RenderingServer.ViewportSetSize(_rootViewport, _physicalWidth, _physicalHeight);
        RenderingServer.CanvasItemSetCustomRect(
            _compositeItem,
            true,
            new GRect2(0, 0, MathF.Max(0, _canvasSize.Width), MathF.Max(0, _canvasSize.Height)));
    }

    /// <inheritdoc />
    public SBitmap CaptureBitmap()
    {
        EnsureNotDisposed();
        if (!_hasPresentedFrame)
            throw new InvalidOperationException("Godot renderer has not presented a frame.");
        var image = RenderingServer.Texture2DGet(RenderingServer.ViewportGetTexture(_rootViewport));
        image.Convert(GImage.Format.Rgba8);
        var data = image.GetData().ToArray();
        var bitmap = new SBitmap(image.GetWidth(), image.GetHeight());
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var source = (y * bitmap.Width + x) * 4;
                var target = y * bitmap.Stride + x * 4;
                bitmap.Pixels[target] = data[source + 2];
                bitmap.Pixels[target + 1] = data[source + 1];
                bitmap.Pixels[target + 2] = data[source];
                bitmap.Pixels[target + 3] = data[source + 3];
            }
        }
        return bitmap;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FreeCommittedResources();
        foreach (var texture in _imageTextures)
            RenderingServer.FreeRid(texture);
        _imageTextures.Clear();
        foreach (var cache in _gradientCache.Values)
            RenderingServer.FreeRid(cache.Texture);
        _gradientCache.Clear();
        if (_glyphAtlasTexture.IsValid)
            RenderingServer.FreeRid(_glyphAtlasTexture);
        RenderingServer.FreeRid(_compositeMaterial);
        RenderingServer.FreeRid(_compositeItem);
        RenderingServer.FreeRid(_rootItem);
        RenderingServer.FreeRid(_rootCanvas);
        RenderingServer.FreeRid(_rootViewport);
        RenderingServer.FreeRid(_maskShader);
        RenderingServer.FreeRid(_paintShader);
        RenderingServer.FreeRid(_compositeShader);
    }

    private CommandList CurrentCommands => _layerStack.Count == 0 ? _rootCommands : _layerStack.Peek().List;

    private void AddTriangles(TriangleData triangles, Paint paint)
    {
        if (triangles.Points.Length == 0 || triangles.Indices.Length == 0 || _currentClip.IsEmpty) return;
        CurrentCommands.Commands.Add(RenderCommand.ForTriangles(triangles, paint, _currentClip, _currentMask));
        _frameDirty = true;
    }

    private void AddBitmapQuad(SBitmap bitmap, Rect destination, Rect? source)
    {
        var sourceRect = source ?? new Rect(0, 0, bitmap.Width, bitmap.Height);
        if (sourceRect.IsEmpty) return;
        var points = TransformQuad(destination);
        var u0 = sourceRect.X / bitmap.Width;
        var v0 = sourceRect.Y / bitmap.Height;
        var u1 = sourceRect.Right / bitmap.Width;
        var v1 = sourceRect.Bottom / bitmap.Height;
        var uvs = new[]
        {
            new GVector2(u0, v0), new GVector2(u1, v0),
            new GVector2(u1, v1), new GVector2(u0, v1)
        };
        var texturePaint = Paint.ForImage(bitmap, sourceRect);
        CurrentCommands.Commands.Add(RenderCommand.ForTriangles(
            new TriangleData(points, [0, 1, 2, 0, 2, 3], CreateWhiteColors(4), uvs),
            texturePaint,
            _currentClip,
            _currentMask));
        _frameDirty = true;
    }

    private void AddGlyphQuad(Rect logicalRect, GlyphRecord glyph, Paint paint)
    {
        var points = TransformQuad(logicalRect);
        var u0 = (float)glyph.X / GlyphAtlasWidth;
        var v0 = (float)glyph.Y / GlyphAtlasHeight;
        var u1 = (float)(glyph.X + glyph.PixelWidth) / GlyphAtlasWidth;
        var v1 = (float)(glyph.Y + glyph.PixelHeight) / GlyphAtlasHeight;
        CurrentCommands.Commands.Add(RenderCommand.ForTriangles(
            new TriangleData(
                points,
                [0, 1, 2, 0, 2, 3],
                CreateWhiteColors(4),
                [new GVector2(u0, v0), new GVector2(u1, v0), new GVector2(u1, v1), new GVector2(u0, v1)]),
            paint,
            _currentClip,
            _currentMask));
        _frameDirty = true;
    }

    private void AddClearCommand(CommandList list, Rect rect, SColor color)
    {
        var command = RenderCommand.ForTriangles(
            new TriangleData(
                [new GVector2(rect.X, rect.Y), new GVector2(rect.Right, rect.Y),
                 new GVector2(rect.Right, rect.Bottom), new GVector2(rect.X, rect.Bottom)],
                [0, 1, 2, 0, 2, 3],
                CreateWhiteColors(4),
                null),
            CreatePaint(new SolidColorBrush(color)),
            rect,
            null);
        list.Commands.Insert(0, command);
    }

    private void MaterializeList(
        CommandList list,
        GRid viewport,
        GRid parentItem,
        Point origin,
        List<GRid> items,
        List<GRid> canvases,
        List<GRid> viewports,
        List<GRid> materials,
        List<GRid> gradientTextures)
    {
        var drawIndex = 0;
        foreach (var command in list.Commands)
        {
            if (command.Kind == CommandKind.Coverage)
            {
                MaterializeCoverage(command, viewport, parentItem, origin, items, canvases, viewports, materials, gradientTextures);
                continue;
            }
            if (command.Kind == CommandKind.Layer)
            {
                MaterializeLayer(command, viewport, parentItem, origin, items, canvases, viewports, materials, gradientTextures);
                continue;
            }

            var item = RenderingServer.CanvasItemCreate();
            items.Add(item);
            RenderingServer.CanvasItemSetParent(item, parentItem);
            RenderingServer.CanvasItemSetDrawIndex(item, drawIndex++);
            RenderingServer.CanvasItemSetCustomRect(item, true, ToGodotRect(command.Bounds, origin));
            var texture = ResolveTexture(command.Paint);
            var mask = ResolveMask(command.Mask, viewport, origin, items, canvases, viewports, materials);
            var material = CreatePaintMaterial(command.Paint, command.Clip, origin, mask, null, materials, gradientTextures);
            RenderingServer.CanvasItemSetMaterial(item, material);
            var points = OffsetPoints(command.Triangles.Points, origin);
            RenderingServer.CanvasItemAddTriangleArray(
                item,
                command.Triangles.Indices,
                points,
                command.Triangles.Colors,
                command.Triangles.Uvs ?? [],
                Array.Empty<int>(),
                Array.Empty<float>(),
                texture,
                -1);
        }
    }
    private void MaterializeCoverage(
        RenderCommand command,
        GRid parentViewport,
        GRid parentItem,
        Point parentOrigin,
        List<GRid> items,
        List<GRid> canvases,
        List<GRid> viewports,
        List<GRid> materials,
        List<GRid> gradientTextures)
    {
        var bounds = command.Bounds;
        var width = Math.Max(1, (int)MathF.Ceiling(bounds.Width));
        var height = Math.Max(1, (int)MathF.Ceiling(bounds.Height));
        var clipMask = ResolveMask(command.Mask, parentViewport, parentOrigin, items, canvases, viewports, materials);

        var coverageViewport = RenderingServer.ViewportCreate();
        var coverageCanvas = RenderingServer.CanvasCreate();
        var coverageItem = RenderingServer.CanvasItemCreate();
        ConfigureViewport(coverageViewport, width, height, parentViewport);
        RenderingServer.ViewportAttachCanvas(coverageViewport, coverageCanvas);
        RenderingServer.CanvasItemSetParent(coverageItem, coverageCanvas);
        var coverageMaterial = CreateMaterial(_maskShader, materials);
        RenderingServer.MaterialSetParam(coverageMaterial, MaskEnabledName, false);
        RenderingServer.MaterialSetParam(coverageMaterial, MaskOriginName, new GVector2(bounds.X, bounds.Y));
        RenderingServer.CanvasItemSetMaterial(coverageItem, coverageMaterial);
        var coveragePoints = OffsetPoints(command.Triangles.Points, new Point(bounds.X, bounds.Y));
        RenderingServer.CanvasItemAddTriangleArray(
            coverageItem,
            command.Triangles.Indices,
            coveragePoints,
            command.Triangles.Colors,
            command.Triangles.Uvs ?? [],
            Array.Empty<int>(),
            Array.Empty<float>(),
            default,
            -1);
        items.Add(coverageItem);
        canvases.Add(coverageCanvas);
        viewports.Add(coverageViewport);

        var coverage = new MaskMaterialization(
            coverageViewport,
            RenderingServer.ViewportGetTexture(coverageViewport),
            bounds);
        var paintItem = RenderingServer.CanvasItemCreate();
        items.Add(paintItem);
        RenderingServer.CanvasItemSetParent(paintItem, parentItem);
        RenderingServer.CanvasItemSetCustomRect(paintItem, true, ToGodotRect(bounds, parentOrigin));
        var material = CreatePaintMaterial(
            command.Paint,
            command.Clip,
            parentOrigin,
            clipMask,
            coverage,
            materials,
            gradientTextures);
        RenderingServer.CanvasItemSetMaterial(paintItem, material);
        var destination = new GVector2[]
        {
            new(bounds.X - parentOrigin.X, bounds.Y - parentOrigin.Y),
            new(bounds.Right - parentOrigin.X, bounds.Y - parentOrigin.Y),
            new(bounds.Right - parentOrigin.X, bounds.Bottom - parentOrigin.Y),
            new(bounds.X - parentOrigin.X, bounds.Bottom - parentOrigin.Y)
        };
        var quadIndices = new[] { 0, 1, 2, 0, 2, 3 };
        var quadUvs = new[]
        {
            new GVector2(0, 0), new GVector2(1, 0),
            new GVector2(1, 1), new GVector2(0, 1)
        };
        RenderingServer.CanvasItemAddTriangleArray(
            paintItem,
            quadIndices,
            destination,
            CreateWhiteColors(4),
            quadUvs,
            Array.Empty<int>(),
            Array.Empty<float>(),
            default,
            -1);
    }

    private MaskMaterialization? ResolveMask(
        ClipMask? mask,
        GRid currentViewport,
        Point origin,
        List<GRid> items,
        List<GRid> canvases,
        List<GRid> viewports,
        List<GRid> materials)
    {
        if (mask == null) return null;
        if (_maskCache.TryGetValue((mask, currentViewport), out var cached)) return cached;

        var parent = ResolveMask(mask.Parent, currentViewport, origin, items, canvases, viewports, materials);
        var width = Math.Max(1, (int)MathF.Ceiling(mask.Bounds.Width));
        var height = Math.Max(1, (int)MathF.Ceiling(mask.Bounds.Height));
        var viewport = RenderingServer.ViewportCreate();
        var canvas = RenderingServer.CanvasCreate();
        var item = RenderingServer.CanvasItemCreate();
        var parentViewport = parent?.Viewport ?? currentViewport;
        ConfigureViewport(viewport, width, height, parentViewport);
        RenderingServer.ViewportAttachCanvas(viewport, canvas);
        RenderingServer.CanvasItemSetParent(item, canvas);
        viewports.Add(viewport);
        canvases.Add(canvas);
        items.Add(item);

        var material = CreateMaterial(_maskShader, materials);
        RenderingServer.MaterialSetParam(material, MaskEnabledName, parent != null);
        RenderingServer.MaterialSetParam(material, MaskOriginName, new GVector2(mask.Bounds.X, mask.Bounds.Y));
        if (parent is { } parentMask)
        {
            RenderingServer.MaterialSetParam(material, MaskTextureName, parentMask.Texture);
            RenderingServer.MaterialSetParam(material, MaskRectName, new GRect2(
                parentMask.Bounds.X,
                parentMask.Bounds.Y,
                parentMask.Bounds.Width,
                parentMask.Bounds.Height));
        }
        RenderingServer.CanvasItemSetMaterial(item, material);
        var points = OffsetPoints(mask.Triangles.Points, new Point(mask.Bounds.X, mask.Bounds.Y));
        RenderingServer.CanvasItemAddTriangleArray(
            item,
            mask.Triangles.Indices,
            points,
            mask.Triangles.Colors,
            mask.Triangles.Uvs ?? [],
            Array.Empty<int>(),
            Array.Empty<float>(),
            default,
            -1);

        var result = new MaskMaterialization(viewport, RenderingServer.ViewportGetTexture(viewport), mask.Bounds);
        _maskCache[(mask, currentViewport)] = result;
        return result;
    }

    private GRid ResolveTexture(Paint paint) => paint.Kind switch
    {
        PaintKind.Image => GetBitmapTexture(paint.Bitmap ?? throw new InvalidOperationException("Bitmap paint has no source.")),
        PaintKind.Glyph => _glyphAtlasTexture,
        _ => default
    };

    private void MaterializeLayer(
        RenderCommand command,
        GRid parentViewport,
        GRid parentItem,
        Point parentOrigin,
        List<GRid> items,
        List<GRid> canvases,
        List<GRid> viewports,
        List<GRid> materials,
        List<GRid> gradientTextures)
    {
        var bounds = command.Bounds;
        var width = Math.Max(1, (int)MathF.Ceiling(bounds.Width));
        var height = Math.Max(1, (int)MathF.Ceiling(bounds.Height));
        var viewport = RenderingServer.ViewportCreate();
        var canvas = RenderingServer.CanvasCreate();
        var item = RenderingServer.CanvasItemCreate();
        canvases.Add(canvas);
        items.Add(item);
        ConfigureViewport(viewport, width, height, parentViewport);
        RenderingServer.ViewportAttachCanvas(viewport, canvas);
        RenderingServer.CanvasItemSetParent(item, canvas);
        MaterializeList(
            command.Layer!,
            viewport,
            item,
            new Point(bounds.X, bounds.Y),
            items,
            canvases,
            viewports,
            materials,
            gradientTextures);
        viewports.Add(viewport);

        var composite = RenderingServer.CanvasItemCreate();
        items.Add(composite);
        RenderingServer.CanvasItemSetParent(composite, parentItem);
        RenderingServer.CanvasItemSetSelfModulate(composite, new GColor(1, 1, 1, command.Layer!.Opacity));
        var destination = new GRect2(
            bounds.X - parentOrigin.X,
            bounds.Y - parentOrigin.Y,
            bounds.Width,
            bounds.Height);
        RenderingServer.CanvasItemAddTextureRectRegion(
            composite,
            destination,
            RenderingServer.ViewportGetTexture(viewport),
            new GRect2(0, 0, width, height),
            new GColor(1, 1, 1, 1));
    }

    private GRid CreatePaintMaterial(
        Paint paint,
        Rect clip,
        Point origin,
        MaskMaterialization? mask,
        MaskMaterialization? coverage,
        List<GRid> materials,
        List<GRid> gradientTextures)
    {
        var material = CreateMaterial(_paintShader, materials);
        RenderingServer.MaterialSetParam(material, BrushKindName, (int)paint.Kind);
        RenderingServer.MaterialSetParam(material, SolidColorName, ToGodotColor(paint.Color));
        RenderingServer.MaterialSetParam(material, ClipEnabledName, !Contains(PhysicalBounds, clip));
        RenderingServer.MaterialSetParam(material, ClipRectName, ToGodotRect(clip, origin));
        RenderingServer.MaterialSetParam(material, MaskEnabledName, mask != null);
        if (mask is { } maskValue)
        {
            RenderingServer.MaterialSetParam(material, MaskTextureName, maskValue.Texture);
            RenderingServer.MaterialSetParam(material, MaskRectName, ToGodotRect(maskValue.Bounds, origin));
        }
        RenderingServer.MaterialSetParam(material, CoverageEnabledName, coverage != null);
        if (coverage is { } coverageValue)
        {
            RenderingServer.MaterialSetParam(material, CoverageTextureName, coverageValue.Texture);
            RenderingServer.MaterialSetParam(material, CoverageRectName, ToGodotRect(coverageValue.Bounds, origin));
        }
        if (paint.Kind == PaintKind.Linear)
        {
            RenderingServer.MaterialSetParam(material, GradientStartName, ToGodotPoint(TransformPoint(paint.Transform, paint.Start), origin));
            RenderingServer.MaterialSetParam(material, GradientEndName, ToGodotPoint(TransformPoint(paint.Transform, paint.End), origin));
            var gradient = GetGradientTexture(paint.Stops!);
            gradientTextures.Add(gradient.Texture);
            RenderingServer.MaterialSetParam(material, GradientStopsName, gradient.Texture);
            RenderingServer.MaterialSetParam(material, GradientStopCountName, paint.Stops!.Length);
            RenderingServer.MaterialSetParam(material, GradientSpreadName, (int)paint.Spread);
        }
        else if (paint.Kind == PaintKind.Radial)
        {
            RenderingServer.MaterialSetParam(material, GradientCenterName, ToGodotPoint(TransformPoint(paint.Transform, paint.Center), origin));
            RenderingServer.MaterialSetParam(material, GradientRadiusName, paint.Radius * MaxScale(paint.Transform));
            var gradient = GetGradientTexture(paint.Stops!);
            gradientTextures.Add(gradient.Texture);
            RenderingServer.MaterialSetParam(material, GradientStopsName, gradient.Texture);
            RenderingServer.MaterialSetParam(material, GradientStopCountName, paint.Stops!.Length);
            RenderingServer.MaterialSetParam(material, GradientSpreadName, (int)paint.Spread);
        }
        return material;
    }

    private GradientCache GetGradientTexture(GradientStopSnapshot[] stops)
    {
        var keyBuilder = new StringBuilder(stops.Length * 32);
        foreach (var stop in stops)
            keyBuilder.Append(stop.Offset).Append(':').Append(stop.Color.ToString()).Append(';');
        var key = keyBuilder.ToString();
        if (_gradientCache.TryGetValue(key, out var cached)) return cached;

        var bytes = new byte[stops.Length * 2 * sizeof(float) * 4];
        for (var i = 0; i < stops.Length; i++)
        {
            var colorOffset = i * 32;
            WriteFloat(bytes, colorOffset, stops[i].Color.R / 255f);
            WriteFloat(bytes, colorOffset + 4, stops[i].Color.G / 255f);
            WriteFloat(bytes, colorOffset + 8, stops[i].Color.B / 255f);
            WriteFloat(bytes, colorOffset + 12, stops[i].Color.A / 255f);
            WriteFloat(bytes, colorOffset + 16, stops[i].Offset);
        }
        var image = GImage.CreateFromData(
            stops.Length * 2,
            1,
            false,
            GImage.Format.Rgbaf,
            bytes);
        var texture = RenderingServer.Texture2DCreate(image);
        cached = new GradientCache(texture);
        _gradientCache[key] = cached;
        return cached;
    }

    private GRid GetBitmapTexture(SBitmap bitmap)
    {
        if (_imageCache.TryGetValue(bitmap, out var cached) &&
            cached.Width == bitmap.Width && cached.Height == bitmap.Height)
        {
            if (cached.Version != bitmap.ContentVersion)
            {
                RenderingServer.Texture2DUpdate(cached.Texture, CreateBitmapImage(bitmap), 0);
                _imageCache[bitmap] = cached with { Version = bitmap.ContentVersion };
            }
            return cached.Texture;
        }

        var texture = RenderingServer.Texture2DCreate(CreateBitmapImage(bitmap));
        _imageTextures.Add(texture);
        _imageCache[bitmap] = new ImageCache(texture, bitmap.Width, bitmap.Height, bitmap.ContentVersion);
        return texture;
    }

    private GImage CreateBitmapImage(SBitmap bitmap)
    {
        var rgba = new byte[bitmap.Pixels.Length];
        for (var y = 0; y < bitmap.Height; y++)
        {
            var sourceRow = y * bitmap.Stride;
            for (var x = 0; x < bitmap.Width; x++)
            {
                var source = sourceRow + x * 4;
                rgba[source] = bitmap.Pixels[source + 2];
                rgba[source + 1] = bitmap.Pixels[source + 1];
                rgba[source + 2] = bitmap.Pixels[source];
                rgba[source + 3] = bitmap.Pixels[source + 3];
            }
        }
        return GImage.CreateFromData(bitmap.Width, bitmap.Height, false, GImage.Format.Rgba8, rgba);
    }

    private GlyphRecord GetGlyph(SFont font, char character)
    {
        var generation = FontCollection.Shared.CustomGeneration;
        if (_glyphGeneration != generation || MathF.Abs(_glyphDpi - _dpiScale) > 0.0001f)
        {
            _glyphs.Clear();
            _glyphCursorX = 0;
            _glyphCursorY = 0;
            _glyphRowHeight = 0;
            Array.Clear(_glyphAtlasPixels);
            _glyphGeneration = generation;
            _glyphDpi = _dpiScale;
            _glyphAtlasDirty = true;
        }

        var physicalFont = font.WithSize(font.Size * _dpiScale);
        var key = new GlyphKey(physicalFont.Family, physicalFont.Size, (int)physicalFont.Weight, (int)physicalFont.Style, character, generation);
        if (_glyphs.TryGetValue(key, out var existing)) return existing;

        var rasterized = _glyphRasterizer.Rasterize(physicalFont, character) ?? CreateFallbackGlyph(physicalFont.Size);
        var record = new GlyphRecord
        {
            OffsetX = rasterized.OffsetX / _dpiScale,
            OffsetY = rasterized.OffsetY / _dpiScale,
            Width = rasterized.Width / _dpiScale,
            Height = rasterized.Height / _dpiScale,
            X = 0,
            Y = 0,
            PixelWidth = rasterized.Width,
            PixelHeight = rasterized.Height
        };
        if (rasterized.Width > 0 && rasterized.Height > 0)
        {
            AllocateGlyph(rasterized.Width + 2, rasterized.Height + 2, out var x, out var y);
            record.X = x + 1;
            record.Y = y + 1;
            for (var row = 0; row < rasterized.Height; row++)
            {
                for (var column = 0; column < rasterized.Width; column++)
                {
                    var source = row * rasterized.Stride + column;
                    if ((uint)source >= (uint)rasterized.Coverage.Length) continue;
                    var destination = ((y + 1 + row) * GlyphAtlasWidth + x + 1 + column) * 4;
                    _glyphAtlasPixels[destination] = 255;
                    _glyphAtlasPixels[destination + 1] = 255;
                    _glyphAtlasPixels[destination + 2] = 255;
                    _glyphAtlasPixels[destination + 3] = rasterized.Coverage[source];
                }
            }
            _glyphAtlasDirty = true;
        }
        _glyphs[key] = record;
        EnsureGlyphAtlasTexture();
        return record;
    }

    private void EnsureGlyphAtlasTexture()
    {
        if (!_glyphAtlasDirty) return;
        var image = GImage.CreateFromData(
            GlyphAtlasWidth,
            GlyphAtlasHeight,
            false,
            GImage.Format.Rgba8,
            _glyphAtlasPixels);
        if (!_glyphAtlasTexture.IsValid)
        {
            _glyphAtlasTexture = RenderingServer.Texture2DCreate(image);
            _glyphAtlasTextures.Add(_glyphAtlasTexture);
        }
        else
        {
            RenderingServer.Texture2DUpdate(_glyphAtlasTexture, image, 0);
        }
        _glyphAtlasDirty = false;
    }

    private void AllocateGlyph(int width, int height, out int x, out int y)
    {
        if (_glyphCursorX + width > GlyphAtlasWidth)
        {
            _glyphCursorX = 0;
            _glyphCursorY += _glyphRowHeight;
            _glyphRowHeight = 0;
        }
        if (_glyphCursorY + height > GlyphAtlasHeight)
            throw new InvalidOperationException("Godot glyph atlas is full; reduce the number of physical font variants.");
        x = _glyphCursorX;
        y = _glyphCursorY;
        _glyphCursorX += width;
        _glyphRowHeight = Math.Max(_glyphRowHeight, height);
    }

    private static RasterizedGlyph CreateFallbackGlyph(float size)
    {
        const int width = 7;
        const int height = 9;
        var coverage = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x is 0 or (width - 1) || y is 0 or (height - 1) || x == y || x + y == width - 1)
                    coverage[y * width + x] = 255;
            }
        }
        return new RasterizedGlyph
        {
            Width = width,
            Height = height,
            Stride = width,
            OffsetX = 0,
            OffsetY = -(int)MathF.Ceiling(size * 0.8f),
            AdvanceX = size * 0.6f,
            Coverage = coverage
        };
    }

    private Paint CreatePaint(Brush brush)
    {
        var paint = brush switch
        {
            SolidColorBrush solid => Paint.ForSolid(solid.Color),
            LinearGradientBrush linear => Paint.ForLinear(linear),
            RadialGradientBrush radial => Paint.ForRadial(radial),
            _ => throw new NotSupportedException($"Godot rendering does not support brush {brush.GetType().FullName}.")
        };
        return paint with { Transform = _currentTransform };
    }

    private static PathGeometry CreateEllipsePath(Point center, float radiusX, float radiusY, bool close = true)
    {
        var path = PathGeometry.Create();
        const int segments = 64;
        for (var i = 0; i <= segments; i++)
        {
            var angle = MathF.Tau * i / segments;
            var point = new Point(
                center.X + radiusX * MathF.Cos(angle),
                center.Y + radiusY * MathF.Sin(angle));
            if (i == 0) path.MoveTo(point);
            else path.LineTo(point);
        }
        if (close) path.Close();
        return path;
    }

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
        _ => throw new NotSupportedException($"Godot clipping does not support geometry {geometry.GetType().FullName}.")
    };

    private TriangleData CreateClipTriangles(Geometry geometry)
    {
        var path = geometry switch
        {
            RectGeometry rect => PathGeometry.Create()
                .MoveTo(new Point(rect.Rect.X, rect.Rect.Y))
                .LineTo(new Point(rect.Rect.Right, rect.Rect.Y))
                .LineTo(new Point(rect.Rect.Right, rect.Rect.Bottom))
                .LineTo(new Point(rect.Rect.X, rect.Rect.Bottom))
                .Close(),
            RoundedRectGeometry rounded => rounded.ToPath(),
            EllipseGeometry ellipse => CreateEllipsePath(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY),
            PathGeometry pathGeometry => pathGeometry,
            _ => throw new NotSupportedException($"Godot clipping does not support geometry {geometry.GetType().FullName}.")
        };
        var contours = PathTessellator.FlattenPath(path, _currentTransform);
        var tessellation = PathTessellator.Triangulate(contours);
        var pointCount = tessellation.ElementCount * 3;
        var points = new GVector2[pointCount];
        var indices = new int[pointCount];
        for (var i = 0; i < pointCount; i++)
        {
            var vertex = tessellation.Vertices[tessellation.Elements[i]].Position;
            points[i] = new GVector2(vertex.X, vertex.Y);
            indices[i] = i;
        }
        return new TriangleData(points, indices, CreateWhiteColors(pointCount), null);
    }

    private static Rect GetPathBounds(PathGeometry path)
    {
        var bounds = Rect.Empty;
        var hasBounds = false;
        foreach (var command in path.Commands)
        {
            Point? point = command switch
            {
                MoveToCmd move => move.Point,
                LineToCmd line => line.Point,
                ArcToCmd arc => arc.Oval.Center,
                _ => null
            };
            if (point is not { } value) continue;
            var pointRect = new Rect(value.X, value.Y, 0, 0);
            bounds = hasBounds ? Rect.Union(bounds, pointRect) : pointRect;
            hasBounds = true;
        }
        return hasBounds ? bounds : Rect.Empty;
    }

    private void UpdatePhysicalSize()
    {
        _physicalWidth = ToPhysical(_canvasSize.Width, _dpiScale);
        _physicalHeight = ToPhysical(_canvasSize.Height, _dpiScale);
    }

    private Rect PhysicalBounds => new(0, 0, _physicalWidth, _physicalHeight);

    private static void ConfigureViewport(GRid viewport, int width, int height, GRid parent)
    {
        RenderingServer.ViewportSetSize(viewport, Math.Max(1, width), Math.Max(1, height));
        RenderingServer.ViewportSetTransparentBackground(viewport, true);
        RenderingServer.ViewportSetDisable3D(viewport, true);
        RenderingServer.ViewportSetMsaa2D(viewport, RenderingServer.ViewportMsaa.Msaa4X);
        RenderingServer.ViewportSetClearMode(viewport, RenderingServer.ViewportClearMode.Always);
        RenderingServer.ViewportSetUpdateMode(viewport, RenderingServer.ViewportUpdateMode.Once);
        RenderingServer.ViewportSetParentViewport(viewport, parent);
    }

    private void ActivateGraph(GRid root, GRid parent, IReadOnlyList<GRid> producers)
    {
        foreach (var viewport in producers)
        {
            RenderingServer.ViewportSetActive(viewport, false);
            RenderingServer.ViewportSetActive(viewport, true);
        }
        RenderingServer.ViewportSetActive(root, false);
        RenderingServer.ViewportSetParentViewport(root, _parentViewport);
        RenderingServer.ViewportSetActive(root, true);
    }

    private void CommitFrame(
        List<GRid> items,
        List<GRid> canvases,
        List<GRid> viewports,
        List<GRid> materials,
        List<GRid> gradientTextures)
    {
        FreeCommittedResources();
        _committedItems.AddRange(items);
        _committedCanvases.AddRange(canvases);
        _committedViewports.AddRange(viewports);
        _committedMaterials.AddRange(materials);
        _committedGradientTextures.AddRange(gradientTextures);
    }

    private void FreeCommittedResources()
    {
        for (var i = _committedItems.Count - 1; i >= 0; i--)
            RenderingServer.FreeRid(_committedItems[i]);
        for (var i = _committedCanvases.Count - 1; i >= 0; i--)
            RenderingServer.FreeRid(_committedCanvases[i]);
        for (var i = _committedViewports.Count - 1; i >= 0; i--)
            RenderingServer.FreeRid(_committedViewports[i]);
        for (var i = _committedMaterials.Count - 1; i >= 0; i--)
            RenderingServer.FreeRid(_committedMaterials[i]);
        _committedItems.Clear();
        _committedCanvases.Clear();
        _committedViewports.Clear();
        _committedMaterials.Clear();
        _committedGradientTextures.Clear();
    }

    private static void FreeResources(
        IReadOnlyList<GRid> items,
        IReadOnlyList<GRid> canvases,
        IReadOnlyList<GRid> viewports,
        IReadOnlyList<GRid> materials)
    {
        for (var i = items.Count - 1; i >= 0; i--) RenderingServer.FreeRid(items[i]);
        for (var i = canvases.Count - 1; i >= 0; i--) RenderingServer.FreeRid(canvases[i]);
        for (var i = viewports.Count - 1; i >= 0; i--) RenderingServer.FreeRid(viewports[i]);
        for (var i = materials.Count - 1; i >= 0; i--) RenderingServer.FreeRid(materials[i]);
    }

    private GRid CreateShader(string resourceName)
    {
        using var stream = typeof(GodotRenderContext).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded Godot shader resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var shader = RenderingServer.ShaderCreate();
        RenderingServer.ShaderSetCode(shader, reader.ReadToEnd());
        return shader;
    }

    private static GRid CreateMaterial(GRid shader, List<GRid> owner)
    {
        var material = RenderingServer.MaterialCreate();
        RenderingServer.MaterialSetShader(material, shader);
        owner.Add(material);
        return material;
    }

    private TriangleData CreateQuad(Rect rect) => new(
        TransformQuad(rect),
        [0, 1, 2, 0, 2, 3],
        CreateWhiteColors(4),
        null);
    private GVector2[] TransformQuad(Rect rect)
    {
        return
        [
            ToGodotPoint(TransformPoint(new Point(rect.X, rect.Y))),
            ToGodotPoint(TransformPoint(new Point(rect.Right, rect.Y))),
            ToGodotPoint(TransformPoint(new Point(rect.Right, rect.Bottom))),
            ToGodotPoint(TransformPoint(new Point(rect.X, rect.Bottom)))
        ];
    }

    private GVector2[] OffsetPoints(GVector2[] points, Point origin)
    {
        if (origin == Point.Zero) return points;
        var result = new GVector2[points.Length];
        for (var i = 0; i < points.Length; i++)
            result[i] = new GVector2(points[i].X - origin.X, points[i].Y - origin.Y);
        return result;
    }

    private Point TransformPoint(Point point) => TransformPoint(_currentTransform, point);

    private static Point TransformPoint(Matrix3x2 transform, Point point)
    {
        return new Point(
            point.X * transform.M11 + point.Y * transform.M21 + transform.M31,
            point.X * transform.M12 + point.Y * transform.M22 + transform.M32);
    }

    private Rect TransformRect(Rect rect)
    {
        var p0 = TransformPoint(new Point(rect.X, rect.Y));
        var p1 = TransformPoint(new Point(rect.Right, rect.Y));
        var p2 = TransformPoint(new Point(rect.Right, rect.Bottom));
        var p3 = TransformPoint(new Point(rect.X, rect.Bottom));
        var minX = MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X));
        var minY = MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y));
        var maxX = MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X));
        var maxY = MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static Rect Intersect(Rect a, Rect b) => Rect.Intersect(a, b);

    private static bool Contains(Rect outer, Rect inner) =>
        inner.X <= outer.X && inner.Y <= outer.Y && inner.Right >= outer.Right && inner.Bottom >= outer.Bottom;

    private static float MaxScale(Matrix3x2 matrix)
    {
        var x = MathF.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);
        var y = MathF.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22);
        return MathF.Max(x, y);
    }

    private static GRect2 ToGodotRect(Rect rect, Point origin) =>
        new(rect.X - origin.X, rect.Y - origin.Y, MathF.Max(0, rect.Width), MathF.Max(0, rect.Height));

    private static GVector2 ToGodotPoint(Point point, Point origin = default) =>
        new(point.X - origin.X, point.Y - origin.Y);

    private static GColor ToGodotColor(SColor color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    private static GColor PackedColor(uint color) =>
        new((color & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f,
            ((color >> 16) & 0xFF) / 255f, ((color >> 24) & 0xFF) / 255f);

    private static GColor[] CreateWhiteColors(int count)
    {
        var colors = new GColor[count];
        Array.Fill(colors, new GColor(1, 1, 1, 1));
        return colors;
    }

    private static int ToPhysical(float logical, float dpi) =>
        Math.Max(1, (int)MathF.Ceiling(MathF.Max(0, logical) * dpi));

    private static float NormalizeDpi(float dpi) => float.IsFinite(dpi) && dpi > 0 ? dpi : 1f;

    private static float NormalizeOpacity(float opacity) =>
        float.IsNaN(opacity) ? 1f : Math.Clamp(opacity, 0, 1);

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void WriteFloat(byte[] destination, int offset, float value) =>
        BitConverter.TryWriteBytes(destination.AsSpan(offset, sizeof(float)), value);

    private enum CommandKind : byte { Triangles, Coverage, Layer }
    private enum PaintKind : byte { Solid, Linear, Radial, Image, Glyph }

    private sealed class CommandList
    {
        public List<RenderCommand> Commands { get; } = [];
        public Rect Bounds { get; set; }
        public float Opacity { get; set; } = 1;
        public bool HasClearColor { get; set; }
        public SColor ClearColor { get; set; } = SColor.Transparent;
    }

    private readonly record struct LayerCapture(CommandList List, Rect Bounds);

    private readonly record struct ClipFrame(Rect Bounds, ClipMask? Mask);

    private sealed class ClipMask(TriangleData triangles, Rect bounds, ClipMask? parent)
    {
        public TriangleData Triangles { get; } = triangles;
        public Rect Bounds { get; } = bounds;
        public ClipMask? Parent { get; } = parent;
    }

    private readonly record struct MaskMaterialization(GRid Viewport, GRid Texture, Rect Bounds);

    private sealed class RenderCommand
    {
        public CommandKind Kind;
        public TriangleData Triangles;
        public Paint Paint;
        public Rect Clip;
        public ClipMask? Mask;
        public CommandList? Layer;
        public Rect Bounds;

        public static RenderCommand ForTriangles(TriangleData triangles, Paint paint, Rect clip, ClipMask? mask) => new()
        {
            Kind = CommandKind.Triangles,
            Triangles = triangles,
            Paint = paint,
            Clip = clip,
            Mask = mask,
            Bounds = BoundsOf(triangles.Points)
        };

        public static RenderCommand ForCoverage(TriangleData triangles, Paint paint, Rect clip, ClipMask? mask) => new()
        {
            Kind = CommandKind.Coverage,
            Triangles = triangles,
            Paint = paint,
            Clip = clip,
            Mask = mask,
            Bounds = BoundsOf(triangles.Points)
        };

        public static RenderCommand ForLayer(CommandList layer, Rect bounds) => new()
        {
            Kind = CommandKind.Layer,
            Layer = layer,
            Bounds = bounds
        };

        private static Rect BoundsOf(IReadOnlyList<GVector2> points)
        {
            if (points.Count == 0) return Rect.Empty;
            var minX = points[0].X;
            var maxX = points[0].X;
            var minY = points[0].Y;
            var maxY = points[0].Y;
            for (var i = 1; i < points.Count; i++)
            {
                minX = MathF.Min(minX, points[i].X);
                maxX = MathF.Max(maxX, points[i].X);
                minY = MathF.Min(minY, points[i].Y);
                maxY = MathF.Max(maxY, points[i].Y);
            }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    private readonly record struct TriangleData(
        GVector2[] Points,
        int[] Indices,
        GColor[] Colors,
        GVector2[]? Uvs);

    private readonly record struct Paint(
        PaintKind Kind,
        SColor Color,
        Point Start,
        Point End,
        Point Center,
        float Radius,
        GradientStopSnapshot[]? Stops,
        GradientSpreadMethod Spread,
        SBitmap? Bitmap,
        Rect Source,
        GRid Texture,
        Matrix3x2 Transform)
    {
        public static Paint ForSolid(SColor color) => new(PaintKind.Solid, color, default, default, default, 0, null, 0, null, default, default, Matrix3x2.Identity);
        public static Paint ForLinear(LinearGradientBrush brush) => new(PaintKind.Linear, SColor.White, brush.Start, brush.End, default, 0, Snapshot(brush.Stops), brush.SpreadMethod, null, default, default, Matrix3x2.Identity);
        public static Paint ForRadial(RadialGradientBrush brush) => new(PaintKind.Radial, SColor.White, default, default, brush.Center, brush.Radius, Snapshot(brush.Stops), brush.SpreadMethod, null, default, default, Matrix3x2.Identity);
        public static Paint ForImage(SBitmap bitmap, Rect source) => new(PaintKind.Image, SColor.White, default, default, default, 0, null, 0, bitmap, source, default, Matrix3x2.Identity);
        private static GradientStopSnapshot[] Snapshot(IReadOnlyList<GradientStop> stops) =>
            stops.OrderBy(stop => stop.Offset).Select(stop => new GradientStopSnapshot(stop.Offset, stop.Color)).ToArray();
    }

    private readonly record struct GradientStopSnapshot(float Offset, SColor Color);
    private readonly record struct ImageCache(GRid Texture, int Width, int Height, long Version);
    private sealed record GradientCache(GRid Texture);
    private readonly record struct GlyphKey(string Family, float Size, int Weight, int Style, char Character, int Generation);

    private struct GlyphRecord
    {
        public int X, Y;
        public int PixelWidth, PixelHeight;
        public float Width, Height, OffsetX, OffsetY;
    }
}
