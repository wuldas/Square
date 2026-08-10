using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Vulkan;
using Square.Graphics;
using Square.Text.Glyph;
using Image = Square.Graphics.Image;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Square.Backends.Vulkan;

/// <summary>
/// Vulkan GPU implementation of IRenderContext using ImGui-style batched 2D rendering.
/// All draw calls are triangulated on CPU, batched by texture/scissor, and submitted in one draw.
/// </summary>
internal sealed unsafe class VulkanRenderContext : IRenderContext, IDpiResizableRenderContext, IRenderBitmapSource
{
    private readonly VulkanDevice _device;
    private readonly VulkanSwapchain _swapchain;
    private readonly VulkanPipeline _pipeline;
    private readonly VulkanBatchRenderer _batchRenderer;
    private readonly VulkanTextureAtlas _atlas;
    private readonly VulkanReadbackBuffer _readback;
    private readonly bool _readbackEnabled;
    private readonly SystemGlyphRasterizer _glyphRasterizer = new(cacheGlyphs: false);
    private readonly Dictionary<GlyphCacheKey, CachedGlyph> _glyphCache = [];
    private readonly ConditionalWeakTable<Bitmap, CachedImage> _imageCache = new();
    private readonly List<Vertex2D> _scratchVertices = new(512);
    private readonly List<uint> _scratchIndices = new(768);

    private CommandBuffer _currentCmd;
    private bool _frameStarted;
    private bool _disposed;
    private Color _clearColor;

    // True while the window is minimized/collapsed to a degenerate (0x0) size.
    // Vulkan forbids 0-extent swapchains and 0-size buffers, so rendering is paused
    // until a valid size arrives instead of crashing in vkAllocateMemory.
    private bool _minimized;

    // Transform stack
    private readonly Stack<Matrix3x2> _transformStack = new();
    private Matrix3x2 _currentTransform;

    // Clip stack
    private readonly Stack<Rect> _clipStack = new();
    private Rect _currentClip;

    // Layer (opacity) stack
    private readonly Stack<float> _opacityStack = new();
    private float _currentOpacity = 1f;

    public Size CanvasSize { get; private set; }
    public float DpiScale { get; private set; }

    /// <summary>
    /// Enables the Vulkan validation layer + debug messenger (messages go to the console).
    /// Opt in by setting SQUARE_VULKAN_VALIDATION=1; requires the Vulkan SDK runtime layers.
    /// </summary>
    private static bool EnableValidation =>
        Environment.GetEnvironmentVariable("SQUARE_VULKAN_VALIDATION") is "1" or "true";

    internal VulkanRenderContext(RenderContextCreateInfo info)
    {
        CanvasSize = info.CanvasSize;
        DpiScale = NormalizeDpi(info.DpiScale);
        _currentTransform = Matrix3x2.CreateScale(DpiScale);

        var physicalW = ToPhysical(CanvasSize.Width, DpiScale);
        var physicalH = ToPhysical(CanvasSize.Height, DpiScale);
        _readbackEnabled = Environment.GetEnvironmentVariable("SQUARE_VULKAN_READBACK") is "1" or "true";

        _device = new VulkanDevice(info.NativeTarget
            ?? throw new VulkanException("Vulkan backend requires a NativeTarget."),
            enableValidation: EnableValidation);
        _device.ConfigureColorSampleCount(physicalW, physicalH);
        _swapchain = new VulkanSwapchain(_device, _device.Surface, physicalW, physicalH, info.VSync, _readbackEnabled);
        _pipeline = new VulkanPipeline(_device, _swapchain);
        _batchRenderer = new VulkanBatchRenderer(_device, _pipeline);
        _atlas = new VulkanTextureAtlas(_device, _pipeline);
        _readback = new VulkanReadbackBuffer(_device);
        _minimized = _swapchain.Extent.Width < 1 || _swapchain.Extent.Height < 1;
        if (_readbackEnabled)
            _readback.EnsureSize(_swapchain.Extent.Width, _swapchain.Extent.Height);

        if (!_minimized)
            _pipeline.UpdateProjection(_swapchain.Extent.Width, _swapchain.Extent.Height);
        _currentClip = new Rect(0, 0, _swapchain.Extent.Width, _swapchain.Extent.Height);
    }

    // ─── Frame lifecycle ──────────────────────────────────────────────────

    private bool EnsureFrame()
    {
        if (_frameStarted) return true;
        if (_minimized) return false;
        if (!_swapchain.AcquireNextImage())
        {
            // Surface is unavailable (window minimized / swapchain out-of-date). Pause
            // rendering; Resize() clears _minimized once a valid size arrives.
            _minimized = true;
            return false;
        }
        _batchRenderer.BeginFrame();
        _frameStarted = true;
        _clearColor = new Color(0, 0, 0, 255);
        return true;
    }

    public void Clear(Color color)
    {
        if (!EnsureFrame()) return;
        _clearColor = color;
    }

    public void Clear(Color color, Rect rect)
    {
        if (!EnsureFrame()) return;
        FillRectColor(rect, color);
    }

    public void Flush()
    {
        if (!EnsureFrame()) return;
        // Flush is a no-op; actual submission happens in Present
    }

    public void Present() => Present(null);

    public void Present(IReadOnlyList<Rect>? dirtyRects)
    {
        if (_minimized) return;
        if (dirtyRects is { Count: 0 } && !_frameStarted) return;
        if (!EnsureFrame()) return;
        SubmitFrame();
        _frameStarted = false;
    }

    private void SubmitFrame()
    {
        // Draw calls can add glyphs to the atlas, so upload after the frame has been built.
        _atlas.Flush();

        var api = _device.Api;
        var cmd = AllocateCommandBuffer();
        _currentCmd = cmd;

        var beginInfo = new CommandBufferBeginInfo(StructureType.CommandBufferBeginInfo)
        {
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        VulkanDevice.ThrowIfFailed(api.BeginCommandBuffer(cmd, in beginInfo), "vkBeginCommandBuffer");

        // Begin render pass
        var clearValue = new ClearValue(new ClearColorValue(
            _clearColor.R / 255f, _clearColor.G / 255f, _clearColor.B / 255f, _clearColor.A / 255f));

        var rpBegin = new RenderPassBeginInfo(StructureType.RenderPassBeginInfo)
        {
            RenderPass = _swapchain.RenderPass,
            Framebuffer = _swapchain.CurrentFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), _swapchain.Extent),
            ClearValueCount = 1,
            PClearValues = &clearValue
        };
        api.CmdBeginRenderPass(cmd, in rpBegin, SubpassContents.Inline);

