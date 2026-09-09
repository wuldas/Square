using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Square.Rendering.Tessellation;

namespace Square.Backends.Vulkan;

/// <summary>
/// ImGui-style batched 2D renderer: dynamic VBO/IBO, batched draw calls by texture + scissor.
/// Reference: imgui_impl_vulkan.cpp rendering backend.
/// </summary>

internal struct DrawBatch
{
    public int IndexOffset;
    public int IndexCount;
    public ulong TextureId; // Descriptor set handle or texture id
    public int ScissorX, ScissorY, ScissorW, ScissorH;
}

internal sealed unsafe class VulkanBatchRenderer : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly VulkanPipeline _pipeline;

    private List<Vertex2D> _vertices = new(4096);
    private List<uint> _indices = new(8192);
    private List<DrawBatch> _batches = new(64);

    private Buffer _vertexBuffer;
    private DeviceMemory _vertexMemory;
    private void* _vertexMapped;
    private Buffer _indexBuffer;
    private DeviceMemory _indexMemory;
    private void* _indexMapped;
    private int _vertexCapacity;
    private int _indexCapacity;
    private bool _disposed;

    public VulkanBatchRenderer(VulkanDevice device, VulkanPipeline pipeline)
    {
        _device = device;
        _pipeline = pipeline;
        EnsureBuffers(4096, 8192);
    }

    public void BeginFrame()
    {
        _vertices.Clear();
        _indices.Clear();
        _batches.Clear();
    }

    /// <summary>Add vertices and indices for a draw call batch.</summary>
    public void AddBatch(ReadOnlySpan<Vertex2D> vertices, ReadOnlySpan<uint> indices, ulong textureId, int scissorX, int scissorY, int scissorW, int scissorH)
    {
        var vertexOffset = _vertices.Count;
        var indexOffset = _indices.Count;

        _vertices.AddRange(vertices);
        foreach (var idx in indices)
            _indices.Add(idx + (uint)vertexOffset);

        if (_batches.Count > 0)
        {
            var previous = _batches[^1];
            if (previous.TextureId == textureId && previous.ScissorX == scissorX && previous.ScissorY == scissorY &&
                previous.ScissorW == scissorW && previous.ScissorH == scissorH &&
                previous.IndexOffset + previous.IndexCount == indexOffset)
            {
                previous.IndexCount += indices.Length;
                _batches[^1] = previous;
                return;
            }
        }

        _batches.Add(new DrawBatch
        {
            IndexOffset = indexOffset,
            IndexCount = indices.Length,
            TextureId = textureId,
            ScissorX = scissorX,
            ScissorY = scissorY,
            ScissorW = scissorW,
            ScissorH = scissorH
        });
    }

    /// <summary>Upload geometry and record draw commands into command buffer.</summary>
    public void Render(CommandBuffer cmd, VulkanTextureAtlas atlas, Extent2D framebufferExtent)
    {
        if (_batches.Count == 0) return;

        EnsureBuffers(_vertices.Count, _indices.Count);
        UploadVertices();
        UploadIndices();

        var api = _device.Api;

        // Bind pipeline
        api.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline.Pipeline);

        // Bind vertex/index buffers
        var vertexBuffer = _vertexBuffer;
        ulong offset = 0;
        api.CmdBindVertexBuffers(cmd, 0, 1, &vertexBuffer, &offset);
        api.CmdBindIndexBuffer(cmd, _indexBuffer, 0, IndexType.Uint32);

        // Bind descriptor set (texture atlas)
        var descriptorSet = atlas.DescriptorSet;
        api.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _pipeline.PipelineLayout, 0, 1, in descriptorSet, 0, null);

        // Push projection matrix as push constant
        var proj = _pipeline.CurrentProjection;
        api.CmdPushConstants(cmd, _pipeline.PipelineLayout, ShaderStageFlags.VertexBit, 0, 64, &proj);

        // Draw batches
        foreach (var batch in _batches)
        {
            var left = Math.Clamp(batch.ScissorX, 0, (int)framebufferExtent.Width);
            var top = Math.Clamp(batch.ScissorY, 0, (int)framebufferExtent.Height);
            var right = Math.Clamp((long)batch.ScissorX + batch.ScissorW, 0, framebufferExtent.Width);
            var bottom = Math.Clamp((long)batch.ScissorY + batch.ScissorH, 0, framebufferExtent.Height);
            if (right <= left || bottom <= top) continue;

            var scissor = new Rect2D(new Offset2D(left, top),
                new Extent2D((uint)(right - left), (uint)(bottom - top)));
            api.CmdSetScissor(cmd, 0, 1, in scissor);
            api.CmdDrawIndexed(cmd, (uint)batch.IndexCount, 1, (uint)batch.IndexOffset, 0, 0);
        }
    }

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
            CreateMappedBuffer((ulong)(newVertexCap * sizeof(Vertex2D)), BufferUsageFlags.VertexBufferBit,
                out newVertexBuffer, out newVertexMemory, out newVertexMapped);
            CreateMappedBuffer((ulong)(newIndexCap * sizeof(uint)), BufferUsageFlags.IndexBufferBit,
                out newIndexBuffer, out newIndexMemory, out newIndexMapped);
        }
        catch
        {
            DestroyMappedBuffer(ref newVertexBuffer, ref newVertexMemory, ref newVertexMapped);
            DestroyMappedBuffer(ref newIndexBuffer, ref newIndexMemory, ref newIndexMapped);
            throw;
        }

        DestroyMappedBuffer(ref _vertexBuffer, ref _vertexMemory, ref _vertexMapped);
        DestroyMappedBuffer(ref _indexBuffer, ref _indexMemory, ref _indexMapped);
        _vertexBuffer = newVertexBuffer;
        _vertexMemory = newVertexMemory;
        _vertexMapped = newVertexMapped;
        _indexBuffer = newIndexBuffer;
        _indexMemory = newIndexMemory;
        _indexMapped = newIndexMapped;

        _vertexCapacity = newVertexCap;
        _indexCapacity = newIndexCap;
    }

    private void UploadVertices()
    {
        if (_vertices.Count == 0) return;
        CollectionsMarshal.AsSpan(_vertices).CopyTo(new Span<Vertex2D>(_vertexMapped, _vertices.Count));
    }

    private void UploadIndices()
    {
        if (_indices.Count == 0) return;
        CollectionsMarshal.AsSpan(_indices).CopyTo(new Span<uint>(_indexMapped, _indices.Count));
    }

    private void CreateMappedBuffer(ulong size, BufferUsageFlags usage,
        out Buffer buffer, out DeviceMemory memory, out void* mapped)
    {
        buffer = CreateBuffer(size, usage);
        memory = default;
        mapped = null;
        try
        {
            memory = AllocateMemory(buffer, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            VulkanDevice.ThrowIfFailed(_device.Api.BindBufferMemory(_device.Device, buffer, memory, 0), "vkBindBufferMemory");
            void* mappedPointer;
            VulkanDevice.ThrowIfFailed(_device.Api.MapMemory(_device.Device, memory, 0, size, 0, &mappedPointer), "vkMapMemory");
            mapped = mappedPointer;
        }
        catch
        {
            DestroyMappedBuffer(ref buffer, ref memory, ref mapped);
            throw;
        }
    }

    private void DestroyMappedBuffer(ref Buffer buffer, ref DeviceMemory memory, ref void* mapped)
    {
        if (mapped != null && memory.Handle != 0) _device.Api.UnmapMemory(_device.Device, memory);
        if (buffer.Handle != 0) _device.Api.DestroyBuffer(_device.Device, buffer, null);
        if (memory.Handle != 0) _device.Api.FreeMemory(_device.Device, memory, null);
        buffer = default;
        memory = default;
        mapped = null;
    }

    private Buffer CreateBuffer(ulong size, BufferUsageFlags usage)
    {
        var info = new BufferCreateInfo(StructureType.BufferCreateInfo)
        {
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };
        var result = _device.Api.CreateBuffer(_device.Device, in info, null, out var buffer);
        VulkanDevice.ThrowIfFailed(result, "vkCreateBuffer");
        return buffer;
    }

    private DeviceMemory AllocateMemory(Buffer buffer, MemoryPropertyFlags properties)
    {
        _device.Api.GetBufferMemoryRequirements(_device.Device, buffer, out var reqs);
        var allocInfo = new MemoryAllocateInfo(StructureType.MemoryAllocateInfo)
        {
            AllocationSize = reqs.Size,
            MemoryTypeIndex = FindMemoryType(reqs.MemoryTypeBits, properties)
        };
        var result = _device.Api.AllocateMemory(_device.Device, in allocInfo, null, out var memory);
        VulkanDevice.ThrowIfFailed(result, "vkAllocateMemory");
        return memory;
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _device.Api.GetPhysicalDeviceMemoryProperties(_device.PhysicalDevice, out var memProps);
        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << (int)i)) != 0 && (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                return i;
        }
        throw new VulkanException("No suitable memory type found.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyMappedBuffer(ref _vertexBuffer, ref _vertexMemory, ref _vertexMapped);
        DestroyMappedBuffer(ref _indexBuffer, ref _indexMemory, ref _indexMapped);
    }
}
