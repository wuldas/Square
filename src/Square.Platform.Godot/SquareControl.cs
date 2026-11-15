using System.Diagnostics;
using Godot;
using Square.Graphics;
using Square.Hosting;
using Square.Runtime;
using GControl = Godot.Control;
using GWindow = Godot.Window;

namespace Square.Platform.Godot;

/// <summary>在 Godot Control 内承载一个 Square ApplicationSession。</summary>
public partial class SquareControl : GControl
{
    private GodotPlatformHost? _host;
    private GWindow? _subscribedWindow;
    private Viewport? _subscribedViewport;
    private double? _nextFrameTimestamp;
    private int _frameRequestPosted;
    private int _mainThreadId;
    private bool _processingFrame;
    private bool _updatingFrameSchedule;
    private bool _suspended;
    private bool _pointerInside;
    private bool _mouseButtonDown;
    private bool _imeActive;
    private bool _detaching;
    private bool _signalsSubscribed;
    private bool _lastProcessable;

    /// <summary>当前附加的 Square 会话。</summary>
    public ApplicationSession? Session { get; private set; }

    /// <summary>将窗口附加到当前已入树的 Control。</summary>
    public void Attach(AppWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!IsInsideTree())
            throw new InvalidOperationException("Attach requires SquareControl to be inside the scene tree.");
        if (Session != null)
        {
            if (ReferenceEquals(Session.Window, window)) return;
            throw new InvalidOperationException("SquareControl already has an attached window.");
        }
        if (Application.IsStarted && Application.Current.IsRunning)
            throw new InvalidOperationException("Only one active Square application is supported on the Godot main thread.");

        window.RenderBackend = "Godot";
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;

