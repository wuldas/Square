using System.Collections.Concurrent;
using Square.Controls;
using Square.CSS.Engine;
using Square.Events;
using Square.Native.Html;
using Square.Runtime;
using Square.UI;

namespace Square.Hosting.Web;

internal sealed class SquareWebInteractiveSession : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly UIDocument _document = new();
    private bool _disposed;
    private long _lastAccessTicks = DateTime.UtcNow.Ticks;

    internal SquareWebInteractiveSession(string token, Element page)
    {
        Token = token;
        Page = page;
        _document.Body.Children.Add(page);
        try
        {
            _document.Build();
            _document.FlushPendingUpdates();
            ((IComponentLifecycle)page).OnAttached();
            ((IComponentLifecycle)page).OnLoaded();
            _document.FlushPendingUpdates();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal string Token { get; }
    internal Element Page { get; }
    internal long Revision { get; private set; }
    internal bool IsExpired(TimeSpan timeout) =>
        DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastAccessTicks), DateTimeKind.Utc) >= timeout;

    internal async Task<SquareWebInteractionResult?> DispatchAsync(
        SquareWebEventRequest request,
        HtmlExportOptions options,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed || request.Revision != Revision) return null;
            Interlocked.Exchange(ref _lastAccessTicks, DateTime.UtcNow.Ticks);

            var target = FindByDebugId(Page, request.ElementId);
            if (target == null || IsDisabledInPath(target) || !HasListenerInPath(target, request.Type)) return null;

            SynchronizeControlValue(target, request);
            var accepted = target.DispatchTrusted(StandardEvents.Create(request.Type));
            _document.FlushPendingUpdates();
            Revision++;
            var result = HtmlExporter.Export(Page, options);
            return new SquareWebInteractionResult(result, Revision, DefaultPrevented: !accepted);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            var lifecycle = (IComponentLifecycle)Page;
            if (Page.IsLoaded) lifecycle.OnUnloaded();
            if (Page.IsAttached) lifecycle.OnDetached();
            CssStyleReconciler.UnregisterScopesForTree(_document.Ui);
            Page.DiscardGeneratedSubtree();
            _document.Context.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Element? FindByDebugId(Element element, int debugId)
    {
        if (element.DebugId == debugId) return element;
        foreach (var child in element.Children)
        {
            var found = FindByDebugId(child, debugId);
            if (found != null) return found;
        }
        return null;
    }

    private static bool HasListenerInPath(Element target, string type)
    {
        for (Element? current = target; current != null; current = current.Parent)
            if (current.RegisteredEventTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsDisabledInPath(Element target)
    {
        for (Element? current = target; current != null; current = current.Parent)
            if (current is UIElement { IsDisabled: true }) return true;
        return false;
    }

    private static void SynchronizeControlValue(Element target, SquareWebEventRequest request)
    {
        if (request.Type is "input" or "change")
        {
            switch (target)
            {
                case Input input when request.Value != null:
                    input.Value = request.Value;
                    break;
                case TextArea textArea when request.Value != null:
                    textArea.Value = request.Value;
                    break;
                case Select select when request.Value != null:
                    select.Value = request.Value;
                    break;
                case CheckBox checkBox when request.Checked.HasValue:
                    checkBox.IsChecked = request.Checked.Value;
                    break;
                case Radio radio when request.Checked == true:
                    if (radio.Parent != null && radio.GroupName.Length > 0)
                        foreach (var sibling in radio.Parent.QueryAll<Radio>())
                            if (!ReferenceEquals(sibling, radio) && sibling.GroupName == radio.GroupName)
                                sibling.IsChecked = false;
                    radio.IsChecked = true;
                    break;
            }
        }
    }
}

internal sealed class SquareWebInteractiveSessionStore : IDisposable
{
    private readonly ConcurrentDictionary<string, SquareWebInteractiveSession> _sessions = new(StringComparer.Ordinal);
    private readonly object _addGate = new();
    private readonly TimeSpan _idleTimeout;
    private readonly int _maxSessions;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    internal SquareWebInteractiveSessionStore(TimeSpan idleTimeout, int maxSessions)
    {
        _idleTimeout = idleTimeout;
        _maxSessions = maxSessions;
        var interval = idleTimeout < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromSeconds(1)
            : idleTimeout < TimeSpan.FromMinutes(1) ? idleTimeout : TimeSpan.FromMinutes(1);
        _cleanupTimer = new Timer(_ => RemoveExpired(), null, interval, interval);
    }

    internal bool TryAdd(SquareWebInteractiveSession session)
    {
        RemoveExpired();
        lock (_addGate)
        {
            if (_disposed || _sessions.Count >= _maxSessions) return false;
            return _sessions.TryAdd(session.Token, session);
        }
    }

    internal bool TryGet(string token, out SquareWebInteractiveSession session)
    {
        if (!_sessions.TryGetValue(token, out session!)) return false;
        if (!session.IsExpired(_idleTimeout)) return true;
        if (_sessions.TryRemove(token, out var expired)) expired.Dispose();
        session = null!;
        return false;
    }

    internal void Remove(string token)
    {
        if (_sessions.TryRemove(token, out var session)) session.Dispose();
    }

    public void Dispose()
    {
        lock (_addGate)
        {
            if (_disposed) return;
            _disposed = true;
            _cleanupTimer.Dispose();
        }
        foreach (var pair in _sessions.ToArray())
            if (_sessions.TryRemove(pair.Key, out var session)) session.Dispose();
    }

    private void RemoveExpired()
    {
        if (_disposed) return;
        foreach (var pair in _sessions.ToArray())
            if (pair.Value.IsExpired(_idleTimeout) && _sessions.TryRemove(pair.Key, out var session))
                session.Dispose();
    }
}

internal sealed record SquareWebEventRequest(
    string Token,
    long Revision,
    int ElementId,
    string Type,
    string? Value,
    bool? Checked);

internal sealed record SquareWebInteractionResult(
    HtmlExportResult Export,
    long Revision,
    bool DefaultPrevented);
