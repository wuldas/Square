using Godot;
using Square.Backends;
using Square.Backends.Godot;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using GControl = Godot.Control;
using GSize = Godot.Vector2;
using GVector2I = Godot.Vector2I;

namespace Square.Platform.Godot;

/// <summary>将 Godot Control 的外部主循环映射到 Square 平台宿主契约。</summary>
internal sealed class GodotPlatformHost : IPlatformHost
{
    private readonly GControl _target;
    private readonly PlatformHostCreateInfo _createInfo;
    private IRenderContext? _renderContext;
    private Size _clientSize;
    private float _dpiScale = 1f;
    private CursorKind _cursor = CursorKind.Arrow;
    private string _title;
    private bool _running;
    private bool _disposed;

    internal GodotPlatformHost(GControl target, PlatformHostCreateInfo createInfo)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _createInfo = createInfo ?? throw new ArgumentNullException(nameof(createInfo));
        _title = createInfo.Title;
        UpdateMetrics(new Size(MathF.Max(0, target.Size.X), MathF.Max(0, target.Size.Y)), 1f, notify: false);
    }

    public Size ClientSize => _clientSize;
    public float DpiScale => _dpiScale;
    public bool IsRunning => _running;
    public AppWindowState State => AppWindowState.Normal;

    public string Title
    {
        get => _title;
        set => _title = value ?? string.Empty;
    }

    public CursorKind Cursor
    {
        get => _cursor;
        set => _cursor = value;
    }

    public KeyModifiers Modifiers { get; internal set; }

    public event Action<PointerInput>? PointerEvent;
    public event Action<Size>? SizeChanged;
    event Action<Point, MouseAction, MouseButton>? IPlatformHost.MouseEvent
    {
        add { }
        remove { }
    }
    public event Action<WheelInput>? WheelEvent;
    public event Action<int, KeyAction>? KeyEvent;
    public event Action<string>? TextInput;
    public event Action? Tick;
    public event Action? RenderRequested;
    event Action<AppWindowState>? IPlatformHost.StateChanged
    {
        add { }
        remove { }
    }
    public event Action? Closed;
    internal event Action? CloseRequested;

    internal GControl Target => _target;

    internal void UpdateMetrics(Size size, float dpiScale, bool notify = true)
    {
        if (_disposed) return;
        var normalizedDpi = float.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1f;
        var changed = size != _clientSize || MathF.Abs(normalizedDpi - _dpiScale) >= 0.001f;
        _clientSize = size;
        _dpiScale = normalizedDpi;
        if (!changed) return;
        if (_renderContext is IDpiResizableRenderContext dpiResizable && size.Width > 0 && size.Height > 0)
            dpiResizable.Resize(size, _dpiScale);
        else if (_renderContext is IResizableRenderContext resizable && size.Width > 0 && size.Height > 0)
            resizable.Resize(size);
        if (notify) SizeChanged?.Invoke(size);
    }

    internal void SetRunning(bool running) => _running = running;

    internal void RaisePointer(PointerInput input) => PointerEvent?.Invoke(input);
    internal void RaiseWheel(WheelInput input) => WheelEvent?.Invoke(input);
    internal void RaiseKey(int keyCode, KeyAction action) => KeyEvent?.Invoke(keyCode, action);
    internal void RaiseText(string text) => TextInput?.Invoke(text);
    internal void RaiseTick() => Tick?.Invoke();
    internal void RequestRender() => RenderRequested?.Invoke();

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _running = true;
    }

    public void ShowAfterFirstFrame()
    {
    }

    public void Close()
    {
        if (_disposed) return;
        _running = false;
        CloseRequested?.Invoke();
        Closed?.Invoke();
    }

    public void Minimize() => throw new PlatformNotSupportedException("Godot embedded controls do not expose an independent minimize operation.");
    public void Maximize() => throw new PlatformNotSupportedException("Godot embedded controls do not expose an independent maximize operation.");
    public void Restore() => throw new PlatformNotSupportedException("Godot embedded controls do not expose an independent restore operation.");
    public void BeginMove() => throw new PlatformNotSupportedException("Godot embedded controls do not expose an independent move operation.");

    public IRenderContext CreateRenderContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_renderContext != null) return _renderContext;
        if (_clientSize.Width <= 0 || _clientSize.Height <= 0)
            throw new InvalidOperationException("The SquareControl must have a nonzero size before creating a renderer.");
        _renderContext = new GodotRenderContext(_target, new RenderContextCreateInfo
        {
            CanvasSize = _clientSize,
            DpiScale = _dpiScale,
            VSync = _createInfo.RenderBackend.Equals("Godot", StringComparison.OrdinalIgnoreCase),
            RequestRender = RequestRender
        });
        return _renderContext;
    }

    public void PumpEvents()
    {
    }

    public void SetTextInputRect(Rect rect)
    {
        if (_disposed || rect.IsEmpty || !DisplayServer.HasFeature(DisplayServer.Feature.Ime)) return;
        var window = _target.GetWindow();
        if (window == null) return;
        var transform = _target.GetScreenTransform();
        var caret = transform * new Vector2(rect.X, rect.Bottom);
        var screenPosition = window.Position;
        window.SetImePosition(new GVector2I(
            Mathf.RoundToInt(caret.X - screenPosition.X),
            Mathf.RoundToInt(caret.Y - screenPosition.Y)));
    }

    public string GetClipboardText() => DisplayServer.ClipboardGet();
    public void SetClipboardText(string text) => DisplayServer.ClipboardSet(text ?? string.Empty);

    internal void SetImeActive(bool active)
    {
        if (_disposed || !DisplayServer.HasFeature(DisplayServer.Feature.Ime)) return;
        _target.GetWindow()?.SetImeActive(active);
    }

    internal void DisposeRenderContext()
    {
        var context = _renderContext;
        _renderContext = null;
        context?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        DisposeRenderContext();
        PointerEvent = null;
        SizeChanged = null;
        WheelEvent = null;
        KeyEvent = null;
        TextInput = null;
        Tick = null;
        RenderRequested = null;
        Closed = null;
        CloseRequested = null;
    }
}
