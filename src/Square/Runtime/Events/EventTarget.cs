namespace Square.Events;

/// <summary>对象式事件监听器（对齐 <c>EventListener</c> 接口的 <c>handleEvent</c>）。</summary>
public interface IEventListener
{
    /// <summary>处理事件。</summary>
    void HandleEvent(Event e);
}

/// <summary><see cref="EventTarget.AddEventListener"/> 的选项（对齐 <c>AddEventListenerOptions</c>）。</summary>
public sealed class AddEventListenerOptions
{
    /// <summary>是否在捕获阶段调用。</summary>
    public bool Capture { get; init; }

    /// <summary>触发一次后自动移除。</summary>
    public bool Once { get; init; }

    /// <summary>声明不会调用 <see cref="Event.PreventDefault"/>（调用将被忽略）。</summary>
    public bool Passive { get; init; }

    /// <summary>取消令牌；abort 时移除监听（对齐 AbortSignal 的最小实现）。</summary>
    public CancellationToken Signal { get; init; }
}

/// <summary><see cref="EventTarget.RemoveEventListener"/> 的选项。</summary>
public sealed class EventListenerOptions
{
    /// <summary>是否匹配捕获阶段注册的监听器。</summary>
    public bool Capture { get; init; }
}

/// <summary>
/// 可接收事件的对象（对齐 DOM <c>EventTarget</c>）。
/// 提供 <see cref="AddEventListener"/> / <see cref="RemoveEventListener"/> / <see cref="DispatchEvent"/>。
/// </summary>
public class EventTarget
{
    private readonly List<ListenerEntry> _listeners = [];
    private readonly List<AdapterEntry> _adapters = [];

    /// <summary>当前已注册的事件类型快照，按首次注册顺序去重。</summary>
    public IReadOnlyList<string> RegisteredEventTypes => _listeners
        .Select(static entry => entry.Type)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>注册事件监听器（对齐 <c>addEventListener</c>）。</summary>
    public void AddEventListener(string type, Action<Event>? listener, AddEventListenerOptions? options = null)
    {
        if (listener == null) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        type = type.Trim();
        options ??= new AddEventListenerOptions();

        if (ContainsListener(type, listener, options.Capture)) return;

        CancellationTokenRegistration? registration = null;
        if (options.Signal.CanBeCanceled)
        {
            if (options.Signal.IsCancellationRequested) return;
            registration = options.Signal.Register(() => RemoveEventListener(type, listener, options.Capture));
        }

        _listeners.Add(new ListenerEntry(type, listener, options.Capture, options.Once, options.Passive, registration));
    }

    /// <summary>注册事件监听器；<paramref name="useCapture"/> 为 true 时在捕获阶段调用。</summary>
    public void AddEventListener(string type, Action<Event>? listener, bool useCapture) =>
        AddEventListener(type, listener, new AddEventListenerOptions { Capture = useCapture });

    /// <summary>注册事件监听器并返回可用于移除该监听器的句柄。</summary>
    public IDisposable Listen(string type, Action<Event> listener, AddEventListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(listener);
        options ??= new AddEventListenerOptions();
        AddEventListener(type, listener, options);
        return new ListenerSubscription(this, type.Trim(), listener, options.Capture);
    }

    /// <summary>注册无事件参数的监听器（Square 便捷重载）。</summary>
    public void AddEventListener(string type, Action handler, AddEventListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        options ??= new AddEventListenerOptions();
        if (HasAdapter(type, handler, options.Capture)) return;
        Action<Event> adapter = _ => handler();
        AddEventListener(type, adapter, options);
        TrackAdapter(type, handler, adapter, options.Capture);
    }

    /// <summary>注册无事件参数的监听器（捕获标志重载）。</summary>
    public void AddEventListener(string type, Action handler, bool useCapture) =>
        AddEventListener(type, handler, new AddEventListenerOptions { Capture = useCapture });

    /// <summary>注册对象式监听器。</summary>
    public void AddEventListener(string type, IEventListener listener, AddEventListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(listener);
        options ??= new AddEventListenerOptions();
        if (HasAdapter(type, listener, options.Capture)) return;
        Action<Event> adapter = listener.HandleEvent;
        AddEventListener(type, adapter, options);
        TrackAdapter(type, listener, adapter, options.Capture);
    }

