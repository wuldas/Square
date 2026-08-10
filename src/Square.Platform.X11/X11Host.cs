using System.Diagnostics;
using System.Runtime.InteropServices;
using Square.Graphics;
using Square.Hosting;

namespace Square.Platform.X11;

internal sealed unsafe class X11Host : IPlatformHost, IPlatformNativeWindow
{
    private string _title;
    private readonly int _width;
    private readonly int _height;
    private readonly string _renderBackend;
    private readonly SoftwareRenderSurfaceKind _softwareSurfaceKind;
    private readonly IntPtr _display;
    private readonly int _screen;
    private readonly IntPtr _root;
    private readonly IntPtr _window;
    private readonly IntPtr _visual;
    private readonly int _depth;
    private readonly IntPtr _colormap;
    private readonly IntPtr _gc;
    private readonly IntPtr _wmDeleteWindow;
    private readonly IntPtr _wmProtocols;
    private readonly IntPtr _clipboardAtom;
    private readonly IntPtr _utf8StringAtom;
    private readonly IntPtr _targetsAtom;
    private readonly IntPtr _primaryAtom;
    private readonly IntPtr _textAtom;
    private readonly IntPtr _clipboardContentAtom;
    private readonly IntPtr _wmState;
    private readonly IntPtr _netWmState;
    private readonly IntPtr _netWmStateMaximizedHorz;
    private readonly IntPtr _netWmStateMaximizedVert;
    private readonly IntPtr _netWmMoveresize;
    private readonly IntPtr _textCursor;
    private readonly IntPtr _handCursor;
    private readonly IntPtr _arrowCursor;
    private Size _clientSize;
    private Size _physicalClientSize;
    private int _winX;
    private int _winY;
    private float _dpiScale = 1f;
    private double _refreshRate = X11DisplayMetrics.DefaultRefreshRate;
    private bool _refreshRateDetectionFailed;
    private bool _running;
    private IRenderContext? _renderContext;
    private X11SoftwareRenderSurface? _softwareSurface;
    private Bitmap? _lastFrame;
    private IntPtr _ximage;
    private IntPtr _imageBufferPtr;
    private int _imageBufferSize;
    private IntPtr _imagePixmap;
    private CursorKind _cursor = CursorKind.Arrow;
    private string? _clipboardText;
    private IntPtr _xim;
    private IntPtr _xic;
    private Rect _textInputRect;
    private X11CaretSpot _textInputSpot;
    private bool _disposed;
    private bool _closed;
    private AppWindowState _state = AppWindowState.Normal;

