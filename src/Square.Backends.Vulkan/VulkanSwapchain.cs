using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using VulkanImageView = Silk.NET.Vulkan.ImageView;
using VulkanResult = Silk.NET.Vulkan.Result;

namespace Square.Backends.Vulkan;

/// <summary>
/// Manages Vulkan swapchain, image views, render pass and framebuffers.
/// </summary>
internal sealed unsafe class VulkanSwapchain : IDisposable
{
    private readonly VulkanDevice _device;
    private SurfaceKHR _surface;
    private KhrSwapchain _khrSwapchain = null!;

    public SwapchainKHR Swapchain { get; private set; }
    /// <summary>True when a usable swapchain exists; false while the surface is lost or minimised.</summary>
    public bool IsAvailable => !_surfaceUnavailable && Swapchain.Handle != 0;
    public Format ImageFormat { get; private set; }
    public ColorSpaceKHR ImageColorSpace { get; private set; }
    public Extent2D Extent { get; private set; }
    public Silk.NET.Vulkan.Image[] Images { get; private set; } = [];
    public VulkanImageView[] ImageViews { get; private set; } = [];
    public Framebuffer[] Framebuffers { get; private set; } = [];
    public RenderPass RenderPass { get; private set; }
    public uint ImageCount { get; private set; }
    public bool VSync { get; }
    public bool SupportsReadback { get; }
    public bool UsesBgraFormat { get; private set; }

    private Silk.NET.Vulkan.Image[] _multisampleImages = [];
    private DeviceMemory[] _multisampleImageMemories = [];
    private VulkanImageView[] _multisampleImageViews = [];

    private Semaphore[] _imageAvailableSemaphores = [];
    private Semaphore[] _renderFinishedSemaphores = [];
    private Fence[] _inFlightFences = [];
    private int _currentFrame;
    private uint _currentImageIndex;
    private uint _requestedWidth;
    private uint _requestedHeight;
    private bool _recreateAfterPresent;
    private SampleCountFlags _renderPassSampleCount;
    private bool _disposed;
    private bool _surfaceUnavailable;

    private const int MaxFramesInFlight = 2;
    public int FrameCount => MaxFramesInFlight;
    public int CurrentFrameIndex => _currentFrame;
    public ulong Generation { get; private set; }

    public Fence GetInFlightFence(int index)
    {
        if ((uint)index >= MaxFramesInFlight) throw new ArgumentOutOfRangeException(nameof(index));
        return _inFlightFences[index];
    }

