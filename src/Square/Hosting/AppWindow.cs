using Square.CSS.Engine;
using Square.Graphics;
using Square.Platform;
using Square.Rendering;
using Square.Runtime;
using Square.Runtime.State;
using Square.UI;

namespace Square.Hosting;

/// <summary>A stable, dispatcher-aware facade over the native application window.</summary>
public sealed class AppWindow : IRenderBackendApplication
{
    private readonly object _gate = new();
    private Dispatcher _dispatcher;
    private IAppWindowRuntime? _runtime;
    private bool _applicationBound;
    private IPlatformHost? _host;
    private readonly UIDocument _document;
    private readonly int _initialWidth;
    private readonly int _initialHeight;
    private string _title;
    private Size _clientSize;
    private float _dpiScale = 1f;
    private AppWindowState _state;
    private bool _isClosed;
    private bool _closeRequested;
    private Action<object?>? _dialogCompletion;
    private object? _dialogResult;
    private bool _hasDialogResult;

    /// <summary>以指定标题和初始尺寸构造应用程序窗口。</summary>
    public AppWindow(string title, int width = 800, int height = 600)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        _title = title;
        _initialWidth = width;
        _initialHeight = height;
        _clientSize = new Size(width, height);
        _document = new UIDocument { Title = title };
        _document.AppWindow = this;
        _dispatcher = _document.Context.Dispatcher;
    }

    /// <summary>窗口标题。</summary>
    public string Title
    {
        get
        {
            lock (_gate) return _title;
        }
        set
        {
            value ??= "";
            lock (_gate) _title = value;
            _document.Title = value;
            Post(host => host.Title = value);
        }
    }

    /// <summary>窗口对应的 DOM 文档。</summary>
    public Document Document => _document;

    internal UIDocument WindowDocument => _document;

    /// <summary>窗口加载的内容根元素。</summary>
    public Element? Content { get; private set; }

    /// <summary>自定义标题栏元素。</summary>
    public UIElement? CustomTitleBar { get; private set; }

    /// <summary>标题栏样式。</summary>
    public TitleStyle TitleStyle { get; set; } = TitleStyle.System;

    /// <summary>边框样式。</summary>
    public BorderStyle BorderStyle { get; set; } = BorderStyle.Resizable;

    internal IntPtr OwnerHandle { get; set; }

    internal bool IsModal { get; set; }

    /// <summary>渲染后端名称。</summary>
    public string RenderBackend { get; set; } = "Software";

    /// <summary>软件渲染表面类型。</summary>
    public SoftwareRenderSurfaceKind SoftwareSurface { get; set; } = SoftwareRenderSurfaceKind.Auto;

    /// <summary>窗口背景色。</summary>
    public Color Background { get; set; } = Color.White;

    /// <summary>渲染模式。</summary>
    public RenderMode RenderingMode { get; set; } = RenderMode.FullFrame;

    /// <summary>允许的最大脏矩形数量。</summary>
    public int MaxDirtyRectCount { get; set; } = 16;

    /// <summary>触发全帧重绘的脏区面积比例上限。</summary>
    public float MaxDirtyAreaRatio { get; set; } = 0.35f;

    /// <summary>是否显示渲染诊断覆盖层。</summary>
    public bool ShowRenderDiagnosticsOverlay { get; set; }

    /// <summary>是否显示脏区合并矩形覆盖层。</summary>
    public bool ShowDirtyUnionOverlay { get; set; } = true;

    /// <summary>最近一次渲染的诊断信息。</summary>
    public RenderDiagnostics LastRenderDiagnostics { get; internal set; } =
        new(RenderMode.FullFrame, true, "NotRendered", 0, 0, Rect.Empty);

    /// <summary>窗口关联的调度器。</summary>
    public Dispatcher Dispatcher => _dispatcher;

    /// <summary>窗口关联的状态存储作用域。</summary>
    public StoreScope Stores => _document.Context.Stores;

    /// <summary>客户区尺寸。</summary>
    public Size ClientSize
    {
        get
        {
            lock (_gate) return _clientSize;
        }
    }

    /// <summary>DPI 缩放系数。</summary>
    public float DpiScale
    {
        get
        {
            lock (_gate) return _dpiScale;
        }
    }

    /// <summary>窗口状态。</summary>
    public AppWindowState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    /// <summary>窗口是否已关闭。</summary>
    public bool IsClosed
    {
        get
        {
            lock (_gate) return _isClosed;
        }
    }

    /// <summary>原生窗口句柄。</summary>
    public IntPtr NativeWindow
    {
        get
        {
            lock (_gate)
                return _host is IPlatformNativeWindow nativeWindow ? nativeWindow.Handle : IntPtr.Zero;
        }
    }

    /// <summary>客户区尺寸变化时触发。</summary>
    public event Action<Size>? SizeChanged;
    /// <summary>窗口状态变化时触发。</summary>
    public event Action<AppWindowState>? StateChanged;
    /// <summary>窗口关闭时触发。</summary>
    public event Action? Closed;
    /// <summary>全局按键事件。</summary>
    public event Action<int, KeyAction>? GlobalKeyEvent;

    /// <summary>加载内容根元素到窗口正文。</summary>
    public void Load(Element content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (_runtime?.IsRunning == true)
            throw new InvalidOperationException("Window content cannot be replaced while the application is running.");
        if (content.ParentNode != null && !ReferenceEquals(content, Content))
            throw new InvalidOperationException("Window content already has a parent.");
        if (ReferenceEquals(content, Content)) return;

        if (Content != null) _document.Body.Children.Remove(Content);
        Content = content;
        _document.Body.Children.Add(content);
    }

    /// <summary>按给定顺序加载一个或多个全局 CSS 文件。</summary>
    /// <param name="paths">CSS 文件路径；相对路径以应用程序基目录为准。</param>
    public void LoadGlobalCss(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureGlobalCssCanBeLoaded();
        foreach (var path in paths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            _document.LoadGlobalCss(path);
        }
    }

    /// <summary>按给定顺序加载一个或多个全局 CSS 源文本。</summary>
    /// <param name="css">CSS 源文本。</param>
    public void LoadGlobalCssText(params string[] css)
    {
        ArgumentNullException.ThrowIfNull(css);
        EnsureGlobalCssCanBeLoaded();
        foreach (var source in css)
        {
            ArgumentNullException.ThrowIfNull(source);
            _document.LoadGlobalCssText(source);
        }
    }

    /// <summary>加载自定义标题栏元素。</summary>
    public void LoadCustomTitleBar(UIElement titleBar)
    {
        ArgumentNullException.ThrowIfNull(titleBar);
        if (_runtime?.IsRunning == true)
            throw new InvalidOperationException("The custom title bar must be loaded before the application starts.");
        if (titleBar.ParentNode != null && !ReferenceEquals(titleBar, CustomTitleBar))
            throw new InvalidOperationException("The custom title bar already has a parent.");
        if (ReferenceEquals(titleBar, CustomTitleBar)) return;

        if (CustomTitleBar != null) _document.Head.Children.Remove(CustomTitleBar);
        CustomTitleBar = titleBar;
        _document.Head.Children.Add(titleBar);
        TitleStyle = TitleStyle.Custom;
    }

    /// <summary>请求关闭窗口。</summary>
    public void Close()
    {
        lock (_gate) _closeRequested = true;
        Post(static host => host.Close());
    }

    /// <summary>异步关闭窗口。</summary>
    public Task CloseAsync() => InvokeAsync(static host => host.Close());

    /// <summary>异步最小化窗口。</summary>
    public Task MinimizeAsync() => InvokeAsync(static host => host.Minimize());

    /// <summary>异步最大化窗口。</summary>
    public Task MaximizeAsync() => InvokeAsync(static host => host.Maximize());

    /// <summary>异步还原窗口。</summary>
    public Task RestoreAsync() => InvokeAsync(static host => host.Restore());

    /// <summary>异步开始窗口拖动。</summary>
    public Task BeginMoveAsync() => InvokeAsync(static host => host.BeginMove());

    /// <summary>异步读取系统剪贴板文本。</summary>
    public Task<string> GetClipboardTextAsync() => InvokeAsync(static host => host.GetClipboardText());

    /// <summary>异步写入系统剪贴板文本。</summary>
    public Task SetClipboardTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return InvokeAsync(host => host.SetClipboardText(text));
    }

    /// <summary>打开非模态子窗口显示指定内容。</summary>
    public void Open(Element content, Size? size = null, UIElement? customTitleBar = null)
    {
        var child = CreateChildWindow(content, customTitleBar, size, isModal: false);
        StartChildWindow(child, failure: null);
    }

    /// <summary>打开模态对话框并返回其结果。</summary>
    public Task<object?> OpenDialog(Element content, Size? size = null, UIElement? customTitleBar = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var child = CreateChildWindow(content, customTitleBar, size, isModal: true);
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        child._dialogCompletion = value => completion.TrySetResult(value);
        StartChildWindow(child, exception => completion.TrySetException(exception));
        return completion.Task;
    }

    /// <summary>打开模态对话框并返回强类型结果。</summary>
    public async Task<T?> OpenDialog<T>(Element content, Size? size = null, UIElement? customTitleBar = null)
    {
        var result = await OpenDialog(content, size, customTitleBar).ConfigureAwait(false);
        if (result == null) return default;
        if (result is T typedResult) return typedResult;
        throw new InvalidOperationException(
            $"The dialog result is '{result.GetType().FullName}', not '{typeof(T).FullName}'.");
    }

    /// <summary>关闭模态对话框并返回结果。</summary>
    public void CloseDialog<T>(T result)
    {
        lock (_gate)
        {
            if (_dialogCompletion == null)
                throw new InvalidOperationException("This window was not opened as a modal dialog.");
            _dialogResult = result;
            _hasDialogResult = true;
        }
        Close();
    }

    /// <summary>最小化窗口。</summary>
    public void Minimize() => Post(static host => host.Minimize());

    /// <summary>最大化窗口。</summary>
    public void Maximize() => Post(static host => host.Maximize());

    /// <summary>还原窗口。</summary>
    public void Restore() => Post(static host => host.Restore());

    /// <summary>请求重新渲染窗口。</summary>
    public void RequestRender() => RequireRuntime().RequestRender();

    /// <summary>元素失效时请求渲染；应用绑定前保持无操作。</summary>
    internal void RequestRenderIfBound() => _runtime?.RequestRender();

    /// <summary>注入 DevTools 指针事件。</summary>
    public Task InjectPointerAsync(DevToolsPointerInput input) => RequireRuntime().InjectPointerAsync(input);

    /// <summary>注入 DevTools 按键事件。</summary>
    public Task InjectKeyAsync(DevToolsKeyInput input) => RequireRuntime().InjectKeyAsync(input);

    /// <summary>注入 DevTools 文本输入。</summary>
    public Task InjectTextAsync(string text) => RequireRuntime().InjectTextAsync(text);

    /// <summary>注入 DevTools 滚轮事件。</summary>
    public Task InjectWheelAsync(DevToolsWheelInput input) => RequireRuntime().InjectWheelAsync(input);

    /// <summary>捕获当前渲染器位图。</summary>
    public Task<Bitmap> CaptureRendererBitmapAsync() => RequireRuntime().CaptureRendererBitmapAsync();

    /// <summary>捕获元素检查快照。</summary>
    public Task<ElementInspectionSnapshot> CaptureInspectionSnapshotAsync(
        bool includeSourcePaths = true,
        bool includeTextContent = true) =>
        RequireRuntime().CaptureInspectionSnapshotAsync(includeSourcePaths, includeTextContent);

    /// <summary>按调试 ID 检查元素。</summary>
    public Task<ElementInspectionNode?> InspectElementAsync(
        int debugId,
        bool includeSourcePaths = true,
        bool includeTextContent = true) =>
        RequireRuntime().InspectElementAsync(debugId, includeSourcePaths, includeTextContent);

    /// <summary>按命中测试检查元素。</summary>
    public Task<ElementInspectionNode?> HitTestInspectionAsync(
        Point point,
        bool includeSourcePaths = true,
        bool includeTextContent = true) =>
        RequireRuntime().HitTestInspectionAsync(point, includeSourcePaths, includeTextContent);

    internal PlatformHostCreateInfo CreateHostInfo() => new()
    {
        Title = Title,
        Width = _initialWidth,
        Height = _initialHeight,
        RenderBackend = RenderBackend,
        SoftwareSurface = SoftwareSurface,
        TitleStyle = TitleStyle,
        BorderStyle = BorderStyle,
        OwnerHandle = OwnerHandle,
        IsModal = IsModal
    };

    internal void RegisterGlobalCssScope(Element root)
    {
        if (_document.StyleSheets.Count > 0)
            CssStyleReconciler.RegisterScope(_document.GlobalCssEngine, root);
    }

    private void EnsureGlobalCssCanBeLoaded()
    {
        if (_runtime?.IsRunning == true)
            throw new InvalidOperationException("Global CSS must be loaded before the application starts.");
    }

    private AppWindow CreateChildWindow(Element content, UIElement? customTitleBar, Size? size, bool isModal)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.ParentNode != null)
            throw new InvalidOperationException("Window content already has a parent.");
        if (customTitleBar?.ParentNode != null)
            throw new InvalidOperationException("The custom title bar already has a parent.");

        var ownerHost = GetHost() ?? throw new InvalidOperationException(
            "A child window can only be opened while the owner window is running.");
        if (ownerHost is not IPlatformNativeWindow { Handle: not 0 } nativeOwner)
            throw new PlatformNotSupportedException("The current platform host does not expose a native window handle.");

        var requestedSize = size ?? new Size(480, 320);
        if (requestedSize.Width <= 0 || requestedSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Window width and height must be greater than zero.");

        var child = new AppWindow(content.TagName, checked((int)requestedSize.Width), checked((int)requestedSize.Height))
        {
            RenderBackend = RenderBackend,
            Background = Background,
            RenderingMode = RenderingMode,
            BorderStyle = BorderStyle,
            OwnerHandle = nativeOwner.Handle,
            IsModal = isModal
        };
        child.Load(content);
        if (customTitleBar != null) child.LoadCustomTitleBar(customTitleBar);
        return child;
    }

    private static void StartChildWindow(AppWindow child, Action<Exception>? failure)
    {
        var thread = new Thread(() =>
        {
            try
            {
                var application = new DesktopApplication(child);
                application.Run();
                Action<object?>? dialogCompletion;
                lock (child._gate)
                {
                    dialogCompletion = child._dialogCompletion;
                    child._dialogCompletion = null;
                }
                dialogCompletion?.Invoke(child._hasDialogResult ? child._dialogResult : null);
            }
            catch (Exception exception)
            {
                failure?.Invoke(exception);
            }
        })
        {
            IsBackground = true,
            Name = $"Square window: {child.Title}"
        };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    internal void BindApplication(Dispatcher dispatcher, IAppWindowRuntime runtime)
    {
        if (_applicationBound)
            throw new InvalidOperationException("The AppWindow is already bound to a DesktopApplication.");
        _applicationBound = true;
        _dispatcher = dispatcher;
        _runtime = runtime;
        _document.Context.Dispatcher = dispatcher;
    }

    internal void Attach(IPlatformHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        bool closeRequested;
        lock (_gate)
        {
            _host = host;
            _isClosed = false;
            _clientSize = host.ClientSize;
            _dpiScale = host.DpiScale;
            _state = host.State;
            host.Title = _title;
            closeRequested = _closeRequested;
        }

        host.SizeChanged += HandleSizeChanged;
        host.StateChanged += HandleStateChanged;
        host.Closed += HandleClosed;
        if (closeRequested) host.Close();
    }

    internal void Detach(IPlatformHost host)
    {
        host.SizeChanged -= HandleSizeChanged;
        host.StateChanged -= HandleStateChanged;
        host.Closed -= HandleClosed;
        var raiseClosed = false;
        lock (_gate)
        {
            if (ReferenceEquals(_host, host)) _host = null;
            if (!_isClosed)
            {
                _isClosed = true;
                raiseClosed = true;
            }
        }

        if (raiseClosed) Closed?.Invoke();
    }

    internal void SynchronizeTitle(string title)
    {
        lock (_gate) _title = title ?? "";
    }

    internal void RaiseGlobalKeyEvent(int keyCode, KeyAction action) =>
        GlobalKeyEvent?.Invoke(keyCode, action);

    private void HandleSizeChanged(Size size)
    {
        lock (_gate)
        {
            _clientSize = size;
            if (_host != null) _dpiScale = _host.DpiScale;
        }

        SizeChanged?.Invoke(size);
    }

    private void HandleStateChanged(AppWindowState state)
    {
        lock (_gate) _state = state;
        StateChanged?.Invoke(state);
    }

    private void HandleClosed()
    {
        lock (_gate)
        {
            if (_isClosed) return;
            _isClosed = true;
            _host = null;
        }

        Closed?.Invoke();
    }

    private void Post(Action<IPlatformHost> action)
    {
        if (_dispatcher.CheckAccess())
        {
            if (GetHost() is { } host) action(host);
            return;
        }

        _dispatcher.Invoke(() =>
        {
            if (GetHost() is { } host) action(host);
        });
    }

    private Task InvokeAsync(Action<IPlatformHost> action)
    {
        return _dispatcher.InvokeAsync(() =>
        {
            var host = GetHost();
            if (host == null)
                throw new InvalidOperationException("The native application window is not available.");
            action(host);
        });
    }

    private Task<T> InvokeAsync<T>(Func<IPlatformHost, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_dispatcher.CheckAccess())
        {
            try
            {
                var host = GetHost();
                if (host == null)
                    throw new InvalidOperationException("The native application window is not available.");
                return Task.FromResult(action(host));
            }
            catch (Exception exception)
            {
                return Task.FromException<T>(exception);
            }
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Invoke(() =>
        {
            try
            {
                var host = GetHost();
                if (host == null)
                    throw new InvalidOperationException("The native application window is not available.");
                completion.SetResult(action(host));
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    private IPlatformHost? GetHost()
    {
        lock (_gate) return _isClosed ? null : _host;
    }

    private IAppWindowRuntime RequireRuntime() =>
        _runtime ?? throw new InvalidOperationException("The AppWindow is not bound to a DesktopApplication.");
}

/// <summary>标题栏样式。</summary>
public enum TitleStyle
{
    /// <summary>使用系统默认标题栏。</summary>
    System,
    /// <summary>隐藏标题栏。</summary>
    Hidden,
    /// <summary>使用自定义标题栏。</summary>
    Custom
}

/// <summary>窗口边框样式。</summary>
public enum BorderStyle
{
    /// <summary>可调整大小的边框。</summary>
    Resizable,
    /// <summary>固定边框。</summary>
    Fixed,
    /// <summary>无边框。</summary>
    None
}
