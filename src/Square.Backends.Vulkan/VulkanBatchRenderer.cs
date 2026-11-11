using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Square.Graphics;
using Square.Rendering.Tessellation;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Square.Backends.Vulkan;

internal delegate void VulkanLayerCompositeEmitter(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices, int layerId);

internal struct DrawBatch
{
    public int IndexOffset, IndexCount;
    public ulong TextureId;
    public int ScissorX, ScissorY, ScissorW, ScissorH;
    public int MaterialId, TargetId, SampleLayerId;
}

/// <summary>GPU geometry/material batches and a frame-local tree of real offscreen group targets.</summary>
internal sealed unsafe class VulkanBatchRenderer : IDisposable
{
    internal const int MaxLayerPoolEntries = 256;
    internal const ulong MaxLayerPoolBytes = 64 * 1024 * 1024;
    private readonly VulkanDevice _device;
    private readonly VulkanPipeline _pipeline;
    private readonly List<Vertex2D> _vertices = new(4096);
    private readonly List<uint> _indices = new(8192);
    private readonly List<DrawBatch> _batches = new(64);
    private readonly List<FrameTarget> _targets = [];
    private readonly VulkanLayerResourcePool<LayerResource> _layerPool;
    private readonly List<MaterialEntry> _materials = [];
    private readonly VulkanBrushCache _brushCache = new();
    private readonly Dictionary<VulkanBrushMaterial, int> _materialIds = [];
    private readonly List<VulkanGradientStop> _gradientStops = [];
    private int _currentTarget;
    private bool _prepared, _offscreenRendered;
    private Buffer _vertexBuffer;
    private DeviceMemory _vertexMemory;
    private void* _vertexMapped;
    private Buffer _indexBuffer;
    private DeviceMemory _indexMemory;
    private void* _indexMapped;
    private int _vertexCapacity, _indexCapacity;
    private bool _disposed;

    public int CurrentTargetId => _currentTarget;
    public Rect CurrentTargetBounds => _targets.Count == 0
        ? new Rect(0, 0, _pipeline.FramebufferExtent.Width, _pipeline.FramebufferExtent.Height)
        : _targets[_currentTarget].Bounds;
    public Color RootClearColor => _targets.Count == 0 ? Color.Black : _targets[0].ClearColor;
    internal int LayerPoolCount => _layerPool.Count;
    internal ulong LayerPoolBytes => _layerPool.Bytes;
    internal long LayerPoolHitCount => _layerPool.HitCount;
    internal long LayerCreateCount { get; private set; }
    internal long LayerDestroyCount { get; private set; }

    public VulkanBatchRenderer(VulkanDevice device, VulkanPipeline pipeline)
    {
        _device = device;
        _pipeline = pipeline;
        _layerPool = new VulkanLayerResourcePool<LayerResource>(MaxLayerPoolEntries, MaxLayerPoolBytes, DestroyLayer);
        EnsureBuffers(4096, 8192);
    }

