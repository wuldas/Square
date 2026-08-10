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
/// <summary>DevTools 滚轮输入。</summary>
public sealed record DevToolsWheelInput(Point Position, int Delta, KeyModifiers Modifiers = KeyModifiers.None);
