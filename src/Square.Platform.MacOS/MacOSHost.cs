using Square.Graphics;
using Square.Hosting;
using System.Runtime.InteropServices;

namespace Square.Platform.MacOS;

internal sealed unsafe class MacOSHost : IPlatformHost, IPlatformNativeWindow
{
    private readonly string _renderBackend;
    private readonly BorderStyle _borderStyle;
    private Size _clientSize;
    private string _title;
    private IntPtr _application;
    private IntPtr _window;
    private IntPtr _contentView;
    private IntPtr _textView;
    private IntPtr _layer;
    private IntPtr _runLoopMode;
    private IntPtr _colorSpace;
    private IRenderContext? _renderContext;
    private bool _running;
    private bool _pointerCaptured;
    private bool _closed;
    private CursorKind _cursor;
    private KeyModifiers _modifiers;
    private Rect _textInputRect;
    private AppWindowState _state = AppWindowState.Normal;

    private const nuint EventLeftMouseDown = 1;
    private const nuint EventLeftMouseUp = 2;
    private const nuint EventMouseMoved = 5;
    private const nuint EventLeftMouseDragged = 6;
    private const nuint EventRightMouseDown = 3;
    private const nuint EventRightMouseUp = 4;
    private const nuint EventKeyDown = 10;
    private const nuint EventKeyUp = 11;
    private const nuint EventFlagsChanged = 12;
    private const nuint EventScrollWheel = 22;
    private const nuint ModifierShift = 1 << 17;
    private const nuint ModifierControl = 1 << 18;
    private const nuint ModifierOption = 1 << 19;
    private const nuint ModifierCommand = 1 << 20;

    public MacOSHost(PlatformHostCreateInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        _title = info.Title;
        _renderBackend = info.RenderBackend;
        _borderStyle = info.BorderStyle;
        _clientSize = new Size(Math.Max(1, info.Width), Math.Max(1, info.Height));
    }

