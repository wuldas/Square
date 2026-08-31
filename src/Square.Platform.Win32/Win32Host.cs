using Square.Graphics;
using Square.Hosting;
using System.Runtime.InteropServices;
using System.Text;

namespace Square.Platform.Win32;

internal sealed class Win32Host : IPlatformHost, IPlatformNativeWindow
{
    private IntPtr _hwnd;
    private bool _running;
    private string _title;
    private readonly int _width;
    private readonly int _height;
    private readonly string _renderBackend;
    private readonly SoftwareRenderSurfaceKind _softwareSurfaceKind;
    private readonly TitleStyle _titleStyle;
    private readonly BorderStyle _borderStyle;
    private readonly IntPtr _ownerHandle;
    private readonly bool _isModal;
    private Size _clientSize;
    private Size _physicalClientSize;
    private float _dpiScale = 1f;
    private IRenderContext? _renderContext;
    private Win32DibSoftwareRenderSurface? _softwareSurface;
    private Bitmap? _lastFrame;
    private char? _pendingHighSurrogate;
    private CursorKind _cursor = CursorKind.Arrow;
    private Rect _textInputRect;
    private AppWindowState _state = AppWindowState.Normal;
    private bool _closed;
    private bool _skipPointerCapture;

    private const uint FrameTimerIntervalMs = 16;

    private static readonly Dictionary<IntPtr, Win32Host> Hosts = [];
    private static readonly object HostsGate = new();
    [ThreadStatic]
    private static Win32Host? s_creating;
    private static WndProcDelegate? s_wndProc;
    private static bool s_classRegistered;
    private static bool s_dpiAwarenessInitialized;

