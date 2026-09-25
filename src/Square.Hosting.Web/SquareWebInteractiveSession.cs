using System.Collections.Concurrent;
using System.Text;
using Square.Controls;
using Square.CSS.Engine;
using Square.Events;
using Square.Native.Html;
using Square.Runtime;
using Square.UI;
using Square.Html;

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
            if (current is UIElement { IsDisabled: true } or HTMLElement { IsDisabled: true }) return true;
        return false;
    }

    private static void SynchronizeControlValue(Element target, SquareWebEventRequest request)
    {
        if (target is HTMLElement)
        {
            SynchronizeHtmlFormState(target, request);
            return;
        }
        if (request.Type is not ("input" or "change")) return;
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

    /// <summary>
    /// Mirrors the browser-side form state carried in the event payload onto the HTML semantic host
    /// before dispatch: value for input/textarea, checked for checkbox/radio (radio exclusivity
    /// sweeps same-name hosts), and selected on the matching option. Handlers then observe current
    /// state and the next export reflects it; host OnPropertyChanged reconciles native proxies.
    /// </summary>
    private static void SynchronizeHtmlFormState(Element host, SquareWebEventRequest request)
    {
        switch (host)
        {
            case HTMLInputElement input:
                SynchronizeHtmlInput(input, request);
                break;
            case HTMLTextAreaElement textarea when request.Value != null:
                textarea.Value = request.Value;
                break;
            case HTMLSelectElement select when request.Value != null:
                SynchronizeHtmlSelectedOption(select, request.Value);
                break;
        }
    }

    private static void SynchronizeHtmlInput(HTMLInputElement input, SquareWebEventRequest request)
    {
        var type = (input.Type ?? "text").Trim().ToLowerInvariant();
        if (type is "checkbox" or "radio")
        {
            if (!request.Checked.HasValue) return;
            if (request.Checked.Value && type == "radio") UncheckHtmlRadioGroup(input);
            input.Checked = request.Checked.Value;
            return;
        }
        if (request.Value != null) input.Value = request.Value;
    }

    /// <summary>Radio exclusivity: clears the checked attribute of same-name radio hosts tree-wide.</summary>
    private static void UncheckHtmlRadioGroup(HTMLInputElement radio)
    {
        if (string.IsNullOrEmpty(radio.Name) || RootOf(radio) is not { } root) return;
        ClearHtmlCheckedAttribute(root, radio, radio.Name);
    }

    private static Element? RootOf(Element element)
    {
        Element? root = element;
        while (root.Parent != null) root = root.Parent;
        return root;
    }

    private static void ClearHtmlCheckedAttribute(Element element, HTMLInputElement radio, string name)
    {
        foreach (var child in element.Children)
        {
            if (child is HTMLInputElement input && !ReferenceEquals(input, radio) &&
                string.Equals((input.Type ?? "text").Trim(), "radio", StringComparison.OrdinalIgnoreCase) &&
                input.Name == name && input.Checked)
                input.Checked = false;
            ClearHtmlCheckedAttribute(child, radio, name);
        }
    }

    /// <summary>Selects the first option whose effective value matches and deselects every other one.</summary>
    private static void SynchronizeHtmlSelectedOption(HTMLSelectElement select, string value)
    {
        var options = new List<HTMLOptionElement>();
        CollectHtmlOptions(select, options);
        HTMLOptionElement? match = null;
        foreach (var option in options)
            if (HtmlOptionValue(option) == value)
            {
                match = option;
                break;
            }
        foreach (var option in options)
            option.Selected = ReferenceEquals(option, match);
        select.Value = value;
    }

    private static void CollectHtmlOptions(Element element, List<HTMLOptionElement> options)
    {
        foreach (var child in element.Children)
        {
            if (child is HTMLOptionElement option) options.Add(option);
            else CollectHtmlOptions(child, options);
        }
    }

    /// <summary>Option value mirrors the DOM: the value attribute, else the concatenated text runs.</summary>
    private static string HtmlOptionValue(HTMLOptionElement option)
    {
        if (option.Value != null) return option.Value;
        var builder = new StringBuilder();
        AppendHtmlText(option, builder);
        return builder.ToString();
    }

    private static void AppendHtmlText(Element element, StringBuilder builder)
    {
        if (element is Square.Controls.Text run)
        {
            builder.Append(run.TextContent);
            return;
        }
        foreach (var child in element.ChildNodes)
        {
            if (child is Square.UI.Text text) builder.Append(text.Data);
            else if (child is Element childElement) AppendHtmlText(childElement, builder);
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
