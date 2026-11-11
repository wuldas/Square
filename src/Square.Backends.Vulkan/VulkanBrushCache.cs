using System.Numerics;
using Square.Graphics;

namespace Square.Backends.Vulkan;

/// <summary>Per-batch CPU LRU. Probe mutable inputs before allocating; retain only immutable snapshots.</summary>
internal sealed class VulkanBrushCache
{
    // Conservative charge for material, array header, entry, LRU node and hash-table metadata, plus stop bytes.
    internal const long EntryOverheadBytes = 320;
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly Dictionary<int, Entry> _buckets = [];
    private readonly LinkedList<Entry> _lru = new();
    public int Count => _lru.Count;
    public long Bytes { get; private set; }
    public long BuildCount { get; private set; }
    public long HitCount { get; private set; }

    public VulkanBrushCache(int maxEntries = 2048, long maxBytes = 4 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
    }

    public VulkanBrushMaterial Get(Brush brush, Matrix3x2 transform) => Get(VulkanBrushMaterial.Describe(brush, transform));
    public VulkanBrushMaterial GetLayer(float opacity) => Get(VulkanBrushMaterial.DescribeLayer(opacity));

    private VulkanBrushMaterial Get(in VulkanBrushMaterial.Descriptor descriptor)
    {
        var hash = VulkanBrushMaterial.ContentHash(descriptor);
        _buckets.TryGetValue(hash, out var head);
        for (var candidate = head; candidate != null; candidate = candidate.Next)
        {
            // Hash only selects a bucket. Full bit comparisons preserve NaN payloads, signed zero and stop order.
            if (!candidate.Material.Matches(descriptor)) continue;
            HitCount++;
            if (candidate.Node != _lru.First)
            {
                _lru.Remove(candidate.Node);
                _lru.AddFirst(candidate.Node);
            }
            return candidate.Material;
        }
        var material = VulkanBrushMaterial.Snapshot(descriptor);
        BuildCount++;
        var cost = EntryOverheadBytes + (long)material.Stops.Length * 8;
        if (_maxEntries == 0 || cost > _maxBytes) return material;
        while (Count >= _maxEntries || Bytes > _maxBytes - cost) RemoveLast();
        // Eviction may have removed the earlier bucket head.
        _buckets.TryGetValue(hash, out head);
        var entry = new Entry(material, hash, cost, head);
        entry.Node = _lru.AddFirst(entry);
        _buckets[hash] = entry;
        Bytes += cost;
        return material;
    }

    private void RemoveLast()
    {
        var entry = _lru.Last!.Value;
        var head = _buckets[entry.Hash];
        if (ReferenceEquals(head, entry))
        {
            if (entry.Next == null) _buckets.Remove(entry.Hash);
            else _buckets[entry.Hash] = entry.Next;
        }
        else
        {
            var previous = head;
            while (!ReferenceEquals(previous.Next, entry)) previous = previous.Next!;
            previous.Next = entry.Next;
        }
        _lru.RemoveLast();
        Bytes -= entry.Cost;
    }

    /// <summary>Release retained snapshots. Build/hit counters remain cumulative.</summary>
    public void Clear()
    {
        _buckets.Clear();
        _lru.Clear();
        Bytes = 0;
    }

    private sealed class Entry(VulkanBrushMaterial material, int hash, long cost, Entry? next)
    {
        public readonly VulkanBrushMaterial Material = material;
        public readonly int Hash = hash;
        public readonly long Cost = cost;
        public Entry? Next = next;
        public LinkedListNode<Entry> Node = null!;
    }
}
