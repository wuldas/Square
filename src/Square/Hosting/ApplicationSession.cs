using Square.Platform;
using Square.Runtime;

namespace Square.Hosting;

/// <summary>由外部事件循环驱动的 Square 应用会话。</summary>
public sealed class ApplicationSession : IDisposable
{
    private readonly DesktopApplication _application;
    private readonly IPlatformHost _host;
    private bool _attached;
    private bool _suspended;
    private bool _entered;
    private bool _hotReloadRegistered;
    private bool _detached;

    /// <summary>创建由外部宿主驱动的应用会话。</summary>
    public ApplicationSession(AppWindow window, IPlatformHost host)
        : this(window, host, new DesktopApplication(window), registerHotReload: true)
    {
    }

    internal ApplicationSession(
        AppWindow window,
        IPlatformHost host,
        DesktopApplication application,
        bool registerHotReload = false)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(application);
        if (!ReferenceEquals(application.MainWindow, window))
            throw new ArgumentException("The application must be bound to the supplied window.", nameof(application));

        Window = window;
        _host = host;
        _application = application;
        _hotReloadRegistered = registerHotReload;
    }

    /// <summary>会话窗口。</summary>
    public AppWindow Window { get; }

    /// <summary>会话是否已附加。</summary>
    public bool IsAttached => _attached;

    /// <summary>会话是否已暂停。</summary>
    public bool IsSuspended => _suspended;

    /// <summary>会话是否已分离。</summary>
    public bool IsDetached => _detached;
    /// <summary>会话是否仍有待处理的帧工作。</summary>
    public bool HasPendingFrame => _attached && !_suspended && _application.HasPendingSessionFrame;
    /// <summary>会话当前是否聚焦文本编辑器。</summary>
    public bool HasTextEditorFocus => _attached && _application.HasFocusedTextEditor;
    /// <summary>当前焦点文本编辑器的组合输入客户端。</summary>
    public Square.Controls.ITextInputClient? FocusedTextInputClient =>
        _attached && !_suspended ? _application.FocusedTextInputClient : null;

    /// <summary>未被 Popup 消费时尝试处理路由后退。</summary>
    public Func<bool>? BackRequested { get; set; }

    /// <summary>一帧提交给渲染后端后触发；外部宿主可据此刷新呈现 View。</summary>
    public event Action? FramePresented
    {
        add => _application.FramePresented += value;
        remove => _application.FramePresented -= value;
    }

    /// <summary>按 Android Back 顺序处理 Popup 和应用路由。</summary>
    public bool HandleBack()
    {
        if (!_attached || _suspended) return false;
        if (_application.HandleBackSession()) return true;
        if (BackRequested?.Invoke() != true) return false;
        _application.RequestRender();
        _application.ProcessSessionFrame();
        return true;
    }

    /// <summary>附加会话；重复调用不会重复注册资源。</summary>
    public void Attach()
    {
        if (_detached) throw new InvalidOperationException("The application session has been detached.");
        if (_attached) return;
        if (_host.UsesMobileScrollbarProfile &&
            Window.ScrollbarProfile == Square.UI.Scrolling.ScrollbarDeviceProfile.Auto)
            Window.ScrollbarProfile = Square.UI.Scrolling.ScrollbarDeviceProfile.Mobile;

        try
        {
            _application.EnterExternalRun();
            _entered = true;
            if (_hotReloadRegistered)
                SquareHotReloadHandler.Register(_application);

            _application.PrepareSession();
            _application.AttachSessionHost(_host);
            _attached = true;
            _host.Show();
            _application.ProcessSessionFrame();
        }
        catch
        {
            _detached = true;
            DetachCore();
            throw;
        }
    }

    /// <summary>处理一帧；会话分离或暂停时不执行任何操作。</summary>
    public void ProcessFrame()
    {
        if (!_attached || _suspended) return;
        _application.ProcessSessionFrame();
    }

    /// <summary>处理一次时钟滴答；会话分离或暂停时不执行任何操作。</summary>
    public void Tick()
    {
        if (!_attached || _suspended) return;
        _application.ProcessSessionTick();
    }

    /// <summary>暂停会话而不卸载文档。</summary>
    public void Suspend()
    {
        if (!_attached || _suspended) return;
        _suspended = true;
        _application.SuspendSession();
    }

    /// <summary>恢复会话并重置动画时间基线。</summary>
    public void Resume()
    {
        if (!_attached || !_suspended) return;
        _suspended = false;
        _application.ResumeSession();
    }

    /// <summary>在暂停期间释放失效的原生渲染目标所关联的上下文；恢复后重新创建。</summary>
    public void ReleaseRenderContext()
    {
        if (!_attached) return;
        if (!_suspended)
            throw new InvalidOperationException("Suspend the session before releasing its render context.");
        _application.ReleaseSessionRenderContext();
    }

    /// <summary>分离并释放会话拥有的全部资源。</summary>
    public void Detach()
    {
        if (_detached) return;
        _detached = true;
        DetachCore();
    }

    /// <inheritdoc/>
    public void Dispose() => Detach();

    private void DetachCore()
    {
        _attached = false;
        _suspended = false;
        var applicationOwnsHost = _application.HasSessionHost;
        try
        {
            _application.DetachSession();
            if (!applicationOwnsHost)
                _host.Dispose();
        }
        finally
        {
            if (_hotReloadRegistered)
            {
                SquareHotReloadHandler.Unregister(_application);
                _hotReloadRegistered = false;
            }

            if (_entered)
            {
                _entered = false;
                _application.ExitExternalRun();
            }
        }
    }
}
