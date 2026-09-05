namespace Square.Events;

/// <summary>
/// 标准事件类型名与工厂方法（DOM 风格小写类型 + 默认 bubbles/cancelable）。
/// </summary>
public static class StandardEvents
{
    /// <summary>指针按下。</summary>
    public const string PointerDown = "pointerdown";
    /// <summary>指针抬起。</summary>
    public const string PointerUp = "pointerup";
    /// <summary>指针移动。</summary>
    public const string PointerMove = "pointermove";
    /// <summary>指针取消。</summary>
    public const string PointerCancel = "pointercancel";
    /// <summary>滚轮。</summary>
    public const string Wheel = "wheel";
    /// <summary>滚动位置变化。</summary>
    public const string Scroll = "scroll";
    /// <summary>键按下。</summary>
    public const string KeyDown = "keydown";
    /// <summary>键抬起。</summary>
    public const string KeyUp = "keyup";
    /// <summary>文本输入（IME/组合输入相关，框架扩展名）。</summary>
    public const string TextInput = "textinput";
    /// <summary>焦点进入祖先链（冒泡）。</summary>
    public const string FocusIn = "focusin";
    /// <summary>焦点离开祖先链（冒泡）。</summary>
    public const string FocusOut = "focusout";
    /// <summary>获得焦点（不冒泡）。</summary>
    public const string Focus = "focus";
    /// <summary>失去焦点（不冒泡）。</summary>
    public const string Blur = "blur";
    /// <summary>单击。</summary>
    public const string Click = "click";
    /// <summary>请求上下文菜单。</summary>
    public const string ContextMenu = "contextmenu";
    /// <summary>值变更。</summary>
    public const string Change = "change";
    /// <summary>选择集合变更。</summary>
    public const string SelectionChange = "selectionchange";
    /// <summary>输入过程中的值变化。</summary>
    public const string Input = "input";
    /// <summary>请求动画帧（Square 扩展，非标准 DOM）。</summary>
    public const string RequestFrame = "requestframe";

