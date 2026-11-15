namespace Square.Platform.MacOS;

internal sealed class MacOSFrameScheduler
{
    internal const int MaxEventsPerBatch = 64;
    internal static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1d / 60d);
    private static readonly TimeSpan LegacyFrameInterval = TimeSpan.FromMilliseconds(16);
    private TimeSpan? _deadline;
    private int _wakePending;

    internal bool HasPendingWake => Volatile.Read(ref _wakePending) != 0;

    internal bool RequestWake() => Interlocked.Exchange(ref _wakePending, 1) == 0;

    internal bool ConsumeWake() => Interlocked.Exchange(ref _wakePending, 0) != 0;

    // Called only on the UI thread. Keep the deadline across native event batches so
    // a busy input queue cannot repeatedly postpone a scheduled frame.
    internal TimeSpan? GetWaitDelay(TimeSpan now, Func<TimeSpan?>? query)
    {
        var delay = query == null ? LegacyFrameInterval : query();
        if (delay == null)
        {
            _deadline = null;
        }
        else
        {
            if (delay <= TimeSpan.Zero) delay = FrameInterval;
            var candidate = delay.Value > TimeSpan.MaxValue - now ? TimeSpan.MaxValue : now + delay.Value;
            if (_deadline == null || candidate < _deadline) _deadline = candidate;
        }

        if (HasPendingWake) return TimeSpan.Zero;
        if (_deadline == null) return null;
        return _deadline <= now ? TimeSpan.Zero : _deadline.Value - now;
    }

    internal bool IsFrameDue(TimeSpan now) => _deadline != null && _deadline <= now;

    internal void FrameCompleted() => _deadline = null;
}
