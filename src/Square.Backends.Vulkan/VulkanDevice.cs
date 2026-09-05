using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan.Extensions.EXT;
using VulkanResult = Silk.NET.Vulkan.Result;
using Square.Graphics;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Square.Backends.Vulkan;

/// <summary>
/// Manages Vulkan instance, surface, physical device, logical device and graphics/present queue.
/// Initialization order: Instance -> Surface -> PhysicalDevice -> LogicalDevice.
/// (Surface must exist before device creation so queue family surface-support can be queried.)
/// </summary>
internal sealed unsafe class VulkanDevice : IDisposable
{
    public Vk Api { get; }
    public Instance Instance { get; private set; }
    public SurfaceKHR Surface { get; private set; }
    public PhysicalDevice PhysicalDevice { get; private set; }
    public Device Device { get; private set; }
    public Queue GraphicsQueue { get; private set; }
    public uint GraphicsQueueFamily { get; private set; }
    public Queue PresentQueue { get; private set; }
    public uint PresentQueueFamily { get; private set; }
    public CommandPool CommandPool { get; private set; }
    public KhrSurface KhrSurface { get; private set; } = null!;
    public SampleCountFlags ColorSampleCount { get; private set; } = SampleCountFlags.Count1Bit;
    private SampleCountFlags _supportedColorSampleCounts;

    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _debugMessenger;
    private bool _disposed;

    public VulkanDevice(INativeRenderTarget nativeTarget, bool enableValidation = false)
    {
        Api = VulkanApi.Create();
        CreateInstance(enableValidation);
        Surface = VulkanSurface.Create(this, nativeTarget);
        CreateDevice();
        CreateCommandPool();
    }

    private void CreateInstance(bool enableValidation)
    {
        var appInfo = new ApplicationInfo(StructureType.ApplicationInfo)
        {
            ApplicationVersion = Vk.MakeVersion(1, 0, 0),
            EngineVersion = Vk.MakeVersion(0, 1, 0),
            ApiVersion = Vk.Version11
        };

        var extensions = new List<string> { KhrSurface.ExtensionName };
        // Add platform-specific surface extensions
        if (OperatingSystem.IsAndroid()) extensions.Add(KhrAndroidSurface.ExtensionName);
        else if (OperatingSystem.IsWindows()) extensions.Add("VK_KHR_win32_surface");
        else extensions.Add("VK_KHR_xlib_surface");

        var available = GetAvailableInstanceExtensions();

        var layers = Array.Empty<string>();
        if (enableValidation)
        {
            var availableLayers = GetAvailableLayers();
            if (availableLayers.Contains("VK_LAYER_KHRONOS_validation"))
            {
                layers = ["VK_LAYER_KHRONOS_validation"];
                // Debug utils extension lets us receive validation messages via a messenger.
                if (available.Contains(ExtDebugUtils.ExtensionName))
                    extensions.Add(ExtDebugUtils.ExtensionName);
            }
        }

        var enabledExtensions = extensions.Where(e => available.Contains(e)).ToArray();

        fixed (byte* pAppName = "Square.Vulkan"u8)
        fixed (byte* pEngineName = "Square"u8)
        {
            appInfo.PApplicationName = pAppName;
            appInfo.PEngineName = pEngineName;

            var extPtrs = stackalloc byte*[enabledExtensions.Length];
            for (var i = 0; i < enabledExtensions.Length; i++)
                extPtrs[i] = (byte*)Marshal.StringToCoTaskMemUTF8(enabledExtensions[i]);

            var layerPtrs = stackalloc byte*[layers.Length];
            for (var i = 0; i < layers.Length; i++)
                layerPtrs[i] = (byte*)Marshal.StringToCoTaskMemUTF8(layers[i]);

            var createInfo = new InstanceCreateInfo(StructureType.InstanceCreateInfo)
            {
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = (uint)enabledExtensions.Length,
                PpEnabledExtensionNames = extPtrs,
                EnabledLayerCount = (uint)layers.Length,
                PpEnabledLayerNames = layerPtrs
            };

            var result = Api.CreateInstance(in createInfo, null, out var instance);
            ThrowIfFailed(result, "vkCreateInstance");
            Instance = instance;

            for (var i = 0; i < enabledExtensions.Length; i++)
                Marshal.FreeCoTaskMem((IntPtr)extPtrs[i]);
            for (var i = 0; i < layers.Length; i++)
                Marshal.FreeCoTaskMem((IntPtr)layerPtrs[i]);
        }

        Api.TryGetInstanceExtension(Instance, out KhrSurface khrSurface);
        KhrSurface = khrSurface;

        if (enableValidation && Api.TryGetInstanceExtension(Instance, out ExtDebugUtils debugUtils))
        {
            _debugUtils = debugUtils;
            var messengerInfo = new DebugUtilsMessengerCreateInfoEXT(StructureType.DebugUtilsMessengerCreateInfoExt)
            {
                MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt |
                                  DebugUtilsMessageSeverityFlagsEXT.WarningBitExt,
                MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                              DebugUtilsMessageTypeFlagsEXT.ValidationBitExt |
                              DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
                PfnUserCallback = new PfnDebugUtilsMessengerCallbackEXT((delegate* unmanaged[Cdecl]<
                    DebugUtilsMessageSeverityFlagsEXT,
                    DebugUtilsMessageTypeFlagsEXT,
                    DebugUtilsMessengerCallbackDataEXT*,
                    void*,
                    Bool32>)&DebugCallback)
            };
            DebugUtilsMessengerEXT messenger;
            debugUtils.CreateDebugUtilsMessenger(Instance, &messengerInfo, null, &messenger);
            _debugMessenger = messenger;
        }
    }