    public Size ClientSize => _clientSize;
    public float DpiScale => _dpiScale;
    public bool IsRunning => _running;
    public AppWindowState State => _state;

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            if (_hwnd != IntPtr.Zero)
                Win32Api.SetWindowText(_hwnd, _title);
        }
    }

    public KeyModifiers Modifiers
    {
        get
        {
            var modifiers = KeyModifiers.None;
            if (Win32Api.GetKeyState(Win32Api.VK_SHIFT) < 0) modifiers |= KeyModifiers.Shift;
            if (Win32Api.GetKeyState(Win32Api.VK_CONTROL) < 0) modifiers |= KeyModifiers.Control;
            if (Win32Api.GetKeyState(Win32Api.VK_MENU) < 0) modifiers |= KeyModifiers.Alt;
            return modifiers;
        }
    }

    public CursorKind Cursor
    {
        get => _cursor;
        set
        {
            if (_cursor == value) return;
            _cursor = value;
            if (_hwnd != IntPtr.Zero) ApplyCursor();
        }
    }

    public event Action<Size>? SizeChanged;
    public event Action<Point, MouseAction, MouseButton>? MouseEvent;
    public event Action<Point, int>? WheelEvent;
    public event Action<int, KeyAction>? KeyEvent;
    public event Action<string>? TextInput;
    public event Action? Tick;
    public event Action? RenderRequested;
    public event Action<AppWindowState>? StateChanged;
    public event Action? Closed;

    public Win32Host(PlatformHostCreateInfo info)
    {
        _title = info.Title;
        _width = info.Width;
        _height = info.Height;
        _renderBackend = info.RenderBackend;
        _softwareSurfaceKind = info.SoftwareSurface;
        _titleStyle = info.TitleStyle;
        _borderStyle = info.BorderStyle;
        _ownerHandle = info.OwnerHandle;
        _isModal = info.IsModal;
    }

    public void Show()
    {
        if (!s_dpiAwarenessInitialized)
        {
            // Square currently lays out and rasterizes in physical pixels. Declaring
            // DPI awareness prevents Windows from scaling the completed bitmap again.
            Win32Api.SetProcessDpiAwarenessContext(Win32Api.DpiAwarenessContextPerMonitorAwareV2);
            s_dpiAwarenessInitialized = true;
        }

        if (!s_classRegistered)
        {
            s_wndProc = WndProc;
            var wc = new Win32Api.WNDCLASSEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32Api.WNDCLASSEX>(),
                style = Win32Api.CS_HREDRAW | Win32Api.CS_VREDRAW,
                lpfnWndProc = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(s_wndProc),
                hInstance = Win32Api.GetModuleHandle(null),
                hCursor = Win32Api.LoadCursor(IntPtr.Zero, new IntPtr(Win32Api.IDC_ARROW)),
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = "SquareWindow",
                hIconSm = IntPtr.Zero
            };
            Win32Api.RegisterClassEx(ref wc);
            s_classRegistered = true;
        }

        s_creating = this;
        try
        {
            _hwnd = Win32Api.CreateWindowEx(
                0, "SquareWindow", _title,
                CreateWindowStyle(),
                100, 100, _width, _height,
                _ownerHandle, IntPtr.Zero, Win32Api.GetModuleHandle(null), IntPtr.Zero);
        }
        finally
        {
            s_creating = null;
        }

        if (_hwnd == IntPtr.Zero)
        {
            var err = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            throw new InvalidOperationException($"CreateWindowEx failed: {err}");
        }

        lock (HostsGate) Hosts[_hwnd] = this;
        if (_isModal && _ownerHandle != IntPtr.Zero) Win32Api.EnableWindow(_ownerHandle, false);

        UpdateWindowFrameAppearance(AppWindowState.Normal);

        _dpiScale = DpiToScale(Win32Api.GetDpiForWindow(_hwnd));
        if (_dpiScale != 1f)
        {
            Win32Api.GetWindowRect(_hwnd, out var windowRect);
            Win32Api.SetWindowPos(
                _hwnd, IntPtr.Zero,
                windowRect.Left, windowRect.Top,
                ToPhysical(_width, _dpiScale), ToPhysical(_height, _dpiScale),
                Win32Api.SWP_NOZORDER | Win32Api.SWP_NOACTIVATE);
        }

        CenterOnOwner();

        Win32Api.GetClientRect(_hwnd, out var rect);
        UpdateClientSize(rect);

        Win32Api.SetTimer(_hwnd, new UIntPtr(1), FrameTimerIntervalMs, IntPtr.Zero);
        _running = true;
    }

    private int CreateWindowStyle()
    {
        if (_titleStyle == TitleStyle.System)
        {
            var style = Win32Api.WS_CAPTION | Win32Api.WS_SYSMENU | Win32Api.WS_MINIMIZEBOX;
            if (_borderStyle == BorderStyle.Resizable)
                style |= Win32Api.WS_THICKFRAME | Win32Api.WS_MAXIMIZEBOX;
            return style;
        }

        return _borderStyle switch
        {
            BorderStyle.Resizable => Win32Api.WS_POPUP | Win32Api.WS_THICKFRAME |
                                     Win32Api.WS_MINIMIZEBOX | Win32Api.WS_MAXIMIZEBOX,
            BorderStyle.Fixed => Win32Api.WS_POPUP | Win32Api.WS_BORDER,
            _ => Win32Api.WS_POPUP
        };
    }

    public void ShowAfterFirstFrame()
    {
        if (_hwnd == IntPtr.Zero) return;
        Win32Api.ShowWindow(_hwnd, Win32Api.SW_SHOW);
        Win32Api.UpdateWindow(_hwnd);
    }

    private void CenterOnOwner()
    {
        if (_hwnd == IntPtr.Zero || _ownerHandle == IntPtr.Zero) return;
        if (!Win32Api.GetWindowRect(_ownerHandle, out var ownerRect) ||
            !Win32Api.GetWindowRect(_hwnd, out var windowRect))
            return;

        var (x, y) = CalculateCenteredPosition(ownerRect, windowRect);
        Win32Api.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_NOACTIVATE);
    }

    internal static (int X, int Y) CalculateCenteredPosition(Win32Api.RECT owner, Win32Api.RECT window) =>
        (
            owner.Left + (owner.Width - window.Width) / 2,
            owner.Top + (owner.Height - window.Height) / 2
        );

    public void Close()
    {
        _running = false;
        ReleasePresentResources();
        if (_hwnd != IntPtr.Zero)
        {
            Win32Api.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        SignalClosed();
        Win32Api.PostQuitMessage(0);
    }

    public void Minimize()
    {
        if (_hwnd != IntPtr.Zero) Win32Api.ShowWindow(_hwnd, Win32Api.SW_MINIMIZE);
    }

    public void Maximize()
    {
        if (_hwnd != IntPtr.Zero) Win32Api.ShowWindow(_hwnd, Win32Api.SW_MAXIMIZE);
    }

    public void Restore()
    {
        if (_hwnd != IntPtr.Zero) Win32Api.ShowWindow(_hwnd, Win32Api.SW_RESTORE);
    }

    public void BeginMove()
    {
        if (_hwnd == IntPtr.Zero) return;
        _skipPointerCapture = true;
        Win32Api.ReleaseCapture();
        Win32Api.SendMessage(
            _hwnd, Win32Api.WM_SYSCOMMAND,
            new IntPtr(Win32Api.SC_MOVE | Win32Api.HTCAPTION), IntPtr.Zero);
    }

    private void ReleasePresentResources()
    {
        if (_presentMemoryDc != IntPtr.Zero)
        {
            if (_presentPreviousBitmap != IntPtr.Zero)
                Win32Api.SelectObject(_presentMemoryDc, _presentPreviousBitmap);
            if (_presentBitmap != IntPtr.Zero)
                Win32Api.DeleteObject(_presentBitmap);
            Win32Api.DeleteDC(_presentMemoryDc);
        }
        _presentMemoryDc = IntPtr.Zero;
        _presentBitmap = IntPtr.Zero;
        _presentPreviousBitmap = IntPtr.Zero;
        _presentBits = IntPtr.Zero;
        _presentWidth = 0;
        _presentHeight = 0;
        _presentDibNeedsFullCopy = false;

        if (_presentPixelsHandle.IsAllocated)
            _presentPixelsHandle.Free();
        _presentPixelsArray = null;
        _presentInfoReady = false;
    }

    public IRenderContext CreateRenderContext()
    {
        if (_renderContext != null) return _renderContext;
        var factory = RenderBackendRegistry.Get(_renderBackend);
        if (string.Equals(_renderBackend, "Software", StringComparison.OrdinalIgnoreCase)
            && _softwareSurfaceKind == SoftwareRenderSurfaceKind.Auto)
        {
            var width = Math.Max(1, (int)MathF.Ceiling(_clientSize.Width * _dpiScale));
            var height = Math.Max(1, (int)MathF.Ceiling(_clientSize.Height * _dpiScale));
            _softwareSurface = new Win32DibSoftwareRenderSurface(() => _hwnd, width, height);
        }
        _renderContext = factory.CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = _clientSize,
            DpiScale = _dpiScale,
            SoftwareSurface = _softwareSurface,
            PresentFrame = _softwareSurface == null ? PresentFrame : null,
            NativeTarget = new Win32RenderTarget(_hwnd, Win32Api.GetModuleHandle(null)),
            RequestRender = () => RenderRequested?.Invoke()
        });
        return _renderContext;
    }

    private void PresentFrame(Bitmap bitmap, IReadOnlyList<Rect>? dirtyRects)
    {
        if (_hwnd == IntPtr.Zero) return;
        _lastFrame = bitmap;
        var dibReady = EnsurePresentResources(bitmap);

        if (dibReady)
        {
            CopyFrameToPresentDib(bitmap, dirtyRects);
            var destinationDc = Win32Api.GetDC(_hwnd);
            try
            {
                if (destinationDc != IntPtr.Zero)
                    BlitPresentDib(destinationDc, dirtyRects);
            }
            finally
            {
                if (destinationDc != IntPtr.Zero) Win32Api.ReleaseDC(_hwnd, destinationDc);
            }
            return;
        }

        var dc = Win32Api.GetDC(_hwnd);
        try
        {
            if (dirtyRects == null)
            {
                // Full window
                Win32Api.StretchDIBits(
                    dc,
                    0, 0, (int)_physicalClientSize.Width, (int)_physicalClientSize.Height,
                    0, 0, bitmap.Width, bitmap.Height,
                    _presentPixelsHandle.AddrOfPinnedObject(), ref _presentInfo,
                    Win32Api.DIB_RGB_COLORS, Win32Api.SRCCOPY);
                return;
            }

            // The software buffer is already updated only inside dirtyRects. Blit the
            // complete retained buffer here: sub-rectangle StretchDIBits calls with a
            // top-down DIB use source coordinates inconsistently across Windows GDI
            // paths and can copy an unrelated (usually blank) row range over the dirty
            // destination. That made focused controls disappear even though the
            // retained buffer contained the correctly replayed background and siblings.
            Win32Api.StretchDIBits(
                dc,
                0, 0, (int)_physicalClientSize.Width, (int)_physicalClientSize.Height,
                0, 0, bitmap.Width, bitmap.Height,
                _presentPixelsHandle.AddrOfPinnedObject(), ref _presentInfo,
                Win32Api.DIB_RGB_COLORS, Win32Api.SRCCOPY);
        }
        finally
        {
            if (dc != IntPtr.Zero) Win32Api.ReleaseDC(_hwnd, dc);
        }
    }

    private bool EnsurePresentResources(Bitmap bitmap)
    {
        if (!_presentInfoReady
            || _presentInfo.bmiHeader.biWidth != bitmap.Width
            || _presentInfo.bmiHeader.biHeight != -bitmap.Height)
        {
            _presentInfo = new Win32Api.BITMAPINFO
            {
                bmiHeader = new Win32Api.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<Win32Api.BITMAPINFOHEADER>(),
                    biWidth = bitmap.Width,
                    biHeight = -bitmap.Height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = Win32Api.BI_RGB,
                    biSizeImage = (uint)bitmap.Pixels.Length
                }
            };
            _presentInfoReady = true;
        }
        else
        {
            _presentInfo.bmiHeader.biSizeImage = (uint)bitmap.Pixels.Length;
        }

        if (_presentMemoryDc != IntPtr.Zero
            && _presentWidth == bitmap.Width
            && _presentHeight == bitmap.Height)
            return true;

        ReleasePresentDib();
        var memoryDc = Win32Api.CreateCompatibleDC(IntPtr.Zero);
        if (memoryDc != IntPtr.Zero)
        {
            var presentBitmap = Win32Api.CreateDIBSection(
                memoryDc,
                ref _presentInfo,
                Win32Api.DIB_RGB_COLORS,
                out var bits,
                IntPtr.Zero,
                0);
            if (presentBitmap != IntPtr.Zero && bits != IntPtr.Zero)
            {
                var previousBitmap = Win32Api.SelectObject(memoryDc, presentBitmap);
                if (previousBitmap != IntPtr.Zero && previousBitmap != new IntPtr(-1))
                {
                    _presentMemoryDc = memoryDc;
                    _presentBitmap = presentBitmap;
                    _presentPreviousBitmap = previousBitmap;
                    _presentBits = bits;
                    _presentWidth = bitmap.Width;
                    _presentHeight = bitmap.Height;
                    _presentDibNeedsFullCopy = true;
                    if (_presentPixelsHandle.IsAllocated)
                        _presentPixelsHandle.Free();
                    _presentPixelsArray = null;
                    return true;
                }

                Win32Api.DeleteObject(presentBitmap);
            }
            Win32Api.DeleteDC(memoryDc);
        }

        if (!_presentPixelsHandle.IsAllocated || !ReferenceEquals(_presentPixelsArray, bitmap.Pixels))
        {
            if (_presentPixelsHandle.IsAllocated) _presentPixelsHandle.Free();
            _presentPixelsArray = bitmap.Pixels;
            _presentPixelsHandle = GCHandle.Alloc(bitmap.Pixels, GCHandleType.Pinned);
        }
        return false;
    }

    private void ReleasePresentDib()
    {
        if (_presentMemoryDc != IntPtr.Zero)
        {
            if (_presentPreviousBitmap != IntPtr.Zero)
                Win32Api.SelectObject(_presentMemoryDc, _presentPreviousBitmap);
            if (_presentBitmap != IntPtr.Zero)
                Win32Api.DeleteObject(_presentBitmap);
            Win32Api.DeleteDC(_presentMemoryDc);
        }
        _presentMemoryDc = IntPtr.Zero;
        _presentBitmap = IntPtr.Zero;
        _presentPreviousBitmap = IntPtr.Zero;
        _presentBits = IntPtr.Zero;
        _presentWidth = 0;
        _presentHeight = 0;
        _presentDibNeedsFullCopy = false;
    }

    private void CopyFrameToPresentDib(Bitmap bitmap, IReadOnlyList<Rect>? dirtyRects)
    {
        if (dirtyRects == null || _presentDibNeedsFullCopy)
        {
            Marshal.Copy(bitmap.Pixels, 0, _presentBits, bitmap.Pixels.Length);
            _presentDibNeedsFullCopy = false;
            return;
        }

        foreach (var dirtyRect in dirtyRects)
        {
            if (!TryClipPresentRect(dirtyRect, out var left, out var top, out var width, out var height))
                continue;
            var bytesPerRow = width * 4;
            for (var y = top; y < top + height; y++)
            {
                var sourceOffset = y * bitmap.Stride + left * 4;
                var destination = IntPtr.Add(_presentBits, sourceOffset);
                Marshal.Copy(bitmap.Pixels, sourceOffset, destination, bytesPerRow);
            }
        }
    }

    private void BlitPresentDib(IntPtr destinationDc, IReadOnlyList<Rect>? dirtyRects)
    {
        if (dirtyRects == null)
        {
            Win32Api.BitBlt(
                destinationDc, 0, 0, _presentWidth, _presentHeight,
                _presentMemoryDc, 0, 0, Win32Api.SRCCOPY);
            return;
        }

        foreach (var dirtyRect in dirtyRects)
        {
            if (!TryClipPresentRect(dirtyRect, out var left, out var top, out var width, out var height))
                continue;
            Win32Api.BitBlt(
                destinationDc, left, top, width, height,
                _presentMemoryDc, left, top, Win32Api.SRCCOPY);
        }
    }

    private bool TryClipPresentRect(Rect rect, out int left, out int top, out int width, out int height)
    {
        left = Math.Clamp((int)MathF.Floor(rect.Left), 0, _presentWidth);
        top = Math.Clamp((int)MathF.Floor(rect.Top), 0, _presentHeight);
        var right = Math.Clamp((int)MathF.Ceiling(rect.Right), left, _presentWidth);
        var bottom = Math.Clamp((int)MathF.Ceiling(rect.Bottom), top, _presentHeight);
        width = right - left;
        height = bottom - top;
        return width > 0 && height > 0;
    }

    public void PumpEvents()
    {
        while (_running)
        {
            var result = Win32Api.GetMessage(out var msg, IntPtr.Zero, 0, 0);
            if (result <= 0)
            {
                _running = false;
                break;
            }

            Win32Api.TranslateMessage(ref msg);
            Win32Api.DispatchMessage(ref msg);
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        Win32Host? host;
        lock (HostsGate) Hosts.TryGetValue(hWnd, out host);
        if (host == null && s_creating != null)
        {
            host = s_creating;
            lock (HostsGate) Hosts[hWnd] = host;
        }
        if (host == null) return Win32Api.DefWindowProc(hWnd, msg, wParam, lParam);

        switch (msg)
        {
            case Win32Api.WM_MOUSEACTIVATE:
                if (host._titleStyle == TitleStyle.Custom)
                {
                    Win32Api.SetActiveWindow(hWnd);
                    return new IntPtr(Win32Api.MA_ACTIVATE);
                }

                break;
            case Win32Api.WM_NCCALCSIZE:
                if (host._titleStyle == TitleStyle.Custom)
                    return IntPtr.Zero;
                break;
            case Win32Api.WM_NCPAINT:
                if (host._titleStyle == TitleStyle.Custom)
                    return IntPtr.Zero;
                break;
            case Win32Api.WM_NCACTIVATE:
                if (host._titleStyle == TitleStyle.Custom)
                {
                    host.HideSystemBorder();
                    return new IntPtr(1);
                }
                break;
            case Win32Api.WM_NCHITTEST:
                if (host._titleStyle == TitleStyle.Custom && host._borderStyle == BorderStyle.Resizable &&
                    host._state != AppWindowState.Maximized)
                    return host.HitTestResizeBorder(hWnd, lParam);
                break;
            case Win32Api.WM_SIZE:
                var state = wParam.ToInt32() switch
                {
                    Win32Api.SIZE_MINIMIZED => AppWindowState.Minimized,
                    Win32Api.SIZE_MAXIMIZED => AppWindowState.Maximized,
                    _ => AppWindowState.Normal
                };
                host.UpdateState(state);
                host.UpdateWindowFrameAppearance(state);
                if (state == AppWindowState.Minimized) break;
                Win32Api.GetClientRect(hWnd, out var rect);
                var newPhysicalSize = new Size(rect.Width, rect.Height);
                var newSize = host.ToLogicalSize(newPhysicalSize);
                if (newSize == host._clientSize) break;
                host._physicalClientSize = newPhysicalSize;
                host._clientSize = newSize;
                if (host._renderContext is IResizableRenderContext resizable)
                    resizable.Resize(newSize);
                host.SizeChanged?.Invoke(host._clientSize);
                break;
            case Win32Api.WM_DPICHANGED:
            {
                var dpi = (uint)(wParam.ToInt64() & 0xffff);
                host._dpiScale = DpiToScale(dpi);
                var suggested = Marshal.PtrToStructure<Win32Api.RECT>(lParam);
                Win32Api.SetWindowPos(
                    hWnd, IntPtr.Zero,
                    suggested.Left, suggested.Top, suggested.Width, suggested.Height,
                    Win32Api.SWP_NOZORDER | Win32Api.SWP_NOACTIVATE);
                Win32Api.GetClientRect(hWnd, out var clientRect);
                host.UpdateClientSize(clientRect);
                if (host._renderContext is IDpiResizableRenderContext dpiResizable)
                    dpiResizable.Resize(host._clientSize, host._dpiScale);
                else if (host._renderContext is IResizableRenderContext dpiFallbackResizable)
                    dpiFallbackResizable.Resize(host._clientSize);
                host.SizeChanged?.Invoke(host._clientSize);
            }
                return IntPtr.Zero;
            case Win32Api.WM_LBUTTONDOWN:
            {
                var x = (short)(lParam.ToInt64() & 0xFFFF);
                var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                host.MouseEvent?.Invoke(host.ToLogicalPoint(x, y), MouseAction.Down, MouseButton.Left);
                if (host._skipPointerCapture)
                    host._skipPointerCapture = false;
                else
                    Win32Api.SetCapture(hWnd);
            }
                break;
            case Win32Api.WM_LBUTTONUP:
            {
                var x = (short)(lParam.ToInt64() & 0xFFFF);
                var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                host.MouseEvent?.Invoke(host.ToLogicalPoint(x, y), MouseAction.Up, MouseButton.Left);
                Win32Api.ReleaseCapture();
            }
                break;
            case Win32Api.WM_RBUTTONDOWN:
            {
                var x = (short)(lParam.ToInt64() & 0xFFFF);
                var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                host.MouseEvent?.Invoke(host.ToLogicalPoint(x, y), MouseAction.Down, MouseButton.Right);
            }
                return IntPtr.Zero;
            case Win32Api.WM_RBUTTONUP:
            {
                var x = (short)(lParam.ToInt64() & 0xFFFF);
                var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                host.MouseEvent?.Invoke(host.ToLogicalPoint(x, y), MouseAction.Up, MouseButton.Right);
            }
                return IntPtr.Zero;
            case Win32Api.WM_MOUSEMOVE:
            {
                var x = (short)(lParam.ToInt64() & 0xFFFF);
                var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                host.MouseEvent?.Invoke(host.ToLogicalPoint(x, y), MouseAction.Move, MouseButton.None);
            }
                break;
            case Win32Api.WM_MOUSEWHEEL:
            {
                var wheelDelta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                var lParam64 = lParam.ToInt64();
                var x = (short)(lParam64 & 0xFFFF);
                var y = (short)((lParam64 >> 16) & 0xFFFF);
                var screenPoint = new Win32Api.POINT { X = x, Y = y };
                Win32Api.ScreenToClient(hWnd, ref screenPoint);
                host.WheelEvent?.Invoke(host.ToLogicalPoint(screenPoint.X, screenPoint.Y), wheelDelta);
            }
                return IntPtr.Zero;
            case Win32Api.WM_KEYDOWN:
                host.KeyEvent?.Invoke(wParam.ToInt32(), KeyAction.Down);
                break;
            case Win32Api.WM_KEYUP:
                host.KeyEvent?.Invoke(wParam.ToInt32(), KeyAction.Up);
                break;
            case Win32Api.WM_CHAR:
                host.DispatchUtf16Character((char)wParam.ToInt32());
                return IntPtr.Zero;
            case Win32Api.WM_UNICHAR:
                if (wParam.ToInt32() == Win32Api.UNICODE_NOCHAR) return new IntPtr(1);
                if (Rune.IsValid(wParam.ToInt32()))
                    host.TextInput?.Invoke(char.ConvertFromUtf32(wParam.ToInt32()));
                return IntPtr.Zero;
            case Win32Api.WM_IME_STARTCOMPOSITION:
                host.ApplyTextInputRect(hWnd);
                break;
            case Win32Api.WM_TIMER:
                host.Tick?.Invoke();
                return IntPtr.Zero;
            case Win32Api.WM_SETCURSOR:
                if ((lParam.ToInt64() & 0xffff) == Win32Api.HTCLIENT)
                {
                    host.ApplyCursor();
                    return new IntPtr(1);
                }

                break;
            case Win32Api.WM_PAINT:
            {
                var paint = new Win32Api.PAINTSTRUCT();
                var paintDc = Win32Api.BeginPaint(hWnd, ref paint);
                try
                {
                    if (paintDc != IntPtr.Zero && host._softwareSurface != null)
                    {
                        host._softwareSurface.Repaint(paintDc, paint.rcPaint);
                    }
                    else if (host._lastFrame != null)
                    {
                        host.PresentFrame(host._lastFrame, null);
                    }
                    else
                    {
                        host.RenderRequested?.Invoke();
                    }
                }
                finally
                {
                    Win32Api.EndPaint(hWnd, ref paint);
                }
            }
                return IntPtr.Zero;
            case Win32Api.WM_CLOSE:
                Win32Api.KillTimer(hWnd, new UIntPtr(1));
                host._running = false;
                Win32Api.DestroyWindow(hWnd);
                return IntPtr.Zero;
            case Win32Api.WM_DESTROY:
                Win32Api.KillTimer(hWnd, new UIntPtr(1));
                host.ReleasePresentResources();
                host._hwnd = IntPtr.Zero;
                host._running = false;
                lock (HostsGate) Hosts.Remove(hWnd);
                host.RestoreOwner();
                host.SignalClosed();
                Win32Api.PostQuitMessage(0);
                break;
        }

        return Win32Api.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private IntPtr HitTestResizeBorder(IntPtr hWnd, IntPtr lParam)
    {
        if (!Win32Api.GetWindowRect(hWnd, out var bounds)) return new IntPtr(Win32Api.HTCLIENT);
        var packed = lParam.ToInt64();
        var x = (short)(packed & 0xffff);
        var y = (short)((packed >> 16) & 0xffff);
        var dpi = Win32Api.GetDpiForWindow(hWnd);
        var frame = Win32Api.GetSystemMetricsForDpi(Win32Api.SM_CXSIZEFRAME, dpi) +
                    Win32Api.GetSystemMetricsForDpi(Win32Api.SM_CXPADDEDBORDER, dpi);
        var left = x < bounds.Left + frame;
        var right = x >= bounds.Right - frame;
        var top = y < bounds.Top + frame;
        var bottom = y >= bounds.Bottom - frame;

        if (top && left) return new IntPtr(Win32Api.HTTOPLEFT);
        if (top && right) return new IntPtr(Win32Api.HTTOPRIGHT);
        if (bottom && left) return new IntPtr(Win32Api.HTBOTTOMLEFT);
        if (bottom && right) return new IntPtr(Win32Api.HTBOTTOMRIGHT);
        if (left) return new IntPtr(Win32Api.HTLEFT);
        if (right) return new IntPtr(Win32Api.HTRIGHT);
        if (top) return new IntPtr(Win32Api.HTTOP);
        if (bottom) return new IntPtr(Win32Api.HTBOTTOM);
        return new IntPtr(Win32Api.HTCLIENT);
    }

    private void UpdateWindowFrameAppearance(AppWindowState state)
    {
        if (_hwnd == IntPtr.Zero || _titleStyle == TitleStyle.System) return;
        var preference = state == AppWindowState.Maximized
            ? Win32Api.DWMWCP_DONOTROUND
            : Win32Api.DWMWCP_ROUND;
        Win32Api.DwmSetWindowAttribute(
            _hwnd,
            Win32Api.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref preference,
            sizeof(int));
        HideSystemBorder();
    }

    private void HideSystemBorder()
    {
        var borderColor = Win32Api.DWMWA_COLOR_NONE;
        Win32Api.DwmSetWindowAttribute(
            _hwnd,
            Win32Api.DWMWA_BORDER_COLOR,
            ref borderColor,
            sizeof(int));
    }

    public IntPtr Handle => _hwnd;

    public void SetTextInputRect(Rect rect)
    {
        _textInputRect = rect;
        if (_hwnd != IntPtr.Zero) ApplyTextInputRect(_hwnd);
    }

    public string GetClipboardText()
    {
        if (!Win32Api.OpenClipboard(_hwnd)) return "";
        try
        {
            var memory = Win32Api.GetClipboardData(Win32Api.CF_UNICODETEXT);
            if (memory == IntPtr.Zero) return "";
            var pointer = Win32Api.GlobalLock(memory);
            if (pointer == IntPtr.Zero) return "";
            try
            {
                return Marshal.PtrToStringUni(pointer) ?? "";
            }
            finally
            {
                Win32Api.GlobalUnlock(memory);
            }
        }
        finally
        {
            Win32Api.CloseClipboard();
        }
    }

    public void SetClipboardText(string text)
    {
        if (!Win32Api.OpenClipboard(_hwnd)) return;
        IntPtr memory = IntPtr.Zero;
        try
        {
            Win32Api.EmptyClipboard();
            var characters = (text ?? "").ToCharArray();
            var byteCount = (characters.Length + 1) * sizeof(char);
            memory = Win32Api.GlobalAlloc(
                Win32Api.GMEM_MOVEABLE | Win32Api.GMEM_ZEROINIT,
                new UIntPtr((uint)byteCount));
            if (memory == IntPtr.Zero) return;
            var pointer = Win32Api.GlobalLock(memory);
            if (pointer == IntPtr.Zero) return;
            try
            {
                Marshal.Copy(characters, 0, pointer, characters.Length);
                Marshal.WriteInt16(pointer, characters.Length * sizeof(char), 0);
            }
            finally
            {
                Win32Api.GlobalUnlock(memory);
            }

            if (Win32Api.SetClipboardData(Win32Api.CF_UNICODETEXT, memory) != IntPtr.Zero)
                memory = IntPtr.Zero;
        }
        finally
        {
            if (memory != IntPtr.Zero) Win32Api.GlobalFree(memory);
            Win32Api.CloseClipboard();
        }
    }

    IntPtr IPlatformNativeWindow.Handle => _hwnd;

    private void DispatchUtf16Character(char character)
    {
        if (char.IsControl(character)) return;

        if (char.IsHighSurrogate(character))
        {
            _pendingHighSurrogate = character;
            return;
        }

        if (char.IsLowSurrogate(character) && _pendingHighSurrogate is char high)
        {
            TextInput?.Invoke(new string([high, character]));
            _pendingHighSurrogate = null;
            return;
        }

        _pendingHighSurrogate = null;
        TextInput?.Invoke(character.ToString());
    }

    private void ApplyCursor()
    {
        var cursorId = _cursor switch
        {
            CursorKind.Text => Win32Api.IDC_IBEAM,
            CursorKind.Hand => Win32Api.IDC_HAND,
            CursorKind.ResizeHorizontal => Win32Api.IDC_SIZEWE,
            CursorKind.ResizeVertical => Win32Api.IDC_SIZENS,
            _ => Win32Api.IDC_ARROW
        };
        Win32Api.SetCursor(Win32Api.LoadCursor(IntPtr.Zero, new IntPtr(cursorId)));
    }

    private void ApplyTextInputRect(IntPtr hWnd)
    {
        if (_textInputRect.IsEmpty) return;
        var inputContext = Win32Api.ImmGetContext(hWnd);
        if (inputContext == IntPtr.Zero) return;
        try
        {
            var x = (int)MathF.Round(_textInputRect.X * _dpiScale);
            var y = (int)MathF.Round(_textInputRect.Y * _dpiScale);
            var bottom = (int)MathF.Round(_textInputRect.Bottom * _dpiScale);
            var composition = new Win32Api.COMPOSITIONFORM
            {
                Style = Win32Api.CFS_POINT,
                CurrentPosition = new Win32Api.POINT { X = x, Y = y }
            };
            var candidate = new Win32Api.CANDIDATEFORM
            {
                Style = Win32Api.CFS_EXCLUDE,
                CurrentPosition = new Win32Api.POINT { X = x, Y = bottom },
                Area = new Win32Api.RECT { Left = x, Top = y, Right = x + 2, Bottom = bottom }
            };
            Win32Api.ImmSetCompositionWindow(inputContext, ref composition);
            Win32Api.ImmSetCandidateWindow(inputContext, ref candidate);
        }
        finally
        {
            Win32Api.ImmReleaseContext(hWnd, inputContext);
        }
    }

    private static float DpiToScale(uint dpi) => dpi > 0 ? dpi / 96f : 1f;

    private static int ToPhysical(int logicalValue, float dpiScale)
        => Math.Max(1, (int)MathF.Round(logicalValue * dpiScale));

    private void UpdateClientSize(Win32Api.RECT rect)
    {
        _physicalClientSize = new Size(rect.Width, rect.Height);
        _clientSize = ToLogicalSize(_physicalClientSize);
    }

    private Size ToLogicalSize(Size size)
        => new(size.Width / _dpiScale, size.Height / _dpiScale);

    private Point ToLogicalPoint(float x, float y)
        => new(x / _dpiScale, y / _dpiScale);

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

    private void RestoreOwner()
    {
        if (!_isModal || _ownerHandle == IntPtr.Zero) return;
        Win32Api.EnableWindow(_ownerHandle, true);
        Win32Api.SetActiveWindow(_ownerHandle);
    }

    // Reused across presents to avoid per-frame struct setup cost
    private Win32Api.BITMAPINFO _presentInfo;
    private bool _presentInfoReady;
    private GCHandle _presentPixelsHandle;
    private byte[]? _presentPixelsArray;
    private IntPtr _presentMemoryDc;
    private IntPtr _presentBitmap;
    private IntPtr _presentPreviousBitmap;
    private IntPtr _presentBits;
    private int _presentWidth;
    private int _presentHeight;
    private bool _presentDibNeedsFullCopy;

    public void Dispose()
    {
        ReleasePresentResources();
        _lastFrame = null;
        _renderContext = null;
        SizeChanged = null;
        MouseEvent = null;
        WheelEvent = null;
        KeyEvent = null;
        TextInput = null;
        Tick = null;
        RenderRequested = null;
        StateChanged = null;
        Closed = null;
        if (_hwnd != IntPtr.Zero)
        {
            var handle = _hwnd;
            Win32Api.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            lock (HostsGate) Hosts.Remove(handle);
        }

        _running = false;
        RestoreOwner();
    }
}
