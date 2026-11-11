using Silk.NET.Vulkan;
using VulkanFilter = Silk.NET.Vulkan.Filter;
using VulkanImage = Silk.NET.Vulkan.Image;

namespace Square.Backends.Vulkan;

/// <summary>A device-local color target whose resolved image can be sampled by its parent pass.</summary>
/// <remarks>The owner must finish GPU work before disposing or reusing this target.</remarks>
internal sealed unsafe class VulkanOffscreenLayer : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly SampleCountFlags _samples;
    private VulkanImage _image;
    private DeviceMemory _memory;
    private ImageView _imageView;
    private VulkanImage _multisampleImage;
    private DeviceMemory _multisampleMemory;
    private ImageView _multisampleView;
    private Framebuffer _framebuffer;
    private RenderPass _renderPass;
    private Sampler _sampler;
    private bool _disposed;

    public uint Width { get; }
    public uint Height { get; }
    public ulong Bytes { get; private set; }
    public Framebuffer Framebuffer => _framebuffer;
    public RenderPass RenderPass => _renderPass;
    /// <summary>The single-sample resolved image view, never the multisample attachment.</summary>
    public ImageView ImageView => _imageView;
    public Sampler Sampler => _sampler;

    public VulkanOffscreenLayer(VulkanDevice device, Format format, uint width, uint height)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (width == 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height == 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (format == Format.Undefined) throw new ArgumentOutOfRangeException(nameof(format));
        _device = device;
        _samples = device.ColorSampleCount;
        Width = width;
        Height = height;
        try
        {
            CreateAttachment(format, SampleCountFlags.Count1Bit,
                ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
                ref _image, ref _memory, ref _imageView);
            if (_samples != SampleCountFlags.Count1Bit)
                CreateAttachment(format, _samples,
                    ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransientAttachmentBit,
                    ref _multisampleImage, ref _multisampleMemory, ref _multisampleView);
            CreateRenderPass(format);
            CreateFramebuffer();
            CreateSampler();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void CreateAttachment(Format format, SampleCountFlags samples, ImageUsageFlags usage,
        ref VulkanImage image, ref DeviceMemory memory, ref ImageView view)
    {
        var api = _device.Api;
        var imageInfo = new ImageCreateInfo(StructureType.ImageCreateInfo)
        {
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D(Width, Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = samples,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        VulkanDevice.ThrowIfFailed(api.CreateImage(_device.Device, in imageInfo, null, out image), "vkCreateImage(offscreen)");
        api.GetImageMemoryRequirements(_device.Device, image, out var requirements);
        var allocation = new MemoryAllocateInfo(StructureType.MemoryAllocateInfo)
        {
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindDeviceLocalMemory(requirements.MemoryTypeBits)
        };
        VulkanDevice.ThrowIfFailed(api.AllocateMemory(_device.Device, in allocation, null, out memory), "vkAllocateMemory(offscreen)");
        Bytes = checked(Bytes + requirements.Size);
        VulkanDevice.ThrowIfFailed(api.BindImageMemory(_device.Device, image, memory, 0), "vkBindImageMemory(offscreen)");
        var viewInfo = new ImageViewCreateInfo(StructureType.ImageViewCreateInfo)
        {
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        VulkanDevice.ThrowIfFailed(api.CreateImageView(_device.Device, in viewInfo, null, out view), "vkCreateImageView(offscreen)");
    }

    private uint FindDeviceLocalMemory(uint mask)
    {
        _device.Api.GetPhysicalDeviceMemoryProperties(_device.PhysicalDevice, out var properties);
        for (uint index = 0; index < properties.MemoryTypeCount; index++)
            if ((mask & (1u << (int)index)) != 0 &&
                (properties.MemoryTypes[(int)index].PropertyFlags & MemoryPropertyFlags.DeviceLocalBit) != 0)
                return index;
        throw new VulkanException("No device-local memory type for the offscreen color attachment.");
    }

    private void CreateRenderPass(Format format)
    {
        var multisampled = _samples != SampleCountFlags.Count1Bit;
        var attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = format,
            Samples = _samples,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = multisampled ? AttachmentStoreOp.DontCare : AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = multisampled ? ImageLayout.ColorAttachmentOptimal : ImageLayout.ShaderReadOnlyOptimal
        };
        attachments[1] = new AttachmentDescription
        {
            Format = format,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal
        };
        var color = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var resolve = new AttachmentReference(1, ImageLayout.ColorAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &color,
            PResolveAttachments = multisampled ? &resolve : null
        };
        // These dependencies must match the root render pass for pipeline compatibility.
        // Previous-frame reuse is protected by the owner's existing queue-idle/fence wait.
        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DependencyFlags = 0
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.FragmentShaderBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            DependencyFlags = 0
        };
        var info = new RenderPassCreateInfo(StructureType.RenderPassCreateInfo)
        {
            AttachmentCount = multisampled ? 2u : 1u,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies
        };
        VulkanDevice.ThrowIfFailed(_device.Api.CreateRenderPass(_device.Device, in info, null, out _renderPass), "vkCreateRenderPass(offscreen)");
    }

    private void CreateFramebuffer()
    {
        var views = stackalloc ImageView[2];
        views[0] = _samples == SampleCountFlags.Count1Bit ? _imageView : _multisampleView;
        views[1] = _imageView;
        var info = new FramebufferCreateInfo(StructureType.FramebufferCreateInfo)
        {
            RenderPass = _renderPass,
            AttachmentCount = _samples == SampleCountFlags.Count1Bit ? 1u : 2u,
            PAttachments = views,
            Width = Width,
            Height = Height,
            Layers = 1
        };
        VulkanDevice.ThrowIfFailed(_device.Api.CreateFramebuffer(_device.Device, in info, null, out _framebuffer), "vkCreateFramebuffer(offscreen)");
    }

    private void CreateSampler()
    {
        var info = new SamplerCreateInfo(StructureType.SamplerCreateInfo)
        {
            MagFilter = VulkanFilter.Linear,
            MinFilter = VulkanFilter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            AnisotropyEnable = false,
            CompareEnable = false,
            MinLod = 0,
            MaxLod = 0,
            BorderColor = BorderColor.FloatTransparentBlack,
            UnnormalizedCoordinates = false
        };
        VulkanDevice.ThrowIfFailed(_device.Api.CreateSampler(_device.Device, in info, null, out _sampler), "vkCreateSampler(offscreen)");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var api = _device.Api;
        if (_framebuffer.Handle != 0) api.DestroyFramebuffer(_device.Device, _framebuffer, null);
        if (_renderPass.Handle != 0) api.DestroyRenderPass(_device.Device, _renderPass, null);
        if (_sampler.Handle != 0) api.DestroySampler(_device.Device, _sampler, null);
        if (_imageView.Handle != 0) api.DestroyImageView(_device.Device, _imageView, null);
        if (_multisampleView.Handle != 0) api.DestroyImageView(_device.Device, _multisampleView, null);
        if (_image.Handle != 0) api.DestroyImage(_device.Device, _image, null);
        if (_multisampleImage.Handle != 0) api.DestroyImage(_device.Device, _multisampleImage, null);
        if (_memory.Handle != 0) api.FreeMemory(_device.Device, _memory, null);
        if (_multisampleMemory.Handle != 0) api.FreeMemory(_device.Device, _multisampleMemory, null);
        _framebuffer = default;
        _renderPass = default;
        _sampler = default;
        _imageView = default;
        _multisampleView = default;
        _image = default;
        _multisampleImage = default;
        _memory = default;
        _multisampleMemory = default;
        Bytes = 0;
    }
}
