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
    private bool _suspended;
    private bool _pointerInside;
    private bool _mouseButtonDown;
    private bool _imeActive;
    private bool _detaching;
    private bool _signalsSubscribed;
    private bool _lastProcessable;
    private float _lastDpi = 1f;

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
            SubscribeSignals();
            host.UpdateMetrics(size, ResolveDpi(), notify: false);
            host.CloseRequested += HandleHostCloseRequested;
            session.Attach();
        }
        catch
        {
            UnsubscribeSignals();
            host.CloseRequested -= HandleHostCloseRequested;
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
        var session = Session;
        var host = _host;
        if (session == null || host == null) return;

        var size = CurrentSize();
        var dpi = ResolveDpi();
        host.UpdateMetrics(size, dpi);
        var processable = Visible && size.Width > 0 && size.Height > 0 && CanProcess();
        if (!processable)
        {
            if (!_suspended)
            {
                session.NotifyFocusLost();
                session.Suspend();
                _suspended = true;
            }
            UpdateImeState(false);
            _lastProcessable = false;
            return;
        }

        if (_suspended)
        {
            session.Resume();
            _suspended = false;
        }
        _lastProcessable = true;
        UpdateImeState(HasFocus() && session.HasTextEditorFocus);
        session.Tick();
        if (session.HasPendingFrame) session.ProcessFrame();
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
        _subscribedWindow = GetWindow();
        if (_subscribedWindow != null)
        {
            _subscribedWindow.FocusEntered += HandleWindowFocusEntered;
            _subscribedWindow.FocusExited += HandleWindowFocusExited;
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
        if (_subscribedWindow != null)
        {
            _subscribedWindow.FocusEntered -= HandleWindowFocusEntered;
            _subscribedWindow.FocusExited -= HandleWindowFocusExited;
            _subscribedWindow = null;
        }
    }

    private void HandleResized() => _host?.UpdateMetrics(CurrentSize(), ResolveDpi());
    private void HandleVisibilityChanged() => RefreshProcessingState();
    private void HandleFocusEntered() => _pointerInside = true;

    private void HandleFocusExited()
    {
        if (_host != null) _host.Modifiers = KeyModifiers.None;
        Session?.NotifyFocusLost();
        UpdateImeState(false);
    }

    private void HandleWindowFocusEntered() => _pointerInside = true;

    private void HandleWindowFocusExited()
    {
        if (_host != null) _host.Modifiers = KeyModifiers.None;
        Session?.NotifyFocusLost();
        UpdateImeState(false);
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
        var processable = Visible && CurrentSize() is { Width: > 0, Height: > 0 } && CanProcess();
        if (!processable && !_suspended)
        {
            Session.NotifyFocusLost();
            Session.Suspend();
            _suspended = true;
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
