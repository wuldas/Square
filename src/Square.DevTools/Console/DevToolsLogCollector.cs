namespace Square.DevTools;

/// <summary>
/// A single log entry captured for Chrome DevTools <c>Log.entryAdded</c> forwarding.
/// </summary>
internal readonly record struct DevToolsLogEntry(long Sequence, string Level, string Text, long Timestamp);

/// <summary>
/// Per-server collector for managed console and framework diagnostics. Entries live
/// in a fixed 256-entry ring under a monotonic sequence; <see cref="Subscribe"/>
/// atomically registers a live callback and returns the ordered history snapshot
/// under the same lock, so replay and live delivery never overlap or drop rows.
/// <see cref="Clear"/> empties the ring and advances a watermark that consumers use
/// to filter stale queued entries; sequences never rewind. Every callback is invoked
/// synchronously under the collector lock in publication order and must only try to
/// write to its transport.
/// </summary>
internal sealed class DevToolsLogCollector : IDisposable
{
    private const int RingCapacity = 256;

    private readonly object Gate = new();
    private readonly DevToolsLogEntry[] Ring = new DevToolsLogEntry[RingCapacity];
    private readonly List<Action<DevToolsLogEntry>> Subscribers = [];
    private int Head;
    private int Count;
    private long Sequence;
    private long ClearedThroughField;
    private bool Disposed;

    /// <summary>Latest sequence number covered by <see cref="Clear"/>; consumers skip entries at or below it.</summary>
    public long ClearedThrough
    {
        get { lock (Gate) return ClearedThroughField; }
    }

    public void Append(string level, string text)
    {
        lock (Gate)
        {
            if (Disposed) return;
            var entry = new DevToolsLogEntry(++Sequence, level, text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Ring[(Head + Count) % RingCapacity] = entry;
            if (Count == RingCapacity) Head = (Head + 1) % RingCapacity;
            else Count++;
            for (var i = 0; i < Subscribers.Count; i++) Subscribers[i](entry);
        }
    }

    /// <summary>
    /// Registers a live callback and returns the ordered history snapshot captured in
    /// the same lock, so the consumer can ack first and then replay the snapshot
    /// without missing or duplicating concurrent entries.
    /// </summary>
    public IDisposable Subscribe(Action<DevToolsLogEntry> callback, out DevToolsLogEntry[] snapshot)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (Gate)
        {
            snapshot = Disposed ? [] : SnapshotCore();
            if (!Disposed) Subscribers.Add(callback);
            return new CollectorSubscription(this, callback);
        }
    }

    public void Clear()
    {
        lock (Gate)
        {
            if (Disposed) return;
            Head = 0;
            Count = 0;
            ClearedThroughField = Sequence;
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (Disposed) return;
            Disposed = true;
            Subscribers.Clear();
            Array.Clear(Ring);
        }
    }

    private DevToolsLogEntry[] SnapshotCore()
    {
        if (Count == 0) return [];
        var snapshot = new DevToolsLogEntry[Count];
        for (var i = 0; i < Count; i++) snapshot[i] = Ring[(Head + i) % RingCapacity];
        return snapshot;
    }

    private void Unsubscribe(Action<DevToolsLogEntry> callback)
    {
        lock (Gate) Subscribers.Remove(callback);
    }

    private sealed class CollectorSubscription(DevToolsLogCollector owner, Action<DevToolsLogEntry> callback) : IDisposable
    {
        private Action<DevToolsLogEntry>? _callback = callback;

        public void Dispose()
        {
            var callback = _callback;
            if (callback is null) return;
            _callback = null;
            owner.Unsubscribe(callback);
        }
    }
}