    public VulkanSwapchain(VulkanDevice device, SurfaceKHR surface, uint width, uint height, bool vsync, bool enableReadback)
    {
        _device = device;
        _surface = surface;
        VSync = vsync;
        SupportsReadback = enableReadback;

        if (!device.Api.TryGetDeviceExtension(device.Instance, device.Device, out _khrSwapchain))
            throw new VulkanException("VK_KHR_swapchain extension not available.");

        // ImageFormat must be resolved before the render pass is created, otherwise the
        // color attachment would use Format.Undefined and fragment output gets discarded.
        var surfaceFormat = SelectFormat(device.PhysicalDevice);
        ImageFormat = surfaceFormat.Format;
        ImageColorSpace = surfaceFormat.ColorSpace;
        UsesBgraFormat = ImageFormat is Format.B8G8R8A8Unorm or Format.B8G8R8A8Srgb;
        try
        {
            CreateRenderPass();
            _renderPassSampleCount = _device.ColorSampleCount;
            CreateSyncObjects();
            Recreate(width, height);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Retires presentation before replacing its surface; Recreate builds new images afterwards.</summary>
    internal void RecoverSurface()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _surfaceUnavailable = true;
        VulkanDevice.ThrowIfFailed(_device.Api.DeviceWaitIdle(_device.Device), "vkDeviceWaitIdle(surface recovery)");
        DestroySwapchainImageResources();
        if (Swapchain.Handle != 0)
        {
            _khrSwapchain.DestroySwapchain(_device.Device, Swapchain, null);
            Swapchain = default;
        }
        // VUID-vkDestroySurfaceKHR-surface-01266 requires all associated swapchains
        // to be destroyed first. If creation fails, the old surface remains owned.
        _surface = _device.RecreateSurface();
    }

    public void Recreate(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _requestedWidth = width;
        _requestedHeight = height;
        if (width == 0 || height == 0)
        {
            _surfaceUnavailable = true;
            return;
        }
        var api = _device.Api;
        var physicalDevice = _device.PhysicalDevice;
        var capsResult = _device.KhrSurface.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, _surface, out var caps);
        if (capsResult == VulkanResult.ErrorSurfaceLostKhr)
        {
            RecoverSurface();
            capsResult = _device.KhrSurface.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, _surface, out caps);
        }
        if (capsResult == VulkanResult.ErrorSurfaceLostKhr)
        {
            // Pause and let EnsureFrame retry at a bounded rate while presentation
            // remains unavailable; the previous swapchain has already been retired.
            _surfaceUnavailable = true;
            return;
        }
        VulkanDevice.ThrowIfFailed(capsResult, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

        var newExtent = caps.CurrentExtent.Width != uint.MaxValue
            ? caps.CurrentExtent
            : new Extent2D(Math.Clamp(width, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                Math.Clamp(height, caps.MinImageExtent.Height, caps.MaxImageExtent.Height));
        if (newExtent.Width < 1 || newExtent.Height < 1)
        {
            _surfaceUnavailable = true;
            return;
        }
        var useExtraImage = Environment.GetEnvironmentVariable("SQUARE_VULKAN_EXTRA_SWAPCHAIN_IMAGE") is "1" or "true";
        var imageCount = caps.MinImageCount + (useExtraImage ? 1u : 0u);
        if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount) imageCount = caps.MaxImageCount;
        var requiredUsage = ImageUsageFlags.ColorAttachmentBit |
            (SupportsReadback ? ImageUsageFlags.TransferSrcBit : 0);
        if ((caps.SupportedUsageFlags & requiredUsage) != requiredUsage)
            throw new VulkanException(SupportsReadback
                ? "The Vulkan surface must support color attachment and transfer-source swapchain images."
                : "The Vulkan surface must support color attachment swapchain images.");

        // Frame-slot semaphores/fences remain stable; only image-owned resources are replaced.
        _surfaceUnavailable = true;
        VulkanDevice.ThrowIfFailed(api.DeviceWaitIdle(_device.Device), "vkDeviceWaitIdle");
        var oldSwapchain = Swapchain;
        DestroySwapchainImageResources();
        Swapchain = default;
        try
        {
            var createInfo = new SwapchainCreateInfoKHR(StructureType.SwapchainCreateInfoKhr)
            {
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = ImageFormat,
                ImageColorSpace = ImageColorSpace,
                ImageExtent = newExtent,
                ImageArrayLayers = 1,
                ImageUsage = requiredUsage,
                PreTransform = caps.CurrentTransform,
                CompositeAlpha = SelectCompositeAlpha(caps.SupportedCompositeAlpha),
                PresentMode = SelectPresentMode(physicalDevice),
                Clipped = true,
                OldSwapchain = oldSwapchain
            };
            VulkanResult result;
            SwapchainKHR swapchain;
            if (_device.GraphicsQueueFamily == _device.PresentQueueFamily)
            {
                createInfo.ImageSharingMode = SharingMode.Exclusive;
                result = _khrSwapchain.CreateSwapchain(_device.Device, in createInfo, null, out swapchain);
            }
            else
            {
                var queueFamilies = stackalloc uint[2];
                queueFamilies[0] = _device.GraphicsQueueFamily;
                queueFamilies[1] = _device.PresentQueueFamily;
                createInfo.ImageSharingMode = SharingMode.Concurrent;
                createInfo.QueueFamilyIndexCount = 2;
                createInfo.PQueueFamilyIndices = queueFamilies;
                result = _khrSwapchain.CreateSwapchain(_device.Device, in createInfo, null, out swapchain);
            }
            VulkanDevice.ThrowIfFailed(result, "vkCreateSwapchainKHR");
            Swapchain = swapchain;
            Extent = newExtent;
            if (RenderPass.Handle == 0 || _renderPassSampleCount != _device.ColorSampleCount)
            {
                if (RenderPass.Handle != 0) api.DestroyRenderPass(_device.Device, RenderPass, null);
                RenderPass = default;
                CreateRenderPass();
                _renderPassSampleCount = _device.ColorSampleCount;
            }
            uint count = 0;
            VulkanDevice.ThrowIfFailed(_khrSwapchain.GetSwapchainImages(_device.Device, Swapchain, ref count, null), "vkGetSwapchainImagesKHR");
            var images = new Silk.NET.Vulkan.Image[count];
            fixed (Silk.NET.Vulkan.Image* pImages = images)
                VulkanDevice.ThrowIfFailed(_khrSwapchain.GetSwapchainImages(_device.Device, Swapchain, ref count, pImages), "vkGetSwapchainImagesKHR");
            if (count == 0) throw new VulkanException("The Vulkan swapchain has no images.");
            if (count != images.Length) Array.Resize(ref images, checked((int)count));
            ImageCount = count;
            Images = images;
            ImageViews = new VulkanImageView[count];
            for (var i = 0; i < count; i++)
            {
                var viewInfo = new ImageViewCreateInfo(StructureType.ImageViewCreateInfo)
                {
                    Image = images[i],
                    ViewType = ImageViewType.Type2D,
                    Format = ImageFormat,
                    SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
                };
                VulkanDevice.ThrowIfFailed(api.CreateImageView(_device.Device, in viewInfo, null, out var view), "vkCreateImageView");
                ImageViews[i] = view;
            }
            CreateMultisampleResources();
            CreateRenderFinishedSemaphores();
            CreateFramebuffers();
            _currentImageIndex = 0;
            _recreateAfterPresent = false;
            _surfaceUnavailable = false;
            Generation++;
        }
        catch
        {
            // Creating with oldSwapchain retires it, so failed replacement cannot reuse old images.
            DestroySwapchainImageResources();
            if (Swapchain.Handle != 0) _khrSwapchain.DestroySwapchain(_device.Device, Swapchain, null);
            Swapchain = default;
            Extent = default;
            throw;
        }
        finally
        {
            if (oldSwapchain.Handle != 0) _khrSwapchain.DestroySwapchain(_device.Device, oldSwapchain, null);
        }
    }

