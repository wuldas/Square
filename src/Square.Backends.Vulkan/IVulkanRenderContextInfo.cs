namespace Square.Backends.Vulkan;

/// <summary>Read-only information about the Vulkan device and active render configuration.</summary>
public interface IVulkanRenderContextInfo
{
    /// <summary>Gets the physical device selected by this render context.</summary>
    VulkanDeviceInfo DeviceInfo { get; }

    /// <summary>Gets the actual color sample count; the value can change after resizing.</summary>
    uint ColorSampleCount { get; }

    /// <summary>Gets whether presented frames are copied to a host-visible readback buffer.</summary>
    bool ReadbackEnabled { get; }
}

/// <summary>Immutable properties of the selected Vulkan physical device.</summary>
/// <param name="Name">The device name reported by the Vulkan driver.</param>
/// <param name="DeviceType">The Vulkan physical-device type, such as DiscreteGpu, IntegratedGpu, or Cpu.</param>
/// <param name="ApiVersion">The raw Vulkan API version reported by the device.</param>
/// <param name="DriverVersion">The raw, vendor-specific driver version reported by the device.</param>
public sealed record VulkanDeviceInfo(string Name, string DeviceType, uint ApiVersion, uint DriverVersion);
