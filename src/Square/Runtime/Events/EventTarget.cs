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
    private readonly object _listenerSync = new();
    private readonly List<ListenerEntry> _listeners = [];
    private readonly List<AdapterEntry> _adapters = [];

    /// <summary>当前已注册的事件类型快照，按首次注册顺序去重。</summary>
    public IReadOnlyList<string> RegisteredEventTypes
    {
        get
        {
            lock (_listenerSync)
                return _listeners
                    .Select(static entry => entry.Type)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }
    }

    /// <summary>注册事件监听器（对齐 <c>addEventListener</c>）。</summary>
    public void AddEventListener(string type, Action<Event>? listener, AddEventListenerOptions? options = null)
    {
        if (listener == null) return;
        _ = AddEventListenerCore(NormalizeEventType(type), listener, options ?? new AddEventListenerOptions());
    }

    /// <summary>注册事件监听器；<paramref name="useCapture"/> 为 true 时在捕获阶段调用。</summary>
    public void AddEventListener(string type, Action<Event>? listener, bool useCapture) =>
        AddEventListener(type, listener, new AddEventListenerOptions { Capture = useCapture });

    /// <summary>注册事件监听器并返回可用于移除该监听器的句柄。</summary>
    public IDisposable Listen(string type, Action<Event> listener, AddEventListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(listener);
        type = NormalizeEventType(type);
        options ??= new AddEventListenerOptions();
        return CreateSubscription(AddEventListenerCore(type, listener, options));
    }

    /// <summary>注册无事件参数的监听器并返回移除句柄。</summary>
    public IDisposable Listen(string type, Action handler, AddEventListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        type = NormalizeEventType(type);
        options ??= new AddEventListenerOptions();
        Action<Event> adapter = _ => handler();
        return CreateSubscription(AddAdapterCore(type, handler, adapter, options));
    }

    /// <summary>注册强类型监听器并返回移除句柄。</summary>
    public IDisposable Listen<TEvent>(string type, Action<TEvent> handler, AddEventListenerOptions? options = null)
        where TEvent : Event
    {
        ArgumentNullException.ThrowIfNull(handler);
        type = NormalizeEventType(type);
        options ??= new AddEventListenerOptions();
        Action<Event> adapter = e =>
        {
            if (e is TEvent typed) handler(typed);
        };
        return CreateSubscription(AddAdapterCore(type, handler, adapter, options));
    }

    /// <summary>订阅无载荷的组件事件。</summary>
    public IDisposable Listen(ComponentEvent componentEvent, Action handler, AddEventListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(componentEvent);
        return Listen(componentEvent.Name, handler, options);
    }

    /// <summary>订阅无载荷的组件事件并接收事件对象。</summary>
    public IDisposable Listen(ComponentEvent componentEvent, Action<Event> handler, AddEventListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(componentEvent);
        return Listen(componentEvent.Name, handler, options);
    }

    /// <summary>订阅带载荷的组件事件。</summary>
    public IDisposable Listen<TDetail>(
        ComponentEvent<TDetail> componentEvent,
        Action handler,
        AddEventListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(componentEvent);
        return Listen(componentEvent.Name, handler, options);
    }

    /// <summary>订阅带载荷的组件事件并接收基础事件对象。</summary>
    public IDisposable Listen<TDetail>(
        ComponentEvent<TDetail> componentEvent,
        Action<Event> handler,
        AddEventListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(componentEvent);
        return Listen(componentEvent.Name, handler, options);
    }

    /// <summary>订阅带载荷的组件事件并接收强类型事件对象。</summary>
    public IDisposable Listen<TDetail>(
        ComponentEvent<TDetail> componentEvent,
        Action<CustomEvent<TDetail>> handler,
        AddEventListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(componentEvent);
        return Listen(componentEvent.Name, handler, options);
    }

    /// <summary>注册无事件参数的监听器（Square 便捷重载）。</summary>
    public void AddEventListener(string type, Action handler, AddEventListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        type = NormalizeEventType(type);
        options ??= new AddEventListenerOptions();
        Action<Event> adapter = _ => handler();
        _ = AddAdapterCore(type, handler, adapter, options);
    }

    /// <summary>注册无事件参数的监听器（捕获标志重载）。</summary>
    public void AddEventListener(string type, Action handler, bool useCapture) =>
        AddEventListener(type, handler, new AddEventListenerOptions { Capture = useCapture });

    /// <summary>注册对象式监听器。</summary>
    public void AddEventListener(string type, IEventListener listener, AddEventListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(listener);
        type = NormalizeEventType(type);
        options ??= new AddEventListenerOptions();
        _ = AddAdapterCore(type, listener, listener.HandleEvent, options);
    }

    /// <summary>注册强类型事件监听器（Square 便捷重载）。</summary>
    public void AddEventListener<TEvent>(string type, Action<TEvent> handler, AddEventListenerOptions? options = null)
        where TEvent : Event
    {
        ArgumentNullException.ThrowIfNull(handler);
        type = NormalizeEventType(type);
        options ??= new AddEventListenerOptions();
        Action<Event> adapter = e =>
        {
            if (e is TEvent typed) handler(typed);
        };
        _ = AddAdapterCore(type, handler, adapter, options);
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
        type = NormalizeEventType(type);
        ListenerEntry? entry = null;
        lock (_listenerSync)
        {
            var index = FindListenerIndex(type, listener, useCapture);
            if (index >= 0) entry = _listeners[index];
            else RemoveAdapterEntries(type, listener, useCapture);
        }
        if (entry != null) RemoveListenerEntry(entry);
    }

    /// <summary>移除无参监听器（须与注册时同一委托实例）。</summary>
    public void RemoveEventListener(string type, Action handler, bool useCapture = false)
    {
        type = NormalizeEventType(type);
        Action<Event>? adapter = null;
        lock (_listenerSync)
            if (TryTakeAdapter(type, handler, useCapture, out var found)) adapter = found;
        if (adapter != null) RemoveEventListener(type, adapter, useCapture);
    }

    /// <summary>移除对象式监听器。</summary>
    public void RemoveEventListener(string type, IEventListener listener, bool useCapture = false)
    {
        type = NormalizeEventType(type);
        Action<Event>? adapter = null;
        lock (_listenerSync)
            if (TryTakeAdapter(type, listener, useCapture, out var found)) adapter = found;
        if (adapter != null) RemoveEventListener(type, adapter, useCapture);
    }

    /// <summary>移除强类型监听器。</summary>
    public void RemoveEventListener<TEvent>(string type, Action<TEvent> handler, bool useCapture = false)
        where TEvent : Event
    {
        type = NormalizeEventType(type);
        Action<Event>? adapter = null;
        lock (_listenerSync)
            if (TryTakeAdapter(type, handler, useCapture, out var found)) adapter = found;
        if (adapter != null) RemoveEventListener(type, adapter, useCapture);
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

    private sealed class ListenerSubscription(EventTarget target, ListenerEntry entry) : IDisposable
    {
        private EventTarget? _target = target;

        /// <summary>移除监听器并释放句柄。</summary>
        public void Dispose()
        {
            _target?.RemoveListenerEntry(entry);
            _target = null;
        }
    }

    private sealed class EmptySubscription : IDisposable
    {
        public static EmptySubscription Instance { get; } = new();
        public void Dispose() { }
    }

    private bool DispatchCore(Event e, bool isTrusted)
    {
        e.ResetDispatchFlags();
        e.IsTrusted = isTrusted;
        e.Target ??= this;

        var path = e.TargetOnly ? new List<EventTarget> { this } : BuildPath();
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

        ListenerEntry[] snapshot;
        lock (_listenerSync) snapshot = _listeners.ToArray();
        foreach (var entry in snapshot)
        {
            if (e.ImmediatePropagationStopped) break;
            // 事件类型大小写不敏感（DOM 通常小写；兼容历史大小写混用）
            if (!string.Equals(entry.Type, e.Type, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Capture != captureOnly) continue;
            var invoke = false;
            CancellationTokenRegistration? onceRegistration = null;
            lock (_listenerSync)
            {
                invoke = !entry.Removed && _listeners.Contains(entry);
                if (invoke && entry.Once)
                {
                    entry.Removed = true;
                    _listeners.Remove(entry);
                    RemoveAdapterEntries(entry.Type, entry.Listener, entry.Capture);
                    onceRegistration = entry.Registration;
                }
            }
            onceRegistration?.Dispose();
            if (!invoke) continue;

            e.SetInPassiveListener(entry.Passive);
            try
            {
                entry.Listener(e);
            }
            finally
            {
                e.SetInPassiveListener(false);
            }

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

    private ListenerEntry? AddEventListenerCore(
        string type, Action<Event> listener, AddEventListenerOptions options)
    {
        lock (_listenerSync)
        {
            if (options.Signal.IsCancellationRequested) return null;
            var existing = FindListenerIndex(type, listener, options.Capture);
            if (existing >= 0) return _listeners[existing];

            var entry = new ListenerEntry(type, listener, options.Capture, options.Once, options.Passive, null);
            _listeners.Add(entry);
            if (!options.Signal.CanBeCanceled) return entry;

            var registration = options.Signal.Register(() => RemoveListenerEntry(entry));
            entry.Registration = registration;
            if (entry.Removed || !_listeners.Contains(entry))
            {
                registration.Dispose();
                return null;
            }
            return entry;
        }
    }

    private ListenerEntry? AddAdapterCore(
        string type, object original, Action<Event> adapter, AddEventListenerOptions options)
    {
        lock (_listenerSync)
        {
            if (options.Signal.IsCancellationRequested) return null;
            var adapterIndex = _adapters.FindLastIndex(entry =>
                entry.Capture == options.Capture &&
                string.Equals(entry.Type, type, StringComparison.OrdinalIgnoreCase) &&
                OriginalMatches(entry.Original, original));
            if (adapterIndex >= 0)
            {
                var listenerIndex = FindListenerIndex(type, _adapters[adapterIndex].Adapter, options.Capture);
                if (listenerIndex >= 0) return _listeners[listenerIndex];
                _adapters.RemoveAt(adapterIndex);
            }

            var entry = AddEventListenerCore(type, adapter, options);
            if (entry != null)
                _adapters.Add(new AdapterEntry(type, original, adapter, options.Capture));
            return entry;
        }
    }

    private IDisposable CreateSubscription(ListenerEntry? entry) =>
        entry == null ? EmptySubscription.Instance : new ListenerSubscription(this, entry);

    private void RemoveListenerEntry(ListenerEntry entry)
    {
        CancellationTokenRegistration? registration = null;
        lock (_listenerSync)
        {
            if (entry.Removed) return;
            entry.Removed = true;
            var index = _listeners.FindIndex(candidate => ReferenceEquals(candidate, entry));
            if (index < 0) return;
            registration = _listeners[index].Registration;
            _listeners.RemoveAt(index);
            RemoveAdapterEntries(entry.Type, entry.Listener, entry.Capture);
        }
        registration?.Dispose();
    }

    private void RemoveAdapterEntries(string type, Action<Event> adapter, bool capture) =>
        _adapters.RemoveAll(entry =>
            entry.Capture == capture &&
            string.Equals(entry.Type, type, StringComparison.OrdinalIgnoreCase) &&
            Equals(entry.Adapter, adapter));

    private bool HasAdapter(string type, object original, bool capture) =>
        _adapters.Exists(entry =>
            entry.Capture == capture &&
            string.Equals(entry.Type, type, StringComparison.OrdinalIgnoreCase) &&
            OriginalMatches(entry.Original, original));

    private bool TryTakeAdapter(string type, object original, bool capture, out Action<Event> adapter)
    {
        var index = _adapters.FindLastIndex(entry =>
            entry.Capture == capture &&
            string.Equals(entry.Type, type, StringComparison.OrdinalIgnoreCase) &&
            OriginalMatches(entry.Original, original));
        if (index < 0)
        {
            adapter = null!;
            return false;
        }
        adapter = _adapters[index].Adapter;
        _adapters.RemoveAt(index);
        return true;
    }

    private static bool OriginalMatches(object registered, object candidate) =>
        registered is IEventListener || candidate is IEventListener
            ? ReferenceEquals(registered, candidate)
            : Equals(registered, candidate);

    private static string NormalizeEventType(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return type.Trim();
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
        public CancellationTokenRegistration? Registration { get; set; } = registration;
        /// <summary>该具体注册是否已被移除。</summary>
        public bool Removed { get; set; }
    }

    private sealed record AdapterEntry(string Type, object Original, Action<Event> Adapter, bool Capture);
}