    /// <summary>The owner has waited for the previous GPU frame before reusing mapped data/resources.</summary>
    public void BeginFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RecycleFrameLayers();
        _vertices.Clear(); _indices.Clear(); _batches.Clear();
        _materials.Clear(); _materials.Add(default);
        _materialIds.Clear(); _gradientStops.Clear();
        _targets.Clear();
        var extent = _pipeline.FramebufferExtent;
        _targets.Add(new FrameTarget(new Rect(0, 0, extent.Width, extent.Height), -1, 1, Color.Black));
        _currentTarget = 0;
        _prepared = _offscreenRendered = false;
    }

    public int RegisterBrush(Brush brush, Matrix3x2 logicalToDevice)
        => brush is SolidColorBrush ? 0 : RegisterMaterial(_brushCache.Get(brush, logicalToDevice));

    private int RegisterMaterial(VulkanBrushMaterial material)
    {
        if (_materialIds.TryGetValue(material, out var id)) return id;
        var offset = checked((uint)_gradientStops.Count);
        _gradientStops.AddRange(material.Stops);
        id = _materials.Count;
        _materials.Add(new MaterialEntry(material, offset));
        _materialIds.Add(material, id);
        return id;
    }

    public void AddBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices, ulong textureId,
        int scissorX, int scissorY, int scissorW, int scissorH, int materialId = 0, uint? fillColor = null)
        => AddBatchCore(vertices, indices, textureId, scissorX, scissorY, scissorW, scissorH, materialId, -1, fillColor);

    private void AddBatchCore(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices, ulong textureId,
        int scissorX, int scissorY, int scissorW, int scissorH, int materialId, int sampleLayerId, uint? fillColor = null)
    {
        if (_prepared) throw new InvalidOperationException("The frame has already been prepared for GPU recording.");
        if (_targets.Count == 0) throw new InvalidOperationException("BeginFrame is required before drawing.");
        if ((uint)materialId >= (uint)_materials.Count) throw new ArgumentOutOfRangeException(nameof(materialId));
        if (indices.IsEmpty || vertices.IsEmpty || scissorW <= 0 || scissorH <= 0 || CurrentTargetBounds.IsEmpty) return;
        var vertexOffset = _vertices.Count;
        var indexOffset = _indices.Count;
        _vertices.AddRange(vertices);
        if (fillColor is { } color)
            ApplyFillColor(CollectionsMarshal.AsSpan(_vertices).Slice(vertexOffset, vertices.Length), color);
        foreach (var index in indices) _indices.Add(checked(index + (uint)vertexOffset));
        if (_batches.Count > 0)
        {
            var previous = _batches[^1];
            if (previous.TextureId == textureId && previous.MaterialId == materialId && previous.TargetId == _currentTarget &&
                previous.SampleLayerId == sampleLayerId && previous.ScissorX == scissorX && previous.ScissorY == scissorY &&
                previous.ScissorW == scissorW && previous.ScissorH == scissorH && previous.IndexOffset + previous.IndexCount == indexOffset)
            {
                previous.IndexCount += indices.Length;
                _batches[^1] = previous;
                return;
            }
        }
        _batches.Add(new DrawBatch
        {
            IndexOffset = indexOffset, IndexCount = indices.Length, TextureId = textureId,
            ScissorX = scissorX, ScissorY = scissorY, ScissorW = scissorW, ScissorH = scissorH,
            MaterialId = materialId, TargetId = _currentTarget, SampleLayerId = sampleLayerId
        });
    }

    internal static void ApplyFillColor(Span<Vertex2D> appendedVertices, uint color)
    {
        foreach (ref var vertex in appendedVertices) vertex.Color = color;
    }

    public int PushLayer(Rect physicalBounds, float opacity)
    {
        if (_targets.Count == 0 || _prepared) throw new InvalidOperationException("An unprepared frame is required.");
        var bounds = RoundLayerBounds(physicalBounds, CurrentTargetBounds);
        opacity = float.IsNaN(opacity) ? 1 : Math.Clamp(opacity, 0, 1);
        var target = new FrameTarget(bounds, _currentTarget, opacity, Color.Transparent)
        {
            CompositeMaterial = RegisterMaterial(_brushCache.GetLayer(opacity))
        };
        _currentTarget = _targets.Count;
        _targets.Add(target);
        return _currentTarget;
    }

    internal static Rect RoundLayerBounds(Rect bounds, Rect parent)
    {
        if (bounds.IsEmpty || parent.IsEmpty || !float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) ||
            !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom)) return new Rect(0, 0, 0, 0);
        var visible = Rect.Intersect(bounds, parent);
        if (visible.IsEmpty) return new Rect(0, 0, 0, 0);
        var left = MathF.Floor(visible.Left);
        var top = MathF.Floor(visible.Top);
        return new Rect(left, top, MathF.Ceiling(visible.Right) - left, MathF.Ceiling(visible.Bottom) - top);
    }

    /// <summary>Restore the parent first; Context's callback applies its exact parent mask once.</summary>
    public void PopLayer(VulkanLayerCompositeEmitter? callback = null)
    {
        if (_currentTarget == 0) throw new InvalidOperationException("No active offscreen layer.");
        var id = _currentTarget;
        var layer = _targets[id];
        _currentTarget = layer.Parent;
        if (layer.Bounds.IsEmpty || layer.Opacity <= 0) return;
        var bounds = layer.Bounds;
        Span<Vertex2D> vertices = stackalloc Vertex2D[4];
        vertices[0] = new Vertex2D(bounds.Left, bounds.Top, 0, 0, uint.MaxValue);
        vertices[1] = new Vertex2D(bounds.Right, bounds.Top, 1, 0, uint.MaxValue);
        vertices[2] = new Vertex2D(bounds.Right, bounds.Bottom, 1, 1, uint.MaxValue);
        vertices[3] = new Vertex2D(bounds.Left, bounds.Bottom, 0, 1, uint.MaxValue);
        ReadOnlySpan<uint> indices = [0u, 1u, 2u, 0u, 2u, 3u];
        if (callback != null) callback(vertices, indices, id);
        else
        {
            var parent = CurrentTargetBounds;
            AddLayerBatch(vertices, indices, id, (int)parent.X, (int)parent.Y, (int)parent.Width, (int)parent.Height);
        }
    }

    public void AddLayerBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices, int layerId,
        int scissorX, int scissorY, int scissorW, int scissorH)
    {
        if (layerId <= 0 || layerId >= _targets.Count || _targets[layerId].Parent != _currentTarget)
            throw new ArgumentOutOfRangeException(nameof(layerId), "Only a direct child may be composited into the current target.");
        var layer = _targets[layerId];
        if (layer.Opacity <= 0 || layer.Bounds.IsEmpty) return;
        AddBatchCore(vertices, indices, 0, scissorX, scissorY, scissorW, scissorH, layer.CompositeMaterial, layerId);
    }

    public void ClearCurrentTarget(Color color)
    {
        if (_prepared || _targets.Count == 0) throw new InvalidOperationException("An unprepared frame is required.");
        _targets[_currentTarget].ClearColor = color;
        _batches.RemoveAll(batch => batch.TargetId == _currentTarget);
    }

    private void PrepareFrame()
    {
        if (_prepared) return;
        if (_currentTarget != 0) throw new InvalidOperationException("All opacity layers must be popped before rendering.");
        EnsureBuffers(_vertices.Count, _indices.Count);
        UploadVertices(); UploadIndices();
        _pipeline.UploadGradientStops(CollectionsMarshal.AsSpan(_gradientStops));
        foreach (var target in _targets) target.Required = false;
        MarkRequiredLayers(0);
        _prepared = true;
    }

    private void MarkRequiredLayers(int targetId)
    {
        foreach (var batch in _batches)
        {
            if (batch.TargetId != targetId || batch.SampleLayerId < 0) continue;
            var child = _targets[batch.SampleLayerId];
            if (child.Required) continue;
            child.Required = true;
            MarkRequiredLayers(batch.SampleLayerId);
        }
    }

    /// <summary>Record children before parents, before the swapchain render pass begins.</summary>
    public void RenderOffscreenLayers(CommandBuffer cmd, VulkanTextureAtlas atlas, Format format)
    {
        if (_offscreenRendered) return;
        PrepareFrame();
        for (var id = _targets.Count - 1; id > 0; id--)
        {
            var target = _targets[id];
            if (!target.Required) continue;
            target.Resource ??= RentLayer(format, (uint)target.Bounds.Width, (uint)target.Bounds.Height);
            var resource = target.Resource.Native;
            var alpha = target.ClearColor.A / 255f;
            var clear = new ClearValue(new ClearColorValue(target.ClearColor.R / 255f * alpha,
                target.ClearColor.G / 255f * alpha, target.ClearColor.B / 255f * alpha, alpha));
            var begin = new RenderPassBeginInfo(StructureType.RenderPassBeginInfo)
            {
                RenderPass = resource.RenderPass, Framebuffer = resource.Framebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(resource.Width, resource.Height)),
                ClearValueCount = 1, PClearValues = &clear
            };
            _device.Api.CmdBeginRenderPass(cmd, in begin, SubpassContents.Inline);
            var viewport = new Viewport(0, 0, resource.Width, resource.Height, 0, 1);
            _device.Api.CmdSetViewport(cmd, 0, 1, in viewport);
            DrawTarget(cmd, atlas, id, new Extent2D(resource.Width, resource.Height));
            _device.Api.CmdEndRenderPass(cmd);
        }
        _offscreenRendered = true;
    }

    public void Render(CommandBuffer cmd, VulkanTextureAtlas atlas, Extent2D framebufferExtent)
    {
        PrepareFrame();
        if (!_offscreenRendered && _targets.Skip(1).Any(target => target.Required))
            throw new InvalidOperationException("RenderOffscreenLayers must run before the root render pass.");
        DrawTarget(cmd, atlas, 0, framebufferExtent);
    }

    private void DrawTarget(CommandBuffer cmd, VulkanTextureAtlas atlas, int targetId, Extent2D extent)
    {
        var api = _device.Api;
        api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline.Pipeline);
        var vertexBuffer = _vertexBuffer;
        ulong offset = 0;
        api.CmdBindVertexBuffers(cmd, 0, 1, &vertexBuffer, &offset);
        api.CmdBindIndexBuffer(cmd, _indexBuffer, 0, IndexType.Uint32);
        var bounds = _targets[targetId].Bounds;
        var origin = new Vector2(bounds.X, bounds.Y);
        var projection = targetId == 0 ? _pipeline.CurrentProjection : new Matrix4x4(
            2f / extent.Width, 0, 0, 0, 0, 2f / extent.Height, 0, 0, 0, 0, 1, 0,
            -1 - 2 * origin.X / extent.Width, -1 - 2 * origin.Y / extent.Height, 0, 1);
        api.CmdPushConstants(cmd, _pipeline.PipelineLayout, ShaderStageFlags.VertexBit, 0, 64, &projection);
        foreach (var batch in _batches)
        {
            if (batch.TargetId != targetId) continue;
            var left = Math.Clamp((long)batch.ScissorX - (int)origin.X, 0, extent.Width);
            var top = Math.Clamp((long)batch.ScissorY - (int)origin.Y, 0, extent.Height);
            var right = Math.Clamp((long)batch.ScissorX + batch.ScissorW - (int)origin.X, 0, extent.Width);
            var bottom = Math.Clamp((long)batch.ScissorY + batch.ScissorH - (int)origin.Y, 0, extent.Height);
            if (right <= left || bottom <= top) continue;
            var scissor = new Rect2D(new Offset2D((int)left, (int)top), new Extent2D((uint)(right - left), (uint)(bottom - top)));
            api.CmdSetScissor(cmd, 0, 1, in scissor);
            var descriptor = batch.SampleLayerId < 0 ? atlas.GetDescriptorSet(_pipeline) : _targets[batch.SampleLayerId].Resource!.Descriptor;
            api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _pipeline.PipelineLayout, 0, 1, in descriptor, 0, null);
            var material = _materials[batch.MaterialId];
            var push = material.Material?.ToPushConstants(material.StopOffset, origin) ?? default;
            api.CmdPushConstants(cmd, _pipeline.PipelineLayout, ShaderStageFlags.FragmentBit, 64, 48, &push);
            api.CmdDrawIndexed(cmd, (uint)batch.IndexCount, 1, (uint)batch.IndexOffset, 0, 0);
        }
    }

    private LayerResource RentLayer(Format format, uint width, uint height)
    {
        var spec = new VulkanLayerSpec(format, _device.ColorSampleCount, width, height);
        if (_layerPool.TryRent(spec, out var pooled)) return pooled;
        var native = new VulkanOffscreenLayer(_device, format, width, height);
        try
        {
            var resource = new LayerResource(native, _pipeline.CreateTextureDescriptor(native.ImageView, native.Sampler), format, _device.ColorSampleCount);
            LayerCreateCount++;
            return resource;
        }
        catch { native.Dispose(); throw; }
    }

    private void RecycleFrameLayers()
    {
        foreach (var target in _targets)
        {
            if (target.Resource is not { } resource) continue;
            target.Resource = null;
            _layerPool.Return(new VulkanLayerSpec(resource.Format, resource.Samples, resource.Native.Width, resource.Native.Height),
                resource.Native.Bytes, resource);
        }
    }

    private void DestroyLayer(LayerResource resource)
    {
        _pipeline.ReleaseTextureDescriptor(resource.Descriptor);
        resource.Native.Dispose();
        LayerDestroyCount++;
    }

    /// <summary>Caller waits GPU idle before resize/dispose; no per-layer device wait is introduced.</summary>
    public void ReleaseLayerResources()
    {
        foreach (var target in _targets)
        {
            if (target.Resource is not { } resource) continue;
            DestroyLayer(resource); target.Resource = null;
        }
        _layerPool.Clear();
        _targets.Clear(); _currentTarget = 0;
        _batches.Clear(); _vertices.Clear(); _indices.Clear();
        _prepared = _offscreenRendered = false;
    }

    private readonly record struct MaterialEntry(VulkanBrushMaterial? Material, uint StopOffset);
    private sealed class FrameTarget(Rect bounds, int parent, float opacity, Color clearColor)
    {
        public Rect Bounds { get; } = bounds;
        public int Parent { get; } = parent;
        public float Opacity { get; } = opacity;
        public Color ClearColor { get; set; } = clearColor;
        public int CompositeMaterial { get; init; }
        public LayerResource? Resource { get; set; }
        public bool Required { get; set; }
    }
    private sealed record LayerResource(VulkanOffscreenLayer Native, DescriptorSet Descriptor, Format Format, SampleCountFlags Samples);

    private void EnsureBuffers(int vertexCount, int indexCount)
    {
        if (vertexCount <= _vertexCapacity && indexCount <= _indexCapacity) return;
        var newVertexCap = Math.Max(vertexCount, _vertexCapacity * 2);
        var newIndexCap = Math.Max(indexCount, _indexCapacity * 2);
        Buffer newVertexBuffer = default;
        DeviceMemory newVertexMemory = default;
        void* newVertexMapped = null;
        Buffer newIndexBuffer = default;
        DeviceMemory newIndexMemory = default;
        void* newIndexMapped = null;
        try
        {
            CreateMappedBuffer((ulong)(newVertexCap * sizeof(Vertex2D)), BufferUsageFlags.VertexBufferBit, out newVertexBuffer, out newVertexMemory, out newVertexMapped);
            CreateMappedBuffer((ulong)(newIndexCap * sizeof(uint)), BufferUsageFlags.IndexBufferBit, out newIndexBuffer, out newIndexMemory, out newIndexMapped);
        }
        catch
        {
            DestroyMappedBuffer(ref newVertexBuffer, ref newVertexMemory, ref newVertexMapped);
            DestroyMappedBuffer(ref newIndexBuffer, ref newIndexMemory, ref newIndexMapped);
            throw;
        }
        DestroyMappedBuffer(ref _vertexBuffer, ref _vertexMemory, ref _vertexMapped);
        DestroyMappedBuffer(ref _indexBuffer, ref _indexMemory, ref _indexMapped);
        _vertexBuffer = newVertexBuffer; _vertexMemory = newVertexMemory; _vertexMapped = newVertexMapped;
        _indexBuffer = newIndexBuffer; _indexMemory = newIndexMemory; _indexMapped = newIndexMapped;
        _vertexCapacity = newVertexCap; _indexCapacity = newIndexCap;
    }
    private void UploadVertices()
    {
        if (_vertices.Count > 0) CollectionsMarshal.AsSpan(_vertices).CopyTo(new Span<Vertex2D>(_vertexMapped, _vertices.Count));
    }
    private void UploadIndices()
    {
        if (_indices.Count > 0) CollectionsMarshal.AsSpan(_indices).CopyTo(new Span<uint>(_indexMapped, _indices.Count));
    }
    private void CreateMappedBuffer(ulong size, BufferUsageFlags usage, out Buffer buffer, out DeviceMemory memory, out void* mapped)
    {
        buffer = CreateBuffer(size, usage); memory = default; mapped = null;
        try
        {
            memory = AllocateMemory(buffer, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            VulkanDevice.ThrowIfFailed(_device.Api.BindBufferMemory(_device.Device, buffer, memory, 0), "vkBindBufferMemory");
            void* pointer;
            VulkanDevice.ThrowIfFailed(_device.Api.MapMemory(_device.Device, memory, 0, size, 0, &pointer), "vkMapMemory");
            mapped = pointer;
        }
        catch { DestroyMappedBuffer(ref buffer, ref memory, ref mapped); throw; }
    }
    private void DestroyMappedBuffer(ref Buffer buffer, ref DeviceMemory memory, ref void* mapped)
    {
        if (mapped != null && memory.Handle != 0) _device.Api.UnmapMemory(_device.Device, memory);
        if (buffer.Handle != 0) _device.Api.DestroyBuffer(_device.Device, buffer, null);
        if (memory.Handle != 0) _device.Api.FreeMemory(_device.Device, memory, null);
        buffer = default; memory = default; mapped = null;
    }
    private Buffer CreateBuffer(ulong size, BufferUsageFlags usage)
    {
        var info = new BufferCreateInfo(StructureType.BufferCreateInfo) { Size = size, Usage = usage, SharingMode = SharingMode.Exclusive };
        VulkanDevice.ThrowIfFailed(_device.Api.CreateBuffer(_device.Device, in info, null, out var buffer), "vkCreateBuffer");
        return buffer;
    }
    private DeviceMemory AllocateMemory(Buffer buffer, MemoryPropertyFlags properties)
    {
        _device.Api.GetBufferMemoryRequirements(_device.Device, buffer, out var requirements);
        var allocate = new MemoryAllocateInfo(StructureType.MemoryAllocateInfo)
        {
            AllocationSize = requirements.Size, MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, properties)
        };
        VulkanDevice.ThrowIfFailed(_device.Api.AllocateMemory(_device.Device, in allocate, null, out var memory), "vkAllocateMemory");
        return memory;
    }
    private uint FindMemoryType(uint filter, MemoryPropertyFlags properties)
    {
        _device.Api.GetPhysicalDeviceMemoryProperties(_device.PhysicalDevice, out var types);
        for (uint i = 0; i < types.MemoryTypeCount; i++)
            if ((filter & (1u << (int)i)) != 0 && (types.MemoryTypes[(int)i].PropertyFlags & properties) == properties) return i;
        throw new VulkanException("No suitable memory type found.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseLayerResources();
        _brushCache.Clear();
        _materials.Clear(); _materialIds.Clear(); _gradientStops.Clear();
        DestroyMappedBuffer(ref _vertexBuffer, ref _vertexMemory, ref _vertexMapped);
        DestroyMappedBuffer(ref _indexBuffer, ref _indexMemory, ref _indexMapped);
    }
}