    private static readonly Dictionary<string, EventInit> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        [PointerDown] = BubblingCancelable(),
        [PointerUp] = BubblingCancelable(),
        [PointerMove] = BubblingCancelable(),
        [PointerCancel] = BubblingCancelable(),
        [Wheel] = BubblingCancelable(),
        [KeyDown] = BubblingCancelable(),
        [KeyUp] = BubblingCancelable(),
        [TextInput] = Bubbling(),
        [FocusIn] = Bubbling(),
        [FocusOut] = Bubbling(),
        [Focus] = None(),
        [Blur] = None(),
        [Click] = BubblingCancelable(),
        [ContextMenu] = BubblingCancelable(),
        [Change] = Bubbling(),
        [SelectionChange] = Bubbling(),
        [Input] = Bubbling(),
        [RequestFrame] = Bubbling(),
    };

    /// <summary>获取类型默认的 <see cref="EventInit"/>（未知类型返回 null）。</summary>
    public static EventInit? GetDefaultInit(string type) =>
        Defaults.GetValueOrDefault(type);

    /// <summary>按类型创建事件；未知类型默认冒泡、不可取消。</summary>
    public static Event Create(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var init = GetDefaultInit(type) ?? Bubbling();
        return new Event(type, init);
    }

    /// <summary>创建无坐标兼容 pointerdown 事件。</summary>
    public static Event CreatePointerDown() => CreatePointer(PointerDown, default);
    /// <summary>创建无坐标兼容 pointerup 事件。</summary>
    public static Event CreatePointerUp() => CreatePointer(PointerUp, default);
    /// <summary>创建无坐标兼容 pointermove 事件。</summary>
    public static Event CreatePointerMove() => CreatePointer(PointerMove, default);
    /// <summary>创建无坐标兼容 pointercancel 事件。</summary>
    public static PointerEvent CreatePointerCancel() => CreatePointer(PointerCancel, default);
    /// <summary>创建带平台输入载荷的 pointerdown 事件。</summary>
    public static PointerEvent CreatePointerDown(Square.Platform.PointerInput input) =>
        CreatePointer(PointerDown, input);
    /// <summary>创建带平台输入载荷的 pointerup 事件。</summary>
    public static PointerEvent CreatePointerUp(Square.Platform.PointerInput input) =>
        CreatePointer(PointerUp, input);
    /// <summary>创建带平台输入载荷的 pointermove 事件。</summary>
    public static PointerEvent CreatePointerMove(Square.Platform.PointerInput input) =>
        CreatePointer(PointerMove, input);
    /// <summary>创建带平台输入载荷的 pointercancel 事件。</summary>
    public static PointerEvent CreatePointerCancel(Square.Platform.PointerInput input) =>
        CreatePointer(PointerCancel, input);
    private static PointerEvent CreatePointer(string type, Square.Platform.PointerInput input) =>
        new(type, input.Position.X, input.Position.Y, input.PointerId, input.DeviceKind,
            input.Button switch
            {
                Square.Platform.MouseButton.Left => 0,
                Square.Platform.MouseButton.Middle => 1,
                Square.Platform.MouseButton.Right => 2,
                _ => 0
            }, input.IsPrimary);
    /// <summary>创建 contextmenu 事件。</summary>
    public static PointerEvent CreateContextMenu(float x, float y) => new(ContextMenu, x, y, 2);
    /// <summary>创建 wheel 事件。</summary>
    public static WheelEvent CreateWheel(
        float deltaX = 0,
        float deltaY = 0,
        bool isPrecise = false,
        bool isInertial = false) => new(deltaX, deltaY, isPrecise, isInertial);
    /// <summary>创建 scroll 事件（不冒泡）。</summary>
    public static Event CreateScroll() => Create(Scroll);
    /// <summary>创建 keydown 事件。</summary>
    public static KeyboardEvent CreateKeyDown(int keyCode = 0, bool shiftKey = false, bool controlKey = false, bool altKey = false) =>
        new(KeyDown, keyCode, shiftKey, controlKey, altKey);
    /// <summary>创建 keyup 事件。</summary>
    public static KeyboardEvent CreateKeyUp(int keyCode = 0, bool shiftKey = false, bool controlKey = false, bool altKey = false) =>
        new(KeyUp, keyCode, shiftKey, controlKey, altKey);
    /// <summary>创建 click 事件。</summary>
    public static Event CreateClick() => Create(Click);
    /// <summary>创建 change 事件。</summary>
    public static Event CreateChange() => Create(Change);
    /// <summary>创建 selectionchange 事件。</summary>
    public static Event CreateSelectionChange() => Create(SelectionChange);
    /// <summary>创建 input 事件。</summary>
    public static Event CreateInput() => Create(Input);
    /// <summary>创建 focus 事件（不冒泡）。</summary>
    public static Event CreateFocus() => Create(Focus);
    /// <summary>创建 blur 事件（不冒泡）。</summary>
    public static Event CreateBlur() => Create(Blur);
    /// <summary>创建 focusin 事件（冒泡）。</summary>
    public static Event CreateFocusIn() => Create(FocusIn);
    /// <summary>创建 focusout 事件（冒泡）。</summary>
    public static Event CreateFocusOut() => Create(FocusOut);

    /// <summary>创建 requestframe 帧请求事件（Square 扩展）。</summary>
    public static FrameRequestEvent CreateRequestFrame(double framesPerSecond = 60d) =>
        new(framesPerSecond);

    /// <summary>创建带精确延迟的 requestframe 帧请求事件（Square 扩展）。</summary>
    public static FrameRequestEvent CreateRequestFrame(TimeSpan delay) => new(delay);

    private static EventInit BubblingCancelable() => new() { Bubbles = true, Cancelable = true };
    private static EventInit Bubbling() => new() { Bubbles = true, Cancelable = false };
    private static EventInit None() => new() { Bubbles = false, Cancelable = false };
}
