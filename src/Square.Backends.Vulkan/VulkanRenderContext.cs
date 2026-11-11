using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Vulkan;
using Square.Graphics;
using Square.Rendering.Tessellation;
using Square.Text.Glyph;
using Image = Square.Graphics.Image;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Square.Backends.Vulkan;

/// <summary>
/// Vulkan GPU implementation of IRenderContext using ImGui-style batched 2D rendering.
/// All draw calls are triangulated on CPU, batched by texture/scissor, and submitted in one draw.
/// </summary>
internal sealed unsafe class VulkanRenderContext : IRenderContext, IDpiResizableRenderContext, IRenderBitmapSource, IVulkanRenderContextInfo, IVulkanFrameDiagnostics, ISolidLeafOpacityRenderer
{
    private readonly VulkanDevice _device;
    private readonly VulkanSwapchain _swapchain;
    private readonly FrameResources[] _frames;
    private int _activeFrame;
    private ulong _swapchainGeneration;
    private ulong _submittedFrames;
    private ulong _completedFrames;
    private VulkanPipeline _pipeline => _frames[_activeFrame].Pipeline;
    private VulkanBatchRenderer _batchRenderer => _frames[_activeFrame].Batch;
    private readonly VulkanTextureAtlas _atlas;
    private VulkanReadbackBuffer _readback => _frames[_activeFrame].Readback;
    private readonly bool _readbackEnabled;
    private readonly SystemGlyphRasterizer _glyphRasterizer = new(cacheGlyphs: false);
    private readonly Dictionary<GlyphCacheKey, CachedGlyph> _glyphCache = [];
    private readonly ConditionalWeakTable<Bitmap, CachedImage> _imageCache = new();
    private readonly VulkanGeometryCache _geometryCache = new();
    private readonly VulkanClipCache _clipCache = new();
    private int _currentMaterial;

    private bool _frameStarted;
    private bool _disposed;

    // True while the window is minimized/collapsed to a degenerate (0x0) size.
    // Vulkan forbids 0-extent swapchains and 0-size buffers, so rendering is paused
    // until a valid size arrives instead of crashing in vkAllocateMemory.
    private bool _minimized;

    // Timestamp of the last attempt to rebuild a lost presentation surface.
    private long _lastPresentationRetry;

    // Transform stack
    private readonly Stack<Matrix3x2> _transformStack = new();
    private Matrix3x2 _currentTransform;

    // Clip/layer state is snapshotted at Push; transforms remain an independent stack.
    private readonly Stack<SavedState> _stateStack = new();
    private VulkanClipRegion _currentClip;