    /// <summary>注册强类型事件监听器（Square 便捷重载）。</summary>
    public void AddEventListener<TEvent>(string type, Action<TEvent> handler, AddEventListenerOptions? options = null)
        where TEvent : Event
    {
        ArgumentNullException.ThrowIfNull(handler);
        options ??= new AddEventListenerOptions();
        if (HasAdapter(type, handler, options.Capture)) return;
        Action<Event> adapter = e =>
        {
            if (e is TEvent typed) handler(typed);
        };
        AddEventListener(type, adapter, options);
        TrackAdapter(type, handler, adapter, options.Capture);
    }

    /// <summary>移除事件监听器（对齐 <c>removeEventListener</c>）。</summary>
    public void RemoveEventListener(string type, Action<Event>? listener, EventListenerOptions? options = null)
    {
        if (listener == null) return;
        RemoveEventListener(type, listener, options?.Capture ?? false);
    }

    /// <summary>移除事件监听器。</summary>
    public void RemoveEventListener(string type, Action<Event>? listener, bool useCapture)
    {
        if (listener == null) return;
        var index = FindListenerIndex(type, listener, useCapture);
        if (index < 0) return;
        _listeners[index].Registration?.Dispose();
        _listeners.RemoveAt(index);
    }

    /// <summary>移除无参监听器（须与注册时同一委托实例）。</summary>
    public void RemoveEventListener(string type, Action handler, bool useCapture = false)
    {
        if (TryTakeAdapter(type, handler, useCapture, out var adapter))
            RemoveEventListener(type, adapter, useCapture);
    }

    /// <summary>移除对象式监听器。</summary>
    public void RemoveEventListener(string type, IEventListener listener, bool useCapture = false)
    {
        if (TryTakeAdapter(type, listener, useCapture, out var adapter))
            RemoveEventListener(type, adapter, useCapture);
    }

    /// <summary>移除强类型监听器。</summary>
    public void RemoveEventListener<TEvent>(string type, Action<TEvent> handler, bool useCapture = false)
        where TEvent : Event
    {
        if (TryTakeAdapter(type, handler, useCapture, out var adapter))
            RemoveEventListener(type, adapter, useCapture);
    }

    /// <summary>
    /// 同步派发事件（对齐 <c>dispatchEvent</c>）。
    /// 返回 false 表示事件可取消且已 <see cref="Event.PreventDefault"/>。
    /// </summary>
    public bool DispatchEvent(Event e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (string.IsNullOrWhiteSpace(e.Type))
            throw new InvalidOperationException("Event type was not specified.");

        return DispatchCore(e, isTrusted: false);
    }

    /// <summary>派发用户代理/平台事件，并将 <see cref="Event.IsTrusted"/> 设为 true。</summary>
    public bool DispatchTrusted(Event e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return DispatchCore(e, isTrusted: true);
    }

    /// <summary>返回事件路径上的父目标（Element 为 Parent 或 OwnerDocument）。</summary>
    protected virtual EventTarget? GetEventParent() => null;

    /// <summary>派发结束后若未 preventDefault，可执行默认行为（扩展点）。</summary>
    protected virtual void OnDefaultAction(Event e) { }

    private sealed class ListenerSubscription(
        EventTarget target,
        string type,
        Action<Event> listener,
        bool capture) : IDisposable
    {
        private EventTarget? _target = target;

        /// <summary>移除监听器并释放句柄。</summary>
        public void Dispose()
        {
            _target?.RemoveEventListener(type, listener, capture);
            _target = null;
        }
    }