    private static X11Api.XErrorHandler? _errorHandler;

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            if (_window != IntPtr.Zero)
                X11Api.StoreName(_display, _window, _title);
        }
    }

    public X11Host(PlatformHostCreateInfo info)
    {
        _title = info.Title;
        _width = info.Width;
        _height = info.Height;
        _renderBackend = info.RenderBackend;
        _softwareSurfaceKind = info.SoftwareSurface;
        _display = X11Api.OpenDisplay(null);
        if (_display == IntPtr.Zero)
            throw new InvalidOperationException("Cannot open X display. Is DISPLAY set?");

        _errorHandler = OnXError;
        X11Api.SetErrorHandler(_errorHandler);

        _screen = X11Api.DefaultScreen(_display);
        _root = X11Api.DefaultRootWindow(_display);
        _dpiScale = DetectDpiScale();
        _refreshRate = DetectRefreshRate();

        // Use the server default visual for opaque desktop windows. Selecting a 32-bit ARGB
        // visual makes compositors such as WSLg interpret the XImage high byte as window alpha.
        _visual = X11Api.DefaultVisual(_display, _screen);
        _depth = X11Api.DefaultDepth(_display, _screen);
        _colormap = X11Api.CreateColormap(_display, _root, _visual, X11Api.AllocNone);

        var attr = new X11Api.XSetWindowAttributes
        {
            backgroundPixel = X11Api.WhitePixel(_display, _screen),
            borderPixel = 0,
            bitGravity = 1,
            eventMask = X11Api.ExposureMask
                       | X11Api.StructureNotifyMask
                       | X11Api.KeyPressMask | X11Api.KeyReleaseMask
                       | X11Api.ButtonPressMask | X11Api.ButtonReleaseMask
                       | X11Api.PointerMotionMask
                       | X11Api.FocusChangeMask
                       | X11Api.PropertyChangeMask,
            colormap = _colormap,
            cursor = IntPtr.Zero
        };

        var valueMask = X11Api.CWBackPixel | X11Api.CWBorderPixel
                      | X11Api.CWBitGravity | X11Api.CWEventMask
                      | X11Api.CWColormap;

        _window = X11Api.CreateWindow(_display, _root,
            0, 0, (uint)ToPhysical(_width), (uint)ToPhysical(_height),
            0, _depth, X11Api.InputOutput,
            _visual, valueMask, ref attr);

        if (_window == IntPtr.Zero)
            throw new InvalidOperationException("XCreateWindow failed");

        if (info.TitleStyle != TitleStyle.System)
            SetWindowManagerDecorations(info.BorderStyle);

        X11Api.StoreName(_display, _window, _title);
        if (info.OwnerHandle != IntPtr.Zero)
            X11Api.SetTransientForHint(_display, _window, info.OwnerHandle);
        SetProcessIdProperty();

        _wmDeleteWindow = X11Api.InternAtom(_display, "WM_DELETE_WINDOW", false);
        _wmProtocols = X11Api.InternAtom(_display, "WM_PROTOCOLS", false);
        _clipboardAtom = X11Api.InternAtom(_display, "CLIPBOARD", false);
        _utf8StringAtom = X11Api.InternAtom(_display, "UTF8_STRING", false);
        _targetsAtom = X11Api.InternAtom(_display, "TARGETS", false);
        _primaryAtom = X11Api.InternAtom(_display, "PRIMARY", false);
        _textAtom = X11Api.InternAtom(_display, "TEXT", false);
        _clipboardContentAtom = X11Api.InternAtom(_display, "SQUARE_CLIPBOARD_CONTENT", false);
        _wmState = X11Api.InternAtom(_display, "WM_STATE", false);
        _netWmState = X11Api.InternAtom(_display, "_NET_WM_STATE", false);
        _netWmStateMaximizedHorz = X11Api.InternAtom(_display, "_NET_WM_STATE_MAXIMIZED_HORZ", false);
        _netWmStateMaximizedVert = X11Api.InternAtom(_display, "_NET_WM_STATE_MAXIMIZED_VERT", false);
        _netWmMoveresize = X11Api.InternAtom(_display, "_NET_WM_MOVERESIZE", false);
        if (info.IsModal)
        {
            var modal = X11Api.InternAtom(_display, "_NET_WM_STATE_MODAL", false);
            var atom = X11Api.InternAtom(_display, "ATOM", false);
            var modalValue = IntPtr.Size == sizeof(long)
                ? BitConverter.GetBytes(modal.ToInt64())
                : BitConverter.GetBytes(modal.ToInt32());
            X11Api.ChangeProperty(_display, _window, _netWmState, atom, 32,
                X11Api.PropModeReplace, modalValue, 1);
        }

        var protocols = new[] { _wmDeleteWindow };
        X11Api.SetWMProtocols(_display, _window, protocols, protocols.Length);

        var allEvents = X11Api.ExposureMask
                       | X11Api.StructureNotifyMask
                       | X11Api.KeyPressMask | X11Api.KeyReleaseMask
                       | X11Api.ButtonPressMask | X11Api.ButtonReleaseMask
                       | X11Api.PointerMotionMask
                       | X11Api.FocusChangeMask
                       | X11Api.PropertyChangeMask
                       | X11Api.ButtonMotionMask;
        X11Api.SelectInput(_display, _window, allEvents);

        _gc = X11Api.CreateGC(_display, _window, 0, IntPtr.Zero);

        _textCursor = X11Api.CreateFontCursor(_display, X11Api.XC_Xterm);
        _handCursor = X11Api.CreateFontCursor(_display, X11Api.XC_hand2);
        _arrowCursor = X11Api.CreateFontCursor(_display, X11Api.XC_left_ptr);
        ApplyCursor();

        X11Api.GetGeometry(_display, _window, out _, out _, out _, out var w, out var h, out _, out _);
        UpdateClientSize(new Size((int)w, (int)h));

        InitInputMethod();
    }

    private void InitInputMethod()
    {
        // Required for multi-byte/IME input (Chinese/Japanese/etc).
        try { X11Api.SetLocale(X11Api.LcCtype, ""); } catch { /* optional */ }
        try { X11Api.SetLocaleModifiers(""); } catch { /* optional */ }

        _xim = X11Api.OpenIM(_display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_xim == IntPtr.Zero) return;

        _xic = X11Api.CreateIC(
            _xim,
            "inputStyle", X11InputPolicy.FallbackStyle,
            "clientWindow", _window,
            "focusWindow", _window,
            IntPtr.Zero);
    }

    private void SetProcessIdProperty()
    {
        var property = X11Api.InternAtom(_display, "_NET_WM_PID", false);
        var cardinal = X11Api.InternAtom(_display, "CARDINAL", false);
        var pid = BitConverter.GetBytes(Environment.ProcessId);
        X11Api.ChangeProperty(_display, _window, property, cardinal, 32, X11Api.PropModeReplace, pid, 1);
    }

    private void SetWindowManagerDecorations(BorderStyle borderStyle)
    {
        var property = X11Api.InternAtom(_display, "_MOTIF_WM_HINTS", false);
        var hints = new uint[]
        {
            X11Api.MotifWmHintsDecorations,
            borderStyle switch
            {
                BorderStyle.Resizable => X11Api.MotifWmDecorBorder | X11Api.MotifWmDecorResizeH,
                BorderStyle.Fixed => X11Api.MotifWmDecorBorder,
                _ => 0
            },
            0,
            0,
            0
        };
        var bytes = new byte[hints.Length * sizeof(uint)];
        Buffer.BlockCopy(hints, 0, bytes, 0, bytes.Length);
        X11Api.ChangeProperty(
            _display,
            _window,
            property,
            property,
            32,
            X11Api.PropModeReplace,
            bytes,
            hints.Length);
    }

    public Size ClientSize => _clientSize;
    public float DpiScale => _dpiScale;
    public bool IsRunning => _running;
    public AppWindowState State => _state;
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

    public KeyModifiers Modifiers
    {
        get
        {
            var m = KeyModifiers.None;
            if ((_lastModifierState & (1u << X11Api.ShiftMapIndex)) != 0) m |= KeyModifiers.Shift;
            if ((_lastModifierState & (1u << X11Api.ControlMapIndex)) != 0) m |= KeyModifiers.Control;
            if ((_lastModifierState & (1u << X11Api.Mod1MapIndex)) != 0) m |= KeyModifiers.Alt;
            return m;
        }
    }

    private uint _lastModifierState;

    public event Action<Size>? SizeChanged;
    public event Action<Point, MouseAction, MouseButton>? MouseEvent;
    public event Action<Point, int>? WheelEvent;
    public event Action<int, KeyAction>? KeyEvent;
    public event Action<string>? TextInput;
    public event Action? Tick;
    public event Action<AppWindowState>? StateChanged;
    public event Action? Closed;

    public void Show()
    {
        X11Api.MapRaised(_display, _window);
        if (_xic != IntPtr.Zero) X11Api.SetICFocus(_xic);
        X11Api.Flush(_display);
        _running = true;
    }

    public void Close()
    {
        _running = false;
        X11Api.UnmapWindow(_display, _window);
        X11Api.Flush(_display);
        SignalClosed();
    }

    public void Minimize()
    {
        if (_window == IntPtr.Zero) return;
        X11Api.IconifyWindow(_display, _window, _screen);
        X11Api.Flush(_display);
    }

    public void Maximize()
    {
        SendNetWmState(X11Api.NetWmStateAdd);
    }

    public void Restore()
    {
        if (_window == IntPtr.Zero) return;
        SendNetWmState(X11Api.NetWmStateRemove);
        X11Api.MapRaised(_display, _window);
        X11Api.Flush(_display);
    }

    public void BeginMove()
    {
        if (_window == IntPtr.Zero) return;
        if (!X11Api.QueryPointer(
                _display, _root, out _, out _, out var rootX, out var rootY,
                out _, out _, out _))
            return;

        X11Api.UngrabPointer(_display, X11Api.CurrentTime);
        X11Api.XEvent e = default;
        e.clientMessage.type = X11Api.ClientMessage;
        e.clientMessage.display = _display;
        e.clientMessage.window = _window;
        e.clientMessage.messageType = _netWmMoveresize;
        e.clientMessage.format = 32;
        e.clientMessage.data.l[0] = rootX;
        e.clientMessage.data.l[1] = rootY;
        e.clientMessage.data.l[2] = X11Api.NetWmMoveresizeMove;
        e.clientMessage.data.l[3] = X11Api.Button1;
        e.clientMessage.data.l[4] = X11Api.NetWmSourceApplication;
        X11Api.SendEvent(
            _display, _root, false,
            X11Api.SubstructureRedirectMask | X11Api.SubstructureNotifyMask, ref e);
        X11Api.Flush(_display);
    }

    public IRenderContext CreateRenderContext()
    {
        if (_renderContext != null) return _renderContext;
        var factory = RenderBackendRegistry.Get(_renderBackend);
        if (string.Equals(_renderBackend, "Software", StringComparison.OrdinalIgnoreCase)
            && _softwareSurfaceKind == SoftwareRenderSurfaceKind.Auto)
        {
            _softwareSurface = new X11SoftwareRenderSurface(
                _display, _window, _visual, _depth, _gc,
                Math.Max(1, (int)MathF.Ceiling(_clientSize.Width * _dpiScale)),
                Math.Max(1, (int)MathF.Ceiling(_clientSize.Height * _dpiScale)));
        }
        _renderContext = factory.CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = _clientSize,
            DpiScale = _dpiScale,
            SoftwareSurface = _softwareSurface,
            PresentFrame = _softwareSurface == null ? PresentFrame : null,
            NativeTarget = new X11VulkanRenderTarget(_display, _window, _screen)
        });
        return _renderContext;
    }

    public void PumpEvents()
    {
        var clock = Stopwatch.StartNew();
        var frameInterval = X11DisplayMetrics.FrameIntervalTicks(_refreshRate, Stopwatch.Frequency);
        var nextFrameDeadline = X11DisplayMetrics.NextFrameDeadline(0, clock.ElapsedTicks, frameInterval);
        while (_running)
        {
            try
            {
                var processedEvents = 0;
                while (X11Api.Pending(_display) > 0 && !X11InputPolicy.ShouldYieldEventPump(processedEvents))
                {
                    X11Api.NextEvent(_display, out var e);
                    processedEvents++;
                    if (X11Api.FilterEvent(ref e, IntPtr.Zero)) continue;
                    DispatchEvent(e);
                }
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"[X11] Exception in event loop: {ex}");
            }

            var now = clock.ElapsedTicks;
            var updatedFrameInterval = X11DisplayMetrics.FrameIntervalTicks(_refreshRate, Stopwatch.Frequency);
            if (updatedFrameInterval != frameInterval)
            {
                frameInterval = updatedFrameInterval;
                nextFrameDeadline = X11DisplayMetrics.NextFrameDeadline(0, now, frameInterval);
            }
            if (now >= nextFrameDeadline)
            {
                nextFrameDeadline = X11DisplayMetrics.NextFrameDeadline(nextFrameDeadline, now, frameInterval);
                try { Tick?.Invoke(); }
                catch (Exception ex) { System.Console.Error.WriteLine($"[X11] Exception in tick: {ex}"); }
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    private void DispatchEvent(X11Api.XEvent e)
    {
        switch (e.type)
        {
            case X11Api.Expose:
                {
                    var raw = (byte*)(&e);
                    int count = *(int*)(raw + 56);
                    if (count == 0)
                    {
                        if (_softwareSurface != null) _softwareSurface.Repaint();
                        else if (_lastFrame != null) PresentFrame(_lastFrame, null);
                    }
                }
                break;
            case X11Api.ConfigureNotify:
                {
                    var raw = (byte*)(&e);
                    _winX = *(int*)(raw + 48);
                    _winY = *(int*)(raw + 52);
                    var w = *(int*)(raw + 56);
                    var h = *(int*)(raw + 60);
                    UpdateDisplayMetrics(new Size(w, h), true);
                }
                break;
            case X11Api.MapNotify:
                UpdateDisplayMetrics(_physicalClientSize, true);
                RefreshWindowState(false);
                break;
            case X11Api.UnmapNotify:
                if (_running) UpdateState(AppWindowState.Minimized);
                break;
            case X11Api.PropertyNotify:
                if (e.property.atom == _wmState || e.property.atom == _netWmState)
                    RefreshWindowState(true);
                break;
            case X11Api.KeyPress:
                {
                    var key = e.key;
                    _lastModifierState = key.state;
                    DispatchKeyPress(key);
                }
                break;
            case X11Api.KeyRelease:
                {
                    var key = e.key;
                    _lastModifierState = key.state;
                    var keysym = ResolveKeysym(key);
                    var vk = MapXKeysymToVirtualKey(keysym);
                    if (vk != 0) KeyEvent?.Invoke(vk, KeyAction.Up);
                }
                break;
            case X11Api.ButtonPress:
                {
                    var raw = (byte*)(&e);
                    int x = *(int*)(raw + 64);
                    int y = *(int*)(raw + 68);
                    int xRoot = *(int*)(raw + 72);
                    int yRoot = *(int*)(raw + 76);
                    uint state = *(uint*)(raw + 80);
                    uint button = *(uint*)(raw + 84);
                    _lastModifierState = state;
                    var pt = ToLogicalPoint(x, y);
                    if (button == X11Api.Button4)
                        WheelEvent?.Invoke(pt, 120);
                    else if (button == X11Api.Button5)
                        WheelEvent?.Invoke(pt, -120);
                    else if (button == X11Api.Button2)
                    {
                        var text = ReadSelection(_primaryAtom);
                        if (!string.IsNullOrEmpty(text)) TextInput?.Invoke(text);
                    }
                    else
                        MouseEvent?.Invoke(pt, MouseAction.Down, button == X11Api.Button3 ? MouseButton.Right : MouseButton.Left);
                }
                break;
            case X11Api.ButtonRelease:
                {
                    var raw = (byte*)(&e);
                    int x = *(int*)(raw + 64);
                    int y = *(int*)(raw + 68);
                    uint state = *(uint*)(raw + 80);
                    uint button = *(uint*)(raw + 84);
                    _lastModifierState = state;
                    if (button is not X11Api.Button2 and not X11Api.Button4 and not X11Api.Button5)
                        MouseEvent?.Invoke(ToLogicalPoint(x, y), MouseAction.Up, button == X11Api.Button3 ? MouseButton.Right : MouseButton.Left);
                }
                break;
            case X11Api.MotionNotify:
                {
                    var raw = (byte*)(&e);
                    int x = *(int*)(raw + 64);
                    int y = *(int*)(raw + 68);
                    MouseEvent?.Invoke(ToLogicalPoint(x, y), MouseAction.Move, MouseButton.None);
                }
                break;
            case X11Api.FocusIn:
                _lastModifierState = 0;
                if (_xic != IntPtr.Zero) X11Api.SetICFocus(_xic);
                break;
            case X11Api.FocusOut:
                if (_xic != IntPtr.Zero) X11Api.UnsetICFocus(_xic);
                break;
            case X11Api.ClientMessage:
                if (e.clientMessage.messageType == _wmProtocols
                    && e.clientMessage.data.l[0] == _wmDeleteWindow.ToInt64())
                {
                    _running = false;
                    SignalClosed();
                }
                break;
            case X11Api.SelectionRequest:
                HandleSelectionRequest(e.selectionRequest);
                break;
        }
    }

    private void SendNetWmState(int action)
    {
        if (_window == IntPtr.Zero) return;
        X11Api.XEvent e = default;
        e.clientMessage.type = X11Api.ClientMessage;
        e.clientMessage.display = _display;
        e.clientMessage.window = _window;
        e.clientMessage.messageType = _netWmState;
        e.clientMessage.format = 32;
        e.clientMessage.data.l[0] = action;
        e.clientMessage.data.l[1] = _netWmStateMaximizedHorz.ToInt64();
        e.clientMessage.data.l[2] = _netWmStateMaximizedVert.ToInt64();
        e.clientMessage.data.l[3] = X11Api.NetWmSourceApplication;
        X11Api.SendEvent(
            _display, _root, false,
            X11Api.SubstructureRedirectMask | X11Api.SubstructureNotifyMask, ref e);
        X11Api.Flush(_display);
    }

    private void RefreshWindowState(bool includeWmState)
    {
        if (includeWmState && HasWmState(X11Api.IconicState))
        {
            UpdateState(AppWindowState.Minimized);
            return;
        }

        var maximizedHorz = false;
        var maximizedVert = false;
        if (TryGetPropertyValues(_netWmState, out var states))
        {
            foreach (var state in states)
            {
                maximizedHorz |= state == _netWmStateMaximizedHorz.ToInt64();
                maximizedVert |= state == _netWmStateMaximizedVert.ToInt64();
            }
        }

        UpdateState(maximizedHorz && maximizedVert
            ? AppWindowState.Maximized
            : AppWindowState.Normal);
    }

    private bool HasWmState(long expectedState)
        => TryGetPropertyValues(_wmState, out var values)
           && values.Length > 0
           && values[0] == expectedState;

    private bool TryGetPropertyValues(IntPtr property, out long[] values)
    {
        values = [];
        var rc = X11Api.GetWindowProperty(
            _display, _window, property, 0, 64, false, IntPtr.Zero,
            out _, out var format, out var count, out _, out var data);
        if (rc != X11Api.Success || data == IntPtr.Zero) return false;
        try
        {
            if (format != 32 || count == 0) return false;
            values = new long[(int)count];
            for (var i = 0; i < values.Length; i++)
                values[i] = Marshal.ReadIntPtr(data, i * IntPtr.Size).ToInt64();
            return true;
        }
        finally
        {
            X11Api.Free(data);
        }
    }

    private void UpdateState(AppWindowState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(state);
    }

    private void SignalClosed()
    {
        if (_closed) return;
        _closed = true;
        Closed?.Invoke();
    }

    private void DispatchKeyPress(X11Api.XKeyEvent key)
    {
        // Prefer XIM so compose/IME (e.g. Chinese) can commit text.
        var text = LookupText(ref key, out var keysym, out var status);
        if (keysym == IntPtr.Zero)
            keysym = ResolveKeysym(key);

        var vk = MapXKeysymToVirtualKey(keysym);
        var control = (key.state & (1u << X11Api.ControlMapIndex)) != 0;
        var alt = (key.state & (1u << X11Api.Mod1MapIndex)) != 0;
        var hasText = X11InputPolicy.HasDispatchableText(text);

        // Shortcuts always go through virtual-key routing.
        if (control || alt)
        {
            if (vk != 0) KeyEvent?.Invoke(vk, KeyAction.Down);
            return;
        }

        // Committed IME/compose text.
        if (hasText && (status is X11Api.XLookupChars or X11Api.XLookupBoth or 0))
        {
            TextInput?.Invoke(text!);

            // Still emit navigation/edit keys (Backspace/Enter/arrows...) when no printable path only.
            if (IsNavigationOrEditKey(vk) && !text!.Any(static c => !char.IsControl(c) && !char.IsWhiteSpace(c)))
                KeyEvent?.Invoke(vk, KeyAction.Down);
            return;
        }

        if (vk != 0) KeyEvent?.Invoke(vk, KeyAction.Down);
    }

    private string? LookupText(ref X11Api.XKeyEvent key, out IntPtr keysym, out int status)
    {
        keysym = IntPtr.Zero;
        status = 0;
        var buffer = new byte[128];

        if (_xic != IntPtr.Zero)
        {
            var len = X11Api.Utf8LookupString(_xic, ref key, buffer, buffer.Length, out keysym, out status);
            if (status == X11Api.XBufferOverflow)
            {
                buffer = new byte[Math.Max(256, len + 1)];
                len = X11Api.Utf8LookupString(_xic, ref key, buffer, buffer.Length, out keysym, out status);
            }

            return X11InputPolicy.DecodeCommittedText(buffer, len, status);
        }

        var n = X11Api.LookupString(ref key, buffer, buffer.Length, ref keysym, IntPtr.Zero);
        if (n <= 0) return null;
        status = X11Api.XLookupBoth;
        // XLookupString returns Latin-1 bytes, not UTF-8.
        return System.Text.Encoding.Latin1.GetString(buffer, 0, n);
    }

    private IntPtr ResolveKeysym(X11Api.XKeyEvent key)
    {
        // Group 0 base; shift level uses index 1. NumLock keypad needs index 0/1 carefully:
        // Prefer XLookup/XIM keysym first (caller), then fall back to keycode map with state.
        var shift = (key.state & (1u << X11Api.ShiftMapIndex)) != 0;
        var index = shift ? 1 : 0;
        var keysym = X11Api.KeycodeToKeysym(_display, key.keycode, index);
        if (keysym != IntPtr.Zero) return keysym;
        return X11Api.KeycodeToKeysym(_display, key.keycode, 0);
    }

    private static bool IsNavigationOrEditKey(int vk) =>
        vk is 8 or 9 or 13 or 27 or 35 or 36 or 37 or 38 or 39 or 40 or 45 or 46;

    private static int MapXKeysymToVirtualKey(IntPtr keysymPtr)
    {
        if (keysymPtr == IntPtr.Zero) return 0;
        var keysym = unchecked((uint)keysymPtr.ToInt64());

        // Keypad digits/operators when NumLock produces them.
        if (keysym is >= X11Api.XK_KP_0 and <= X11Api.XK_KP_9)
            return (int)(keysym - X11Api.XK_KP_0 + 0x30);
        if (keysym == X11Api.XK_KP_Decimal) return 0x6E; // VK_DECIMAL (or treat as '.')
        if (keysym == X11Api.XK_KP_Divide) return 0x6F;
        if (keysym == X11Api.XK_KP_Multiply) return 0x6A;
        if (keysym == X11Api.XK_KP_Subtract) return 0x6D;
        if (keysym == X11Api.XK_KP_Add) return 0x6B;

        return keysym switch
        {
            X11Api.XK_BackSpace => 8,
            X11Api.XK_Tab => 9,
            X11Api.XK_Return or X11Api.XK_KP_Enter => 13,
            X11Api.XK_Shift_L or X11Api.XK_Shift_R => 16,
            X11Api.XK_Control_L or X11Api.XK_Control_R => 17,
            X11Api.XK_Alt_L or X11Api.XK_Alt_R => 18,
            X11Api.XK_Escape => 27,
            X11Api.XK_End or X11Api.XK_KP_End => 35,
            X11Api.XK_Home or X11Api.XK_KP_Home => 36,
            X11Api.XK_Left or X11Api.XK_KP_Left => 37,
            X11Api.XK_Up or X11Api.XK_KP_Up => 38,
            X11Api.XK_Right or X11Api.XK_KP_Right => 39,
            X11Api.XK_Down or X11Api.XK_KP_Down => 40,
            X11Api.XK_Insert or X11Api.XK_KP_Insert => 45,
            X11Api.XK_Delete or X11Api.XK_KP_Delete => 46,
            X11Api.XK_Num_Lock => 144,
            // Latin-1 printable: only letters/digits become VK; other printable stay 0
            // so they only arrive via TextInput (avoids intermittent double/missed input).
            >= 0x61 and <= 0x7a => (int)(keysym - 0x20),
            >= 0x41 and <= 0x5a => (int)keysym,
            >= 0x30 and <= 0x39 => (int)keysym,
            _ => 0
        };
    }

    public void SetTextInputRect(Rect rect)
    {
        _textInputRect = rect;
        _textInputSpot = X11InputPolicy.ToClientPhysicalSpot(rect, _dpiScale);
        // Applying this spot requires XVaCreateNestedList and XSetICValues. Both are variadic Xlib APIs,
        // so the managed policy is retained until a fixed-signature native shim is available.
    }

    public string GetClipboardText() => ReadSelection(_clipboardAtom) ?? "";

    public void SetClipboardText(string text)
    {
        _clipboardText = text ?? "";
        X11Api.SetSelectionOwner(_display, _clipboardAtom, _window, IntPtr.Zero);
        X11Api.Flush(_display);
    }

    private void HandleSelectionRequest(X11Api.XSelectionRequestEvent req)
    {
        if (req.selection != _clipboardAtom || _clipboardText == null)
        {
            X11Api.XEvent sel = default;
            sel.type = X11Api.SelectionNotify;
            sel.selection.requestor = req.requestor;
            sel.selection.selection = req.selection;
            sel.selection.target = req.target;
            sel.selection.property = IntPtr.Zero;
            sel.selection.time = req.time;
            sel.selection.display = _display;
            X11Api.SendEvent(_display, req.requestor, false, 0L, ref sel);
            X11Api.Flush(_display);
            return;
        }

        IntPtr targetProp;
        if (req.target == _targetsAtom)
        {
            var targets = new[] { _targetsAtom, _utf8StringAtom, _textAtom };
            var bytes = new byte[targets.Length * IntPtr.Size];
            Buffer.BlockCopy(targets, 0, bytes, 0, bytes.Length);
            X11Api.ChangeProperty(_display, req.requestor, req.property,
                X11Api.InternAtom(_display, "ATOM", false), 32, 0, bytes, targets.Length);
            targetProp = req.property;
        }
        else if (req.target == _utf8StringAtom || req.target == _textAtom)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(_clipboardText);
            X11Api.ChangeProperty(_display, req.requestor, req.property,
                _utf8StringAtom, 8, 0, bytes, bytes.Length);
            targetProp = req.property;
        }
        else
        {
            targetProp = IntPtr.Zero;
        }

        X11Api.XEvent selNotify = default;
        selNotify.type = X11Api.SelectionNotify;
        selNotify.selection.requestor = req.requestor;
        selNotify.selection.selection = req.selection;
        selNotify.selection.target = req.target;
        selNotify.selection.property = targetProp;
        selNotify.selection.time = req.time;
        selNotify.selection.display = _display;
        X11Api.SendEvent(_display, req.requestor, false, 0L, ref selNotify);
        X11Api.Flush(_display);
    }

    private string? ReadSelection(IntPtr selection)
    {
        var owner = X11Api.GetSelectionOwner(_display, selection);
        if (owner == IntPtr.Zero) return null;
        if (owner == _window) return _clipboardText;

        X11Api.ConvertSelection(_display, selection, _utf8StringAtom,
            _clipboardContentAtom, _window, IntPtr.Zero);

        for (var i = 0; i < 50; i++)
        {
            X11Api.Flush(_display);
            Thread.Sleep(2);
            if (TryReadProperty(_window, _clipboardContentAtom, out var result))
            {
                X11Api.DeleteProperty(_display, _window, _clipboardContentAtom);
                return result;
            }
        }
        return null;
    }

    private bool TryReadProperty(IntPtr window, IntPtr prop, out string? result)
    {
        result = null;
        var rc = X11Api.GetWindowProperty(_display, window, prop,
            0, 1_000_000, true, _utf8StringAtom,
            out _, out var actualFormat, out var nItems, out _, out var ptr);
        if (rc != X11Api.Success || ptr == IntPtr.Zero) return false;
        try
        {
            var itemBytes = actualFormat / 8;
            var total = (int)nItems * itemBytes;
            if (total <= 0) return false;
            var dataBytes = new byte[total];
            Marshal.Copy(ptr, dataBytes, 0, total);
            result = System.Text.Encoding.UTF8.GetString(dataBytes);
            return !string.IsNullOrEmpty(result);
        }
        finally
        {
            X11Api.Free(ptr);
        }
    }

    private void PresentFrame(Bitmap bitmap, IReadOnlyList<Rect>? dirtyRects)
    {
        if (_display == IntPtr.Zero || _window == IntPtr.Zero) return;
        _lastFrame = bitmap;

        EnsureImageBuffer();
        var src = bitmap.Pixels;
        if (_imageBufferPtr != IntPtr.Zero && src.Length <= _imageBufferSize)
        {
            // Always keep full buffer in sync (needed for partial PutImage src)
            Marshal.Copy(src, 0, _imageBufferPtr, src.Length);
        }

        if (_ximage == IntPtr.Zero)
        {
            X11Api.Flush(_display);
            return;
        }

        if (dirtyRects == null)
        {
            X11Api.PutImage(_display, _imagePixmap, _gc, _ximage,
                0, 0, 0, 0, (uint)bitmap.Width, (uint)bitmap.Height);
            X11Api.PutImage(_display, _window, _gc, _ximage,
                0, 0, 0, 0, (uint)_physicalClientSize.Width, (uint)_physicalClientSize.Height);
            X11Api.Flush(_display);
            return;
        }

        foreach (var r in dirtyRects)
        {
            if (r.IsEmpty) continue;
            var x = Math.Max(0, (int)Math.Floor(r.X));
            var y = Math.Max(0, (int)Math.Floor(r.Y));
            var w = Math.Min(bitmap.Width - x, (int)Math.Ceiling(r.Width));
            var h = Math.Min(bitmap.Height - y, (int)Math.Ceiling(r.Height));
            if (w <= 0 || h <= 0) continue;
            w = Math.Min(w, (int)_physicalClientSize.Width - x);
            h = Math.Min(h, (int)_physicalClientSize.Height - y);
            if (w <= 0 || h <= 0) continue;

            X11Api.PutImage(_display, _imagePixmap, _gc, _ximage,
                x, y, x, y, (uint)w, (uint)h);
            X11Api.PutImage(_display, _window, _gc, _ximage,
                x, y, x, y, (uint)w, (uint)h);
        }
        X11Api.Flush(_display);
    }

    private void EnsureImageBuffer()
    {
        if (_lastFrame == null) return;
        if (_imagePixmap == IntPtr.Zero) RebuildPixmap();
        var needed = _lastFrame.Pixels.Length;
        if (_imageBufferPtr != IntPtr.Zero && _imageBufferSize >= needed && _ximage != IntPtr.Zero) return;

        DestroyXImage();

        _imageBufferSize = needed;
        _imageBufferPtr = X11Api.Malloc((nuint)needed);

        _ximage = X11Api.CreateImage(_display, _visual, _depth, X11Api.ZPixmap,
            0, _imageBufferPtr,
            (uint)_lastFrame.Width, (uint)_lastFrame.Height,
            X11Api.BitmapPad, _lastFrame.Stride);
    }

    private void DestroyXImage()
    {
        if (_ximage != IntPtr.Zero)
        {
            X11Api.DestroyImage(_ximage);
            _ximage = IntPtr.Zero;
            _imageBufferPtr = IntPtr.Zero;
            _imageBufferSize = 0;
        }
        else if (_imageBufferPtr != IntPtr.Zero)
        {
            X11Api.CFree(_imageBufferPtr);
            _imageBufferPtr = IntPtr.Zero;
            _imageBufferSize = 0;
        }
    }

    private void RebuildPixmap()
    {
        if (_imagePixmap != IntPtr.Zero)
        {
            X11Api.FreePixmap(_display, _imagePixmap);
            _imagePixmap = IntPtr.Zero;
        }
        _imagePixmap = X11Api.CreatePixmap(_display, _window,
            (uint)Math.Max(1, _physicalClientSize.Width), (uint)Math.Max(1, _physicalClientSize.Height), _depth);

        DestroyXImage();
    }

    private void ApplyCursor()
    {
        var cursor = _cursor switch
        {
            CursorKind.Text => _textCursor,
            CursorKind.Hand => _handCursor,
            _ => _arrowCursor
        };
        X11Api.DefineCursor(_display, _window, cursor);
        X11Api.Flush(_display);
    }

    private static int OnXError(IntPtr display, ref X11Api.XErrorEvent e)
    {
        var buf = new byte[256];
        X11Api.GetErrorText(display, e.error_code, buf, buf.Length);
        var msg = System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
        System.Console.Error.WriteLine($"[X11 ERROR] code={e.error_code} req={e.request_code} minor={e.minor_code}: {msg}");
        return 0;
    }

    private float DetectDpiScale()
    {
        var resourcesPointer = X11Api.XResourceManagerString(_display);
        var resources = resourcesPointer != IntPtr.Zero
            ? Marshal.PtrToStringUTF8(resourcesPointer)
            : null;
        var screen = X11Api.ScreenOfDisplay(_display, _screen);
        var dpi = X11DisplayMetrics.ResolveDpi(
            resources,
            X11Api.DisplayWidth(_display, _screen),
            X11Api.DisplayHeight(_display, _screen),
            screen != IntPtr.Zero ? X11Api.WidthMMOfScreen(screen) : 0,
            screen != IntPtr.Zero ? X11Api.HeightMMOfScreen(screen) : 0);
        return X11DisplayMetrics.DpiToScale(dpi);
    }

    private double DetectRefreshRate()
    {
        if (_refreshRateDetectionFailed) return X11DisplayMetrics.DefaultRefreshRate;

        IntPtr config = IntPtr.Zero;
        try
        {
            config = X11Api.XRRGetScreenInfo(_display, _root);
            if (config == IntPtr.Zero) throw new InvalidOperationException("XRRGetScreenInfo failed.");
            var rate = X11Api.XRRConfigCurrentRate(config);
            var normalized = X11DisplayMetrics.NormalizeRefreshRate(rate);
            if (normalized != rate) _refreshRateDetectionFailed = true;
            return normalized;
        }
        catch (Exception ex) when (ex is DllNotFoundException
                                   or EntryPointNotFoundException
                                   or BadImageFormatException
                                   or InvalidOperationException)
        {
            _refreshRateDetectionFailed = true;
            return X11DisplayMetrics.DefaultRefreshRate;
        }
        finally
        {
            if (config != IntPtr.Zero) X11Api.XRRFreeScreenConfigInfo(config);
        }
    }

    private void UpdateDisplayMetrics(Size physicalSize, bool refreshDpi)
    {
        var oldLogicalSize = _clientSize;
        var oldPhysicalSize = _physicalClientSize;
        var oldDpiScale = _dpiScale;
        if (refreshDpi) _dpiScale = DetectDpiScale();
        _refreshRate = DetectRefreshRate();
        UpdateClientSize(physicalSize);

        var dpiChanged = MathF.Abs(oldDpiScale - _dpiScale) > float.Epsilon;
        var physicalSizeChanged = oldPhysicalSize != _physicalClientSize;
        if (!dpiChanged && !physicalSizeChanged && oldLogicalSize == _clientSize) return;

        if (physicalSizeChanged && _imagePixmap != IntPtr.Zero) RebuildPixmap();

        if (_renderContext is IDpiResizableRenderContext dpiResizable)
            dpiResizable.Resize(_clientSize, _dpiScale);
        else if (_renderContext is IResizableRenderContext resizable)
            resizable.Resize(_clientSize);
        SizeChanged?.Invoke(_clientSize);
    }

    private void UpdateClientSize(Size physicalSize)
    {
        _physicalClientSize = physicalSize;
        _clientSize = new Size(physicalSize.Width / _dpiScale, physicalSize.Height / _dpiScale);
    }

    private int ToPhysical(int logicalValue)
        => Math.Max(1, (int)MathF.Round(logicalValue * _dpiScale));

    private Point ToLogicalPoint(float x, float y)
        => new(x / _dpiScale, y / _dpiScale);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyXImage();
        if (_xic != IntPtr.Zero)
        {
            X11Api.DestroyIC(_xic);
            _xic = IntPtr.Zero;
        }
        if (_xim != IntPtr.Zero)
        {
            X11Api.CloseIM(_xim);
            _xim = IntPtr.Zero;
        }
        if (_imagePixmap != IntPtr.Zero)
        {
            X11Api.FreePixmap(_display, _imagePixmap);
            _imagePixmap = IntPtr.Zero;
        }
        if (_gc != IntPtr.Zero) X11Api.FreeGC(_display, _gc);
        if (_textCursor != IntPtr.Zero) X11Api.FreeCursor(_display, _textCursor);
        if (_handCursor != IntPtr.Zero) X11Api.FreeCursor(_display, _handCursor);
        if (_arrowCursor != IntPtr.Zero) X11Api.FreeCursor(_display, _arrowCursor);
        if (_window != IntPtr.Zero) X11Api.DestroyWindow(_display, _window);
        if (_colormap != IntPtr.Zero) X11Api.FreeColormap(_display, _colormap);
        if (_display != IntPtr.Zero) X11Api.CloseDisplay(_display);
    }

    IntPtr IPlatformNativeWindow.Handle => _window;

}
