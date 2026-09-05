using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using VulkanFilter = Silk.NET.Vulkan.Filter;
using VulkanImageView = Silk.NET.Vulkan.ImageView;

namespace Square.Backends.Vulkan;

/// <summary>
/// Manages a Vulkan texture atlas (RGBA8). Contains a 1x1 white pixel at (0,0)
/// for solid-color rendering, and dynamically allocated regions for glyphs/images.
/// Reference: ImGui font atlas + vkvg texture management.
/// </summary>
internal sealed unsafe class VulkanTextureAtlas : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly VulkanPipeline _pipeline;

    public static readonly int AtlasWidth = ResolveAtlasSize();
    public static readonly int AtlasHeight = AtlasWidth;

    private Silk.NET.Vulkan.Image _image;
    private DeviceMemory _imageMemory;
    private VulkanImageView _imageView;
    private Sampler _sampler;
    private DescriptorPool _descriptorPool;
    private Buffer _stagingBuffer;
    private DeviceMemory _stagingMemory;
    private void* _stagingMapped;
    private ulong _stagingCapacity;
    private ulong _pendingBytes;
    private readonly List<PendingUpload> _pendingUploads = new(64);
    private bool _disposed;
    private bool _uploaded;

    // Simple row-based allocator for atlas regions
    private int _cursorX = 2; // Start after white pixel
    private int _cursorY = 1;
    private int _rowHeight = 0;

    public DescriptorSet DescriptorSet { get; private set; }

    /// <summary>UV rect fixed at the center of the 1x1 white pixel at (0,0).</summary>
    public static readonly (float U0, float V0, float U1, float V1) WhitePixelUV =
        (0.5f / AtlasWidth, 0.5f / AtlasHeight, 0.5f / AtlasWidth, 0.5f / AtlasHeight);

    private static int ResolveAtlasSize() => Environment.GetEnvironmentVariable("SQUARE_VULKAN_ATLAS_SIZE") switch
    {
        "512" => 512,
        "2048" => 2048,
        _ => 1024
    };

    public VulkanTextureAtlas(VulkanDevice device, VulkanPipeline pipeline)
    {
        _device = device;
        _pipeline = pipeline;
        try
        {
            CreateImage();
            CreateSampler();
            CreateDescriptorSet();
            EnsureStagingBuffer(4);
            var whitePixel = new Span<byte>(_stagingMapped, 4);
            whitePixel.Fill(255);
            _pendingUploads.Add(new PendingUpload(0, 0, 1, 1, 0));
            _pendingBytes = 4;
            Flush();
            DestroyStagingBuffer();
        }
        catch
        {
            CleanupResources(waitForDevice: false);
            throw;
        }
    }

    /// <summary>Allocate a region in the atlas. Returns (x, y) pixel position.</summary>
    public (int X, int Y) Allocate(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > AtlasWidth || height > AtlasHeight)
            throw new ArgumentOutOfRangeException(nameof(width), "Atlas regions must have positive dimensions within the atlas bounds.");
        if (_cursorX + width > AtlasWidth)
        {
            _cursorX = 1;
            _cursorY += _rowHeight + 1;
            _rowHeight = 0;
        }
        if (_cursorY + height > AtlasHeight)
            throw new VulkanException("Texture atlas is full.");

        var x = _cursorX;
        var y = _cursorY;
        _cursorX += width + 1;
        _rowHeight = Math.Max(_rowHeight, height);
        return (x, y);
    }

    /// <summary>Write RGBA pixel data into atlas at given position.</summary>
    public void WriteRegion(int x, int y, int width, int height, ReadOnlySpan<byte> rgbaPixels)
    {
        ValidateRegion(x, y, width, height);
        var byteCount = checked(width * height * 4);
        if (rgbaPixels.Length < byteCount)
            throw new ArgumentException("RGBA data is smaller than the destination region.", nameof(rgbaPixels));
        var offset = ReserveUpload(byteCount);
        rgbaPixels[..byteCount].CopyTo(new Span<byte>((byte*)_stagingMapped + (nint)offset, byteCount));
        _pendingUploads.Add(new PendingUpload(x, y, width, height, offset));
    }

    public void WriteBgraRegion(int x, int y, int width, int height, ReadOnlySpan<byte> bgraPixels)
    {
        ValidateRegion(x, y, width, height);
        var byteCount = checked(width * height * 4);
        if (bgraPixels.Length < byteCount)
            throw new ArgumentException("BGRA data is smaller than the destination region.", nameof(bgraPixels));

        var offset = ReserveUpload(byteCount);
        var destination = new Span<byte>((byte*)_stagingMapped + (nint)offset, byteCount);
        for (var row = 0; row < height; row++)
        {
            var sourceOffset = row * width * 4;
            var destinationOffset = row * width * 4;
            for (var column = 0; column < width; column++)
            {
                var source = sourceOffset + column * 4;
                var target = destinationOffset + column * 4;
                destination[target] = bgraPixels[source + 2];
                destination[target + 1] = bgraPixels[source + 1];
                destination[target + 2] = bgraPixels[source];
                destination[target + 3] = bgraPixels[source + 3];
            }
        }
        _pendingUploads.Add(new PendingUpload(x, y, width, height, offset));
    }

    /// <summary>Write coverage (alpha-only) data as white pixels with varying alpha.</summary>
    public void WriteCoverageRegion(int x, int y, int width, int height, int stride, ReadOnlySpan<byte> coverage)
    {
        ValidateRegion(x, y, width, height);
        if (stride < width) throw new ArgumentOutOfRangeException(nameof(stride));
        var byteCount = checked(width * height * 4);
        var offset = ReserveUpload(byteCount);
        var destination = new Span<byte>((byte*)_stagingMapped + (nint)offset, byteCount);
        for (var row = 0; row < height; row++)
        {
            var srcOffset = row * stride;
            var dstOffset = row * width * 4;
            for (var col = 0; col < width; col++)
            {
                var srcIndex = srcOffset + col;
                // GDI GetGlyphOutline can return a buffer smaller than stride*height for
                // certain glyphs; skip out-of-range coverage samples (mirrors the software
                // renderer's defensive bounds check in RenderContext.DrawGlyph).
                var alpha = srcIndex < coverage.Length ? coverage[srcIndex] : (byte)0;
                var dst = dstOffset + col * 4;
                destination[dst] = 255;     // R
                destination[dst + 1] = 255; // G
                destination[dst + 2] = 255; // B
                destination[dst + 3] = alpha; // A
            }
        }
        _pendingUploads.Add(new PendingUpload(x, y, width, height, offset));
    }

    public void WritePaddedCoverageRegion(int x, int y, int width, int height, int stride,
        ReadOnlySpan<byte> coverage, int border)
    {
        var paddedWidth = width + border * 2;
        var paddedHeight = height + border * 2;
        ValidateRegion(x, y, paddedWidth, paddedHeight);
        if (stride < width) throw new ArgumentOutOfRangeException(nameof(stride));

        var byteCount = checked(paddedWidth * paddedHeight * 4);
        var offset = ReserveUpload(byteCount);
        var destination = new Span<byte>((byte*)_stagingMapped + (nint)offset, byteCount);
        for (var pixel = 0; pixel < paddedWidth * paddedHeight; pixel++)
        {
            var target = pixel * 4;
            destination[target] = 255;
            destination[target + 1] = 255;
            destination[target + 2] = 255;
            destination[target + 3] = 0;
        }

        for (var row = 0; row < height; row++)
        {
            var sourceOffset = row * stride;
            var destinationOffset = ((row + border) * paddedWidth + border) * 4;
            for (var column = 0; column < width; column++)
            {
                var sourceIndex = sourceOffset + column;
                destination[destinationOffset + column * 4 + 3] =
                    sourceIndex < coverage.Length ? coverage[sourceIndex] : (byte)0;
            }
        }
        _pendingUploads.Add(new PendingUpload(x, y, paddedWidth, paddedHeight, offset));
    }

    /// <summary>Upload dirty atlas data to GPU.</summary>
    public void Flush()
    {
        if (_pendingUploads.Count == 0) return;
        UploadTexture();
        _pendingUploads.Clear();
        _pendingBytes = 0;
    }

    public (float U0, float V0, float U1, float V1) GetUV(int x, int y, int width, int height)
    {
        return (
            x / (float)AtlasWidth,
            y / (float)AtlasHeight,
            (x + width) / (float)AtlasWidth,
            (y + height) / (float)AtlasHeight);
    }

    private void CreateImage()
    {
        var api = _device.Api;

        var imageInfo = new ImageCreateInfo(StructureType.ImageCreateInfo)
        {
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D((uint)AtlasWidth, (uint)AtlasHeight, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        VulkanDevice.ThrowIfFailed(api.CreateImage(_device.Device, in imageInfo, null, out _image), "vkCreateImage");

        api.GetImageMemoryRequirements(_device.Device, _image, out var memReqs);
        var allocInfo = new MemoryAllocateInfo(StructureType.MemoryAllocateInfo)
        {
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = FindMemoryType(memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        VulkanDevice.ThrowIfFailed(api.AllocateMemory(_device.Device, in allocInfo, null, out _imageMemory), "vkAllocateMemory");
        VulkanDevice.ThrowIfFailed(api.BindImageMemory(_device.Device, _image, _imageMemory, 0), "vkBindImageMemory");

        var viewInfo = new ImageViewCreateInfo(StructureType.ImageViewCreateInfo)
        {
            Image = _image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        VulkanDevice.ThrowIfFailed(api.CreateImageView(_device.Device, in viewInfo, null, out _imageView), "vkCreateImageView");
    }

    private void CreateSampler()
    {
        var samplerInfo = new SamplerCreateInfo(StructureType.SamplerCreateInfo)
        {
            MagFilter = VulkanFilter.Linear,
            MinFilter = VulkanFilter.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            AnisotropyEnable = false,
            BorderColor = BorderColor.FloatTransparentBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = false,
            MipmapMode = SamplerMipmapMode.Linear,
            MipLodBias = 0,
            MinLod = 0,
            MaxLod = 1
        };
        VulkanDevice.ThrowIfFailed(_device.Api.CreateSampler(_device.Device, in samplerInfo, null, out _sampler), "vkCreateSampler");
    }

    private void CreateDescriptorSet()
    {
        var api = _device.Api;

        var poolSize = new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 1);
        var poolInfo = new DescriptorPoolCreateInfo(StructureType.DescriptorPoolCreateInfo)
        {
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize
        };
        VulkanDevice.ThrowIfFailed(api.CreateDescriptorPool(_device.Device, in poolInfo, null, out _descriptorPool), "vkCreateDescriptorPool");

        var layout = _pipeline.DescriptorSetLayout;
        var allocInfo = new DescriptorSetAllocateInfo(StructureType.DescriptorSetAllocateInfo)
        {
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };
        VulkanDevice.ThrowIfFailed(api.AllocateDescriptorSets(_device.Device, in allocInfo, out var descriptorSet), "vkAllocateDescriptorSets");
        DescriptorSet = descriptorSet;

        UpdateDescriptorSet();
    }

    private void UpdateDescriptorSet()
    {
        var imageInfo = new DescriptorImageInfo(_sampler, _imageView, ImageLayout.ShaderReadOnlyOptimal);
        var write = new WriteDescriptorSet(StructureType.WriteDescriptorSet)
        {
            DstSet = DescriptorSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo
        };
        _device.Api.UpdateDescriptorSets(_device.Device, 1, in write, 0, null);
    }

    private unsafe void UploadTexture()
    {
        var api = _device.Api;
        // Record and submit transfer command
        var cmdAllocInfo = new CommandBufferAllocateInfo(StructureType.CommandBufferAllocateInfo)
        {
            CommandPool = _device.CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };
        VulkanDevice.ThrowIfFailed(api.AllocateCommandBuffers(_device.Device, in cmdAllocInfo, out var cmd), "vkAllocateCommandBuffers");

        var beginInfo = new CommandBufferBeginInfo(StructureType.CommandBufferBeginInfo)
        {
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        VulkanDevice.ThrowIfFailed(api.BeginCommandBuffer(cmd, in beginInfo), "vkBeginCommandBuffer");

        // Transition image to TransferDst
        var barrier1 = new ImageMemoryBarrier(StructureType.ImageMemoryBarrier)
        {
            Image = _image,
            OldLayout = _uploaded ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcAccessMask = _uploaded ? AccessFlags.ShaderReadBit : 0,
            DstAccessMask = AccessFlags.TransferWriteBit,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        api.CmdPipelineBarrier(cmd, _uploaded ? PipelineStageFlags.FragmentShaderBit : PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit,
            0, 0, null, 0, null, 1, in barrier1);

        if (!_uploaded)
        {
            var clear = new ClearColorValue(0f, 0f, 0f, 0f);
            var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
            api.CmdClearColorImage(cmd, _image, ImageLayout.TransferDstOptimal, in clear, 1, in range);
        }

        foreach (var upload in _pendingUploads)
        {
            var region = new BufferImageCopy
            {
                BufferOffset = upload.BufferOffset,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageOffset = new Offset3D(upload.X, upload.Y, 0),
                ImageExtent = new Extent3D((uint)upload.Width, (uint)upload.Height, 1)
            };
            api.CmdCopyBufferToImage(cmd, _stagingBuffer, _image, ImageLayout.TransferDstOptimal, 1, in region);
        }

        // Transition image to ShaderReadOnly
        var barrier2 = new ImageMemoryBarrier(StructureType.ImageMemoryBarrier)
        {
            Image = _image,
            OldLayout = ImageLayout.TransferDstOptimal,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        api.CmdPipelineBarrier(cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, 1, in barrier2);

        VulkanDevice.ThrowIfFailed(api.EndCommandBuffer(cmd), "vkEndCommandBuffer");

        // Submit
        var submitInfo = new SubmitInfo(StructureType.SubmitInfo)
        {
            CommandBufferCount = 1,
            PCommandBuffers = &cmd
        };
        VulkanDevice.ThrowIfFailed(api.QueueSubmit(_device.GraphicsQueue, 1, in submitInfo, default), "vkQueueSubmit");
        VulkanDevice.ThrowIfFailed(api.QueueWaitIdle(_device.GraphicsQueue), "vkQueueWaitIdle");
        _uploaded = true;

        // Cleanup command buffer; staging storage is retained for subsequent atlas updates.
        api.FreeCommandBuffers(_device.Device, _device.CommandPool, 1, in cmd);
    }

    private void EnsureStagingBuffer(ulong requiredSize)
    {
        if (_stagingBuffer.Handle != 0 && requiredSize <= _stagingCapacity) return;

        Buffer newBuffer = default;
        DeviceMemory newMemory = default;
        void* newMapped = null;
        var newCapacity = Math.Max(requiredSize, Math.Max(4096UL, _stagingCapacity * 2));
        try
        {
            var bufferInfo = new BufferCreateInfo(StructureType.BufferCreateInfo)
            {
                Size = newCapacity,
                Usage = BufferUsageFlags.TransferSrcBit,
                SharingMode = SharingMode.Exclusive
            };
            VulkanDevice.ThrowIfFailed(_device.Api.CreateBuffer(_device.Device, in bufferInfo, null, out newBuffer), "vkCreateBuffer");

            _device.Api.GetBufferMemoryRequirements(_device.Device, newBuffer, out var requirements);
            var allocation = new MemoryAllocateInfo(StructureType.MemoryAllocateInfo)
            {
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
            };
            VulkanDevice.ThrowIfFailed(_device.Api.AllocateMemory(_device.Device, in allocation, null, out newMemory), "vkAllocateMemory");
            VulkanDevice.ThrowIfFailed(_device.Api.BindBufferMemory(_device.Device, newBuffer, newMemory, 0), "vkBindBufferMemory");
            VulkanDevice.ThrowIfFailed(_device.Api.MapMemory(_device.Device, newMemory, 0, newCapacity, 0, &newMapped), "vkMapMemory");
        }
        catch
        {
            if (newMapped != null && newMemory.Handle != 0) _device.Api.UnmapMemory(_device.Device, newMemory);
            if (newBuffer.Handle != 0) _device.Api.DestroyBuffer(_device.Device, newBuffer, null);
            if (newMemory.Handle != 0) _device.Api.FreeMemory(_device.Device, newMemory, null);
            throw;
        }

        if (_stagingMapped != null && _pendingBytes > 0)
            new ReadOnlySpan<byte>(_stagingMapped, checked((int)_pendingBytes))
                .CopyTo(new Span<byte>(newMapped, checked((int)_pendingBytes)));

        DestroyStagingBuffer();
        _stagingBuffer = newBuffer;
        _stagingMemory = newMemory;
        _stagingMapped = newMapped;
        _stagingCapacity = newCapacity;
    }

    private ulong ReserveUpload(int byteCount)
    {
        var offset = _pendingBytes;
        var required = checked(offset + (ulong)byteCount);
        EnsureStagingBuffer(required);
        _pendingBytes = required;
        return offset;
    }

    private void DestroyStagingBuffer()
    {
        if (_stagingMapped != null && _stagingMemory.Handle != 0)
            _device.Api.UnmapMemory(_device.Device, _stagingMemory);
        if (_stagingBuffer.Handle != 0)
            _device.Api.DestroyBuffer(_device.Device, _stagingBuffer, null);
        if (_stagingMemory.Handle != 0)
            _device.Api.FreeMemory(_device.Device, _stagingMemory, null);
        _stagingBuffer = default;
        _stagingMemory = default;
        _stagingMapped = null;
        _stagingCapacity = 0;
    }

    private void ValidateRegion(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x > AtlasWidth - width || y > AtlasHeight - height)
            throw new ArgumentOutOfRangeException(nameof(width), "Atlas region is outside the texture bounds.");
    }

    private readonly record struct PendingUpload(int X, int Y, int Width, int Height, ulong BufferOffset);

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
        CleanupResources(waitForDevice: true);
    }

    private void CleanupResources(bool waitForDevice)
    {
        var api = _device.Api;
        if (waitForDevice && _device.Device.Handle != 0) api.DeviceWaitIdle(_device.Device);
        DestroyStagingBuffer();
        if (_descriptorPool.Handle != 0) api.DestroyDescriptorPool(_device.Device, _descriptorPool, null);
        if (_sampler.Handle != 0) api.DestroySampler(_device.Device, _sampler, null);
        if (_imageView.Handle != 0) api.DestroyImageView(_device.Device, _imageView, null);
        if (_image.Handle != 0) api.DestroyImage(_device.Device, _image, null);
        if (_imageMemory.Handle != 0) api.FreeMemory(_device.Device, _imageMemory, null);
    }
}