    private bool DispatchCore(Event e, bool isTrusted)
    {
        e.ResetDispatchFlags();
        e.IsTrusted = isTrusted;
        e.Target ??= this;

        var path = BuildPath();
        e.SetPath(path);

        for (var i = path.Count - 1; i > 0; i--)
        {
            if (e.PropagationStopped) break;
            path[i].InvokeListeners(e, EventPhase.CapturingPhase, captureOnly: true);
        }

        if (!e.PropagationStopped)
        {
            InvokeListeners(e, EventPhase.AtTarget, captureOnly: true);
            if (!e.PropagationStopped)
                InvokeListeners(e, EventPhase.AtTarget, captureOnly: false);
        }

        if (e.Bubbles && !e.PropagationStopped)
        {
            for (var i = 1; i < path.Count; i++)
            {
                if (e.PropagationStopped) break;
                path[i].InvokeListeners(e, EventPhase.BubblingPhase, captureOnly: false);
            }
        }

        e.EventPhase = EventPhase.None;
        e.CurrentTarget = null;

        if (e.Cancelable && !e.DefaultPrevented)
            OnDefaultAction(e);

        return !(e.Cancelable && e.DefaultPrevented);
    }

    private List<EventTarget> BuildPath()
    {
        var path = new List<EventTarget>();
        for (EventTarget? current = this; current != null; current = current.GetEventParent())
            path.Add(current);
        return path;
    }

    private void InvokeListeners(Event e, EventPhase phase, bool captureOnly)
    {
        e.CurrentTarget = this;
        e.EventPhase = phase;

        var snapshot = _listeners.ToArray();
        foreach (var entry in snapshot)
        {
            if (e.ImmediatePropagationStopped) break;
            // 事件类型大小写不敏感（DOM 通常小写；兼容历史大小写混用）
            if (!string.Equals(entry.Type, e.Type, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Capture != captureOnly) continue;
            if (!_listeners.Contains(entry)) continue;

            e.SetInPassiveListener(entry.Passive);
            try
            {
                entry.Listener(e);
            }
            finally
            {
                e.SetInPassiveListener(false);
            }

            if (entry.Once)
                RemoveEventListener(entry.Type, entry.Listener, entry.Capture);
        }
    }

    private bool ContainsListener(string type, Action<Event> listener, bool capture) =>
        FindListenerIndex(type, listener, capture) >= 0;

    private int FindListenerIndex(string type, Action<Event> listener, bool capture)
    {
        for (var i = 0; i < _listeners.Count; i++)
        {
            var entry = _listeners[i];
            if (entry.Capture == capture &&
                string.Equals(entry.Type, type, StringComparison.OrdinalIgnoreCase) &&
                Equals(entry.Listener, listener))
                return i;
        }
        return -1;
    }

    private void TrackAdapter(string type, object original, Action<Event> adapter, bool capture) =>
        _adapters.Add(new AdapterEntry(type, original, adapter, capture));

    private bool HasAdapter(string type, object original, bool capture) =>
        _adapters.Exists(entry =>
            entry.Capture == capture &&
            string.Equals(entry.Type, type, StringComparison.Ordinal) &&
            Equals(entry.Original, original));

    private bool TryTakeAdapter(string type, object original, bool capture, out Action<Event> adapter)
    {
        var index = _adapters.FindLastIndex(entry =>
            entry.Capture == capture &&
            string.Equals(entry.Type, type, StringComparison.Ordinal) &&
            Equals(entry.Original, original));
        if (index < 0)
        {
            adapter = null!;
            return false;
        }
        adapter = _adapters[index].Adapter;
        _adapters.RemoveAt(index);
        return true;
    }

    private sealed class ListenerEntry(
        string type,
        Action<Event> listener,
        bool capture,
        bool once,
        bool passive,
        CancellationTokenRegistration? registration)
    {
        /// <summary>事件类型名。</summary>
        public string Type { get; } = type;
        /// <summary>监听回调。</summary>
        public Action<Event> Listener { get; } = listener;
        /// <summary>是否在捕获阶段调用。</summary>
        public bool Capture { get; } = capture;
        /// <summary>是否触发一次后自动移除。</summary>
        public bool Once { get; } = once;
        /// <summary>是否为 passive 监听。</summary>
        public bool Passive { get; } = passive;
        /// <summary>取消令牌注册句柄。</summary>
        public CancellationTokenRegistration? Registration { get; } = registration;
    }

    private sealed record AdapterEntry(string Type, object Original, Action<Event> Adapter, bool Capture);
}
