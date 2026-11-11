using System.Numerics;
using Silk.NET.Vulkan;
using Square.Rendering.Tessellation;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Square.Backends.Vulkan;

/// <summary>
/// Creates and manages the Vulkan graphics pipeline for 2D batch rendering.
/// Uses a single shader: vertex (position+uv+color) → fragment (texture * vertex color).
/// Reference: ImGui Vulkan backend pipeline setup.
/// </summary>
internal sealed unsafe class VulkanPipeline : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly VulkanSwapchain _swapchain;

    public Pipeline Pipeline { get; private set; }
    public PipelineLayout PipelineLayout { get; private set; }
    public DescriptorSetLayout DescriptorSetLayout { get; private set; }
    public Matrix4x4 CurrentProjection { get; set; } = Matrix4x4.Identity;
    public Extent2D FramebufferExtent => _swapchain.Extent;
    private readonly Dictionary<ulong, TextureDescriptor> _textures = [];
    private Buffer _gradientBuffer;
    private DeviceMemory _gradientMemory;
    private void* _gradientMapped;
    private int _gradientCapacity;

    private bool _disposed;

    // SPIR-V bytecode for simple 2D shaders (compiled from GLSL, embedded as bytes)
    // Vertex: layout(push_constant) uniform mat4 proj; in vec2 pos; in vec2 uv; in vec4 color;
    // Fragment: uniform sampler2D tex; out vec4 fragColor; fragColor = color * texture(tex, uv);
    private static readonly byte[] VertexShaderSpirv = EmbeddedShaders.Vertex2D;
    private static readonly byte[] FragmentShaderSpirv = EmbeddedShaders.Fragment2D;

    public VulkanPipeline(VulkanDevice device, VulkanSwapchain swapchain)
    {
        _device = device;
        _swapchain = swapchain;
        try
        {
            CreateDescriptorSetLayout();
            CreatePipelineLayout();
            CreateGraphicsPipeline();
            EnsureGradientCapacity(1);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void UpdateProjection(float width, float height)
    {
        // Orthographic projection: (0,0) top-left, (w,h) bottom-right
        CurrentProjection = new Matrix4x4(
            2f / width, 0, 0, 0,
            0, 2f / height, 0, 0,
            0, 0, 1, 0,
            -1, -1, 0, 1);
    }

    private void CreateDescriptorSetLayout()
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[2];
        bindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0, DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1, StageFlags = ShaderStageFlags.FragmentBit
        };
        bindings[1] = new DescriptorSetLayoutBinding
        {
            Binding = 1, DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1, StageFlags = ShaderStageFlags.FragmentBit
        };
        var layoutInfo = new DescriptorSetLayoutCreateInfo(StructureType.DescriptorSetLayoutCreateInfo)
        {
            BindingCount = 2,
            PBindings = bindings
        };

        var result = _device.Api.CreateDescriptorSetLayout(_device.Device, in layoutInfo, null, out var layout);
        VulkanDevice.ThrowIfFailed(result, "vkCreateDescriptorSetLayout");
        DescriptorSetLayout = layout;
    }

    private void CreatePipelineLayout()
    {
        var ranges = stackalloc PushConstantRange[2];
        ranges[0] = new PushConstantRange { StageFlags = ShaderStageFlags.VertexBit, Offset = 0, Size = 64 };
        ranges[1] = new PushConstantRange { StageFlags = ShaderStageFlags.FragmentBit, Offset = 64, Size = 48 };
        var layout = DescriptorSetLayout;
        var layoutInfo = new PipelineLayoutCreateInfo(StructureType.PipelineLayoutCreateInfo)
        {
            SetLayoutCount = 1,
            PSetLayouts = &layout,
            PushConstantRangeCount = 2,
            PPushConstantRanges = ranges
        };

        var result = _device.Api.CreatePipelineLayout(_device.Device, in layoutInfo, null, out var pipelineLayout);
        VulkanDevice.ThrowIfFailed(result, "vkCreatePipelineLayout");
        PipelineLayout = pipelineLayout;
    }

    private void CreateGraphicsPipeline()
    {
        var api = _device.Api;
        var vertModule = CreateShaderModule(VertexShaderSpirv);
        var fragModule = CreateShaderModule(FragmentShaderSpirv);

        var mainName = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };

        var vertStage = new PipelineShaderStageCreateInfo(StructureType.PipelineShaderStageCreateInfo)
        {
            Stage = ShaderStageFlags.VertexBit,
            Module = vertModule,
            PName = mainName
        };
        var fragStage = new PipelineShaderStageCreateInfo(StructureType.PipelineShaderStageCreateInfo)
        {
            Stage = ShaderStageFlags.FragmentBit,
            Module = fragModule,
            PName = mainName
        };
        var shaderStages = new[] { vertStage, fragStage };

        // Vertex input: pos(2f) + uv(2f) + color(4b)
        var bindingDescription = new VertexInputBindingDescription(0, (uint)sizeof(Vertex2D), VertexInputRate.Vertex);
        var attributeDescriptions = new[]
        {
            new VertexInputAttributeDescription(0, 0, Format.R32G32Sfloat, 0),   // position
            new VertexInputAttributeDescription(1, 0, Format.R32G32Sfloat, 8),   // uv
            new VertexInputAttributeDescription(2, 0, Format.R8G8B8A8Unorm, 16)  // color
        };

        fixed (VertexInputAttributeDescription* pAttributes = attributeDescriptions)
        {
            var vertexInputInfo = new PipelineVertexInputStateCreateInfo(StructureType.PipelineVertexInputStateCreateInfo)
            {
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &bindingDescription,
                VertexAttributeDescriptionCount = (uint)attributeDescriptions.Length,
                PVertexAttributeDescriptions = pAttributes
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo(StructureType.PipelineInputAssemblyStateCreateInfo)
            {
                Topology = PrimitiveTopology.TriangleList,
                PrimitiveRestartEnable = false
            };

            var pipelineExtent = new Extent2D(Math.Max(1, _swapchain.Extent.Width), Math.Max(1, _swapchain.Extent.Height));
            var viewport = new Viewport(0, 0, pipelineExtent.Width, pipelineExtent.Height, 0, 1);
            var scissor = new Rect2D(new Offset2D(0, 0), pipelineExtent);

            var viewportState = new PipelineViewportStateCreateInfo(StructureType.PipelineViewportStateCreateInfo)
            {
                ViewportCount = 1,
                PViewports = &viewport,
                ScissorCount = 1,
                PScissors = &scissor
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo(StructureType.PipelineRasterizationStateCreateInfo)
            {
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1f,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                DepthBiasEnable = false
            };

            var multisampling = new PipelineMultisampleStateCreateInfo(StructureType.PipelineMultisampleStateCreateInfo)
            {
                SampleShadingEnable = false,
                RasterizationSamples = _device.ColorSampleCount
            };

            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = BlendOp.Add
            };

            var colorBlending = new PipelineColorBlendStateCreateInfo(StructureType.PipelineColorBlendStateCreateInfo)
            {
                LogicOpEnable = false,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment
            };

            var dynamicStates = new DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
            fixed (DynamicState* pDynamicStates = dynamicStates)
            {
                var dynamicState = new PipelineDynamicStateCreateInfo(StructureType.PipelineDynamicStateCreateInfo)
                {
                    DynamicStateCount = (uint)dynamicStates.Length,
                    PDynamicStates = pDynamicStates
                };

                fixed (PipelineShaderStageCreateInfo* pStages = shaderStages)
                {
                    var pipelineInfo = new GraphicsPipelineCreateInfo(StructureType.GraphicsPipelineCreateInfo)
                    {
                        StageCount = 2,
                        PStages = pStages,
                        PVertexInputState = &vertexInputInfo,
                        PInputAssemblyState = &inputAssembly,
                        PViewportState = &viewportState,
                        PRasterizationState = &rasterizer,
                        PMultisampleState = &multisampling,
                        PColorBlendState = &colorBlending,
                        PDynamicState = &dynamicState,
                        Layout = PipelineLayout,
                        RenderPass = _swapchain.RenderPass,
                        Subpass = 0
                    };

                    var result = api.CreateGraphicsPipelines(_device.Device, default, 1, &pipelineInfo, null, out var pipeline);
                    VulkanDevice.ThrowIfFailed(result, "vkCreateGraphicsPipelines");
                    Pipeline = pipeline;
                }
            }
        }

        api.DestroyShaderModule(_device.Device, vertModule, null);
        api.DestroyShaderModule(_device.Device, fragModule, null);
    }

    public DescriptorSet CreateTextureDescriptor(ImageView imageView, Sampler sampler)
    {
        var sizes = stackalloc DescriptorPoolSize[2];
        sizes[0] = new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 1);
        sizes[1] = new DescriptorPoolSize(DescriptorType.StorageBuffer, 1);
        var poolInfo = new DescriptorPoolCreateInfo(StructureType.DescriptorPoolCreateInfo)
        {
            MaxSets = 1, PoolSizeCount = 2, PPoolSizes = sizes
        };
        VulkanDevice.ThrowIfFailed(_device.Api.CreateDescriptorPool(_device.Device, in poolInfo, null, out var pool), "vkCreateDescriptorPool");
        DescriptorSet set = default;
        try
        {
            var layout = DescriptorSetLayout;
            var allocate = new DescriptorSetAllocateInfo(StructureType.DescriptorSetAllocateInfo)
            {
                DescriptorPool = pool, DescriptorSetCount = 1, PSetLayouts = &layout
            };
            VulkanDevice.ThrowIfFailed(_device.Api.AllocateDescriptorSets(_device.Device, in allocate, out set), "vkAllocateDescriptorSets");
            _textures.Add(set.Handle, new TextureDescriptor(pool, new DescriptorImageInfo(sampler, imageView, ImageLayout.ShaderReadOnlyOptimal)));
            UpdateTextureDescriptor(set);
            return set;
        }
        catch
        {
            _textures.Remove(set.Handle);
            _device.Api.DestroyDescriptorPool(_device.Device, pool, null);
            throw;
        }
    }

    public void ReleaseTextureDescriptor(DescriptorSet set)
    {
        if (_textures.Remove(set.Handle, out var texture))
            _device.Api.DestroyDescriptorPool(_device.Device, texture.Pool, null);
    }

    private void UpdateTextureDescriptor(DescriptorSet set)
    {
        var image = _textures[set.Handle].Image;
        var buffer = new DescriptorBufferInfo(_gradientBuffer, 0, checked((ulong)_gradientCapacity * (ulong)sizeof(VulkanGradientStop)));
        var writes = stackalloc WriteDescriptorSet[2];
        writes[0] = new WriteDescriptorSet(StructureType.WriteDescriptorSet)
        {
            DstSet = set, DstBinding = 0, DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler, PImageInfo = &image
        };
        writes[1] = new WriteDescriptorSet(StructureType.WriteDescriptorSet)
        {
            DstSet = set, DstBinding = 1, DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &buffer
        };
        _device.Api.UpdateDescriptorSets(_device.Device, 2, writes, 0, null);
    }

    /// <summary>Caller has finished the previous frame; descriptors may be rewritten before recording draws.</summary>
    public void UploadGradientStops(ReadOnlySpan<VulkanGradientStop> stops)
    {
        EnsureGradientCapacity(Math.Max(1, stops.Length));
        if (stops.IsEmpty) new Span<VulkanGradientStop>(_gradientMapped, 1).Clear();
        else stops.CopyTo(new Span<VulkanGradientStop>(_gradientMapped, stops.Length));
    }

    private void EnsureGradientCapacity(int count)
    {
        if (count <= _gradientCapacity) return;
        var capacity = Math.Max(count, Math.Max(16, checked(_gradientCapacity * 2)));
        var size = checked((ulong)capacity * (ulong)sizeof(VulkanGradientStop));
        Buffer buffer = default;
        DeviceMemory memory = default;
        void* mapped = null;
        try
        {
            var info = new BufferCreateInfo(StructureType.BufferCreateInfo)
            {
                Size = size, Usage = BufferUsageFlags.StorageBufferBit, SharingMode = SharingMode.Exclusive
            };
            VulkanDevice.ThrowIfFailed(_device.Api.CreateBuffer(_device.Device, in info, null, out buffer), "vkCreateBuffer(stops)");
            _device.Api.GetBufferMemoryRequirements(_device.Device, buffer, out var requirements);
            _device.Api.GetPhysicalDeviceMemoryProperties(_device.PhysicalDevice, out var types);
            var type = uint.MaxValue;
            var flags = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
            for (uint i = 0; i < types.MemoryTypeCount; i++)
                if ((requirements.MemoryTypeBits & (1u << (int)i)) != 0 && (types.MemoryTypes[(int)i].PropertyFlags & flags) == flags) { type = i; break; }
            if (type == uint.MaxValue) throw new VulkanException("No host-visible coherent memory for gradient stops.");
            var allocation = new MemoryAllocateInfo(StructureType.MemoryAllocateInfo) { AllocationSize = requirements.Size, MemoryTypeIndex = type };
            VulkanDevice.ThrowIfFailed(_device.Api.AllocateMemory(_device.Device, in allocation, null, out memory), "vkAllocateMemory(stops)");
            VulkanDevice.ThrowIfFailed(_device.Api.BindBufferMemory(_device.Device, buffer, memory, 0), "vkBindBufferMemory(stops)");
            void* pointer;
            VulkanDevice.ThrowIfFailed(_device.Api.MapMemory(_device.Device, memory, 0, size, 0, &pointer), "vkMapMemory(stops)");
            mapped = pointer;
        }
        catch
        {
            if (mapped != null) _device.Api.UnmapMemory(_device.Device, memory);
            if (buffer.Handle != 0) _device.Api.DestroyBuffer(_device.Device, buffer, null);
            if (memory.Handle != 0) _device.Api.FreeMemory(_device.Device, memory, null);
            throw;
        }
        var oldBuffer = _gradientBuffer;
        var oldMemory = _gradientMemory;
        var oldMapped = _gradientMapped;
        _gradientBuffer = buffer; _gradientMemory = memory; _gradientMapped = mapped; _gradientCapacity = capacity;
        foreach (var handle in _textures.Keys) UpdateTextureDescriptor(new DescriptorSet(handle));
        if (oldMapped != null) _device.Api.UnmapMemory(_device.Device, oldMemory);
        if (oldBuffer.Handle != 0) _device.Api.DestroyBuffer(_device.Device, oldBuffer, null);
        if (oldMemory.Handle != 0) _device.Api.FreeMemory(_device.Device, oldMemory, null);
    }

    private sealed record TextureDescriptor(DescriptorPool Pool, DescriptorImageInfo Image);

    public void RecreateGraphicsPipeline()
    {
        VulkanDevice.ThrowIfFailed(_device.Api.DeviceWaitIdle(_device.Device), "vkDeviceWaitIdle");
        if (Pipeline.Handle != 0)
        {
            _device.Api.DestroyPipeline(_device.Device, Pipeline, null);
            Pipeline = default;
        }
        CreateGraphicsPipeline();
    }

    private ShaderModule CreateShaderModule(byte[] spirv)
    {
        fixed (byte* pCode = spirv)
        {
            var createInfo = new ShaderModuleCreateInfo(StructureType.ShaderModuleCreateInfo)
            {
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)pCode
            };
            var result = _device.Api.CreateShaderModule(_device.Device, in createInfo, null, out var module);
            VulkanDevice.ThrowIfFailed(result, "vkCreateShaderModule");
            return module;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var api = _device.Api;
        foreach (var texture in _textures.Values) api.DestroyDescriptorPool(_device.Device, texture.Pool, null);
        _textures.Clear();
        if (_gradientMapped != null) api.UnmapMemory(_device.Device, _gradientMemory);
        if (_gradientBuffer.Handle != 0) api.DestroyBuffer(_device.Device, _gradientBuffer, null);
        if (_gradientMemory.Handle != 0) api.FreeMemory(_device.Device, _gradientMemory, null);
        _gradientBuffer = default; _gradientMemory = default; _gradientMapped = null; _gradientCapacity = 0;
        if (Pipeline.Handle != 0) api.DestroyPipeline(_device.Device, Pipeline, null);
        if (PipelineLayout.Handle != 0) api.DestroyPipelineLayout(_device.Device, PipelineLayout, null);
        if (DescriptorSetLayout.Handle != 0) api.DestroyDescriptorSetLayout(_device.Device, DescriptorSetLayout, null);
    }
}