    public Size CanvasSize { get; private set; }
    public float DpiScale { get; private set; }
    public VulkanDeviceInfo DeviceInfo { get; }
    public uint ColorSampleCount => (uint)_device.ColorSampleCount;
    public bool ReadbackEnabled => _readbackEnabled;

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
        _frames = new FrameResources[_swapchain.FrameCount];
        try
        {
            for (var i = 0; i < _frames.Length; i++)
                _frames[i] = new FrameResources(_device, _swapchain, _readbackEnabled);
            _atlas = new VulkanTextureAtlas(_device, _frames[0].Pipeline);
        }
        catch
        {
            foreach (var frame in _frames) frame?.Dispose(_device, releasePipeline: true);
            _swapchain.Dispose();
            _device.Dispose();
            throw;
        }
        _swapchainGeneration = _swapchain.Generation;
        _minimized = _swapchain.Extent.Width < 1 || _swapchain.Extent.Height < 1;
        _currentClip = VulkanClipRegion.CreateCanvas(new Rect(0, 0, _swapchain.Extent.Width, _swapchain.Extent.Height));
        _device.Api.GetPhysicalDeviceProperties(_device.PhysicalDevice, out var properties);
        DeviceInfo = new VulkanDeviceInfo(
            Marshal.PtrToStringUTF8((nint)properties.DeviceName) ?? string.Empty,
            properties.DeviceType.ToString(), properties.ApiVersion, properties.DriverVersion);
    }

    // ─── Frame lifecycle ──────────────────────────────────────────────────

    private bool EnsureFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_frameStarted) return true;
        if (_minimized && !TryResumePresentation()) return false;
        if (!_swapchain.AcquireNextImage())
        {
            // Surface is unavailable (window minimized / swapchain out-of-date). Pause
            // rendering; Resize() clears _minimized once a valid size arrives.
            _minimized = true;
            return false;
        }
        if (_swapchain.Generation != _swapchainGeneration)
            RefreshSwapchainResources();
        _activeFrame = _swapchain.CurrentFrameIndex;
        var frame = _frames[_activeFrame];
        ObserveCompletion(_activeFrame, wait: false);
        frame.Serial = 0;
        VulkanDevice.ThrowIfFailed(_device.Api.ResetCommandBuffer(frame.Command, 0), "vkResetCommandBuffer");
        _batchRenderer.BeginFrame();
        _currentMaterial = 0;
        _frameStarted = true;
        return true;
    }

    /// <summary>
    /// Rebuilds presentation after a lost surface (occluded window, display mode change,
    /// driver reset). Retries are rate-limited so an unavailable surface does not spin,
    /// and rendering resumes on its own once a swapchain can be created again.
    /// </summary>
    private bool TryResumePresentation()
    {
        var now = Environment.TickCount64;
        if (now - _lastPresentationRetry < 250) return false;
        _lastPresentationRetry = now;
        var width = ToPhysical(CanvasSize.Width, DpiScale);
        var height = ToPhysical(CanvasSize.Height, DpiScale);
        if (width < 1 || height < 1) return false;
        var sampleCountChanged = _device.ConfigureColorSampleCount(width, height);
        _swapchain.Recreate(width, height);
        if (!_swapchain.IsAvailable) return false;
        RefreshSwapchainResources(sampleCountChanged);
        _transformStack.Clear();
        _stateStack.Clear();
        _currentTransform = Matrix3x2.CreateScale(DpiScale);
        _currentMaterial = 0;
        _minimized = false;
        return true;
    }

    public void Clear(Color color)
    {
        if (!EnsureFrame()) return;
        _batchRenderer.ClearCurrentTarget(color);
    }

    public void Clear(Color color, Rect rect)
    {
        if (!EnsureFrame()) return;
        _currentMaterial = 0;
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
        if (_stateStack.Any(static state => state.IsLayer))
            throw new InvalidOperationException("PopLayer must close every active layer before presenting.");
        // Draw calls can add glyphs to the atlas, so upload after the frame has been built.
        _atlas.Flush();

        var api = _device.Api;
        var frame = _frames[_activeFrame];
        var cmd = frame.Command;

        var beginInfo = new CommandBufferBeginInfo(StructureType.CommandBufferBeginInfo)
        {
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        VulkanDevice.ThrowIfFailed(api.BeginCommandBuffer(cmd, in beginInfo), "vkBeginCommandBuffer");
        _batchRenderer.RenderOffscreenLayers(cmd, _atlas, _swapchain.ImageFormat);

        // Begin render pass
        var rootClear = _batchRenderer.RootClearColor;
        var clearValue = new ClearValue(new ClearColorValue(
            rootClear.R / 255f, rootClear.G / 255f, rootClear.B / 255f, rootClear.A / 255f));

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
        frame.Serial = ++_submittedFrames;
        frame.Pending = true;
        // CPU recording is finished once submission succeeds, even if presentation
        // or surface recovery throws afterwards. The next draw must acquire anew.
        _frameStarted = false;

        // Presentation is queued. This slot remains immutable until its rendering fence
        // is observed complete, allowing CPU preparation and previous GPU work to overlap.
        _swapchain.Present();
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
        var clip = _clipCache.GetClip(_currentClip, rect, _currentTransform);
        _stateStack.Push(new SavedState(false, _currentClip, default));
        _currentClip = clip;
    }

    public void PushClip(Geometry geometry)
    {
        if (!EnsureFrame()) return;
        var clip = _clipCache.GetClip(_currentClip, geometry, _currentTransform);
        _stateStack.Push(new SavedState(false, _currentClip, default));
        _currentClip = clip;
    }

    public void PopClip()
    {
        if (!EnsureFrame()) return;
        if (_stateStack.Count == 0) return;
        if (_stateStack.Peek().IsLayer)
            throw new InvalidOperationException("PopClip cannot pop an active opacity layer.");
        _currentClip = _stateStack.Pop().Clip;
    }

    // ─── Drawing operations ───────────────────────────────────────────────

    public void FillRect(Rect rect, Brush brush)
    {
        if (!EnsureFrame()) return;
        if (rect.IsEmpty) return;
        var color = ResolveBrushTint(brush);
        FillRectColor(rect, color);
    }

    public bool TryFillSolidLeafOpacity(Rect rect, Color color, float opacity)
    {
        // Group opacity may be folded into one solid fill only when it has no partial
        // coverage edges. Keep nested groups and geometric masks on the offscreen path.
        if (_disposed || rect.IsEmpty || _batchRenderer.CurrentTargetId != 0 || _currentClip.HasMask)
            return false;
        var transform = _currentTransform;
        if (transform.M12 != 0 || transform.M21 != 0 || transform.M11 == 0 || transform.M22 == 0)
            return false;
        var left = rect.Left * transform.M11 + transform.M31;
        var right = rect.Right * transform.M11 + transform.M31;
        var top = rect.Top * transform.M22 + transform.M32;
        var bottom = rect.Bottom * transform.M22 + transform.M32;
        if (!PixelAligned(left) || !PixelAligned(right) || !PixelAligned(top) || !PixelAligned(bottom))
            return false;
        if (!EnsureFrame()) return false;
        opacity = float.IsNaN(opacity) ? 1 : Math.Clamp(opacity, 0, 1);
        var alpha = (byte)Math.Clamp((int)MathF.Round(color.A * opacity), 0, 255);
        _currentMaterial = 0;
        if (alpha != 0) FillRectColor(rect, new Color(color.R, color.G, color.B, alpha));
        return true;
    }

    private static bool PixelAligned(float value) => float.IsFinite(value) && value == MathF.Round(value);

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
        var packed = PackColor(pen.Brush is SolidColorBrush solid ? solid.Color : Color.White);
        if (!_geometryCache.TryGetPrimitiveStroke(rect, 0, 0, false, _currentTransform, pen, packed, out var mesh))
        {
            var path = PathGeometry.Create()
                .MoveTo(new Point(rect.X, rect.Y))
                .LineTo(new Point(rect.Right, rect.Y))
                .LineTo(new Point(rect.Right, rect.Bottom))
                .LineTo(new Point(rect.X, rect.Bottom))
                .Close();
            mesh = _geometryCache.BuildPrimitiveStroke(path, rect, 0, 0, false, _currentTransform, pen, packed);
        }
        if (mesh.Indices.IsEmpty) return;
        ResolveBrushTint(pen.Brush);
        AddBatch(mesh.Vertices, mesh.Indices);
    }

    public void FillPath(PathGeometry path, Brush brush)
    {
        if (!EnsureFrame()) return;
        var packed = PackColor(brush is SolidColorBrush solid ? solid.Color : Color.White);
        var mesh = _geometryCache.GetFillGeometry(path, _currentTransform);
        if (mesh.Indices.IsEmpty) return;
        ResolveBrushTint(brush);
        AddBatch(mesh.Vertices, mesh.Indices, packed);
    }

    public void DrawPath(PathGeometry path, Pen pen)
    {
        if (!EnsureFrame()) return;
        if (pen.Width <= 0) return;
        var packed = PackColor(pen.Brush is SolidColorBrush solid ? solid.Color : Color.White);
        var mesh = _geometryCache.GetStroke(path, _currentTransform, pen, packed);
        if (mesh.Indices.IsEmpty) return;
        ResolveBrushTint(pen.Brush);
        AddBatch(mesh.Vertices, mesh.Indices);
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
        var color = ResolveBrushTint(brush);
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
        _currentMaterial = 0;

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
        var physicalBounds = TransformRect(bounds);
        var parentClip = _currentClip;
        _batchRenderer.PushLayer(IntersectRects(parentClip.Bounds, physicalBounds), opacity);
        _stateStack.Push(new SavedState(true, parentClip, physicalBounds));
        // Parent masks belong to the final composite, not each child draw.
        _currentClip = _clipCache.GetCanvasClip(_batchRenderer.CurrentTargetBounds);
    }

    public void PopLayer()
    {
        if (!EnsureFrame()) return;
        if (_stateStack.Count == 0) return;
        var state = _stateStack.Peek();
        if (!state.IsLayer)
            throw new InvalidOperationException("PopLayer cannot pop an active clip.");
        _stateStack.Pop();
        _currentClip = _clipCache.GetClip(state.Clip, state.LayerBounds, Matrix3x2.Identity);
        try
        {
            _batchRenderer.PopLayer(EmitLayerComposite);
        }
        finally
        {
            _currentClip = state.Clip;
            _currentMaterial = 0;
        }
    }

    private void EmitLayerComposite(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices, int layerId) =>
        AddDrawBatch(vertices, indices, layerId);

    // ─── Resize ───────────────────────────────────────────────────────────

    public void Resize(Size canvasSize) => Resize(canvasSize, DpiScale);

    public void Resize(Size canvasSize, float dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        WaitForSubmittedFrames();
        var sampleCountChanged = _device.ConfigureColorSampleCount(w, h);
        _swapchain.Recreate(w, h);
        if (!_swapchain.IsAvailable)
        {
            // Surface is temporarily lost; EnsureFrame retries instead of failing the frame.
            _minimized = true;
            _frameStarted = false;
            return;
        }
        RefreshSwapchainResources(sampleCountChanged);
        _transformStack.Clear();
        _stateStack.Clear();
        _currentTransform = Matrix3x2.CreateScale(DpiScale);
        _currentMaterial = 0;
        _currentClip = VulkanClipRegion.CreateCanvas(new Rect(0, 0, _swapchain.Extent.Width, _swapchain.Extent.Height));
        _frameStarted = false;
    }

    // ─── Capture (IRenderBitmapSource) ───────────────────────────────────

    /// <summary>
    /// Returns the most recently presented frame as read back from the GPU.
    /// Unlike a software re-render, this reflects the actual Vulkan output.
    /// </summary>
    public bool IsCaptureAvailable => _readbackEnabled;

    public Bitmap CaptureBitmap() => CaptureSubmittedFrame(_submittedFrames);

    internal Bitmap CaptureSubmittedFrame(ulong submissionSerial)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_readbackEnabled)
            throw new VulkanException("GPU readback is disabled. Set SQUARE_VULKAN_READBACK=1 before starting the application to enable it.");
        for (var i = 0; i < _frames.Length; i++)
        {
            var frame = _frames[i];
            if (submissionSerial == 0 || frame.Serial != submissionSerial) continue;
            ObserveCompletion(i, wait: true);
            return frame.Readback.CaptureBitmap(waitForDevice: false);
        }
        throw new VulkanException("The requested GPU frame is unavailable or its resource slot has been reused.");
    }

    public VulkanFrameStatistics GetFrameStatistics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (var i = 0; i < _frames.Length; i++) ObserveCompletion(i, wait: false);
        return new VulkanFrameStatistics(_submittedFrames, _completedFrames,
            checked((int)(_submittedFrames - _completedFrames)), _frames.Length);
    }

    public void WaitForSubmittedFrames()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VulkanDevice.ThrowIfFailed(_device.Api.DeviceWaitIdle(_device.Device), "vkDeviceWaitIdle");
        for (var i = 0; i < _frames.Length; i++) ObserveCompletion(i, wait: false);
    }

    private void ObserveCompletion(int slot, bool wait)
    {
        var frame = _frames[slot];
        if (!frame.Pending) return;
        var fence = _swapchain.GetInFlightFence(slot);
        if (wait)
            VulkanDevice.ThrowIfFailed(_device.Api.WaitForFences(_device.Device, 1, in fence, true, ulong.MaxValue), "vkWaitForFences");
        else
        {
            var status = _device.Api.GetFenceStatus(_device.Device, fence);
            if (status == Silk.NET.Vulkan.Result.NotReady) return;
            VulkanDevice.ThrowIfFailed(status, "vkGetFenceStatus");
        }
        frame.Pending = false;
        _completedFrames++;
    }

    // Recreate has already waited for the device; both independent resource sets are safe.
    private void RefreshSwapchainResources(bool recreatePipelines = false)
    {
        _geometryCache.Clear();
        _clipCache.Clear();
        for (var i = 0; i < _frames.Length; i++)
        {
            ObserveCompletion(i, wait: false);
            var frame = _frames[i];
            frame.Batch.ReleaseLayerResources();
            if (recreatePipelines) frame.Pipeline.RecreateGraphicsPipeline();
            frame.Pipeline.UpdateProjection(_swapchain.Extent.Width, _swapchain.Extent.Height);
            if (_readbackEnabled) frame.Readback.EnsureSize(_swapchain.Extent.Width, _swapchain.Extent.Height);
            frame.Serial = 0;
        }
        _swapchainGeneration = _swapchain.Generation;
        _currentClip = VulkanClipRegion.CreateCanvas(new Rect(0, 0, _swapchain.Extent.Width, _swapchain.Extent.Height));
        _stateStack.Clear();
        _transformStack.Clear();
        _currentTransform = Matrix3x2.CreateScale(DpiScale);
    }

    private void DrawPixelAlignedText(TextLayout text, Point origin, uint packed)
    {
        var physicalOrigin = TransformPoint(origin);
        var lineHeight = TextMetrics.GetLineHeight(text.Font, text.LineHeight) * DpiScale;
        var physicalFont = text.Font.WithSize(text.Font.Size * DpiScale);
        var baselineOffset = TextMetrics.GetBaselineOffset(physicalFont, lineHeight);
        // Match measurement in logical coordinates; scale only glyph rasterization and placement.
        var lines = TextWrapping.Wrap(text.Text, text.MaxSize.Width, (_, rune) =>
            TextLayout.MeasureRuneAdvance(rune, text.Font), text.WrappingOptions);

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var indent = text.GetLineIndent(lineIndex);
            var x = physicalOrigin.X + (indent + GetTextAlignmentOffset(text, line.Width + indent)) * DpiScale;
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
        _geometryCache.Clear();
        _clipCache.Clear();
        foreach (var frame in _frames) frame.Dispose(_device, releasePipeline: false);
        _atlas.Dispose();
        foreach (var frame in _frames) frame.Pipeline.Dispose();
        _swapchain.Dispose();
        _device.Dispose();
    }

    // ─── Shape helpers ────────────────────────────────────────────────────

    private void FillRoundedRect(Rect rect, float rx, float ry, Brush brush)
    {
        rx = Math.Min(rx, rect.Width / 2);
        ry = Math.Min(ry, rect.Height / 2);
        if (rx <= 0 || ry <= 0) { FillRect(rect, brush); return; }
        var segments = PathTessellator.GetCurveSegmentCount(rx, ry, MathF.PI / 2, _currentTransform);
        var perimeterCount = segments * 4 + 4;
        var vertices = ArrayPool<Vertex2D>.Shared.Rent(perimeterCount + 1);
        var indices = ArrayPool<uint>.Shared.Rent(perimeterCount * 3);
        try
        {
            var packed = PackColor(ResolveBrushTint(brush));
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
        if (!EnsureFrame()) return;
        if (pen.Width <= 0) return;
        var packed = PackColor(pen.Brush is SolidColorBrush solid ? solid.Color : Color.White);
        if (!_geometryCache.TryGetPrimitiveStroke(rect, rx, ry, true, _currentTransform, pen, packed, out var mesh))
        {
            var path = CreateRoundedRectPath(rect, rx, ry);
            mesh = _geometryCache.BuildPrimitiveStroke(path, rect, rx, ry, true, _currentTransform, pen, packed);
        }
        if (mesh.Indices.IsEmpty) return;
        ResolveBrushTint(pen.Brush);
        AddBatch(mesh.Vertices, mesh.Indices);
    }

    private void FillEllipse(Point center, float radiusX, float radiusY, Brush brush)
    {
        var segments = PathTessellator.GetCurveSegmentCount(radiusX, radiusY, MathF.Tau, _currentTransform);
        var color = ResolveBrushTint(brush);
        var packed = PackColor(color);
        var transparent = packed & 0x00FFFFFFu;
        var (u0, v0, u1, v1) = VulkanTextureAtlas.WhitePixelUV;
        var feather = PathTessellator.GetLogicalFeatherWidth(_currentTransform);
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
        var segments = PathTessellator.GetCurveSegmentCount(radiusX + pen.Width / 2f, radiusY + pen.Width / 2f, MathF.Tau, _currentTransform);
        var color = ResolveBrushTint(pen.Brush);
        var packed = PackColor(color);
        var transparent = packed & 0x00FFFFFFu;
        var (u0, v0, u1, v1) = VulkanTextureAtlas.WhitePixelUV;
        var halfW = pen.Width / 2f;
        var feather = PathTessellator.GetLogicalFeatherWidth(_currentTransform);
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
        var arcSegments = PathTessellator.GetCurveSegmentCount(rx, ry, MathF.PI / 2, _currentTransform);

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

    private void AddBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices, uint? fillColor = null) =>
        AddDrawBatch(vertices, indices, null, fillColor);

    private void AddDrawBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices, int? layerId, uint? fillColor = null)
    {
        var clip = _currentClip.Bounds;
        if (clip.IsEmpty) return;
        if (_currentClip.HasMask)
        {
            var mesh = _clipCache.Clip(_currentClip, vertices, indices);
            vertices = mesh.Vertices;
            indices = mesh.Indices;
            if (indices.IsEmpty) return;
        }

        var left = (int)MathF.Floor(clip.X);
        var top = (int)MathF.Floor(clip.Y);
        var right = (int)MathF.Ceiling(clip.Right);
        var bottom = (int)MathF.Ceiling(clip.Bottom);
        if (layerId is { } id)
            _batchRenderer.AddLayerBatch(vertices, indices, id, left, top, right - left, bottom - top);
        else
            _batchRenderer.AddBatch(vertices, indices, 0, left, top, right - left, bottom - top, _currentMaterial, fillColor);
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

    private Color ResolveBrushTint(Brush brush)
    {
        _currentMaterial = _batchRenderer.RegisterBrush(brush, _currentTransform);
        return brush switch
        {
            SolidColorBrush solid => solid.Color,
            LinearGradientBrush or RadialGradientBrush => Color.White,
            _ => Color.Transparent
        };
    }

    private uint PackColor(Color c)
    {
        // RGBA8 packed as uint (R in lowest byte for R8G8B8A8Unorm vertex attribute)
        return (uint)(c.R | (c.G << 8) | (c.B << 16) | (c.A << 24));
    }

    private sealed class FrameResources
    {
        public readonly VulkanPipeline Pipeline;
        public readonly VulkanBatchRenderer Batch;
        public readonly VulkanReadbackBuffer Readback;
        public CommandBuffer Command;
        public ulong Serial;
        public bool Pending;
        private bool _disposed;

        public FrameResources(VulkanDevice device, VulkanSwapchain swapchain, bool readbackEnabled)
        {
            Pipeline = new VulkanPipeline(device, swapchain);
            try
            {
                Batch = new VulkanBatchRenderer(device, Pipeline);
                Readback = new VulkanReadbackBuffer(device, swapchain.UsesBgraFormat);
                if (readbackEnabled) Readback.EnsureSize(swapchain.Extent.Width, swapchain.Extent.Height);
                Pipeline.UpdateProjection(swapchain.Extent.Width, swapchain.Extent.Height);
                var allocate = new CommandBufferAllocateInfo(StructureType.CommandBufferAllocateInfo)
                {
                    CommandPool = device.CommandPool,
                    Level = CommandBufferLevel.Primary,
                    CommandBufferCount = 1
                };
                VulkanDevice.ThrowIfFailed(device.Api.AllocateCommandBuffers(device.Device, in allocate, out var command), "vkAllocateCommandBuffers");
                Command = command;
            }
            catch
            {
                Dispose(device, releasePipeline: true);
                throw;
            }
        }

        public void Dispose(VulkanDevice device, bool releasePipeline)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (Command.Handle != 0)
                {
                    var command = Command;
                    device.Api.FreeCommandBuffers(device.Device, device.CommandPool, 1, in command);
                    Command = default;
                }
                Readback?.Dispose();
                Batch?.Dispose();
            }
            if (releasePipeline) Pipeline.Dispose();
        }
    }

    private readonly record struct SavedState(bool IsLayer, VulkanClipRegion Clip, Rect LayerBounds);

    private static uint ToPhysical(float logical, float dpi) => (uint)Math.Max(1, MathF.Ceiling(logical * dpi));
    private static float NormalizeDpi(float dpi) => float.IsFinite(dpi) && dpi > 0 ? dpi : 1f;
}
