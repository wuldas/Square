using System.Reflection;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Square.Graphics;

// Read-only startup metadata shared by the original main engine and the candidate.
// This imports no renderer feature and never substitutes an unavailable counter.
internal sealed unsafe class VulkanMetadata
{
    internal sealed record DeviceRecord(string DeviceName, string DeviceType, uint VendorId,
        uint DeviceId, uint ApiVersion, uint DriverVersion);
    public DeviceRecord DeviceInfo { get; }
    public uint ColorSampleCount { get; }
    public bool ReadbackEnabled { get; }
    private readonly object _context;
    private readonly MethodInfo? _statistics;
    private VulkanMetadata(object context, DeviceRecord device, uint samples, bool readback)
    {
        _context = context;
        DeviceInfo = device;
        ColorSampleCount = samples;
        ReadbackEnabled = readback;
        _statistics = context.GetType().GetMethod("GetFrameStatistics", BindingFlags.Instance | BindingFlags.Public);
    }
    public object? GetFrameStatistics() => _statistics?.Invoke(_context, null);
    public static VulkanMetadata? Read(IRenderContext context)
    {
        var type = context.GetType();
        var device = type.GetField("_device", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(context);
        if (device is null) return null;
        var deviceType = device.GetType();
        var api = deviceType.GetProperty("Api")?.GetValue(device) as Vk;
        var physical = deviceType.GetProperty("PhysicalDevice")?.GetValue(device);
        if (api is null || physical is not PhysicalDevice gpu) return null;
        api.GetPhysicalDeviceProperties(gpu, out var properties);
        var name = Marshal.PtrToStringUTF8((nint)properties.DeviceName) ?? "";
        var samples = Convert.ToUInt32(deviceType.GetProperty("ColorSampleCount")!.GetValue(device));
        var readback = type.GetField("_readbackEnabled", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(context);
        if (readback is not bool enabled) throw new InvalidOperationException("Cannot verify Vulkan readback state");
        return new(context, new(name, properties.DeviceType.ToString(), properties.VendorID,
            properties.DeviceID, properties.ApiVersion, properties.DriverVersion), samples, enabled);
    }
}
