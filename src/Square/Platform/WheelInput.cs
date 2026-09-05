using Square.Graphics;

namespace Square.Platform;

/// <summary>平台滚轮输入，增量采用 DOM/content 方向（正 X 向右，正 Y 向下）。</summary>
public readonly struct WheelInput
{
    public WheelInput(
        Point position,
        float deltaX,
        float deltaY,
        bool isPrecise = false,
        bool isInertial = false)
        : this(position, deltaX, deltaY, isPrecise, isInertial, PointerDeviceKind.Mouse, 0)
    {
    }

    /// <summary>创建带设备来源的滚轮输入。</summary>
    public WheelInput(
        Point position,
        float deltaX,
        float deltaY,
        bool isPrecise,
        bool isInertial,
        PointerDeviceKind deviceKind,
        int pointerId = 0)
    {
        Position = position;
        DeltaX = deltaX;
        DeltaY = deltaY;
        IsPrecise = isPrecise;
        IsInertial = isInertial;
        DeviceKind = deviceKind;
        PointerId = pointerId;
    }

    public Point Position { get; }
    public float DeltaX { get; }
    public float DeltaY { get; }
    public bool IsPrecise { get; }
    public bool IsInertial { get; }
    /// <summary>产生滚轮输入的设备种类。</summary>
    public PointerDeviceKind DeviceKind { get; }
    /// <summary>产生滚轮输入的指针 ID。</summary>
    public int PointerId { get; }
}
