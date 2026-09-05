using Android.App;
using Android.Content;
using Android.Views;
using Square.Backends;
using Square.Backends.AndroidCanvas;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using SkiaSharp;
using AndroidView = global::Android.Views.View;
using AndroidActivity = Android.App.Activity;

namespace Square.Platform.Android;

/// <summary>Android Activity 的 Square surface 宿主。</summary>
public sealed class AndroidPlatformHost : IPlatformHost
{
    private readonly AndroidActivity _activity;
    private readonly PlatformHostCreateInfo _createInfo;
    private readonly AndroidBitmapPresenter _presenter;
    private readonly AndroidInputAdapter _inputAdapter;
    private IRenderContext? _renderContext;
    private SquareView? _view;
    private AndroidView? _inputView;
    private global::Android.Views.Surface? _nativeSurface;
    private AndroidVulkanRenderTarget? _nativeRenderTarget;
    private IntPtr _nativeWindow;
    private Size _clientSize = Size.Zero;
    private float _dpiScale;
    private string _title;
    private CursorKind _cursor = CursorKind.Arrow;
    private KeyModifiers _modifiers;
    private int _pendingAccent;
    private bool _running;
    private bool _disposed;
    private int _insetLeft;
    private int _insetTop;
    private int _insetRight;
    private int _insetBottom;
    /// <summary>查询当前焦点文本编辑器的组合输入客户端。</summary>
    public Func<Square.Controls.ITextInputClient?>? TextInputClientQuery { get; set; }
    /// <summary>查询 Square 当前是否存在文本编辑焦点。</summary>
    public Func<bool>? TextEditorFocusQuery { get; set; }
    /// <summary>查询当前 Square 可访问性根节点。</summary>
    public Func<Square.UI.Element?>? AccessibilityRootQuery { get; set; }
    /// <summary>创建 Android 宿主。</summary>
    public AndroidPlatformHost(AndroidActivity activity, PlatformHostCreateInfo info)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(info);
        _activity = activity;
        _createInfo = info;
        _title = info.Title;
        _dpiScale = ResolveDensity(activity);
        _presenter = new AndroidBitmapPresenter();
        _inputAdapter = new AndroidInputAdapter(this);
    }

    /// <inheritdoc />
    public Size ClientSize => _clientSize;
    /// <inheritdoc />
    public float DpiScale => _dpiScale;
    /// <inheritdoc />
    public bool IsRunning => _running;
    /// <inheritdoc />
    public bool UsesMobileScrollbarProfile => true;
    /// <inheritdoc />
    public AppWindowState State => AppWindowState.Normal;
    /// <inheritdoc />
    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? "";
            if (!_disposed) _activity.Title = _title;
        }
    }
    /// <inheritdoc />
    public CursorKind Cursor
    {
        get => _cursor;
        set => _cursor = value;
    }
    /// <inheritdoc />
    public KeyModifiers Modifiers => _modifiers;

    /// <inheritdoc />
    public event Action<Size>? SizeChanged;
    /// <inheritdoc />
    public event Action<PointerInput>? PointerEvent;
    /// <inheritdoc />
    public event Action<Point, MouseAction, MouseButton>? MouseEvent
    {
        add { }
        remove { }
    }
    /// <inheritdoc />
    public event Action<WheelInput>? WheelEvent;
    /// <inheritdoc />
    public event Action<int, KeyAction>? KeyEvent;
    /// <inheritdoc />
    public event Action<string>? TextInput;
    /// <inheritdoc />
    public event Action? Tick;
    /// <inheritdoc />
    public event Action? RenderRequested;
    /// <inheritdoc />
    public event Action? Closed;

    /// <summary>绑定呈现 View。</summary>
    public void AttachView(SquareView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _view = view;
        _presenter.AttachView(view);
        _activity.Title = _title;
    }
    internal void AttachInputView(AndroidView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _inputView = view;
    }
    internal void SetNativeSurface(global::Android.Views.Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ClearNativeSurface();
        var nativeWindow = AndroidNativeWindow.FromSurface(surface);
        if (nativeWindow == IntPtr.Zero)
            throw new InvalidOperationException("Android did not provide an ANativeWindow for the Surface.");
        _nativeSurface = surface;
        _nativeWindow = nativeWindow;
        _nativeRenderTarget = new AndroidVulkanRenderTarget(nativeWindow);
    }

    internal void ClearNativeSurface()
    {
        // The suspended session releases its context before the native window is released.
        _renderContext = null;
        var nativeWindow = _nativeWindow;
        _nativeWindow = IntPtr.Zero;
        _nativeRenderTarget = null;
        _nativeSurface = null;
        if (nativeWindow != IntPtr.Zero) AndroidNativeWindow.Release(nativeWindow);
    }

    internal bool HasNativeSurface => _nativeRenderTarget != null;

    /// <summary>处理 View 的触摸事件。</summary>
    public bool HandleTouchEvent(MotionEvent e) => !_disposed && _inputAdapter.HandleTouchEvent(e);

    /// <summary>处理 View 的外接鼠标事件。</summary>
    public bool HandleGenericMotionEvent(MotionEvent e) => !_disposed && _inputAdapter.HandleGenericMotionEvent(e);

    /// <summary>处理 View 的硬件按键。</summary>
    public bool HandleKeyEvent(Keycode keyCode, KeyEvent? eventArgs, KeyAction action)
    {
        if (_disposed) return false;
        _modifiers = ResolveModifiers(eventArgs);
        var key = MapKeyCode(keyCode);
        if (action == KeyAction.Down && key == 8 && _pendingAccent != 0)
        {
            _pendingAccent = 0;
            return true;
        }
        if (action == KeyAction.Down && key is 9 or 13 or 27 or >= 33 and <= 46)
            _pendingAccent = 0;
        var handled = key != 0 || eventArgs?.IsPrintingKey == true;
        if (key != 0) KeyEvent?.Invoke(key, action);
        if (action == KeyAction.Down && eventArgs != null &&
            !eventArgs.IsCtrlPressed && !eventArgs.IsMetaPressed)
            handled |= DispatchHardwareCharacter(eventArgs.UnicodeChar);
        if (handled)
        {
            NotifyAccessibilityChanged();
            RequestRenderFrame();
        }
        return handled;
    }

    private bool DispatchHardwareCharacter(int codePoint)
    {
        if ((codePoint & KeyCharacterMap.CombiningAccent) != 0)
        {
            if (_pendingAccent != 0) RaiseTextInput(char.ConvertFromUtf32(_pendingAccent));
            _pendingAccent = codePoint & KeyCharacterMap.CombiningAccentMask;
            return true;
        }
        if (!System.Text.Rune.IsValid(codePoint) || System.Text.Rune.IsControl(new System.Text.Rune(codePoint)))
            return false;
        if (_pendingAccent != 0)
        {
            var combined = KeyCharacterMap.GetDeadChar(_pendingAccent, codePoint);
            if (combined != 0) codePoint = combined;
            else RaiseTextInput(char.ConvertFromUtf32(_pendingAccent));
            _pendingAccent = 0;
        }
        RaiseTextInput(char.ConvertFromUtf32(codePoint));
        return true;
    }

    private static int MapKeyCode(Keycode keyCode)
    {
        if (keyCode >= Keycode.A && keyCode <= Keycode.Z)
            return 65 + ((int)keyCode - (int)Keycode.A);
        if (keyCode >= Keycode.Num0 && keyCode <= Keycode.Num9)
            return 48 + ((int)keyCode - (int)Keycode.Num0);
        if (keyCode >= Keycode.Numpad0 && keyCode <= Keycode.Numpad9)
            return 96 + ((int)keyCode - (int)Keycode.Numpad0);
        if (keyCode >= Keycode.F1 && keyCode <= Keycode.F12)
            return 112 + ((int)keyCode - (int)Keycode.F1);
        return keyCode switch
        {
            Keycode.Del => 8,
            Keycode.Enter or Keycode.NumpadEnter => 13,
            Keycode.DpadLeft => 37,
            Keycode.DpadUp => 38,
            Keycode.DpadRight => 39,
            Keycode.DpadDown => 40,
            Keycode.Escape => 27,
            Keycode.Space => 32,
            Keycode.Tab => 9,
            Keycode.ForwardDel => 46,
            Keycode.MoveHome => 36,
            Keycode.MoveEnd => 35,
            Keycode.PageUp => 33,
            Keycode.PageDown => 34,
            Keycode.Insert => 45,
            Keycode.ShiftLeft or Keycode.ShiftRight => 16,
            Keycode.CtrlLeft or Keycode.CtrlRight => 17,
            Keycode.AltLeft or Keycode.AltRight => 18,
            Keycode.Semicolon => 186,
            Keycode.Equals => 187,
            Keycode.Comma => 188,
            Keycode.Minus => 189,
            Keycode.Period => 190,
            Keycode.Slash => 191,
            Keycode.Grave => 192,
            Keycode.LeftBracket => 219,
            Keycode.Backslash => 220,
            Keycode.RightBracket => 221,
            Keycode.Apostrophe => 222,
            _ => 0
        };
    }
    /// <summary>将 View 像素尺寸转换为 Square DIP 并通知会话。</summary>
    public void UpdateSurfaceSize(int widthPixels, int heightPixels)
    {
        if (_disposed) return;
        var density = ResolveDensity(_activity);
        var contentWidth = Math.Max(0, widthPixels - _insetLeft - _insetRight);
        var contentHeight = Math.Max(0, heightPixels - _insetTop - _insetBottom);
        var nextSize = new Size(contentWidth / density, contentHeight / density);
        var changed = nextSize != _clientSize || MathF.Abs(density - _dpiScale) >= 0.001f;
        _dpiScale = density;
        _clientSize = nextSize;
        if (!changed) return;
        if (_renderContext is IDpiResizableRenderContext dpiResizable && contentWidth > 0 && contentHeight > 0)
            dpiResizable.Resize(_clientSize, _dpiScale);
        else if (_renderContext is IResizableRenderContext resizable && contentWidth > 0 && contentHeight > 0)
            resizable.Resize(_clientSize);
        SizeChanged?.Invoke(_clientSize);
    }
    internal void UpdateInsets(int left, int top, int right, int bottom)
    {
        if (_disposed) return;
        _insetLeft = Math.Max(0, left);
        _insetTop = Math.Max(0, top);
        _insetRight = Math.Max(0, right);
        _insetBottom = Math.Max(0, bottom);
        if (_view != null) UpdateSurfaceSize(_view.Width, _view.Height);
    }

    internal Point ToLogicalPoint(float xPixels, float yPixels) => new(
        (xPixels - _insetLeft) / _dpiScale,
        (yPixels - _insetTop) / _dpiScale);
    internal global::Android.Graphics.Rect ToPhysicalRect(Square.Graphics.Rect rect)
    {
        var left = _insetLeft + (int)MathF.Floor(rect.Left * _dpiScale);
        var top = _insetTop + (int)MathF.Floor(rect.Top * _dpiScale);
        var right = Math.Max(left, _insetLeft + (int)MathF.Ceiling(rect.Right * _dpiScale));
        var bottom = Math.Max(top, _insetTop + (int)MathF.Ceiling(rect.Bottom * _dpiScale));
        return new global::Android.Graphics.Rect(left, top, right, bottom);
    }

    /// <summary>在 View 上绘制最近一次呈现的帧。</summary>
    public void Draw(global::Android.Graphics.Canvas canvas)
    {
        var saveCount = canvas.Save();
        canvas.Translate(_insetLeft, _insetTop);
        if (_renderContext is IAndroidCanvasRenderContext direct)
        {
            canvas.Scale(_dpiScale, _dpiScale);
            direct.Draw(canvas);
        }
        else
        {
            _presenter.Draw(canvas);
        }
        canvas.RestoreToCount(saveCount);
    }
    internal bool IsUsingAndroidSkia => _renderContext is IAndroidSkiaRenderContext;

    internal void DrawSkia(SKCanvas canvas)
    {
        if (_renderContext is not IAndroidSkiaRenderContext direct) return;
        var saveCount = canvas.Save();
        canvas.Translate(_insetLeft, _insetTop);
        canvas.Scale(_dpiScale, _dpiScale);
        direct.Draw(canvas);
        canvas.RestoreToCount(saveCount);
    }


    /// <summary>推进 Android fling。</summary>
    public bool StepFling() => !_disposed && _inputAdapter.StepFling();

    /// <summary>当前是否有 fling 需要继续运行。</summary>
    public bool HasFling => !_disposed && _inputAdapter.HasFling;

    /// <summary>取消触摸、fling 和输入连接。</summary>
    public void CancelInput()
    {
        _pendingAccent = 0;
        _inputAdapter.Cancel();
    }

    internal void RaisePointer(PointerInput input)
    {
        if (input.Action == PointerAction.Down) _pendingAccent = 0;
        PointerEvent?.Invoke(input);
        if (input.Action != PointerAction.Move)
        {
            NotifyAccessibilityChanged();
            RequestRenderFrame();
        }
    }

    internal void RaiseWheel(WheelInput input)
    {
        WheelEvent?.Invoke(input);
        NotifyAccessibilityChanged();
        RequestRenderFrame();
    }

    internal void RaiseTextInput(string text)
    {
        TextInput?.Invoke(text);
        NotifyAccessibilityChanged();
    }
    internal void NotifyAccessibilityChanged() => _view?.NotifyAccessibilityChanged();
    internal void InvalidateView() => _view?.PostInvalidateOnAnimation();
    internal void LogPerformanceDiagnostics(AndroidFrameScheduler? scheduler)
    {
        var presenter = _presenter.GetMetrics();
        var frame = scheduler?.GetMetrics() ?? default;
        var frameAverageMs = frame.FrameCount == 0
            ? 0
            : frame.TotalFrameTicks * 1000d / System.Diagnostics.Stopwatch.Frequency / frame.FrameCount;
        var uploadAverageMs = presenter.PresentCount == 0
            ? 0
            : presenter.TotalUploadTicks * 1000d / System.Diagnostics.Stopwatch.Frequency / presenter.PresentCount;
        var uploadLastMs = presenter.LastUploadTicks * 1000d / System.Diagnostics.Stopwatch.Frequency;
        global::Android.Util.Log.Info(
            "Square",
            $"Android perf: frames={frame.FrameCount}, frameAvgMs={frameAverageMs:0.###}, " +
            $"frameLastMs={frame.LastFrameTicks * 1000d / System.Diagnostics.Stopwatch.Frequency:0.###}, " +
            $"presents={presenter.PresentCount}, uploadAvgMs={uploadAverageMs:0.###}, " +
            $"uploadLastMs={uploadLastMs:0.###}, uploadBytes={presenter.UploadedBytes}, " +
            $"bitmap={presenter.BitmapWidth}x{presenter.BitmapHeight}");
    }
    internal void RaiseTick() => Tick?.Invoke();
    internal void RequestRenderFrame() => RenderRequested?.Invoke();

    internal void RequestTextInputSurface()
    {
        var inputView = _inputView ?? _view;
        if (inputView == null || _disposed) return;
        if (TextInputClientQuery?.Invoke() == null && TextEditorFocusQuery?.Invoke() != true) return;
        inputView.RequestFocus();
        var inputManager = _activity.GetSystemService(Context.InputMethodService) as global::Android.Views.InputMethods.InputMethodManager;
        inputManager?.ShowSoftInput(inputView, global::Android.Views.InputMethods.ShowFlags.Implicit);
    }

    internal void RequestInputRectangle(Square.Graphics.Rect rect)
    {
        if (_view == null || _disposed || rect.IsEmpty) return;
        var left = _insetLeft + (int)MathF.Floor(rect.Left * _dpiScale);
        var top = _insetTop + (int)MathF.Floor(rect.Top * _dpiScale);
        var right = Math.Max(left + 1, _insetLeft + (int)MathF.Ceiling(rect.Right * _dpiScale));
        var bottom = Math.Max(top + 1, _insetTop + (int)MathF.Ceiling(rect.Bottom * _dpiScale));
        _view.RequestRectangleOnScreen(new global::Android.Graphics.Rect(left, top, right, bottom), false);
    }

    /// <inheritdoc />
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _running = true;
        _activity.Title = _title;
    }

    /// <inheritdoc />
    public void ShowAfterFirstFrame()
    {
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_disposed || !_running) return;
        _running = false;
        Closed?.Invoke();
        _activity.Finish();
    }

    /// <inheritdoc />
    public void Minimize() => throw new PlatformNotSupportedException("Android does not support desktop window minimize.");
    /// <inheritdoc />
    public void Maximize() => throw new PlatformNotSupportedException("Android does not support desktop window maximize.");
    /// <inheritdoc />
    public void Restore() => throw new PlatformNotSupportedException("Android does not support desktop window restore.");
    /// <inheritdoc />
    public void BeginMove() => throw new PlatformNotSupportedException("Android does not support desktop window move.");

    /// <inheritdoc />
    public IRenderContext CreateRenderContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_renderContext != null) return _renderContext;
        if (_clientSize.Width <= 0 || _clientSize.Height <= 0)
            throw new InvalidOperationException("The Android surface must have a nonzero size before creating a renderer.");
        var backendName = _createInfo.RenderBackend;
        var isSoftware = string.Equals(backendName, "Software", StringComparison.OrdinalIgnoreCase);
        var isAndroidCanvas = string.Equals(backendName, "AndroidCanvas", StringComparison.OrdinalIgnoreCase);
        var isAndroidSkia = string.Equals(backendName, "AndroidSkia", StringComparison.OrdinalIgnoreCase);
        var isVulkan = string.Equals(backendName, "Vulkan", StringComparison.OrdinalIgnoreCase);
        if (!isSoftware && !isAndroidCanvas && !isAndroidSkia && !isVulkan)
            throw new PlatformNotSupportedException($"Android currently supports only the Software, AndroidCanvas, AndroidSkia, and Vulkan render backends, not '{backendName}'.");
        if (isVulkan && _nativeRenderTarget == null)
            throw new InvalidOperationException("The Android Vulkan Surface must be ready before creating the renderer.");

        var factory = RenderBackendRegistry.Get(backendName);
        _renderContext = factory.CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = _clientSize,
            DpiScale = _dpiScale,
            SoftwareSurface = null,
            NativeTarget = isVulkan ? _nativeRenderTarget : null,
            PresentFrame = isSoftware ? _presenter.Present : null,
            RequestRender = RequestRenderFrame
        });
        return _renderContext;
    }

    /// <inheritdoc />
    public void PumpEvents() => throw new PlatformNotSupportedException(
        "Android is driven by Activity, Looper, and Choreographer; PumpEvents is not supported.");

    /// <inheritdoc />
    public void SetTextInputRect(Square.Graphics.Rect rect) => RequestInputRectangle(rect);
    /// <inheritdoc />
    public string GetClipboardText() => AndroidClipboard.GetText(_activity);
    /// <inheritdoc />
    public void SetClipboardText(string text) => AndroidClipboard.SetText(_activity, text);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        _inputAdapter.Cancel();
        _inputView = null;
        _presenter.Dispose();
        _renderContext = null;
        ClearNativeSurface();
        AccessibilityRootQuery = null;
        TextEditorFocusQuery = null;
        TextInputClientQuery = null;
        SizeChanged = null;
        PointerEvent = null;
        WheelEvent = null;
        KeyEvent = null;
        TextInput = null;
        Tick = null;
        RenderRequested = null;
        Closed = null;
    }

    private static float ResolveDensity(AndroidActivity activity)
    {
        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        return float.IsFinite(density) && density > 0 ? density : 1f;
    }

    private static KeyModifiers ResolveModifiers(KeyEvent? eventArgs)
    {
        if (eventArgs == null) return KeyModifiers.None;
        var modifiers = KeyModifiers.None;
        if (eventArgs.IsShiftPressed) modifiers |= KeyModifiers.Shift;
        if (eventArgs.IsCtrlPressed) modifiers |= KeyModifiers.Control;
        if (eventArgs.IsAltPressed) modifiers |= KeyModifiers.Alt;
        return modifiers;
    }
}
