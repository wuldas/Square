using Square.Graphics;
using Square.Platform;

namespace Square.Hosting;

/// <summary>DevTools 指针输入。</summary>
public sealed record DevToolsPointerInput(
    Point Position,
    MouseAction Action,
    KeyModifiers Modifiers = KeyModifiers.None,
    MouseButton Button = MouseButton.Left);
/// <summary>DevTools 键盘输入。</summary>
public sealed record DevToolsKeyInput(int KeyCode, KeyAction Action, KeyModifiers Modifiers = KeyModifiers.None);
/// <summary>DevTools 滚轮输入（增量采用 DOM/content 方向）。</summary>
public sealed record DevToolsWheelInput(
    Point Position,
    float DeltaX,
    float DeltaY,
    bool IsPrecise = false,
    bool IsInertial = false,
    KeyModifiers Modifiers = KeyModifiers.None)
{
    /// <summary>保留旧版单纵向增量构造形式；旧增量为原生滚轮方向。</summary>
    public DevToolsWheelInput(Point position, int delta, KeyModifiers modifiers = KeyModifiers.None)
        : this(position, 0, -delta, false, false, modifiers)
    {
    }
}
