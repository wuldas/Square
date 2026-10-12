namespace Square.DevTools;

// Log domain state: idempotent enable/disable, ordered ring replay and live entry forwarding.
internal sealed partial class CdpSession
{
    private bool _logEnabled;
    private IDisposable? _logSubscription;
    private long _lastSentLogSequence;
    private void HandleLogEntry(DevToolsLogEntry entry)
    {
        if (_stopping) return;
        if (!_events.Writer.TryWrite(new CdpEvent(CdpEventKind.LogEntry, LogEntry: entry))) CancelSession();
    }

    private async Task EnableLogAsync(int id, CancellationToken cancellationToken)
    {
        if (_logEnabled)
        {
            await SendEmptyResultAsync(id, cancellationToken);
            return;
        }
        DevToolsLogEntry[] snapshot = [];
        if (_server.LogCollector is { } collector)
            _logSubscription = collector.Subscribe(HandleLogEntry, out snapshot);
        _logEnabled = true;
        await SendEmptyResultAsync(id, cancellationToken);
        foreach (var entry in snapshot) await SendLogEntryAsync(entry, cancellationToken);
    }

    private async Task DisableLogAsync(int id, CancellationToken cancellationToken)
    {
        _logEnabled = false;
        Interlocked.Exchange(ref _logSubscription, null)?.Dispose();
        await SendEmptyResultAsync(id, cancellationToken);
    }

    private async Task SendLogEntryAsync(DevToolsLogEntry entry, CancellationToken cancellationToken)
    {
        if (!_logEnabled || entry.Sequence <= _lastSentLogSequence ||
            entry.Sequence <= (_server.LogCollector?.ClearedThrough ?? 0)) return;
        await SendEventAsync("Log.entryAdded", writer =>
        {
            writer.WriteStartObject("entry");
            writer.WriteString("source", "other");
            writer.WriteString("level", entry.Level);
            writer.WriteString("text", entry.Text);
            writer.WriteNumber("timestamp", entry.Timestamp);
            writer.WriteEndObject();
        }, cancellationToken);
        _lastSentLogSequence = entry.Sequence;
    }
}
