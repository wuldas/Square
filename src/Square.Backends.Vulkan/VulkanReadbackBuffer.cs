using Silk.NET.Vulkan;
using Square.Graphics;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Square.Backends.Vulkan;

/// <summary>
/// Host-visible buffer that receives a copy of the presented swapchain image each frame,
/// enabling GPU-accurate screenshots (<see cref="IRenderBitmapSource"/>) without re-rendering
/// the display tree on a software context. This is what lets DevTools capture real GPU output
/// (so GPU-side bugs such as a dropped render pass are visible in screenshots).
/// </summary>
/// <remarks>
/// Swapchain image ownership transfers to the presentation engine after vkQueuePresentKHR,
/// so the image cannot be read back out-of-band. Instead, every submitted frame copies the
/// color attachment into this buffer inside the frame's command buffer, before present.
/// Swapchain BGRA formats already match Square's BGRA bytes; RGBA formats are swizzled
/// while copying into the public Square bitmap.
/// </remarks>
internal sealed unsafe class VulkanReadbackBuffer : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly bool _usesBgraFormat;
    private Buffer _buffer;
    private DeviceMemory _memory;
    private void* _mapped;
    private ulong _capacity;
    private uint _width;
    private uint _height;
    private bool _hasContent;
    private bool _disposed;

    public VulkanReadbackBuffer(VulkanDevice device, bool usesBgraFormat)
    {
        _device = device;
        _usesBgraFormat = usesBgraFormat;
    }

    /// <summary>(Re)create the buffer to hold a <paramref name="width"/>x<paramref name="height"/> frame.</summary>
    public void EnsureSize(uint width, uint height)
    {
        // Vulkan forbids 0-size buffers/allocations; keep the last valid buffer instead.
        if (width < 1 || height < 1) return;
        if (_buffer.Handle != 0 && _width == width && _height == height) return;
        var size = (ulong)width * height * 4;
        if (_buffer.Handle != 0 && size <= _capacity && size >= _capacity / 4)
        {
            _width = width;
            _height = height;
            _hasContent = false;
            return;
        }

        var api = _device.Api;
        Buffer newBuffer = default;
        DeviceMemory newMemory = default;
        void* newMapped = null;
        var shrinking = _capacity > 0 && size < _capacity / 4;
        var newCapacity = shrinking
            ? Math.Max(size, 4096UL)
            : Math.Max(size, Math.Max(4096UL, _capacity * 2));
        var bufferInfo = new BufferCreateInfo(StructureType.BufferCreateInfo)
        {
            Size = newCapacity,
            Usage = BufferUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive
        };
        try
        {
            VulkanDevice.ThrowIfFailed(api.CreateBuffer(_device.Device, in bufferInfo, null, out newBuffer), "vkCreateBuffer");
            api.GetBufferMemoryRequirements(_device.Device, newBuffer, out var memReqs);
            var allocInfo = new MemoryAllocateInfo(StructureType.MemoryAllocateInfo)
            {
                AllocationSize = memReqs.Size,
                MemoryTypeIndex = FindMemoryType(memReqs.MemoryTypeBits,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
            };
            VulkanDevice.ThrowIfFailed(api.AllocateMemory(_device.Device, in allocInfo, null, out newMemory), "vkAllocateMemory");
            VulkanDevice.ThrowIfFailed(api.BindBufferMemory(_device.Device, newBuffer, newMemory, 0), "vkBindBufferMemory");
            VulkanDevice.ThrowIfFailed(api.MapMemory(_device.Device, newMemory, 0, newCapacity, 0, &newMapped), "vkMapMemory");
        }
        catch
        {
            if (newMapped != null && newMemory.Handle != 0) api.UnmapMemory(_device.Device, newMemory);
            if (newBuffer.Handle != 0) api.DestroyBuffer(_device.Device, newBuffer, null);
            if (newMemory.Handle != 0) api.FreeMemory(_device.Device, newMemory, null);
            throw;
        }

        Destroy();
        _buffer = newBuffer;
        _memory = newMemory;
        _mapped = newMapped;
        _capacity = newCapacity;
        _width = width;
        _height = height;
        _hasContent = false;
    }

    /// <summary>
    /// Records a copy of the current swapchain image into the readback buffer.
    /// Must be called after the render pass ends (image is in PresentSrcKhr layout)
    /// and before the command buffer is submitted/presented.
    /// </summary>
    public void RecordCopy(CommandBuffer cmd, Silk.NET.Vulkan.Image image)
    {
        if (_buffer.Handle == 0) return;
        var api = _device.Api;
        var subresource = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);

        // PresentSrcKhr -> TransferSrcOptimal (image was written as a color attachment).
        var toTransfer = new ImageMemoryBarrier(StructureType.ImageMemoryBarrier)
        {
            Image = image,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.TransferReadBit,
            OldLayout = ImageLayout.PresentSrcKhr,
            NewLayout = ImageLayout.TransferSrcOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            SubresourceRange = subresource
        };
        api.CmdPipelineBarrier(cmd,
            PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.TransferBit,
            0, 0, null, 0, null, 1, in toTransfer);

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(_width, _height, 1)
        };
        api.CmdCopyImageToBuffer(cmd, image, ImageLayout.TransferSrcOptimal, _buffer, 1, in region);

        // TransferSrcOptimal -> PresentSrcKhr so presentation sees the expected layout.
        var toPresent = new ImageMemoryBarrier(StructureType.ImageMemoryBarrier)
        {
            Image = image,
            SrcAccessMask = AccessFlags.TransferReadBit,
            DstAccessMask = 0,
            OldLayout = ImageLayout.TransferSrcOptimal,
            NewLayout = ImageLayout.PresentSrcKhr,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            SubresourceRange = subresource
        };
        api.CmdPipelineBarrier(cmd,
            PipelineStageFlags.TransferBit, PipelineStageFlags.BottomOfPipeBit,
            0, 0, null, 0, null, 1, in toPresent);

        _hasContent = true;
    }

    /// <summary>Read the most recently copied frame into a BGRA <see cref="Bitmap"/>.</summary>
    public Bitmap CaptureBitmap()
    {
        if (_buffer.Handle == 0 || !_hasContent)
            throw new VulkanException("No GPU frame is available for capture yet.");

        // The copy is recorded in the frame's command buffer; wait for the device so the
        // buffer contents reflect the last presented frame before mapping.
        VulkanDevice.ThrowIfFailed(_device.Api.DeviceWaitIdle(_device.Device), "vkDeviceWaitIdle");

        var size = (int)(_width * _height * 4);
        var bitmap = new Bitmap((int)_width, (int)_height);
        if (_usesBgraFormat)
        {
            new ReadOnlySpan<byte>(_mapped, size).CopyTo(bitmap.Pixels.AsSpan());
        }
        else
        {
            var source = new ReadOnlySpan<byte>(_mapped, size);
            var destination = bitmap.Pixels.AsSpan();
            for (var i = 0; i < size; i += 4)
            {
                destination[i] = source[i + 2];
                destination[i + 1] = source[i + 1];
                destination[i + 2] = source[i];
                destination[i + 3] = source[i + 3];
            }
        }
        return bitmap;
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _device.Api.GetPhysicalDeviceMemoryProperties(_device.PhysicalDevice, out var memProps);
        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << (int)i)) != 0 &&
                (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                return i;
        }
        throw new VulkanException("No suitable memory type found for the readback buffer.");
    }

    private void Destroy()
    {
        var api = _device.Api;
        if (_mapped != null && _memory.Handle != 0) api.UnmapMemory(_device.Device, _memory);
        if (_buffer.Handle != 0) { api.DestroyBuffer(_device.Device, _buffer, null); _buffer = default; }
        if (_memory.Handle != 0) { api.FreeMemory(_device.Device, _memory, null); _memory = default; }
        _mapped = null;
        _capacity = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _device.Api.DeviceWaitIdle(_device.Device);
        Destroy();
    }
}
