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
    {
        Position = position;
        DeltaX = deltaX;
        DeltaY = deltaY;
        IsPrecise = isPrecise;
        IsInertial = isInertial;
    }

    public Point Position { get; }
    public float DeltaX { get; }
    public float DeltaY { get; }
    public bool IsPrecise { get; }
    public bool IsInertial { get; }
}