        // Set dynamic viewport
        var viewport = new Viewport(0, 0, _swapchain.Extent.Width, _swapchain.Extent.Height, 0, 1);
        api.CmdSetViewport(cmd, 0, 1, in viewport);

        // Render batched geometry
        _batchRenderer.Render(cmd, _atlas, _swapchain.Extent);

        api.CmdEndRenderPass(cmd);

        // Copy the presented frame into the host-visible readback buffer so DevTools can
        // capture GPU-accurate screenshots. The image is in PresentSrcKhr layout here
        // (render pass FinalLayout); RecordCopy transitions it and restores it for present.
        if (_readbackEnabled)
            _readback.RecordCopy(cmd, _swapchain.CurrentImage);

        VulkanDevice.ThrowIfFailed(api.EndCommandBuffer(cmd), "vkEndCommandBuffer");

        // Submit
        var waitSemaphore = _swapchain.CurrentImageAvailableSemaphore;
        var signalSemaphore = _swapchain.CurrentRenderFinishedSemaphore;
        var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;

        var submitInfo = new SubmitInfo(StructureType.SubmitInfo)
        {
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore
        };

        var fence = _swapchain.CurrentInFlightFence;
        // The fence is reset here (not in AcquireNextImage) so that a skipped frame never
        // leaves it reset-but-unsignalled, which would deadlock the next WaitForFences.
        VulkanDevice.ThrowIfFailed(api.ResetFences(_device.Device, 1, in fence), "vkResetFences");
        VulkanDevice.ThrowIfFailed(api.QueueSubmit(_device.GraphicsQueue, 1, in submitInfo, fence), "vkQueueSubmit");

        // Present
        _swapchain.Present();