    public IntPtr Handle => _window;
    public Size ClientSize => _clientSize;
    public float DpiScale { get; private set; } = 1f;
    public bool IsRunning => _running;
    public AppWindowState State => _state;
    public KeyModifiers Modifiers => _modifiers;

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            if (_window != IntPtr.Zero) SetWindowTitle(value);
        }
    }

    public CursorKind Cursor
    {
        get => _cursor;
        set
        {
            if (_cursor == value) return;
            _cursor = value;
            ApplyCursor();
        }
    }

    public event Action? Tick;
    public event Action? Closed;
    public event Action<Point, MouseAction, MouseButton>? MouseEvent;
    public event Action<WheelInput>? WheelEvent;
    public event Action<int, KeyAction>? KeyEvent;
    public event Action<string>? TextInput;

    public event Action<Size>? SizeChanged;
    public event Action<AppWindowState>? StateChanged;

    public void Show()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("The macOS platform host requires macOS.");

        var threadClass = MacOSApi.GetClass("NSThread");
        if (MacOSApi.SendByteResult(threadClass, MacOSApi.Selector("isMainThread")) == 0)
            throw new InvalidOperationException("The macOS platform host must run on the main thread.");

        _application = MacOSApi.SendIntPtr(MacOSApi.GetClass("NSApplication"), MacOSApi.Selector("sharedApplication"));
        MacOSApi.SendNuint(_application, MacOSApi.Selector("setActivationPolicy:"), 0);
        MacOSApi.SendIntPtr(_application, MacOSApi.Selector("finishLaunching"));

        var rect = new MacOSApi.NSRect(
            new MacOSApi.NSPoint(0, 0),
            new MacOSApi.NSSize(_clientSize.Width, _clientSize.Height));
        var style = MacOSApi.WindowStyleTitled |
                    MacOSApi.WindowStyleClosable |
                    MacOSApi.WindowStyleMiniaturizable;
        if (_borderStyle == BorderStyle.Resizable) style |= MacOSApi.WindowStyleResizable;
        var allocatedWindow = MacOSApi.SendIntPtr(MacOSApi.GetClass("NSWindow"), MacOSApi.Selector("alloc"));
        _window = MacOSApi.SendRectNuintNuintByte(
            allocatedWindow,
            MacOSApi.Selector("initWithContentRect:styleMask:backing:defer:"),
            rect,
            style,
            MacOSApi.BackingStoreBuffered,
            0);
        if (_window == IntPtr.Zero) throw new InvalidOperationException("Unable to create NSWindow.");

        MacOSApi.SendByte(_window, MacOSApi.Selector("setReleasedWhenClosed:"), 0);
        SetWindowTitle(_title);
        MacOSApi.SendIntPtr(_window, MacOSApi.Selector("center"));

        _contentView = MacOSApi.SendIntPtr(_window, MacOSApi.Selector("contentView"));
        MacOSApi.SendByte(_contentView, MacOSApi.Selector("setWantsLayer:"), 1);
        MacOSApi.SendByte(_window, MacOSApi.Selector("setAcceptsMouseMovedEvents:"), 1);
        _layer = MacOSApi.SendIntPtr(_contentView, MacOSApi.Selector("layer"));
        if (_layer == IntPtr.Zero) throw new InvalidOperationException("Unable to create the NSView backing layer.");
        CreateTextInputView();

        DpiScale = Math.Max(1f, (float)MacOSApi.SendDoubleResult(_window, MacOSApi.Selector("backingScaleFactor")));
        MacOSApi.SendDouble(_layer, MacOSApi.Selector("setContentsScale:"), DpiScale);
        var gravity = MacOSApi.CreateString("resize");
        try
        {
            MacOSApi.SendPointer(_layer, MacOSApi.Selector("setContentsGravity:"), gravity);
        }
        finally
        {
            MacOSApi.SendIntPtr(gravity, MacOSApi.Selector("release"));
        }

        _runLoopMode = MacOSApi.CreateString("kCFRunLoopDefaultMode");
        _colorSpace = MacOSApi.ColorSpaceCreateDeviceRgb();
        if (_colorSpace == IntPtr.Zero) throw new InvalidOperationException("Unable to create the software color space.");
        _running = true;
    }

    public void ShowAfterFirstFrame()
    {
        if (_window == IntPtr.Zero) return;
        MacOSApi.SendPointer(_window, MacOSApi.Selector("makeKeyAndOrderFront:"), IntPtr.Zero);
        if (_textView != IntPtr.Zero)
            MacOSApi.SendPointer(_window, MacOSApi.Selector("makeFirstResponder:"), _textView);
        MacOSApi.SendByte(_application, MacOSApi.Selector("activateIgnoringOtherApps:"), 1);
    }

    public IRenderContext CreateRenderContext()
    {
        if (_renderContext != null) return _renderContext;
        if (!string.Equals(_renderBackend, "Software", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("The macOS MVP currently supports only the Software render backend.");

        var factory = RenderBackendRegistry.Get(_renderBackend);
        _renderContext = factory.CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = _clientSize,
            DpiScale = DpiScale,
            PresentFrame = PresentFrame
        });
        return _renderContext;
    }

    public void PumpEvents()
    {
        var distantPast = MacOSApi.SendIntPtr(MacOSApi.GetClass("NSDate"), MacOSApi.Selector("distantPast"));
        var eventSelector = MacOSApi.Selector("nextEventMatchingMask:untilDate:inMode:dequeue:");
        while (_running)
        {
            var pool = MacOSApi.SendIntPtr(MacOSApi.GetClass("NSAutoreleasePool"), MacOSApi.Selector("new"));
            try
            {
                IntPtr nativeEvent;
                while ((nativeEvent = MacOSApi.SendEventQuery(
                           _application,
                           eventSelector,
                           nuint.MaxValue,
                           distantPast,
                           _runLoopMode,
                           1)) != IntPtr.Zero)
                {
                    var type = MacOSApi.SendNuintResult(nativeEvent, MacOSApi.Selector("type"));
                    var sendToAppKit = DispatchEvent(nativeEvent, type);
                    if (sendToAppKit)
                        MacOSApi.SendPointer(_application, MacOSApi.Selector("sendEvent:"), nativeEvent);
                    if (type == EventKeyDown && sendToAppKit) DrainCommittedText();
                    RefreshWindowMetrics();
                    RefreshWindowState();
                }

                var visible = MacOSApi.SendByteResult(_window, MacOSApi.Selector("isVisible")) != 0;
                var miniaturized = MacOSApi.SendByteResult(_window, MacOSApi.Selector("isMiniaturized")) != 0;
                if (!visible && !miniaturized)
                {
                    _running = false;
                    SignalClosed();
                    break;
                }

                Tick?.Invoke();
                MacOSApi.SendIntPtr(_application, MacOSApi.Selector("updateWindows"));
                RefreshWindowMetrics();
                RefreshWindowState();
            }
            finally
            {
                MacOSApi.SendIntPtr(pool, MacOSApi.Selector("drain"));
            }

            if (!_running) break;
            Thread.Sleep(16);
        }
    }

    private static bool HasSelector(IntPtr nativeObject, string name)
    {
        return nativeObject != IntPtr.Zero
            && MacOSApi.RespondsToSelector(
                nativeObject,
                MacOSApi.Selector("respondsToSelector:"),
                MacOSApi.Selector(name)) != 0;
    }

    private static float ReadScrollDelta(IntPtr nativeEvent, string selector)
    {
        return HasSelector(nativeEvent, selector)
            ? (float)MacOSApi.SendDoubleResult(nativeEvent, MacOSApi.Selector(selector))
            : 0;
    }

    private void CapturePointer()
    {
        if (_pointerCaptured || _window == IntPtr.Zero) return;
        // AppKit retains the drag stream after the button-down event is delivered;
        // keep moved events enabled so the existing native event loop receives it.
        MacOSApi.SendByte(_window, MacOSApi.Selector("setAcceptsMouseMovedEvents:"), 1);
        _pointerCaptured = true;
    }

    private void ReleasePointerCapture()
    {
        if (!_pointerCaptured) return;
        _pointerCaptured = false;
        // Re-assert normal event tracking after AppKit ends its drag stream.
        if (_window != IntPtr.Zero)
            MacOSApi.SendByte(_window, MacOSApi.Selector("setAcceptsMouseMovedEvents:"), 1);
    }

    private bool DispatchEvent(IntPtr nativeEvent, nuint type)
    {
        _modifiers = MapModifiers(MacOSApi.SendNuintResult(nativeEvent, MacOSApi.Selector("modifierFlags")));
        switch (type)
        {
            case EventLeftMouseDown:
                CapturePointer();
                MouseEvent?.Invoke(GetMousePosition(nativeEvent), MouseAction.Down, MouseButton.Left);
                return true;
            case EventLeftMouseUp:
                MouseEvent?.Invoke(GetMousePosition(nativeEvent), MouseAction.Up, MouseButton.Left);
                ReleasePointerCapture();
                return true;
            case EventRightMouseDown:
                MouseEvent?.Invoke(GetMousePosition(nativeEvent), MouseAction.Down, MouseButton.Right);
                return true;
            case EventRightMouseUp:
                MouseEvent?.Invoke(GetMousePosition(nativeEvent), MouseAction.Up, MouseButton.Right);
                return true;
            case EventMouseMoved:
            case EventLeftMouseDragged:
                MouseEvent?.Invoke(GetMousePosition(nativeEvent), MouseAction.Move, MouseButton.None);
                return true;
            case EventScrollWheel:
            {
                var precise = HasSelector(nativeEvent, "hasPreciseScrollingDeltas")
                    && MacOSApi.SendByteResult(nativeEvent, MacOSApi.Selector("hasPreciseScrollingDeltas")) != 0;
                var scale = precise ? 1f : 120f;
                var deltaX = ReadScrollDelta(nativeEvent, "scrollingDeltaX") * scale;
                // NSEvent's positive vertical delta is toward the top; normalize to DOM direction.
                var deltaY = -ReadScrollDelta(nativeEvent, "scrollingDeltaY") * scale;
                var inertial = HasSelector(nativeEvent, "momentumPhase")
                    && MacOSApi.SendNuintResult(nativeEvent, MacOSApi.Selector("momentumPhase")) != 0;
                if (deltaX != 0 || deltaY != 0)
                    WheelEvent?.Invoke(new WheelInput(
                        GetMousePosition(nativeEvent), deltaX, deltaY, precise, inertial));
                return true;
            }
            case EventKeyDown:
                return DispatchKeyDown(nativeEvent);
            case EventKeyUp:
            {
                var key = MapKeyCode(MacOSApi.SendUshortResult(nativeEvent, MacOSApi.Selector("keyCode")));
                if (key != 0) KeyEvent?.Invoke(key, KeyAction.Up);
                return true;
            }
            case EventFlagsChanged:
                return true;
        }
        return true;
    }

    private bool DispatchKeyDown(IntPtr nativeEvent)
    {
        var key = MapKeyCode(MacOSApi.SendUshortResult(nativeEvent, MacOSApi.Selector("keyCode")));
        var nativeModifiers = MacOSApi.SendNuintResult(nativeEvent, MacOSApi.Selector("modifierFlags"));
        var shortcut = (nativeModifiers & (ModifierControl | ModifierCommand)) != 0;
        var hasMarkedText = _textView != IntPtr.Zero &&
                            MacOSApi.SendByteResult(_textView, MacOSApi.Selector("hasMarkedText")) != 0;
        if (hasMarkedText) return true;
        if (shortcut || IsNavigationOrEditKey(key))
        {
            if (key != 0) KeyEvent?.Invoke(key, KeyAction.Down);
            return false;
        }

        if (_textView != IntPtr.Zero) return true;

        var characters = MacOSApi.SendIntPtr(nativeEvent, MacOSApi.Selector("characters"));
        var utf8 = characters == IntPtr.Zero
            ? IntPtr.Zero
            : MacOSApi.SendIntPtr(characters, MacOSApi.Selector("UTF8String"));
        var text = utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
        if (!string.IsNullOrEmpty(text) && text.Any(static character => !char.IsControl(character)))
            TextInput?.Invoke(text);
        return false;
    }

    private void CreateTextInputView()
    {
        var allocated = MacOSApi.SendIntPtr(MacOSApi.GetClass("NSTextView"), MacOSApi.Selector("alloc"));
        _textView = MacOSApi.SendRect(
            allocated,
            MacOSApi.Selector("initWithFrame:"),
            new MacOSApi.NSRect(new MacOSApi.NSPoint(0, 0), new MacOSApi.NSSize(1, 1)));
        if (_textView == IntPtr.Zero) throw new InvalidOperationException("Unable to create the macOS text input client.");

        MacOSApi.SendByte(_textView, MacOSApi.Selector("setDrawsBackground:"), 0);
        MacOSApi.SendByte(_textView, MacOSApi.Selector("setRichText:"), 0);
        MacOSApi.SendByte(_textView, MacOSApi.Selector("setEditable:"), 1);
        MacOSApi.SendByte(_textView, MacOSApi.Selector("setSelectable:"), 1);
        MacOSApi.SendDouble(_textView, MacOSApi.Selector("setAlphaValue:"), 0);
        var textContainer = MacOSApi.SendIntPtr(_textView, MacOSApi.Selector("textContainer"));
        if (textContainer != IntPtr.Zero)
            MacOSApi.SendDouble(textContainer, MacOSApi.Selector("setLineFragmentPadding:"), 0);
        MacOSApi.SendSize(
            _textView,
            MacOSApi.Selector("setTextContainerInset:"),
            new MacOSApi.NSSize(0, 0));
        MacOSApi.SendPointer(_contentView, MacOSApi.Selector("addSubview:"), _textView);
        MacOSApi.SendIntPtr(_textView, MacOSApi.Selector("release"));
        UpdateTextInputFrame();
    }

    private void DrainCommittedText()
    {
        if (_textView == IntPtr.Zero ||
            MacOSApi.SendByteResult(_textView, MacOSApi.Selector("hasMarkedText")) != 0)
            return;

        var value = MacOSApi.SendIntPtr(_textView, MacOSApi.Selector("string"));
        var utf8 = value == IntPtr.Zero
            ? IntPtr.Zero
            : MacOSApi.SendIntPtr(value, MacOSApi.Selector("UTF8String"));
        var text = utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
        if (string.IsNullOrEmpty(text)) return;

        TextInput?.Invoke(text);
        SetNativeTextViewString("");
    }

    private Point GetMousePosition(IntPtr nativeEvent)
    {
        var point = MacOSApi.SendPointResult(nativeEvent, MacOSApi.Selector("locationInWindow"));
        return new Point((float)point.X, _clientSize.Height - (float)point.Y);
    }

    private static KeyModifiers MapModifiers(nuint flags)
    {
        var result = KeyModifiers.None;
        if ((flags & ModifierShift) != 0) result |= KeyModifiers.Shift;
        if ((flags & (ModifierControl | ModifierCommand)) != 0) result |= KeyModifiers.Control;
        if ((flags & ModifierOption) != 0) result |= KeyModifiers.Alt;
        return result;
    }

    private static bool IsNavigationOrEditKey(int key) =>
        key is 8 or 9 or 13 or 27 or 35 or 36 or 37 or 38 or 39 or 40 or 46;

    private static int MapKeyCode(ushort keyCode) => keyCode switch
    {
        0 => 65,
        1 => 83,
        2 => 68,
        3 => 70,
        4 => 72,
        5 => 71,
        6 => 90,
        7 => 88,
        8 => 67,
        9 => 86,
        11 => 66,
        12 => 81,
        13 => 87,
        14 => 69,
        15 => 82,
        16 => 89,
        17 => 84,
        18 => 49,
        19 => 50,
        20 => 51,
        21 => 52,
        22 => 54,
        23 => 53,
        25 => 57,
        26 => 55,
        28 => 56,
        29 => 48,
        31 => 79,
        32 => 85,
        34 => 73,
        35 => 80,
        36 => 13,
        37 => 76,
        38 => 74,
        40 => 75,
        45 => 78,
        46 => 77,
        48 => 9,
        49 => 32,
        51 => 8,
        53 => 27,
        115 => 36,
        117 => 46,
        119 => 35,
        123 => 37,
        124 => 39,
        125 => 40,
        126 => 38,
        _ => 0
    };

    private void ApplyCursor()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var selector = _cursor switch
        {
            CursorKind.Text => "IBeamCursor",
            CursorKind.Hand => "pointingHandCursor",
            _ => "arrowCursor"
        };
        var cursor = MacOSApi.SendIntPtr(MacOSApi.GetClass("NSCursor"), MacOSApi.Selector(selector));
        if (cursor != IntPtr.Zero) MacOSApi.SendIntPtr(cursor, MacOSApi.Selector("set"));
    }

    private void PresentFrame(Bitmap bitmap, IReadOnlyList<Rect>? dirtyRects)
    {
        if (dirtyRects is { Count: 0 }) return;
        if (_layer == IntPtr.Zero || _colorSpace == IntPtr.Zero || bitmap.IsDisposed) return;

        fixed (byte* pixels = bitmap.Pixels)
        {
            var data = MacOSApi.DataCreate(IntPtr.Zero, pixels, bitmap.Pixels.Length);
            if (data == IntPtr.Zero) throw new InvalidOperationException("Unable to copy the software frame.");
            var provider = IntPtr.Zero;
            var image = IntPtr.Zero;
            try
            {
                provider = MacOSApi.DataProviderCreate(data);
                image = MacOSApi.ImageCreate(
                    (nuint)bitmap.Width,
                    (nuint)bitmap.Height,
                    8,
                    32,
                    (nuint)bitmap.Stride,
                    _colorSpace,
                    MacOSApi.BitmapByteOrder32Little | MacOSApi.ImageAlphaPremultipliedFirst,
                    provider,
                    IntPtr.Zero,
                    0,
                    0);
                if (image == IntPtr.Zero) throw new InvalidOperationException("Unable to create CGImage for the software frame.");
                MacOSApi.SendPointer(_layer, MacOSApi.Selector("setContents:"), image);
            }
            finally
            {
                if (image != IntPtr.Zero) MacOSApi.ReleaseCoreFoundation(image);
                if (provider != IntPtr.Zero) MacOSApi.ReleaseCoreFoundation(provider);
                MacOSApi.ReleaseCoreFoundation(data);
            }
        }
    }

    public void Close()
    {
        _running = false;
        if (_window != IntPtr.Zero) MacOSApi.SendIntPtr(_window, MacOSApi.Selector("close"));
        SignalClosed();
    }

    public void Minimize()
    {
        if (_window != IntPtr.Zero)
            MacOSApi.SendPointer(_window, MacOSApi.Selector("miniaturize:"), IntPtr.Zero);
    }

    public void Maximize()
    {
        if (_window == IntPtr.Zero) return;
        if (MacOSApi.SendByteResult(_window, MacOSApi.Selector("isMiniaturized")) != 0)
            MacOSApi.SendPointer(_window, MacOSApi.Selector("deminiaturize:"), IntPtr.Zero);
        if (MacOSApi.SendByteResult(_window, MacOSApi.Selector("isZoomed")) == 0)
            MacOSApi.SendPointer(_window, MacOSApi.Selector("zoom:"), IntPtr.Zero);
    }

    public void Restore()
    {
        if (_window == IntPtr.Zero) return;
        if (MacOSApi.SendByteResult(_window, MacOSApi.Selector("isMiniaturized")) != 0)
        {
            MacOSApi.SendPointer(_window, MacOSApi.Selector("deminiaturize:"), IntPtr.Zero);
            return;
        }

        if (MacOSApi.SendByteResult(_window, MacOSApi.Selector("isZoomed")) != 0)
            MacOSApi.SendPointer(_window, MacOSApi.Selector("zoom:"), IntPtr.Zero);
    }

    public void SetTextInputRect(Rect rect)
    {
        _textInputRect = rect;
        UpdateTextInputFrame();
    }

    public string GetClipboardText()
    {
        var pasteboard = MacOSApi.SendIntPtr(
            MacOSApi.GetClass("NSPasteboard"),
            MacOSApi.Selector("generalPasteboard"));
        var value = MacOSApi.SendPointer(
            pasteboard,
            MacOSApi.Selector("stringForType:"),
            MacOSApi.PasteboardTypeString);
        if (value == IntPtr.Zero) return "";
        var utf8 = MacOSApi.SendIntPtr(value, MacOSApi.Selector("UTF8String"));
        return utf8 == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(utf8) ?? "";
    }

    public void SetClipboardText(string text)
    {
        var pasteboard = MacOSApi.SendIntPtr(
            MacOSApi.GetClass("NSPasteboard"),
            MacOSApi.Selector("generalPasteboard"));
        MacOSApi.SendIntPtr(pasteboard, MacOSApi.Selector("clearContents"));
        var value = MacOSApi.CreateString(text);
        try
        {
            MacOSApi.SendPointerPointerByteResult(
                pasteboard,
                MacOSApi.Selector("setString:forType:"),
                value,
                MacOSApi.PasteboardTypeString);
        }
        finally
        {
            MacOSApi.SendIntPtr(value, MacOSApi.Selector("release"));
        }
    }

    private void SetNativeTextViewString(string text)
    {
        var value = MacOSApi.CreateString(text);
        try
        {
            MacOSApi.SendPointer(_textView, MacOSApi.Selector("setString:"), value);
        }
        finally
        {
            MacOSApi.SendIntPtr(value, MacOSApi.Selector("release"));
        }
    }

    private void UpdateTextInputFrame()
    {
        if (_textView == IntPtr.Zero) return;
        var frame = MacOSHostPolicy.ToCocoaTextInputRect(_textInputRect, _clientSize);
        MacOSApi.SendRect(
            _textView,
            MacOSApi.Selector("setFrame:"),
            new MacOSApi.NSRect(
                new MacOSApi.NSPoint(frame.X, frame.Y),
                new MacOSApi.NSSize(frame.Width, frame.Height)));
    }

    private void RefreshWindowMetrics()
    {
        if (_contentView == IntPtr.Zero) return;
        var bounds = MacOSApi.SendRectResult(_contentView, MacOSApi.Selector("bounds"));
        var size = new Size(
            Math.Max(1, (float)bounds.Size.Width),
            Math.Max(1, (float)bounds.Size.Height));
        var dpiScale = Math.Max(1f, (float)MacOSApi.SendDoubleResult(_window, MacOSApi.Selector("backingScaleFactor")));
        if (size == _clientSize && MathF.Abs(dpiScale - DpiScale) <= float.Epsilon) return;

        _clientSize = size;
        DpiScale = dpiScale;
        MacOSApi.SendDouble(_layer, MacOSApi.Selector("setContentsScale:"), DpiScale);
        if (_renderContext is IDpiResizableRenderContext dpiResizable)
            dpiResizable.Resize(_clientSize, DpiScale);
        else if (_renderContext is IResizableRenderContext resizable)
            resizable.Resize(_clientSize);
        UpdateTextInputFrame();
        SizeChanged?.Invoke(_clientSize);
    }

    private void RefreshWindowState()
    {
        var state = MacOSHostPolicy.ResolveState(
            MacOSApi.SendByteResult(_window, MacOSApi.Selector("isMiniaturized")) != 0,
            MacOSApi.SendByteResult(_window, MacOSApi.Selector("isZoomed")) != 0,
            MacOSApi.SendNuintResult(_window, MacOSApi.Selector("styleMask")));
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(state);
    }

    private void SetWindowTitle(string title)
    {
        var nativeTitle = MacOSApi.CreateString(title);
        try
        {
            MacOSApi.SendPointer(_window, MacOSApi.Selector("setTitle:"), nativeTitle);
        }
        finally
        {
            MacOSApi.SendIntPtr(nativeTitle, MacOSApi.Selector("release"));
        }
    }

    private void SignalClosed()
    {
        if (_closed) return;
        _closed = true;
        Closed?.Invoke();
    }

    public void Dispose()
    {
        _running = false;
        ReleasePointerCapture();
        _renderContext?.Dispose();
        _renderContext = null;
        if (_window != IntPtr.Zero)
        {
            if (_textView != IntPtr.Zero)
                MacOSApi.SendIntPtr(_textView, MacOSApi.Selector("removeFromSuperview"));
            MacOSApi.SendIntPtr(_window, MacOSApi.Selector("close"));
            MacOSApi.SendIntPtr(_window, MacOSApi.Selector("release"));
            _window = IntPtr.Zero;
        }
        if (_runLoopMode != IntPtr.Zero)
        {
            MacOSApi.SendIntPtr(_runLoopMode, MacOSApi.Selector("release"));
            _runLoopMode = IntPtr.Zero;
        }
        _textView = IntPtr.Zero;
        _contentView = IntPtr.Zero;
        if (_colorSpace != IntPtr.Zero)
        {
            MacOSApi.ReleaseCoreFoundation(_colorSpace);
            _colorSpace = IntPtr.Zero;
        }
        _layer = IntPtr.Zero;
        SizeChanged = null;
        StateChanged = null;
        SignalClosed();
    }
}
