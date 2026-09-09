using System.Text;
using Godot;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using GControl = Godot.Control;

namespace Square.Platform.Godot;

/// <summary>将 Godot GUI 输入事件转换为 Square 既有输入契约。</summary>
internal static class GodotInput
{
    internal static bool TryHandle(GControl control, GodotPlatformHost host, ApplicationSession session, InputEvent input)
    {
        if (!control.Visible || session.IsSuspended || session.IsDetached) return false;

        switch (input)
        {
            case InputEventMouseButton button:
                return HandleMouseButton(control, host, session, button);
            case InputEventMouseMotion motion:
                return HandleMouseMotion(host, motion);
            case InputEventPanGesture pan:
                return HandlePan(host, pan);
            case InputEventKey key:
                return HandleKey(host, session, key);
            default:
                return false;
        }
    }

    private static bool HandleMouseButton(
        GControl control,
        GodotPlatformHost host,
        ApplicationSession session,
        InputEventMouseButton input)
    {
        var position = ToPoint(input.Position);
        var name = input.ButtonIndex.ToString();
        if (name is "WheelUp" or "WheelDown" or "WheelLeft" or "WheelRight")
        {
            if (!input.Pressed) return false;
            var factor = input.Factor == 0 ? 1f : input.Factor;
            var precise = !float.IsFinite(factor) || MathF.Abs(factor - MathF.Round(factor)) > 0.0001f;
            var horizontal = name is "WheelLeft" or "WheelRight";
            var positive = name is "WheelDown" or "WheelRight";
            var amount = 120f * factor * (positive ? 1f : -1f);
            host.RaiseWheel(new WheelInput(
                position,
                horizontal ? amount : 0,
                horizontal ? 0 : amount,
                precise,
                isInertial: false));
            return true;
        }

        var mouseButton = MapMouseButton(name);
        if (mouseButton == MouseButton.None) return false;
        if (input.Canceled)
        {
            host.Modifiers = KeyModifiers.None;
            session.NotifyFocusLost();
            return true;
        }

        if (input.Pressed)
        {
            control.GrabFocus();
            host.RaisePointer(new PointerInput(position, PointerAction.Down, button: mouseButton));
        }
        else
        {
            host.RaisePointer(new PointerInput(position, PointerAction.Up, button: mouseButton));
        }
        return true;
    }

    private static bool HandleMouseMotion(GodotPlatformHost host, InputEventMouseMotion input)
    {
        var mask = (int)input.ButtonMask;
        var button = (mask & 1) != 0
            ? MouseButton.Left
            : (mask & 2) != 0
                ? MouseButton.Right
                : (mask & 4) != 0 ? MouseButton.Middle : MouseButton.None;
        host.RaisePointer(new PointerInput(ToPoint(input.Position), PointerAction.Move, button: button));
        return true;
    }

    private static bool HandlePan(GodotPlatformHost host, InputEventPanGesture input)
    {
        var delta = input.Delta;
        host.RaiseWheel(new WheelInput(
            ToPoint(input.Position),
            delta.X * 120f,
            delta.Y * 120f,
            isPrecise: true,
            isInertial: true));
        return true;
    }

    private static bool HandleKey(GodotPlatformHost host, ApplicationSession session, InputEventKey input)
    {
        var modifiers = KeyModifiers.None;
        if (input.ShiftPressed) modifiers |= KeyModifiers.Shift;
        if (input.CtrlPressed || input.MetaPressed) modifiers |= KeyModifiers.Control;
        if (input.AltPressed) modifiers |= KeyModifiers.Alt;
        host.Modifiers = modifiers;

        var keyCode = MapKeyCode(input.Keycode.ToString());
        var unicode = (uint)Math.Max(0, input.Unicode);
        var text = IsPrintableScalar(unicode) ? char.ConvertFromUtf32((int)unicode) : null;
        var altGrText = input.CtrlPressed && input.AltPressed && text != null;
        var handled = keyCode != 0;
        if (keyCode != 0 && !(altGrText && IsLetterOrDigit(keyCode)))
        {
            host.RaiseKey(keyCode, input.Pressed ? KeyAction.Down : KeyAction.Up);
            handled = true;
        }

        if (input.Pressed && text != null && !input.CtrlPressed && !input.MetaPressed ||
            input.Pressed && text != null && altGrText)
        {
            host.RaiseText(text);
            handled = true;
        }

        if (!handled && input.Pressed && text == null) return false;
        return handled;
    }

    private static bool IsLetterOrDigit(int keyCode) =>
        keyCode is >= 48 and <= 57 or >= 65 and <= 90;

    private static bool IsPrintableScalar(uint value) =>
        value is <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) &&
        !Rune.IsControl(new Rune((int)value));

    private static MouseButton MapMouseButton(string name) => name switch
    {
        "Left" => MouseButton.Left,
        "Right" => MouseButton.Right,
        "Middle" => MouseButton.Middle,
        _ => MouseButton.None
    };

    private static int MapKeyCode(string name)
    {
        if (name.Length == 1)
        {
            var character = name[0];
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9') return character;
        }
        if (name.StartsWith('F') && int.TryParse(name.AsSpan(1), out var function) && function is >= 1 and <= 24)
            return 111 + function;
        return name switch
        {
            "Backspace" => 8,
            "Tab" or "Backtab" => 9,
            "Enter" or "KpEnter" => 13,
            "Shift" or "ShiftL" or "ShiftR" => 16,
            "Ctrl" or "CtrlL" or "CtrlR" => 17,
            "Alt" or "AltL" or "AltR" => 18,
            "Escape" => 27,
            "Space" => 32,
            "Pageup" => 33,
            "Pagedown" => 34,
            "End" => 35,
            "Home" => 36,
            "Left" => 37,
            "Up" => 38,
            "Right" => 39,
            "Down" => 40,
            "Insert" => 45,
            "Delete" => 46,
            _ => 0
        };
    }

    private static Point ToPoint(Vector2 point) => new(point.X, point.Y);
}
