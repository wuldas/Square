namespace Square.Events;

/// <summary>事件传播阶段（对齐 DOM <c>Event.eventPhase</c>）。</summary>
public enum EventPhase
{
    /// <summary>未派发。</summary>
    None = 0,
    /// <summary>捕获阶段。</summary>
    CapturingPhase = 1,
    /// <summary>目标阶段。</summary>
    AtTarget = 2,
    /// <summary>冒泡阶段。</summary>
    BubblingPhase = 3
}

/// <summary>创建 <see cref="Event"/> 时的初始化选项（对齐 <c>EventInit</c>）。</summary>
public sealed class EventInit
{
    /// <summary>是否冒泡。</summary>
    public bool Bubbles { get; init; }

    /// <summary>是否可调用 <see cref="Event.PreventDefault"/> 取消默认行为。</summary>
    public bool Cancelable { get; init; }

    /// <summary>是否可穿越 shadow 边界（预留）。</summary>
    public bool Composed { get; init; }
}

/// <summary>
/// DOM 事件对象（对齐 <c>Event</c>）。
/// 通过 <see cref="EventTarget.DispatchEvent"/> 同步派发。
/// </summary>
public class Event
{
    private bool _propagationStopped;
    private bool _immediatePropagationStopped;
    private bool _inPassiveListener;
    private IReadOnlyList<EventTarget>? _path;

    /// <summary>使用类型名与可选初始化选项创建事件。</summary>
    public Event(string type, EventInit? init = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Type = type;
        init ??= new EventInit();
        Bubbles = init.Bubbles;
        Cancelable = init.Cancelable;
        Composed = init.Composed;
        TimeStamp = Environment.TickCount64;
    }

    /// <summary>事件类型名（对齐 <c>type</c>）。</summary>
    public string Type { get; }

    /// <summary>派发目标（对齐 <c>target</c>）。</summary>
    public EventTarget? Target { get; internal set; }

    /// <summary>当前处理该事件的目标（对齐 <c>currentTarget</c>）。</summary>
    public EventTarget? CurrentTarget { get; internal set; }

    /// <summary>当前传播阶段（对齐 <c>eventPhase</c>）。</summary>
    public EventPhase EventPhase { get; internal set; }

    /// <summary>是否冒泡（对齐 <c>bubbles</c>）。</summary>
    public bool Bubbles { get; }

    /// <summary>是否可取消默认行为（对齐 <c>cancelable</c>）。</summary>
    public bool Cancelable { get; }

    /// <summary>是否可穿越 shadow 边界（对齐 <c>composed</c>；本阶段路径不含 shadow）。</summary>
    public bool Composed { get; }

    /// <summary>是否已调用 <see cref="PreventDefault"/>（对齐 <c>defaultPrevented</c>）。</summary>
    public bool DefaultPrevented { get; private set; }

    /// <summary>是否由用户代理/平台发起（对齐 <c>isTrusted</c>）。</summary>
    public bool IsTrusted { get; internal set; }

    /// <summary>创建时间戳（毫秒量级，对齐 <c>timeStamp</c> 语义简化）。</summary>
    public double TimeStamp { get; }

    internal bool PropagationStopped => _propagationStopped;
    internal bool ImmediatePropagationStopped => _immediatePropagationStopped;
    internal bool TargetOnly { get; private set; }

    /// <summary>阻止默认行为（仅当 <see cref="Cancelable"/> 且非 passive 监听时有效）。</summary>
    public void PreventDefault()
    {
        if (Cancelable && !_inPassiveListener) DefaultPrevented = true;
    }

    internal void SetInPassiveListener(bool value) => _inPassiveListener = value;

    /// <summary>停止继续向后续目标传播（对齐 <c>stopPropagation</c>）。</summary>
    public void StopPropagation() => _propagationStopped = true;

    /// <summary>立即停止传播，且同节点后续监听器不再调用（对齐 <c>stopImmediatePropagation</c>）。</summary>
    public void StopImmediatePropagation()
    {
        _propagationStopped = true;
        _immediatePropagationStopped = true;
    }

    /// <summary>返回派发路径上的目标列表（对齐 <c>composedPath</c> 简化版）。</summary>
    public IReadOnlyList<EventTarget> ComposedPath() => _path ?? Array.Empty<EventTarget>();

    internal void SetPath(IReadOnlyList<EventTarget> path) => _path = path;

    internal void SetTargetOnly() => TargetOnly = true;

    internal void ResetDispatchFlags()
    {
        _propagationStopped = false;
        _immediatePropagationStopped = false;
        DefaultPrevented = false;
        EventPhase = EventPhase.None;
        CurrentTarget = null;
    }
}

/// <summary>滚轮事件（Square 对齐 DOM WheelEvent 的最小实现）。</summary>
public sealed class WheelEvent : Event
{
    /// <summary>用横向/纵向增量、精确性和惯性标记与可选初始化选项创建滚轮事件。</summary>
    public WheelEvent(
        float deltaX,
        float deltaY,
        bool isPrecise = false,
        bool isInertial = false,
        EventInit? init = null)
        : base(StandardEvents.Wheel, init ?? StandardEvents.GetDefaultInit(StandardEvents.Wheel))
    {
        DeltaX = deltaX;
        DeltaY = deltaY;
        IsPrecise = isPrecise;
        IsInertial = isInertial;
    }

    /// <summary>保留带初始化选项的旧构造形式。</summary>
    public WheelEvent(float deltaX, float deltaY, EventInit? init)
        : this(deltaX, deltaY, false, false, init)
    {
    }

    /// <summary>横向滚动增量。</summary>
    public float DeltaX { get; }
    /// <summary>纵向滚动增量。</summary>
    public float DeltaY { get; }
    /// <summary>是否来自精确滚动设备。</summary>
    public bool IsPrecise { get; }
    /// <summary>是否为惯性滚动阶段。</summary>
    public bool IsInertial { get; }
}

/// <summary>指针事件（Square 对齐 DOM PointerEvent 的最小实现）。</summary>
public sealed class PointerEvent : Event
{
    /// <summary>创建带客户区坐标和按键编号的指针事件。</summary>
    public PointerEvent(string type, float clientX, float clientY, int button = 0, EventInit? init = null)
        : base(type, init ?? StandardEvents.GetDefaultInit(type))
    {
        ClientX = clientX;
        ClientY = clientY;
        Button = button;
    }

    /// <summary>客户区横坐标。</summary>
    public float ClientX { get; }
    /// <summary>客户区纵坐标。</summary>
    public float ClientY { get; }
    /// <summary>触发按键编号：0 主键，1 中键，2 次键。</summary>
    public int Button { get; }
}

/// <summary>键盘事件（Square 对齐 DOM KeyboardEvent 的最小实现）。</summary>
public sealed class KeyboardEvent : Event
{
    /// <summary>用类型名、键码与修饰键状态创建键盘事件。</summary>
    public KeyboardEvent(string type, int keyCode, bool shiftKey = false, bool controlKey = false, bool altKey = false,
        EventInit? init = null)
        : base(type, init ?? StandardEvents.GetDefaultInit(type))
    {
        KeyCode = keyCode;
        ShiftKey = shiftKey;
        ControlKey = controlKey;
        AltKey = altKey;
    }

    /// <summary>键码。</summary>
    public int KeyCode { get; }
    /// <summary>是否按下 Shift 键。</summary>
    public bool ShiftKey { get; }
    /// <summary>是否按下 Control 键。</summary>
    public bool ControlKey { get; }
    /// <summary>是否按下 Alt 键。</summary>
    public bool AltKey { get; }
}