    private void CreateDevice()
    {
        PhysicalDevice = SelectPhysicalDevice();
        ColorSampleCount = SelectColorSampleCount(PhysicalDevice);

        var (graphicsFamily, presentFamily) = FindQueueFamilies(PhysicalDevice);
        GraphicsQueueFamily = graphicsFamily;
        PresentQueueFamily = presentFamily;

        var queueFamilies = new HashSet<uint> { graphicsFamily, presentFamily };
        var queueCreateInfos = new List<DeviceQueueCreateInfo>();
        var queuePriority = stackalloc float[1];
        queuePriority[0] = 1.0f;
        foreach (var family in queueFamilies)
            queueCreateInfos.Add(new DeviceQueueCreateInfo(StructureType.DeviceQueueCreateInfo)
            {
                QueueFamilyIndex = family,
                QueueCount = 1,
                PQueuePriorities = queuePriority
            });

        var deviceExtensions = new[] { KhrSwapchain.ExtensionName };
        var extPtr = (byte*)Marshal.StringToCoTaskMemUTF8(deviceExtensions[0]);

        var physicalDeviceFeatures = new PhysicalDeviceFeatures();

        fixed (DeviceQueueCreateInfo* pQueueInfos = queueCreateInfos.ToArray())
        {
            var createInfo = new DeviceCreateInfo(StructureType.DeviceCreateInfo)
            {
                QueueCreateInfoCount = (uint)queueCreateInfos.Count,
                PQueueCreateInfos = pQueueInfos,
                EnabledExtensionCount = 1,
                PpEnabledExtensionNames = &extPtr,
                PEnabledFeatures = &physicalDeviceFeatures
            };

            var result = Api.CreateDevice(PhysicalDevice, in createInfo, null, out var device);
            ThrowIfFailed(result, "vkCreateDevice");
            Device = device;
        }

        Marshal.FreeCoTaskMem((IntPtr)extPtr);

        Api.GetDeviceQueue(Device, GraphicsQueueFamily, 0, out var graphicsQueue);
        GraphicsQueue = graphicsQueue;
        Api.GetDeviceQueue(Device, PresentQueueFamily, 0, out var presentQueue);
        PresentQueue = presentQueue;
    }

    private SampleCountFlags SelectColorSampleCount(PhysicalDevice physicalDevice)
    {
        Api.GetPhysicalDeviceProperties(physicalDevice, out var properties);
        _supportedColorSampleCounts = properties.Limits.FramebufferColorSampleCounts;
        return ResolveColorSampleCount(0);
    }

    public bool ConfigureColorSampleCount(uint width, uint height)
    {
        var selected = ResolveColorSampleCount((ulong)width * height);
        if (selected == ColorSampleCount) return false;
        ColorSampleCount = selected;
        return true;
    }

    private SampleCountFlags ResolveColorSampleCount(ulong pixelCount)
    {
        var requested = Environment.GetEnvironmentVariable("SQUARE_VULKAN_MSAA") switch
        {
            "1" => SampleCountFlags.Count1Bit,
            "4" => SampleCountFlags.Count4Bit,
            "2" => SampleCountFlags.Count2Bit,
            _ => pixelCount > 3_000_000 ? SampleCountFlags.Count2Bit : SampleCountFlags.Count4Bit
        };
        if (requested == SampleCountFlags.Count4Bit && (_supportedColorSampleCounts & SampleCountFlags.Count4Bit) != 0)
            return SampleCountFlags.Count4Bit;
        if (requested != SampleCountFlags.Count1Bit && (_supportedColorSampleCounts & SampleCountFlags.Count2Bit) != 0)
            return SampleCountFlags.Count2Bit;
        return SampleCountFlags.Count1Bit;
    }

    private void CreateCommandPool()
    {
        var createInfo = new CommandPoolCreateInfo(StructureType.CommandPoolCreateInfo)
        {
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = GraphicsQueueFamily
        };
        var result = Api.CreateCommandPool(Device, in createInfo, null, out var pool);
        ThrowIfFailed(result, "vkCreateCommandPool");
        CommandPool = pool;
    }

    private PhysicalDevice SelectPhysicalDevice()
    {
        uint count = 0;
        Api.EnumeratePhysicalDevices(Instance, ref count, null);
        if (count == 0) throw new VulkanException("No Vulkan physical devices found.");

        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* pDevices = devices)
            Api.EnumeratePhysicalDevices(Instance, ref count, pDevices);