    private SurfaceFormatKHR SelectFormat(PhysicalDevice physicalDevice)
    {
        uint count = 0;
        _device.KhrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, _surface, ref count, null);
        if (count == 0) throw new VulkanException("The Vulkan surface exposes no supported formats.");

        var formats = new SurfaceFormatKHR[count];
        fixed (SurfaceFormatKHR* pFormats = formats)
            _device.KhrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, _surface, ref count, pFormats);

        foreach (var fmt in formats)
        {
            if (fmt.Format == Format.B8G8R8A8Unorm && fmt.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                return fmt;
        }
        foreach (var fmt in formats)
        {
            if (fmt.Format is Format.B8G8R8A8Srgb or Format.B8G8R8A8Unorm)
                return fmt;
        }
        foreach (var fmt in formats)
        {
            if (fmt.Format == Format.R8G8B8A8Unorm && fmt.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                return fmt;
        }
        foreach (var fmt in formats)
        {
            if (fmt.Format is Format.R8G8B8A8Srgb or Format.R8G8B8A8Unorm)
                return fmt;
        }
        if (formats.Length == 1 && formats[0].Format == Format.Undefined)
            return new SurfaceFormatKHR(Format.B8G8R8A8Unorm, formats[0].ColorSpace);
        throw new VulkanException("The Vulkan surface does not expose a supported 32-bit RGBA format.");
    }

    private static CompositeAlphaFlagsKHR SelectCompositeAlpha(CompositeAlphaFlagsKHR supported)
    {
        CompositeAlphaFlagsKHR[] preferred =
        [
            CompositeAlphaFlagsKHR.OpaqueBitKhr,
            CompositeAlphaFlagsKHR.PreMultipliedBitKhr,
            CompositeAlphaFlagsKHR.PostMultipliedBitKhr,
            CompositeAlphaFlagsKHR.InheritBitKhr
        ];
        foreach (var mode in preferred)
            if ((supported & mode) != 0) return mode;
        throw new VulkanException("The Vulkan surface exposes no supported composite-alpha mode.");
    }

    private PresentModeKHR SelectPresentMode(PhysicalDevice physicalDevice)
    {
        uint count = 0;
        _device.KhrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, _surface, ref count, null);
        if (count == 0) return PresentModeKHR.FifoKhr;

        var modes = new PresentModeKHR[count];
        fixed (PresentModeKHR* pModes = modes)
            _device.KhrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, _surface, ref count, pModes);

        var selected = PresentModeKHR.FifoKhr;
        if (!VSync)
        {
            if (modes.Contains(PresentModeKHR.MailboxKhr)) selected = PresentModeKHR.MailboxKhr;
            else if (modes.Contains(PresentModeKHR.ImmediateKhr)) selected = PresentModeKHR.ImmediateKhr;
        }
        return selected;    }

    private void CreateRenderPass()
    {
        if (_device.ColorSampleCount == SampleCountFlags.Count1Bit)
        {
            CreateSingleSampleRenderPass();
            return;
        }

        var colorAttachment = new AttachmentDescription
        {
            Format = ImageFormat,
            Samples = _device.ColorSampleCount,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };

        var resolveAttachment = new AttachmentDescription
        {
            Format = ImageFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        var colorRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var resolveRef = new AttachmentReference(1, ImageLayout.ColorAttachmentOptimal);

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PResolveAttachments = &resolveRef
        };

        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.FragmentShaderBit,
            DstAccessMask = AccessFlags.ShaderReadBit
        };

        var attachments = stackalloc AttachmentDescription[2];
        attachments[0] = colorAttachment;
        attachments[1] = resolveAttachment;
        {
            var pAttachments = attachments;
            var renderPassInfo = new RenderPassCreateInfo(StructureType.RenderPassCreateInfo)
            {
                AttachmentCount = 2,
                PAttachments = pAttachments,
                SubpassCount = 1,
                PSubpasses = &subpass,
                DependencyCount = 2,
                PDependencies = dependencies
            };

            var result = _device.Api.CreateRenderPass(_device.Device, in renderPassInfo, null, out var renderPass);
            VulkanDevice.ThrowIfFailed(result, "vkCreateRenderPass");
            RenderPass = renderPass;
        }
    }

    private void CreateSingleSampleRenderPass()
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = ImageFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };
        var colorRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef
        };
        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.FragmentShaderBit,
            DstAccessMask = AccessFlags.ShaderReadBit
        };
        var renderPassInfo = new RenderPassCreateInfo(StructureType.RenderPassCreateInfo)
        {
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies
        };
        var result = _device.Api.CreateRenderPass(_device.Device, in renderPassInfo, null, out var renderPass);
        VulkanDevice.ThrowIfFailed(result, "vkCreateRenderPass");
        RenderPass = renderPass;
    }

    private void CreateMultisampleResources()
    {
        if (_device.ColorSampleCount == SampleCountFlags.Count1Bit) return;

        var imageInfo = new ImageCreateInfo(StructureType.ImageCreateInfo)
        {
            ImageType = ImageType.Type2D,
            Format = ImageFormat,
            Extent = new Extent3D(Extent.Width, Extent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = _device.ColorSampleCount,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransientAttachmentBit | ImageUsageFlags.ColorAttachmentBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        _multisampleImages = new Silk.NET.Vulkan.Image[ImageCount];
        _multisampleImageMemories = new DeviceMemory[ImageCount];
        _multisampleImageViews = new VulkanImageView[ImageCount];
        for (var index = 0; index < ImageCount; index++)
        {
            VulkanDevice.ThrowIfFailed(_device.Api.CreateImage(_device.Device, in imageInfo, null, out _multisampleImages[index]), "vkCreateImage");
            _device.Api.GetImageMemoryRequirements(_device.Device, _multisampleImages[index], out var requirements);
            var allocation = new MemoryAllocateInfo(StructureType.MemoryAllocateInfo)
            {
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
            };
            VulkanDevice.ThrowIfFailed(_device.Api.AllocateMemory(_device.Device, in allocation, null, out _multisampleImageMemories[index]), "vkAllocateMemory");
            VulkanDevice.ThrowIfFailed(_device.Api.BindImageMemory(_device.Device, _multisampleImages[index], _multisampleImageMemories[index], 0), "vkBindImageMemory");
            var viewInfo = new ImageViewCreateInfo(StructureType.ImageViewCreateInfo)
            {
                Image = _multisampleImages[index],
                ViewType = ImageViewType.Type2D,
                Format = ImageFormat,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            VulkanDevice.ThrowIfFailed(_device.Api.CreateImageView(_device.Device, in viewInfo, null, out _multisampleImageViews[index]), "vkCreateImageView");
        }
    }

    private void CreateFramebuffers()
    {
        DestroyFramebuffers();
        Framebuffers = new Framebuffer[ImageViews.Length];
        var attachments = stackalloc VulkanImageView[2];
        for (var i = 0; i < ImageViews.Length; i++)
        {
            var attachmentCount = 1u;
            if (_device.ColorSampleCount == SampleCountFlags.Count1Bit)
                attachments[0] = ImageViews[i];
            else
            {
                attachments[0] = _multisampleImageViews[i];
                attachments[1] = ImageViews[i];
                attachmentCount = 2;
            }
            {
                var pAttachments = attachments;
                var fbInfo = new FramebufferCreateInfo(StructureType.FramebufferCreateInfo)
                {
                    RenderPass = RenderPass,
                    AttachmentCount = attachmentCount,
                    PAttachments = pAttachments,
                    Width = Extent.Width,
                    Height = Extent.Height,
                    Layers = 1
                };
                var result = _device.Api.CreateFramebuffer(_device.Device, in fbInfo, null, out var fb);
                VulkanDevice.ThrowIfFailed(result, "vkCreateFramebuffer");
                Framebuffers[i] = fb;
            }
        }
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _device.Api.GetPhysicalDeviceMemoryProperties(_device.PhysicalDevice, out var memoryProperties);
        for (uint i = 0; i < memoryProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memoryProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                return i;
        }
        throw new VulkanException("No suitable memory type found.");
    }

    private void CreateRenderFinishedSemaphores()
    {
        _renderFinishedSemaphores = new Semaphore[ImageCount];
        var info = new SemaphoreCreateInfo(StructureType.SemaphoreCreateInfo);
        for (var index = 0; index < ImageCount; index++)
            VulkanDevice.ThrowIfFailed(_device.Api.CreateSemaphore(_device.Device, in info, null,
                out _renderFinishedSemaphores[index]), "vkCreateSemaphore(render finished)");
    }

    private void CreateSyncObjects()
    {
        var api = _device.Api;
        _imageAvailableSemaphores = new Semaphore[MaxFramesInFlight];
        _inFlightFences = new Fence[MaxFramesInFlight];
        var semInfo = new SemaphoreCreateInfo(StructureType.SemaphoreCreateInfo);
        var fenceInfo = new FenceCreateInfo(StructureType.FenceCreateInfo) { Flags = FenceCreateFlags.SignaledBit };
        for (var i = 0; i < MaxFramesInFlight; i++)
        {
            VulkanDevice.ThrowIfFailed(api.CreateSemaphore(_device.Device, in semInfo, null, out _imageAvailableSemaphores[i]), "vkCreateSemaphore");
            VulkanDevice.ThrowIfFailed(api.CreateFence(_device.Device, in fenceInfo, null, out _inFlightFences[i]), "vkCreateFence");
        }
    }

    /// <summary>
    /// Acquires the next swapchain image. Returns <c>false</c> when the surface is currently
    /// unavailable (window minimized / swapchain out-of-date that cannot be recreated at a
    /// 0x0 extent); the caller should skip the frame. The in-flight fence is only reset in
    /// <see cref="VulkanRenderContext.SubmitFrame"/> right before submit, so bailing here never
    /// leaves a reset-but-unsignalled fence that would deadlock the next WaitForFences.
    /// </summary>
    public bool AcquireNextImage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_surfaceUnavailable || Swapchain.Handle == 0)
        {
            Recreate(_requestedWidth, _requestedHeight);
            if (_surfaceUnavailable || Swapchain.Handle == 0) return false;
        }

        var api = _device.Api;
        var fence = _inFlightFences[_currentFrame];
        VulkanDevice.ThrowIfFailed(api.WaitForFences(_device.Device, 1, in fence, true, ulong.MaxValue), "vkWaitForFences");

        var semaphore = _imageAvailableSemaphores[_currentFrame];

        // The swapchain can become out-of-date (e.g. on resize/minimize). Recreate and retry,
        // but bound the attempts; if the surface collapses or stays out-of-date, report
        // unavailable so the caller skips the frame instead of crashing or spinning forever.
        const int maxAttempts = 4;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            uint imageIndex;
            var result = _khrSwapchain.AcquireNextImage(_device.Device, Swapchain, ulong.MaxValue, semaphore, default, &imageIndex);
            if (result is VulkanResult.ErrorOutOfDateKhr or VulkanResult.ErrorSurfaceLostKhr)
            {
                if (result == VulkanResult.ErrorSurfaceLostKhr) RecoverSurface();
                Recreate(_requestedWidth, _requestedHeight);
                if (_surfaceUnavailable || Swapchain.Handle == 0)
                    return false;
                continue;
            }
            if (result == VulkanResult.SuboptimalKhr)
                _recreateAfterPresent = true;
            else
                VulkanDevice.ThrowIfFailed(result, "vkAcquireNextImageKHR");
            _currentImageIndex = imageIndex;
            return true;
        }

        return false;
    }

    public Semaphore CurrentImageAvailableSemaphore => _imageAvailableSemaphores[_currentFrame];
    // Reacquiring this image orders reuse after the previous presentation wait consumed its semaphore.
    public Semaphore CurrentRenderFinishedSemaphore => _renderFinishedSemaphores[_currentImageIndex];
    public Fence CurrentInFlightFence => _inFlightFences[_currentFrame];
    public Framebuffer CurrentFramebuffer => Framebuffers[_currentImageIndex];
    public Silk.NET.Vulkan.Image CurrentImage => Images[_currentImageIndex];

    public void Present()
    {
        var semaphore = _renderFinishedSemaphores[_currentImageIndex];
        var swapchain = Swapchain;
        var imageIndex = _currentImageIndex;

        var presentInfo = new PresentInfoKHR(StructureType.PresentInfoKhr)
        {
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &semaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex
        };

        var result = _khrSwapchain.QueuePresent(_device.PresentQueue, in presentInfo);
        if (result == VulkanResult.ErrorSurfaceLostKhr)
        {
            RecoverSurface();
            _recreateAfterPresent = true;
        }
        else if (result == VulkanResult.ErrorOutOfDateKhr || result == VulkanResult.SuboptimalKhr)
            _recreateAfterPresent = true;
        else
            VulkanDevice.ThrowIfFailed(result, "vkQueuePresentKHR");

        if (_recreateAfterPresent)
        {
            _recreateAfterPresent = false;
            Recreate(_requestedWidth, _requestedHeight);
        }

        _currentFrame = (_currentFrame + 1) % MaxFramesInFlight;
    }

    private void DestroyImageViews()
    {
        foreach (var view in ImageViews)
            if (view.Handle != 0) _device.Api.DestroyImageView(_device.Device, view, null);
        ImageViews = [];
    }

    private void DestroyFramebuffers()
    {
        foreach (var fb in Framebuffers)
            if (fb.Handle != 0) _device.Api.DestroyFramebuffer(_device.Device, fb, null);
        Framebuffers = [];
    }

    private void DestroyMultisampleResources()
    {
        foreach (var view in _multisampleImageViews)
            if (view.Handle != 0) _device.Api.DestroyImageView(_device.Device, view, null);
        foreach (var image in _multisampleImages)
            if (image.Handle != 0) _device.Api.DestroyImage(_device.Device, image, null);
        foreach (var memory in _multisampleImageMemories)
            if (memory.Handle != 0) _device.Api.FreeMemory(_device.Device, memory, null);
        _multisampleImageViews = [];
        _multisampleImages = [];
        _multisampleImageMemories = [];
    }

    private void DestroySwapchainImageResources()
    {
        DestroyFramebuffers();
        DestroyMultisampleResources();
        DestroyImageViews();
        foreach (var semaphore in _renderFinishedSemaphores)
            if (semaphore.Handle != 0) _device.Api.DestroySemaphore(_device.Device, semaphore, null);
        _renderFinishedSemaphores = [];
        Images = [];
        ImageCount = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var api = _device.Api;
        api.DeviceWaitIdle(_device.Device);

        foreach (var sem in _imageAvailableSemaphores) if (sem.Handle != 0) api.DestroySemaphore(_device.Device, sem, null);
        foreach (var fence in _inFlightFences) if (fence.Handle != 0) api.DestroyFence(_device.Device, fence, null);
        _imageAvailableSemaphores = [];
        _inFlightFences = [];
        DestroySwapchainImageResources();
        if (RenderPass.Handle != 0) api.DestroyRenderPass(_device.Device, RenderPass, null);
        if (Swapchain.Handle != 0) _khrSwapchain.DestroySwapchain(_device.Device, Swapchain, null);
        RenderPass = default;
        Swapchain = default;
    }
}