        var size = CurrentSize();
        var info = new PlatformHostCreateInfo
        {
            Title = window.Title,
            Width = Math.Max(1, (int)MathF.Ceiling(size.Width)),
            Height = Math.Max(1, (int)MathF.Ceiling(size.Height)),
            RenderBackend = "Godot",
            SoftwareSurface = window.SoftwareSurface,
            TitleStyle = window.TitleStyle,
            BorderStyle = window.BorderStyle
        };
        var host = new GodotPlatformHost(this, info);
        var session = new ApplicationSession(window, host);
        try
        {
            _host = host;
            Session = session;
            _pointerInside = true;
            _lastProcessable = false;
            _mainThreadId = System.Environment.CurrentManagedThreadId;
            SubscribeSignals();
            SetNotifyTransform(true);
            host.UpdateMetrics(size, ResolveDpi(), notify: false);
            host.CloseRequested += HandleHostCloseRequested;
            session.FrameRequested += HandleFrameRequested;
            session.Attach();
            RefreshProcessingState();
        }
        catch
        {
            UnsubscribeSignals();
            host.CloseRequested -= HandleHostCloseRequested;
            session.FrameRequested -= HandleFrameRequested;
            _nextFrameTimestamp = null;
            Session = null;
            _host = null;
            session.Dispose();
            throw;
        }
    }

    /// <summary>分离并释放当前嵌入会话。</summary>
    public void Detach() => DetachCore();

    public override void _GuiInput(InputEvent @event)
    {
        var session = Session;
        var host = _host;
        if (session == null || host == null) return;
        var handled = GodotInput.TryHandle(this, host, session, @event);
        if (@event is InputEventMouseButton button && button.ButtonIndex.ToString() is "Left" or "Middle" or "Right")
        {
            if (button.Pressed) _mouseButtonDown = true;
            else _mouseButtonDown = false;
            if (!button.Pressed && !_pointerInside) session.NotifyPointerExited();
        }
        if (handled) AcceptEvent();
    }

    public override void _Process(double delta)
    {
        // Godot 仍拥有引擎循环；Square 空闲时只检查自己的截止时间。
        if (_nextFrameTimestamp is not { } deadline || Stopwatch.GetTimestamp() < deadline) return;
        var session = Session;
        var host = _host;
        if (session == null || host == null) return;

        _processingFrame = true;
        try
        {
            RefreshProcessingState();
            if (_suspended) return;
            host.UpdateMetrics(CurrentSize(), ResolveDpi());
            session.Tick();
            UpdateImeState(HasFocus() && session.HasTextEditorFocus);
        }
        finally
        {
            _processingFrame = false;
            RefreshFrameSchedule(replace: true);
        }
    }

    public override void _Notification(int what)
    {
        if ((long)what is NotificationPaused or NotificationUnpaused or NotificationEnabled or NotificationDisabled)
            RefreshProcessingState();
        else if (what == NotificationTransformChanged)
            HandleResized();
    }

    public override void _ExitTree() => DetachCore();

    private void SubscribeSignals()
    {
        if (_signalsSubscribed) return;
        _signalsSubscribed = true;
        Resized += HandleResized;
        VisibilityChanged += HandleVisibilityChanged;
        MouseEntered += HandleMouseEntered;
        MouseExited += HandleMouseExited;
        FocusEntered += HandleFocusEntered;
        FocusExited += HandleFocusExited;
        _subscribedViewport = GetViewport();
        if (_subscribedViewport != null) _subscribedViewport.SizeChanged += HandleResized;
        _subscribedWindow = GetWindow();
        if (_subscribedWindow != null)
        {
            _subscribedWindow.FocusEntered += HandleWindowFocusEntered;
            _subscribedWindow.FocusExited += HandleWindowFocusExited;
            _subscribedWindow.DpiChanged += HandleResized;
            _subscribedWindow.VisibilityChanged += HandleVisibilityChanged;
            if (!ReferenceEquals(_subscribedWindow, _subscribedViewport))
                _subscribedWindow.SizeChanged += HandleResized;
        }
    }

    private void UnsubscribeSignals()
    {
        if (!_signalsSubscribed) return;
        _signalsSubscribed = false;
        Resized -= HandleResized;
        VisibilityChanged -= HandleVisibilityChanged;
        MouseEntered -= HandleMouseEntered;
        MouseExited -= HandleMouseExited;
        FocusEntered -= HandleFocusEntered;
        FocusExited -= HandleFocusExited;
        if (_subscribedViewport != null)
        {
            _subscribedViewport.SizeChanged -= HandleResized;
            _subscribedViewport = null;
        }
        if (_subscribedWindow != null)
        {
            _subscribedWindow.FocusEntered -= HandleWindowFocusEntered;
            _subscribedWindow.FocusExited -= HandleWindowFocusExited;
            _subscribedWindow.DpiChanged -= HandleResized;
            _subscribedWindow.VisibilityChanged -= HandleVisibilityChanged;
            _subscribedWindow.SizeChanged -= HandleResized;
            _subscribedWindow = null;
        }
    }

    private void HandleResized()
    {
        _host?.UpdateMetrics(CurrentSize(), ResolveDpi());
        RefreshProcessingState();
    }
    private void HandleVisibilityChanged() => RefreshProcessingState();
    private void HandleFocusEntered()
    {
        _pointerInside = true;
        RefreshProcessingState();
        UpdateImeState(!_suspended && HasFocus() && Session?.HasTextEditorFocus == true);
    }

    private void HandleFocusExited()
    {
        if (_host != null) _host.Modifiers = KeyModifiers.None;
        Session?.NotifyFocusLost();
        UpdateImeState(false);
        RefreshProcessingState();
    }

    private void HandleWindowFocusEntered() => HandleFocusEntered();

    private void HandleWindowFocusExited()
    {
        if (_host != null) _host.Modifiers = KeyModifiers.None;
        Session?.NotifyFocusLost();
        UpdateImeState(false);
        RefreshProcessingState();
    }

    private void HandleMouseEntered() => _pointerInside = true;

    private void HandleMouseExited()
    {
        _pointerInside = false;
        if (!_mouseButtonDown) Session?.NotifyPointerExited();
    }

    private void HandleHostCloseRequested()
    {
        if (!IsInsideTree()) return;
        CallDeferred(nameof(Detach));
    }

    private void RefreshProcessingState()
    {
        if (Session == null || _host == null) return;
        var windowVisible = _subscribedWindow == null ||
            (_subscribedWindow.Visible && _subscribedWindow.Mode != GWindow.ModeEnum.Minimized);
        var processable = windowVisible && IsVisibleInTree() &&
            CurrentSize() is { Width: > 0, Height: > 0 } && CanProcess();
        if (!processable)
        {
            if (!_suspended)
            {
                Session.NotifyFocusLost();
                Session.Suspend();
                _suspended = true;
            }
            UpdateImeState(false);
            _lastProcessable = false;
            _nextFrameTimestamp = null;
            return;
        }
        if (_suspended)
        {
            Session.Resume();
            _suspended = false;
        }
        if (!_lastProcessable) _nextFrameTimestamp = Stopwatch.GetTimestamp();
        _lastProcessable = true;
        RefreshFrameSchedule();
    }

    private void HandleFrameRequested()
    {
        if (System.Environment.CurrentManagedThreadId == _mainThreadId && (_processingFrame || _updatingFrameSchedule)) return;
        if (Interlocked.Exchange(ref _frameRequestPosted, 1) != 0) return;
        // FrameRequested 可以来自后台线程；所有会话查询和 Godot 场景操作留在主线程。
        CallDeferred(nameof(HandleDeferredFrameRequest));
    }

    private void HandleDeferredFrameRequest()
    {
        Interlocked.Exchange(ref _frameRequestPosted, 0);
        RefreshFrameSchedule();
    }

    private void RefreshFrameSchedule(bool replace = false)
    {
        if (_updatingFrameSchedule) return;
        _updatingFrameSchedule = true;
        try
        {
            var delay = !_suspended ? Session?.NextFrameDelay : null;
            if (delay == null)
            {
                _nextFrameTimestamp = null;
                return;
            }
            var deadline = Stopwatch.GetTimestamp() + Math.Max(0, delay.Value.TotalSeconds) * Stopwatch.Frequency;
            _nextFrameTimestamp = !replace && _nextFrameTimestamp is { } pending
                ? Math.Min(pending, deadline)
                : deadline;
        }
        finally
        {
            _updatingFrameSchedule = false;
        }
    }

    private void UpdateImeState(bool active)
    {
        if (_imeActive == active) return;
        _imeActive = active;
        _host?.SetImeActive(active);
    }

    private Size CurrentSize() => new(MathF.Max(0, Size.X), MathF.Max(0, Size.Y));

    private float ResolveDpi()
    {
        var transform = GetScreenTransform();
        var x = transform.X.Length();
        var y = transform.Y.Length();
        var dpi = MathF.Max(x, y);
        return float.IsFinite(dpi) && dpi > 0 ? dpi : 1f;
    }

    private void DetachCore()
    {
        if (_detaching) return;
        _detaching = true;
        try
        {
            UnsubscribeSignals();
            UpdateImeState(false);
            if (_host != null) _host.CloseRequested -= HandleHostCloseRequested;
            var session = Session;
            if (session != null) session.FrameRequested -= HandleFrameRequested;
            _nextFrameTimestamp = null;
            SetNotifyTransform(false);
            Session = null;
            _host = null;
            session?.Dispose();
            _suspended = false;
            _mouseButtonDown = false;
        }
        finally
        {
            _detaching = false;
        }
    }
}