        foreach (var device in devices)
        {
            Api.GetPhysicalDeviceProperties(device, out var props);
            if (props.DeviceType == PhysicalDeviceType.DiscreteGpu && HasRequiredQueues(device))
                return device;
        }

        foreach (var device in devices)
        {
            if (HasRequiredQueues(device))
                return device;
        }

        throw new VulkanException("No suitable Vulkan physical device with graphics and present queue.");
    }

    private bool HasRequiredQueues(PhysicalDevice device)
    {
        uint count = 0;
        Api.GetPhysicalDeviceQueueFamilyProperties(device, ref count, null);
        if (count == 0) return false;

        var families = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* pFamilies = families)
            Api.GetPhysicalDeviceQueueFamilyProperties(device, ref count, pFamilies);

        var hasGraphics = false;
        var hasPresent = false;
        for (uint i = 0; i < count; i++)
        {
            if ((families[i].QueueFlags & QueueFlags.GraphicsBit) != 0) hasGraphics = true;
            KhrSurface.GetPhysicalDeviceSurfaceSupport(device, i, Surface, out var present);
            if (present) hasPresent = true;
        }
        return hasGraphics && hasPresent;
    }

    private (uint Graphics, uint Present) FindQueueFamilies(PhysicalDevice device)
    {
        uint count = 0;
        Api.GetPhysicalDeviceQueueFamilyProperties(device, ref count, null);
        var families = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* pFamilies = families)
            Api.GetPhysicalDeviceQueueFamilyProperties(device, ref count, pFamilies);

        uint graphics = uint.MaxValue;
        uint present = uint.MaxValue;

        for (uint i = 0; i < count; i++)
        {
            KhrSurface.GetPhysicalDeviceSurfaceSupport(device, i, Surface, out var presentSupport);
            if ((families[i].QueueFlags & QueueFlags.GraphicsBit) != 0 && presentSupport)
                return (i, i);
        }

        for (uint i = 0; i < count; i++)
        {
            if ((families[i].QueueFlags & QueueFlags.GraphicsBit) != 0 && graphics == uint.MaxValue)
                graphics = i;

            KhrSurface.GetPhysicalDeviceSurfaceSupport(device, i, Surface, out var presentSupport);
            if (presentSupport && present == uint.MaxValue)
                present = i;

            if (graphics != uint.MaxValue && present != uint.MaxValue) break;
        }

        if (graphics == uint.MaxValue) throw new VulkanException("No graphics queue family found.");
        if (present == uint.MaxValue) present = graphics;
        return (graphics, present);
    }

    private HashSet<string> GetAvailableInstanceExtensions()
    {
        uint count = 0;
        Api.EnumerateInstanceExtensionProperties((byte*)null, ref count, null);
        if (count == 0) return [];
        var extensions = new ExtensionProperties[count];
        var result = new HashSet<string>();
        fixed (ExtensionProperties* pExtensions = extensions)
        {
            Api.EnumerateInstanceExtensionProperties((byte*)null, ref count, pExtensions);
            for (var i = 0; i < count; i++)
            {
                var namePtr = (byte*)pExtensions[i].ExtensionName;
                result.Add(Marshal.PtrToStringUTF8((IntPtr)namePtr)!);
            }
        }
        return result;
    }

    private HashSet<string> GetAvailableLayers()
    {
        uint count = 0;
        Api.EnumerateInstanceLayerProperties(ref count, null);
        if (count == 0) return [];
        var layers = new LayerProperties[count];
        var result = new HashSet<string>();
        fixed (LayerProperties* pLayers = layers)
        {
            Api.EnumerateInstanceLayerProperties(ref count, pLayers);
            for (var i = 0; i < count; i++)
            {
                var namePtr = (byte*)pLayers[i].LayerName;
                result.Add(Marshal.PtrToStringUTF8((IntPtr)namePtr)!);
            }
        }
        return result;
    }

    internal static void ThrowIfFailed(VulkanResult result, string operation)
    {
        if (result != VulkanResult.Success)
            throw new VulkanException($"{operation} failed: {result}");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static Bool32 DebugCallback(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT type,
        DebugUtilsMessengerCallbackDataEXT* data,
        void* userData)
    {
        var message = data->PMessage != null
            ? Marshal.PtrToStringAnsi((IntPtr)data->PMessage)
            : string.Empty;
        Console.WriteLine($"[VulkanValidation] {severity} | {message}");
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_debugUtils is { } debugUtils && _debugMessenger.Handle != 0)
            debugUtils.DestroyDebugUtilsMessenger(Instance, _debugMessenger, null);
        if (CommandPool.Handle != 0) Api.DestroyCommandPool(Device, CommandPool, null);
        if (Device.Handle != 0) Api.DestroyDevice(Device, null);
        if (Surface.Handle != 0) VulkanSurface.Destroy(this, Surface);
        if (Instance.Handle != 0) Api.DestroyInstance(Instance, null);
        Api.Dispose();
    }
}
