using System.Numerics;
using Silk.NET.Vulkan;
using Square.Rendering.Tessellation;

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
        CreateDescriptorSetLayout();
        CreatePipelineLayout();
        CreateGraphicsPipeline();
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
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var layoutInfo = new DescriptorSetLayoutCreateInfo(StructureType.DescriptorSetLayoutCreateInfo)
        {
            BindingCount = 1,
            PBindings = &binding
        };

        var result = _device.Api.CreateDescriptorSetLayout(_device.Device, in layoutInfo, null, out var layout);
        VulkanDevice.ThrowIfFailed(result, "vkCreateDescriptorSetLayout");
        DescriptorSetLayout = layout;
    }

    private void CreatePipelineLayout()
    {
        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = 64 // mat4
        };

        var layout = DescriptorSetLayout;
        var layoutInfo = new PipelineLayoutCreateInfo(StructureType.PipelineLayoutCreateInfo)
        {
            SetLayoutCount = 1,
            PSetLayouts = &layout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange
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
        if (Pipeline.Handle != 0) api.DestroyPipeline(_device.Device, Pipeline, null);
        if (PipelineLayout.Handle != 0) api.DestroyPipelineLayout(_device.Device, PipelineLayout, null);
        if (DescriptorSetLayout.Handle != 0) api.DestroyDescriptorSetLayout(_device.Device, DescriptorSetLayout, null);
    }
}
