using Silk.NET.Vulkan;

namespace Square.Backends.Vulkan;

internal readonly record struct VulkanLayerSpec(Format Format, SampleCountFlags Samples, uint Width, uint Height);

/// <summary>Idle resources only; its frame owner must finish GPU work before Return/Clear.</summary>
internal sealed class VulkanLayerResourcePool<T>(int maxEntries, ulong maxBytes, Action<T> release) where T : class
{
    private readonly LinkedList<Entry> _idle = [];
    public int Count => _idle.Count;
    public ulong Bytes { get; private set; }
    public long HitCount { get; private set; }

    public bool TryRent(VulkanLayerSpec spec, out T resource)
    {
        for (var node = _idle.Last; node is not null; node = node.Previous)
        {
            if (node.Value.Spec != spec) continue;
            _idle.Remove(node);
            Bytes -= node.Value.Bytes;
            HitCount++;
            resource = node.Value.Resource;
            return true;
        }
        resource = null!;
        return false;
    }

    public void Return(VulkanLayerSpec spec, ulong bytes, T resource)
    {
        if (maxEntries <= 0 || bytes > maxBytes)
        {
            release(resource);
            return;
        }
        // Retain the observed working set, evicting least recently returned specs.
        // Byte budget is primary; the entry ceiling also bounds tiny native handles.
        while (_idle.Count >= maxEntries || Bytes > maxBytes - bytes) ReleaseOldest();
        _idle.AddLast(new Entry(spec, bytes, resource));
        Bytes += bytes;
    }

    public void Clear()
    {
        while (_idle.Count != 0) ReleaseOldest();
    }

    private void ReleaseOldest()
    {
        var node = _idle.First!;
        _idle.RemoveFirst();
        Bytes -= node.Value.Bytes;
        release(node.Value.Resource);
    }

    private sealed record Entry(VulkanLayerSpec Spec, ulong Bytes, T Resource);
}
