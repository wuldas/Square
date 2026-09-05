using Square.Graphics;

namespace Square.Platform;

/// <summary>统一指针动作，供鼠标、触摸和手写笔宿主使用。</summary>
public enum PointerAction
{
    /// <summary>指针按下。</summary>
    Down,
    /// <summary>指针移动。</summary>
    Move,
    /// <summary>指针抬起。</summary>
    Up,
    /// <summary>指针取消。</summary>
    Cancel
}

/// <summary>指针设备种类。</summary>
public enum PointerDeviceKind
{
    /// <summary>鼠标。</summary>
    Mouse,
    /// <summary>触摸屏。</summary>
    Touch,
    /// <summary>手写笔。</summary>
    Pen
}

/// <summary>平台无关的指针输入快照。</summary>
public readonly struct PointerInput
{
    /// <summary>创建指针输入。</summary>
    public PointerInput(
        Point position,
        PointerAction action,
        int pointerId = 0,
        PointerDeviceKind deviceKind = PointerDeviceKind.Mouse,
        MouseButton button = MouseButton.None,
        bool isPrimary = true)
    {
        Position = position;
        Action = action;
        PointerId = pointerId;
        DeviceKind = deviceKind;
        Button = button;
        IsPrimary = isPrimary;
    }

    /// <summary>创建指针输入（动作优先的兼容形式）。</summary>
    public PointerInput(
        PointerAction action,
        Point position,
        int pointerId = 0,
        PointerDeviceKind deviceKind = PointerDeviceKind.Mouse,
        MouseButton button = MouseButton.None,
        bool isPrimary = true)
        : this(position, action, pointerId, deviceKind, button, isPrimary)
    {
    }

    /// <summary>客户区逻辑坐标。</summary>
    public Point Position { get; }
    /// <summary>指针动作。</summary>
    public PointerAction Action { get; }
    /// <summary>设备内指针 ID。</summary>
    public int PointerId { get; }
    /// <summary>设备种类。</summary>
    public PointerDeviceKind DeviceKind { get; }
    /// <summary>按钮。</summary>
    public MouseButton Button { get; }
    /// <summary>是否为主指针。</summary>
    public bool IsPrimary { get; }
}