        // Present waits for the render-finished semaphore and then idles the present queue,
        // so the submitted command buffer is no longer pending here.
        api.FreeCommandBuffers(_device.Device, _device.CommandPool, 1, in cmd);
    }

    private CommandBuffer AllocateCommandBuffer()
    {
        var allocInfo = new CommandBufferAllocateInfo(StructureType.CommandBufferAllocateInfo)
        {
            CommandPool = _device.CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };
        VulkanDevice.ThrowIfFailed(_device.Api.AllocateCommandBuffers(_device.Device, in allocInfo, out var cmd), "vkAllocateCommandBuffers");
        return cmd;
    }

    // ─── Transform ────────────────────────────────────────────────────────

    public void PushTransform(Matrix3x2 matrix)
    {
        if (!EnsureFrame()) return;
        _transformStack.Push(_currentTransform);
        _currentTransform = matrix * _currentTransform;
    }

    public void PopTransform()
    {
        if (!EnsureFrame()) return;
        if (_transformStack.Count > 0)
            _currentTransform = _transformStack.Pop();
    }

    // ─── Clip ─────────────────────────────────────────────────────────────

    public void PushClip(Rect rect)
    {
        if (!EnsureFrame()) return;
        _clipStack.Push(_currentClip);
        _currentClip = IntersectRects(_currentClip, TransformRect(rect));
    }

    public void PushClip(Geometry geometry)
    {
        if (!EnsureFrame()) return;
        _clipStack.Push(_currentClip);
        var bounds = geometry switch
        {
            RectGeometry r => r.Rect,
            RoundedRectGeometry rr => rr.Rect,
            EllipseGeometry e => new Rect(e.Center.X - e.RadiusX, e.Center.Y - e.RadiusY, e.RadiusX * 2, e.RadiusY * 2),
            PathGeometry p => GetPathBounds(p),
            _ => _currentClip
        };
        _currentClip = IntersectRects(_currentClip, TransformRect(bounds));
    }

    public void PopClip()
    {
        if (!EnsureFrame()) return;
        if (_clipStack.Count > 0)
            _currentClip = _clipStack.Pop();
    }

    // ─── Drawing operations ───────────────────────────────────────────────

    public void FillRect(Rect rect, Brush brush)
    {
        if (!EnsureFrame()) return;
        if (rect.IsEmpty) return;
        var color = ResolveBrushColor(brush, rect.Center);
        FillRectColor(rect, color);
    }

    private void FillRectColor(Rect rect, Color color)
    {
        if (rect.IsEmpty) return;
        var (u0, v0, u1, v1) = VulkanTextureAtlas.WhitePixelUV;

        var tl = TransformPoint(new Point(rect.X, rect.Y));
        var tr = TransformPoint(new Point(rect.Right, rect.Y));
        var br = TransformPoint(new Point(rect.Right, rect.Bottom));
        var bl = TransformPoint(new Point(rect.X, rect.Bottom));

        var packed = PackColor(color);
        Span<Vertex2D> vertices =
        [
            new(tl.X, tl.Y, u0, v0, packed),
            new(tr.X, tr.Y, u1, v0, packed),
            new(br.X, br.Y, u1, v1, packed),
            new(bl.X, bl.Y, u0, v1, packed)
        ];
        ReadOnlySpan<uint> indices = [0, 1, 2, 0, 2, 3];
        AddBatch(vertices, indices);
    }

    public void DrawRect(Rect rect, Pen pen)
    {
        if (!EnsureFrame()) return;
        if (rect.IsEmpty || pen.Width <= 0) return;
        DrawPath(PathGeometry.Create()
            .MoveTo(new Point(rect.X, rect.Y))
            .LineTo(new Point(rect.Right, rect.Y))
            .LineTo(new Point(rect.Right, rect.Bottom))
            .LineTo(new Point(rect.X, rect.Bottom))
            .Close(), pen);
    }

    public void FillPath(PathGeometry path, Brush brush)
    {
        if (!EnsureFrame()) return;
        var contours = FlattenPath(path);
        if (contours.Count == 0) return;

        var tess = Triangulate(contours);
        var triangleVertexCount = tess.ElementCount * 3;
        if (triangleVertexCount == 0) return;

        var bounds = GetPathBounds(path);
        var color = ResolveBrushColor(brush, bounds.Center);
        var packed = PackColor(color);
        var (u0, v0, u1, v1) = VulkanTextureAtlas.WhitePixelUV;

        var vertices = ArrayPool<Vertex2D>.Shared.Rent(triangleVertexCount);
        var indices = ArrayPool<uint>.Shared.Rent(triangleVertexCount);
        try
        {
            for (var i = 0; i < triangleVertexCount; i++)
            {
                var vertex = tess.Vertices[tess.Elements[i]].Position;
                var p = TransformPoint(new Point(vertex.X, vertex.Y));
                vertices[i] = new Vertex2D(p.X, p.Y, u0, v0, packed);
                indices[i] = (uint)i;
            }
            AddBatch(vertices.AsSpan(0, triangleVertexCount), indices.AsSpan(0, triangleVertexCount));
        }
        finally
        {
            ArrayPool<Vertex2D>.Shared.Return(vertices);
            ArrayPool<uint>.Shared.Return(indices);
        }
    }

    public void DrawPath(PathGeometry path, Pen pen)
    {
        if (!EnsureFrame()) return;
        if (pen.Width <= 0) return;
        var contours = FlattenPath(path);
        if (contours.Count == 0) return;

        var color = ResolveBrushColor(pen.Brush, GetPathBounds(path).Center);
        var packed = PackColor(color);
        var (u0, v0, u1, v1) = VulkanTextureAtlas.WhitePixelUV;

        _scratchVertices.Clear();
        _scratchIndices.Clear();

        foreach (var contour in contours)
        {
            if (contour.Count < 2) continue;
            VulkanStrokeTessellator.Append(contour, pen.Width / 2f, GetLogicalFeatherWidth(), pen.StrokeStyle,
                packed, u0, v0, u1, v1, TransformPoint, _scratchVertices, _scratchIndices);
        }

        if (_scratchVertices.Count > 0)
            AddBatch(CollectionsMarshal.AsSpan(_scratchVertices), CollectionsMarshal.AsSpan(_scratchIndices));
    }

    public void FillGeometry(Geometry geometry, Brush brush)
    {
        if (!EnsureFrame()) return;
        switch (geometry)
        {
            case RectGeometry rect:
                FillRect(rect.Rect, brush);
                break;
            case RoundedRectGeometry rounded:
                FillRoundedRect(rounded.Rect, rounded.RadiusX, rounded.RadiusY, brush);
                break;
            case EllipseGeometry ellipse:
                FillEllipse(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, brush);
                break;
            case PathGeometry path:
                FillPath(path, brush);
                break;
        }
    }

    public void DrawGeometry(Geometry geometry, Pen pen)
    {
        if (!EnsureFrame()) return;
        switch (geometry)
        {
            case RectGeometry rect:
                DrawRect(rect.Rect, pen);
                break;
            case RoundedRectGeometry rounded:
                DrawRoundedRect(rounded.Rect, rounded.RadiusX, rounded.RadiusY, pen);
                break;
            case EllipseGeometry ellipse:
                DrawEllipse(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, pen);
                break;
            case PathGeometry path:
                DrawPath(path, pen);
                break;
        }
    }

    public void DrawText(TextLayout text, Point origin, Brush brush)
    {
        if (!EnsureFrame()) return;
        if (string.IsNullOrEmpty(text.Text)) return;
        var color = brush is SolidColorBrush solid ? solid.Color : Color.Black;
        var packed = PackColor(color);

        if (IsDpiOnlyTransform())
        {
            DrawPixelAlignedText(text, origin, packed);
            foreach (var rect in text.GetDecorationRects(origin))
                FillRectColor(rect, color);
            return;
        }

        var lineHeight = TextMetrics.GetLineHeight(text.Font, text.LineHeight);
        var baselineOffset = TextMetrics.GetBaselineOffset(text.Font, lineHeight);
        var lines = TextWrapping.Wrap(text.Text, text.MaxSize.Width, (offset, rune) =>
            TextLayout.MeasureRuneAdvance(rune, text.Font), text.WrappingOptions);

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var indent = text.GetLineIndent(lineIndex);
            var x = origin.X + indent + GetTextAlignmentOffset(text, line.Width + indent);
            var y = origin.Y + lineIndex * lineHeight + baselineOffset;
            foreach (var visualRune in text.EnumerateVisualRunes(line))
            {
                var rune = visualRune.Glyph;
                var advance = visualRune.Advance;
                if (!rune.IsBmp) { x += advance; continue; }

                var glyph = GetOrRasterizeGlyph(text.Font, (char)rune.Value);
                if (glyph is not { } resolvedGlyph) { x += advance; continue; }

                if (resolvedGlyph.AtlasW > 0 && resolvedGlyph.AtlasH > 0)
                {
                    var gx = x + resolvedGlyph.OffsetX;
                    var gy = y + resolvedGlyph.OffsetY;
                    var (u0, v0, u1, v1) = _atlas.GetUV(resolvedGlyph.AtlasX, resolvedGlyph.AtlasY, resolvedGlyph.AtlasW, resolvedGlyph.AtlasH);

                    var tl = TransformPoint(new Point(gx, gy));
                    var tr = TransformPoint(new Point(gx + resolvedGlyph.DrawWidth, gy));
                    var br = TransformPoint(new Point(gx + resolvedGlyph.DrawWidth, gy + resolvedGlyph.DrawHeight));
                    var bl = TransformPoint(new Point(gx, gy + resolvedGlyph.DrawHeight));

                    Span<Vertex2D> verts =
                    [
                        new(tl.X, tl.Y, u0, v0, packed),
                        new(tr.X, tr.Y, u1, v0, packed),
                        new(br.X, br.Y, u1, v1, packed),
                        new(bl.X, bl.Y, u0, v1, packed)
                    ];
                    ReadOnlySpan<uint> idx = [0, 1, 2, 0, 2, 3];
                    AddBatch(verts, idx);
                }
                x += advance;
            }
        }

        foreach (var rect in text.GetDecorationRects(origin))
            FillRectColor(rect, color);
    }

    public void DrawImage(Image image, Rect dest, Rect? source = null)
    {
        if (!EnsureFrame()) return;
        if (image is not Bitmap bitmap || bitmap.IsDisposed) return;

        // Cache the atlas region per bitmap so re-rendering the same image (e.g. on every
        // caret-blink full-frame redraw) reuses one allocation instead of leaking a fresh
        // region each frame until the atlas fills up and throws. Atlas allocation is
        // append-only, so a cached region stays valid for the atlas lifetime.
        if (!_imageCache.TryGetValue(bitmap, out var cached))
        {
            var (ax, ay) = _atlas.Allocate(bitmap.Width, bitmap.Height);

            _atlas.WriteBgraRegion(ax, ay, bitmap.Width, bitmap.Height, bitmap.Pixels);
            cached = new CachedImage
            {
                AtlasX = ax,
                AtlasY = ay,
                Width = bitmap.Width,
                Height = bitmap.Height,
                ContentVersion = bitmap.ContentVersion
            };
            _imageCache.Add(bitmap, cached);
        }
        else if (cached.ContentVersion != bitmap.ContentVersion)
        {
            _atlas.WriteBgraRegion(cached.AtlasX, cached.AtlasY, cached.Width, cached.Height, bitmap.Pixels);
            cached.ContentVersion = bitmap.ContentVersion;
        }

        var (u0, v0, u1, v1) = _atlas.GetUV(cached.AtlasX, cached.AtlasY, cached.Width, cached.Height);
        // Adjust UVs for source rect
        if (source.HasValue)
        {
            var su0 = source.Value.X / cached.Width;
            var sv0 = source.Value.Y / cached.Height;
            var su1 = source.Value.Right / cached.Width;
            var sv1 = source.Value.Bottom / cached.Height;
            u0 = (cached.AtlasX + su0 * cached.Width) / (float)VulkanTextureAtlas.AtlasWidth;
            v0 = (cached.AtlasY + sv0 * cached.Height) / (float)VulkanTextureAtlas.AtlasHeight;
            u1 = (cached.AtlasX + su1 * cached.Width) / (float)VulkanTextureAtlas.AtlasWidth;
            v1 = (cached.AtlasY + sv1 * cached.Height) / (float)VulkanTextureAtlas.AtlasHeight;
        }

        var white = 0xFFFFFFFFu;
        var tl = TransformPoint(new Point(dest.X, dest.Y));
        var tr = TransformPoint(new Point(dest.Right, dest.Y));
        var br = TransformPoint(new Point(dest.Right, dest.Bottom));
        var bl = TransformPoint(new Point(dest.X, dest.Bottom));

        Span<Vertex2D> verts =
        [
            new(tl.X, tl.Y, u0, v0, white),
            new(tr.X, tr.Y, u1, v0, white),
            new(br.X, br.Y, u1, v1, white),
            new(bl.X, bl.Y, u0, v1, white)
        ];
        ReadOnlySpan<uint> idx = [0, 1, 2, 0, 2, 3];
        AddBatch(verts, idx);
    }

    // ─── Layer / Opacity ──────────────────────────────────────────────────

    public void PushLayer(Rect bounds, float opacity)
    {
        if (!EnsureFrame()) return;
        _opacityStack.Push(_currentOpacity);
        _currentOpacity *= Math.Clamp(opacity, 0, 1);
    }

    public void PopLayer()
    {
        if (!EnsureFrame()) return;
        if (_opacityStack.Count > 0)
            _currentOpacity = _opacityStack.Pop();
    }

    // ─── Resize ───────────────────────────────────────────────────────────

    public void Resize(Size canvasSize) => Resize(canvasSize, DpiScale);

    public void Resize(Size canvasSize, float dpiScale)
    {
        if (_frameStarted)
        {
            SubmitFrame();
            _frameStarted = false;
        }

        DpiScale = NormalizeDpi(dpiScale);
        CanvasSize = canvasSize;
        if (canvasSize.Width <= 0 || canvasSize.Height <= 0)
        {
            _minimized = true;
            return;
        }
        var w = ToPhysical(canvasSize.Width, DpiScale);
        var h = ToPhysical(canvasSize.Height, DpiScale);
        if (w < 1 || h < 1)
        {
            // Degenerate size (window minimized/collapsed): skip swapchain recreation
            // and pause rendering until a valid size arrives.
            _minimized = true;
            return;
        }
        _minimized = false;
        var sampleCountChanged = _device.ConfigureColorSampleCount(w, h);
        _swapchain.Recreate(w, h);
        if (sampleCountChanged)
            _pipeline.RecreateGraphicsPipeline();
        _pipeline.UpdateProjection(_swapchain.Extent.Width, _swapchain.Extent.Height);
        if (_readbackEnabled)
            _readback.EnsureSize(_swapchain.Extent.Width, _swapchain.Extent.Height);
        _transformStack.Clear();
        _clipStack.Clear();
        _opacityStack.Clear();
        _currentTransform = Matrix3x2.CreateScale(DpiScale);
        _currentOpacity = 1f;
        _currentClip = new Rect(0, 0, _swapchain.Extent.Width, _swapchain.Extent.Height);
        _frameStarted = false;
    }

    // ─── Capture (IRenderBitmapSource) ───────────────────────────────────

    /// <summary>
    /// Returns the most recently presented frame as read back from the GPU.
    /// Unlike a software re-render, this reflects the actual Vulkan output.
    /// </summary>
    public bool IsCaptureAvailable => _readbackEnabled;

    public Bitmap CaptureBitmap()
    {
        if (!_readbackEnabled)
            throw new VulkanException("GPU readback is disabled. Set SQUARE_VULKAN_READBACK=1 before starting the application to enable it.");
        return _readback.CaptureBitmap();
    }

    private void DrawPixelAlignedText(TextLayout text, Point origin, uint packed)
    {
        var physicalOrigin = TransformPoint(origin);
        var lineHeight = TextMetrics.GetLineHeight(text.Font, text.LineHeight) * DpiScale;
        var physicalFont = text.Font.WithSize(text.Font.Size * DpiScale);
        var baselineOffset = TextMetrics.GetBaselineOffset(physicalFont, lineHeight);
        var lines = TextWrapping.Wrap(text.Text, text.MaxSize.Width * DpiScale, (offset, rune) =>
            TextLayout.MeasureRuneAdvance(rune, text.Font) * DpiScale, text.WrappingOptions);

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var indent = text.GetLineIndent(lineIndex);
            var x = physicalOrigin.X + (indent + GetTextAlignmentOffset(text, line.Width / DpiScale + indent)) * DpiScale;
            var y = physicalOrigin.Y + lineIndex * lineHeight + baselineOffset;
            foreach (var visualRune in text.EnumerateVisualRunes(line))
            {
                var rune = visualRune.Glyph;
                var advance = visualRune.Advance * DpiScale;
                if (!rune.IsBmp) { x += advance; continue; }

                var glyph = GetOrRasterizeGlyph(text.Font, (char)rune.Value);
                if (glyph is not { } resolvedGlyph)
                {
                    x += advance;
                    continue;
                }

                if (resolvedGlyph.AtlasW > 0 && resolvedGlyph.AtlasH > 0)
                {
                    var glyphX = MathF.Round(x);
                    var glyphY = MathF.Round(y);
                    var left = glyphX + resolvedGlyph.PhysicalOffsetX - resolvedGlyph.FilterBorder;
                    var top = glyphY + resolvedGlyph.PhysicalOffsetY - resolvedGlyph.FilterBorder;
                    var right = left + resolvedGlyph.AtlasW;
                    var bottom = top + resolvedGlyph.AtlasH;
                    var (u0, v0, u1, v1) = _atlas.GetUV(
                        resolvedGlyph.AtlasX, resolvedGlyph.AtlasY, resolvedGlyph.AtlasW, resolvedGlyph.AtlasH);

                    Span<Vertex2D> vertices =
                    [
                        new(left, top, u0, v0, packed),
                        new(right, top, u1, v0, packed),
                        new(right, bottom, u1, v1, packed),
                        new(left, bottom, u0, v1, packed)
                    ];
                    ReadOnlySpan<uint> indices = [0, 1, 2, 0, 2, 3];
                    AddBatch(vertices, indices);
                }
                x += advance;
            }
        }

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

    private bool IsDpiOnlyTransform()
    {
        const float tolerance = 0.0001f;
        return MathF.Abs(_currentTransform.M11 - DpiScale) < tolerance &&
               MathF.Abs(_currentTransform.M22 - DpiScale) < tolerance &&
               MathF.Abs(_currentTransform.M12) < tolerance &&
               MathF.Abs(_currentTransform.M21) < tolerance;
    }

    // ─── Dispose ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _device.Api.DeviceWaitIdle(_device.Device);
        _readback.Dispose();
        _batchRenderer.Dispose();
        _atlas.Dispose();
        _pipeline.Dispose();
        _swapchain.Dispose();
        _device.Dispose();
    }

    // ─── Shape helpers ────────────────────────────────────────────────────

    private void FillRoundedRect(Rect rect, float rx, float ry, Brush brush)
    {
        rx = Math.Min(rx, rect.Width / 2);
        ry = Math.Min(ry, rect.Height / 2);
        if (rx <= 0 || ry <= 0) { FillRect(rect, brush); return; }
        var segments = GetCurveSegmentCount(rx, ry, MathF.PI / 2);
        var perimeterCount = segments * 4 + 4;
        var vertices = ArrayPool<Vertex2D>.Shared.Rent(perimeterCount + 1);
        var indices = ArrayPool<uint>.Shared.Rent(perimeterCount * 3);
        try
        {
            var packed = PackColor(ResolveBrushColor(brush, rect.Center));
            var (u0, v0, _, _) = VulkanTextureAtlas.WhitePixelUV;
            var center = TransformPoint(rect.Center);
            vertices[0] = new Vertex2D(center.X, center.Y, u0, v0, packed);

            var vertexCount = 1;
            AppendRoundedRectArc(vertices, ref vertexCount, rect.Right - rx, rect.Y + ry, rx, ry,
                -MathF.PI / 2, segments, includeStart: true, u0, v0, packed);
            AppendRoundedRectArc(vertices, ref vertexCount, rect.Right - rx, rect.Bottom - ry, rx, ry,
                0, segments, includeStart: false, u0, v0, packed);
            AppendRoundedRectArc(vertices, ref vertexCount, rect.X + rx, rect.Bottom - ry, rx, ry,
                MathF.PI / 2, segments, includeStart: false, u0, v0, packed);
            AppendRoundedRectArc(vertices, ref vertexCount, rect.X + rx, rect.Y + ry, rx, ry,
                MathF.PI, segments, includeStart: false, u0, v0, packed);

            var indexCount = 0;
            var actualPerimeterCount = vertexCount - 1;
            for (var i = 0; i < actualPerimeterCount; i++)
            {
                indices[indexCount++] = 0;
                indices[indexCount++] = (uint)(i + 1);
                indices[indexCount++] = (uint)(i + 1 == actualPerimeterCount ? 1 : i + 2);
            }
            AddBatch(vertices.AsSpan(0, vertexCount), indices.AsSpan(0, indexCount));
        }
        finally
        {
            ArrayPool<Vertex2D>.Shared.Return(vertices);
            ArrayPool<uint>.Shared.Return(indices);
        }
    }

    private void AppendRoundedRectArc(Vertex2D[] vertices, ref int vertexCount,
        float cx, float cy, float rx, float ry, float startAngle, int segments, bool includeStart,
        float u, float v, uint color)
    {
        for (var i = includeStart ? 0 : 1; i <= segments; i++)
        {
            var angle = startAngle + MathF.PI / 2 * i / segments;
            var point = TransformPoint(new Point(cx + rx * MathF.Cos(angle), cy + ry * MathF.Sin(angle)));
            vertices[vertexCount++] = new Vertex2D(point.X, point.Y, u, v, color);
        }
    }

    private void DrawRoundedRect(Rect rect, float rx, float ry, Pen pen)
    {
        rx = Math.Min(rx, rect.Width / 2);
        ry = Math.Min(ry, rect.Height / 2);
        if (rx <= 0 || ry <= 0) { DrawRect(rect, pen); return; }

        var path = CreateRoundedRectPath(rect, rx, ry);
        DrawPath(path, pen);
    }

    private void FillEllipse(Point center, float radiusX, float radiusY, Brush brush)
    {
        var segments = GetCurveSegmentCount(radiusX, radiusY, MathF.Tau);
        var color = ResolveBrushColor(brush, center);
        var packed = PackColor(color);
        var transparent = packed & 0x00FFFFFFu;
        var (u0, v0, u1, v1) = VulkanTextureAtlas.WhitePixelUV;
        var feather = GetLogicalFeatherWidth();
        var innerRadiusX = Math.Max(0, radiusX - feather / 2f);
        var innerRadiusY = Math.Max(0, radiusY - feather / 2f);
        var outerRadiusX = radiusX + feather / 2f;
        var outerRadiusY = radiusY + feather / 2f;

        var vertices = ArrayPool<Vertex2D>.Shared.Rent(segments * 2 + 1);
        var indices = ArrayPool<uint>.Shared.Rent(segments * 9);
        try
        {
            var c = TransformPoint(center);
            vertices[0] = new Vertex2D(c.X, c.Y, u0, v0, packed);

            for (var i = 0; i < segments; i++)
            {
                var angle = i * MathF.Tau / segments;
                AddEllipseRingVertex(vertices, i + 1, center, angle, innerRadiusX, innerRadiusY, u0, v0, packed);
                AddEllipseRingVertex(vertices, segments + i + 1, center, angle, outerRadiusX, outerRadiusY, u0, v0, transparent);
            }

            var indexCount = 0;
            for (var i = 0; i < segments; i++)
            {
                var next = i + 1 == segments ? 0 : i + 1;
                var inner0 = (uint)(i + 1);
                var inner1 = (uint)(next + 1);
                var outer0 = (uint)(segments + i + 1);
                var outer1 = (uint)(segments + next + 1);
                indices[indexCount++] = 0;
                indices[indexCount++] = inner0;
                indices[indexCount++] = inner1;
                AddQuadIndices(indices, ref indexCount, inner0, outer0, outer1, inner1);
            }
            AddBatch(vertices.AsSpan(0, segments * 2 + 1), indices.AsSpan(0, indexCount));
        }
        finally
        {
            ArrayPool<Vertex2D>.Shared.Return(vertices);
            ArrayPool<uint>.Shared.Return(indices);
        }
    }

    private void DrawEllipse(Point center, float radiusX, float radiusY, Pen pen)
    {
        var segments = GetCurveSegmentCount(radiusX + pen.Width / 2f, radiusY + pen.Width / 2f, MathF.Tau);
        var color = ResolveBrushColor(pen.Brush, center);
        var packed = PackColor(color);
        var transparent = packed & 0x00FFFFFFu;
        var (u0, v0, u1, v1) = VulkanTextureAtlas.WhitePixelUV;
        var halfW = pen.Width / 2f;
        var feather = GetLogicalFeatherWidth();
        var outerTransparentX = radiusX + halfW + feather / 2f;
        var outerTransparentY = radiusY + halfW + feather / 2f;
        var outerSolidX = Math.Max(0, radiusX + halfW - feather / 2f);
        var outerSolidY = Math.Max(0, radiusY + halfW - feather / 2f);
        var innerSolidX = Math.Max(0, radiusX - halfW + feather / 2f);
        var innerSolidY = Math.Max(0, radiusY - halfW + feather / 2f);
        var innerTransparentX = Math.Max(0, radiusX - halfW - feather / 2f);
        var innerTransparentY = Math.Max(0, radiusY - halfW - feather / 2f);

        var vertices = ArrayPool<Vertex2D>.Shared.Rent(segments * 8);
        var indices = ArrayPool<uint>.Shared.Rent(segments * 18);
        var vertexCount = 0;
        var indexCount = 0;
        try
        {
            for (var i = 0; i < segments; i++)
            {
                var a0 = i * MathF.Tau / segments;
                var a1 = (i + 1) * MathF.Tau / segments;
                var baseIdx = (uint)vertexCount;

                AddEllipseRingVertex(vertices, ref vertexCount, center, a0, outerTransparentX, outerTransparentY, u0, v0, transparent);
                AddEllipseRingVertex(vertices, ref vertexCount, center, a0, outerSolidX, outerSolidY, u0, v0, packed);
                AddEllipseRingVertex(vertices, ref vertexCount, center, a0, innerSolidX, innerSolidY, u0, v0, packed);
                AddEllipseRingVertex(vertices, ref vertexCount, center, a0, innerTransparentX, innerTransparentY, u0, v0, transparent);
                AddEllipseRingVertex(vertices, ref vertexCount, center, a1, outerTransparentX, outerTransparentY, u1, v1, transparent);
                AddEllipseRingVertex(vertices, ref vertexCount, center, a1, outerSolidX, outerSolidY, u1, v1, packed);
                AddEllipseRingVertex(vertices, ref vertexCount, center, a1, innerSolidX, innerSolidY, u1, v1, packed);
                AddEllipseRingVertex(vertices, ref vertexCount, center, a1, innerTransparentX, innerTransparentY, u1, v1, transparent);

                AddQuadIndices(indices, ref indexCount, baseIdx, baseIdx + 4, baseIdx + 5, baseIdx + 1);
                AddQuadIndices(indices, ref indexCount, baseIdx + 1, baseIdx + 5, baseIdx + 6, baseIdx + 2);
                AddQuadIndices(indices, ref indexCount, baseIdx + 2, baseIdx + 6, baseIdx + 7, baseIdx + 3);
            }
            AddBatch(vertices.AsSpan(0, vertexCount), indices.AsSpan(0, indexCount));
        }
        finally
        {
            ArrayPool<Vertex2D>.Shared.Return(vertices);
            ArrayPool<uint>.Shared.Return(indices);
        }
    }

    private void AddEllipseRingVertex(Vertex2D[] vertices, ref int count, Point center, float angle,
        float radiusX, float radiusY, float u, float v, uint color)
    {
        var point = TransformPoint(new Point(
            center.X + radiusX * MathF.Cos(angle),
            center.Y + radiusY * MathF.Sin(angle)));
        vertices[count++] = new Vertex2D(point.X, point.Y, u, v, color);
    }

    private void AddEllipseRingVertex(Vertex2D[] vertices, int index, Point center, float angle,
        float radiusX, float radiusY, float u, float v, uint color)
    {
        var point = TransformPoint(new Point(
            center.X + radiusX * MathF.Cos(angle),
            center.Y + radiusY * MathF.Sin(angle)));
        vertices[index] = new Vertex2D(point.X, point.Y, u, v, color);
    }

    private static void AddQuadIndices(uint[] indices, ref int count, uint a, uint b, uint c, uint d)
    {
        indices[count++] = a; indices[count++] = b; indices[count++] = c;
        indices[count++] = a; indices[count++] = c; indices[count++] = d;
    }

    private PathGeometry CreateRoundedRectPath(Rect rect, float rx, float ry)
    {
        // Approximate arcs with line segments
        var path = PathGeometry.Create();
        var arcSegments = GetCurveSegmentCount(rx, ry, MathF.PI / 2);

        var l = rect.X; var t = rect.Y; var r = rect.Right; var b = rect.Bottom;

        path.MoveTo(new Point(l + rx, t));
        path.LineTo(new Point(r - rx, t));
        AddArc(path, r - rx, t + ry, rx, ry, -MathF.PI / 2, MathF.PI / 2, arcSegments);
        path.LineTo(new Point(r, b - ry));
        AddArc(path, r - rx, b - ry, rx, ry, 0, MathF.PI / 2, arcSegments);
        path.LineTo(new Point(l + rx, b));
        AddArc(path, l + rx, b - ry, rx, ry, MathF.PI / 2, MathF.PI / 2, arcSegments);
        path.LineTo(new Point(l, t + ry));
        AddArc(path, l + rx, t + ry, rx, ry, MathF.PI, MathF.PI / 2, arcSegments);
        path.Close();
        return path;
    }

    private static void AddArc(PathGeometry path, float cx, float cy, float rx, float ry, float startAngle, float sweep, int segments)
    {
        for (var i = 1; i <= segments; i++)
        {
            var angle = startAngle + sweep * i / segments;
            path.LineTo(new Point(cx + rx * MathF.Cos(angle), cy + ry * MathF.Sin(angle)));
        }
    }

    // ─── Triangulation ────────────────────────────────────────────────────

    private List<List<Point>> FlattenPath(PathGeometry path)
    {
        var contours = new List<List<Point>>();
        var current = new List<Point>();
        Point first = default;

        foreach (var cmd in path.Commands)
        {
            switch (cmd)
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
                    FlattenArc(current, arc);
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

    private void FlattenArc(List<Point> contour, ArcToCmd arc)
    {
        var cx = arc.Oval.X + arc.Oval.Width / 2;
        var cy = arc.Oval.Y + arc.Oval.Height / 2;
        var rx = arc.Oval.Width / 2;
        var ry = arc.Oval.Height / 2;
        var startRad = arc.StartAngle * MathF.PI / 180f;
        var sweepRad = arc.SweepAngle * MathF.PI / 180f;
        var segments = GetCurveSegmentCount(rx, ry, MathF.Abs(sweepRad));

        for (var i = 1; i <= segments; i++)
        {
            var angle = startRad + sweepRad * i / segments;
            contour.Add(new Point(cx + rx * MathF.Cos(angle), cy + ry * MathF.Sin(angle)));
        }
    }

    private int GetCurveSegmentCount(float radiusX, float radiusY, float sweepRadians)
    {
        const float maxSagitta = 0.2f;
        const int minFullCircleSegments = 32;
        const int maxFullCircleSegments = 256;

        var xScale = MathF.Sqrt(
            _currentTransform.M11 * _currentTransform.M11 +
            _currentTransform.M12 * _currentTransform.M12);
        var yScale = MathF.Sqrt(
            _currentTransform.M21 * _currentTransform.M21 +
            _currentTransform.M22 * _currentTransform.M22);
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

    private float GetLogicalFeatherWidth()
    {
        var xScale = MathF.Sqrt(
            _currentTransform.M11 * _currentTransform.M11 +
            _currentTransform.M12 * _currentTransform.M12);
        var yScale = MathF.Sqrt(
            _currentTransform.M21 * _currentTransform.M21 +
            _currentTransform.M22 * _currentTransform.M22);
        return 1f / MathF.Max(0.001f, MathF.Max(xScale, yScale));
    }

    private static LibTessDotNet.Tess Triangulate(List<List<Point>> contours)
    {
        // Use LibTessDotNet for polygon triangulation
        var tess = new LibTessDotNet.Tess();

        foreach (var contour in contours)
        {
            if (contour.Count < 3) continue;
            var points = new LibTessDotNet.ContourVertex[contour.Count];
            for (var i = 0; i < contour.Count; i++)
                points[i] = new LibTessDotNet.ContourVertex
                {
                    Position = new LibTessDotNet.Vec3 { X = contour[i].X, Y = contour[i].Y, Z = 0 }
                };
            tess.AddContour(points, LibTessDotNet.ContourOrientation.Original);
        }

        tess.Tessellate(LibTessDotNet.WindingRule.EvenOdd, LibTessDotNet.ElementType.Polygons, 3);

        return tess;
    }

    // ─── Glyph cache ──────────────────────────────────────────────────────

    private readonly record struct GlyphCacheKey(string Family, float Size, int Weight, int Style, char Char);

    private struct CachedGlyph
    {
        public int AtlasX, AtlasY, AtlasW, AtlasH;
        public int PhysicalOffsetX, PhysicalOffsetY, FilterBorder;
        public float OffsetX, OffsetY, DrawWidth, DrawHeight;
    }

    private sealed class CachedImage
    {
        public int AtlasX, AtlasY, Width, Height;
        public long ContentVersion;
    }

    private CachedGlyph? GetOrRasterizeGlyph(Font font, char ch)
    {
        var physicalSize = font.Size * DpiScale;
        var key = new GlyphCacheKey(font.Family, physicalSize, (int)font.Weight, (int)font.Style, ch);
        if (_glyphCache.TryGetValue(key, out var cached)) return cached;

        var rasterized = _glyphRasterizer.Rasterize(font.WithSize(physicalSize), ch);
        if (rasterized == null) return null;

        var glyph = new CachedGlyph
        {
            PhysicalOffsetX = rasterized.OffsetX,
            PhysicalOffsetY = rasterized.OffsetY,
            OffsetX = rasterized.OffsetX / DpiScale,
            OffsetY = rasterized.OffsetY / DpiScale
        };

        if (rasterized.Width > 0 && rasterized.Height > 0)
        {
            const int filterBorder = 1;
            var atlasWidth = rasterized.Width + filterBorder * 2;
            var atlasHeight = rasterized.Height + filterBorder * 2;
            var (ax, ay) = _atlas.Allocate(atlasWidth, atlasHeight);
            _atlas.WritePaddedCoverageRegion(ax, ay, rasterized.Width, rasterized.Height,
                rasterized.Stride, rasterized.Coverage, filterBorder);
            glyph.AtlasX = ax;
            glyph.AtlasY = ay;
            glyph.AtlasW = atlasWidth;
            glyph.AtlasH = atlasHeight;
            glyph.FilterBorder = filterBorder;
            glyph.DrawWidth = atlasWidth / DpiScale;
            glyph.DrawHeight = atlasHeight / DpiScale;
            glyph.OffsetX -= filterBorder / DpiScale;
            glyph.OffsetY -= filterBorder / DpiScale;
        }

        _glyphCache[key] = glyph;
        return glyph;
    }

    // ─── Utility ──────────────────────────────────────────────────────────

    private void AddBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices)
    {
        var clip = _currentClip;
        if (clip.IsEmpty) return;

        var left = (int)MathF.Floor(clip.X);
        var top = (int)MathF.Floor(clip.Y);
        var right = (int)MathF.Ceiling(clip.Right);
        var bottom = (int)MathF.Ceiling(clip.Bottom);
        _batchRenderer.AddBatch(vertices, indices, 0,
            left, top, right - left, bottom - top);
    }

    private Point TransformPoint(Point p)
    {
        var x = p.X * _currentTransform.M11 + p.Y * _currentTransform.M21 + _currentTransform.M31;
        var y = p.X * _currentTransform.M12 + p.Y * _currentTransform.M22 + _currentTransform.M32;
        return new Point(x, y);
    }

    private Rect TransformRect(Rect rect)
    {
        var tl = TransformPoint(new Point(rect.X, rect.Y));
        var br = TransformPoint(new Point(rect.Right, rect.Bottom));
        var tr = TransformPoint(new Point(rect.Right, rect.Y));
        var bl = TransformPoint(new Point(rect.X, rect.Bottom));
        var minX = Math.Min(Math.Min(tl.X, tr.X), Math.Min(bl.X, br.X));
        var minY = Math.Min(Math.Min(tl.Y, tr.Y), Math.Min(bl.Y, br.Y));
        var maxX = Math.Max(Math.Max(tl.X, tr.X), Math.Max(bl.X, br.X));
        var maxY = Math.Max(Math.Max(tl.Y, tr.Y), Math.Max(bl.Y, br.Y));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static Rect IntersectRects(Rect a, Rect b)
    {
        var x = Math.Max(a.X, b.X);
        var y = Math.Max(a.Y, b.Y);
        var r = Math.Min(a.Right, b.Right);
        var bot = Math.Min(a.Bottom, b.Bottom);
        return new Rect(x, y, Math.Max(0, r - x), Math.Max(0, bot - y));
    }

    private Color ResolveBrushColor(Brush brush, Point at)
    {
        var color = brush switch
        {
            SolidColorBrush solid => solid.Color,
            LinearGradientBrush linear => SampleGradient(linear.Stops, linear.SpreadMethod,
                ProjectGradientOffset(at, linear.Start, linear.End)),
            RadialGradientBrush radial => SampleGradient(radial.Stops, radial.SpreadMethod,
                radial.Radius > 0 ? MathF.Sqrt(MathF.Pow(at.X - radial.Center.X, 2) + MathF.Pow(at.Y - radial.Center.Y, 2)) / radial.Radius : 0),
            _ => Color.Transparent
        };
        // Apply layer opacity
        if (_currentOpacity < 1f)
            color = new Color(color.R, color.G, color.B, (byte)(color.A * _currentOpacity));
        return color;
    }

    private static float ProjectGradientOffset(Point p, Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq <= float.Epsilon) return 0;
        return ((p.X - start.X) * dx + (p.Y - start.Y) * dy) / lenSq;
    }

    private static Color SampleGradient(GradientStop[] stops, GradientSpreadMethod spread, float offset)
    {
        if (stops.Length == 0) return Color.Transparent;
        offset = spread switch
        {
            GradientSpreadMethod.Repeat => offset - MathF.Floor(offset),
            GradientSpreadMethod.Reflect => ReflectOffset(offset),
            _ => Math.Clamp(offset, 0, 1)
        };
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
        var t = range <= float.Epsilon ? 0 : (offset - lower.Offset) / range;
        return LerpColor(lower.Color, upper.Color, t);
    }

    private static float ReflectOffset(float t)
    {
        t = Math.Abs(t % 2f);
        return t > 1f ? 2f - t : t;
    }

    private static Color LerpColor(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Color(
            (byte)MathF.Round(a.R + (b.R - a.R) * t),
            (byte)MathF.Round(a.G + (b.G - a.G) * t),
            (byte)MathF.Round(a.B + (b.B - a.B) * t),
            (byte)MathF.Round(a.A + (b.A - a.A) * t));
    }

    private uint PackColor(Color c)
    {
        // RGBA8 packed as uint (R in lowest byte for R8G8B8A8Unorm vertex attribute)
        return (uint)(c.R | (c.G << 8) | (c.B << 16) | (c.A << 24));
    }

    private static Rect GetPathBounds(PathGeometry path)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var cmd in path.Commands)
        {
            Point? p = cmd switch
            {
                MoveToCmd m => m.Point,
                LineToCmd l => l.Point,
                ArcToCmd a => a.Oval.Center,
                _ => null
            };
            if (p is null) continue;
            minX = Math.Min(minX, p.Value.X);
            minY = Math.Min(minY, p.Value.Y);
            maxX = Math.Max(maxX, p.Value.X);
            maxY = Math.Max(maxY, p.Value.Y);
        }
        if (minX > maxX) return Rect.Empty;
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static uint ToPhysical(float logical, float dpi) => (uint)Math.Max(1, MathF.Ceiling(logical * dpi));
    private static float NormalizeDpi(float dpi) => float.IsFinite(dpi) && dpi > 0 ? dpi : 1f;
}
