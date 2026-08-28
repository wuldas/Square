using System.Diagnostics;
using Square.Backends;
using Square.Controls;
using Square.CSS.Engine;
using Square.Events;
using Square.Graphics;
using Square.Rendering;
using Square.Platform;
using Square.Runtime;
using Square.UI;
using Reconciler = Square.UI.Reconciler;

namespace Square.Hosting;

/// <summary>桌面平台应用程序：驱动平台宿主、布局、渲染与输入分发。</summary>
public sealed class DesktopApplication : Application, IAppWindowRuntime
{
    private static readonly Color DefaultSelectionBackground = Color.FromRgb(51, 144, 255);
    private static readonly Color DefaultSelectionForeground = Color.White;

    private readonly UIDocument _document;
    private readonly Element _root;
    private readonly PlatformHostCreateInfo _hostCreateInfo;
    private readonly LayoutEngine _layout = new();
    private readonly DisplayTree _displayTree = new();
    private readonly Dictionary<Element, double> _scheduledFrames = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastAnimationTickSeconds;
    private IPlatformHost? _host;
    private IRenderContext? _renderContext;
    private UIElement? _focusedInput;
    private ITextEditor? _focusedEditor;
    private TextSelectionState? _textSelection;
    private Point? _pendingTextSelectionPoint;
    private Element? _pendingTextSelectionHit;
    private Rect _textSelectionOverlayDirtyBounds = Rect.Empty;
    private bool _isSelectingText;
    private Element? _pointerDownTarget;
    private Splitter? _draggingSplitter;
    private Point? _pendingSplitterPoint;
    private Element? _lastClickTarget;
    private Point _lastClickPoint;
    private double _lastClickSeconds = double.NegativeInfinity;
    private readonly List<UIElement> _hoverPath = [];
    private readonly List<UIElement> _activePath = [];
    private readonly TooltipPopup _tooltipPopup;
    private UIElement? _tooltipTarget;
    private bool _renderRequested;
    private KeyModifiers? _devToolsModifiers;
    private int? _inspectorHighlightDebugId;
    private bool _inspectorModeEnabled;
    private bool _inspectorOverlayDirty;

    /// <summary>主窗口。</summary>
    public AppWindow MainWindow { get; }

    /// <summary>Inspector 点选元素时触发。</summary>
    public event Action<int>? InspectorNodeSelected;
    /// <summary>基于现有窗口构造桌面应用程序。</summary>
    public DesktopApplication(AppWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        MainWindow = window;
        _document = window.WindowDocument;
        _root = _document.DocumentElement;
        _hostCreateInfo = window.CreateHostInfo();
        window.BindApplication(Dispatcher, this);
        InspectorNodeSelected += window.RaiseInspectorNodeSelected;
        _tooltipPopup = new TooltipPopup();
        _document.Body.Children.Add(_tooltipPopup);
    }

    /// <summary>兼容构造函数：直接由内容根构造单窗口应用程序。</summary>
    [Obsolete("Create an AppWindow, call Load(content), then pass it to DesktopApplication.")]
    public DesktopApplication(Element contentRoot, PlatformHostCreateInfo hostCreateInfo)
        : this(CreateWindow(contentRoot, hostCreateInfo))
    {
    }

    /// <summary>主窗口对应的文档。</summary>
    public Document Document => _document;

    /// <summary>渲染后端名称（已废弃，请使用 <see cref="AppWindow.RenderBackend"/>）。</summary>
    [Obsolete("Use MainWindow.RenderBackend.")]
    public string RenderBackend
    {
        get => MainWindow.RenderBackend;
        set
        {
            if (IsRunning)
                throw new InvalidOperationException(
                    "The render backend cannot be changed while the application is running.");
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            MainWindow.RenderBackend = value;
            _hostCreateInfo.RenderBackend = value;
        }
    }

    /// <summary>窗口背景色（已废弃，请使用 <see cref="AppWindow.Background"/>）。</summary>
    [Obsolete("Use MainWindow.Background.")]
    public Color Background
    {
        get => MainWindow.Background;
        set => MainWindow.Background = value;
    }

    /// <summary>渲染模式（已废弃，请使用 <see cref="AppWindow.RenderingMode"/>）。</summary>
    [Obsolete("Use MainWindow.RenderingMode.")]
    public RenderMode RenderingMode
    {
        get => MainWindow.RenderingMode;
        set => MainWindow.RenderingMode = value;
    }

    /// <summary>最大脏矩形数量（已废弃，请使用 <see cref="AppWindow.MaxDirtyRectCount"/>）。</summary>
    [Obsolete("Use MainWindow.MaxDirtyRectCount.")]
    public int MaxDirtyRectCount
    {
        get => MainWindow.MaxDirtyRectCount;
        set => MainWindow.MaxDirtyRectCount = value;
    }

    /// <summary>触发全帧重绘的脏区面积比例上限（已废弃，请使用 <see cref="AppWindow.MaxDirtyAreaRatio"/>）。</summary>
    [Obsolete("Use MainWindow.MaxDirtyAreaRatio.")]
    public float MaxDirtyAreaRatio
    {
        get => MainWindow.MaxDirtyAreaRatio;
        set => MainWindow.MaxDirtyAreaRatio = value;
    }

    /// <summary>是否显示渲染诊断覆盖层（已废弃，请使用 <see cref="AppWindow.ShowRenderDiagnosticsOverlay"/>）。</summary>
    [Obsolete("Use MainWindow.ShowRenderDiagnosticsOverlay.")]
    public bool ShowRenderDiagnosticsOverlay
    {
        get => MainWindow.ShowRenderDiagnosticsOverlay;
        set => MainWindow.ShowRenderDiagnosticsOverlay = value;
    }

    /// <summary>是否显示脏区合并矩形覆盖层（已废弃，请使用 <see cref="AppWindow.ShowDirtyUnionOverlay"/>）。</summary>
    [Obsolete("Use MainWindow.ShowDirtyUnionOverlay.")]
    public bool ShowDirtyUnionOverlay
    {
        get => MainWindow.ShowDirtyUnionOverlay;
        set => MainWindow.ShowDirtyUnionOverlay = value;
    }

    /// <summary>最近一次渲染诊断信息（已废弃，请使用 <see cref="AppWindow.LastRenderDiagnostics"/>）。</summary>
    [Obsolete("Use MainWindow.LastRenderDiagnostics.")]
    public RenderDiagnostics LastRenderDiagnostics => MainWindow.LastRenderDiagnostics;

    /// <summary>全局按键事件（已废弃，请使用 <see cref="AppWindow.GlobalKeyEvent"/>）。</summary>
    [Obsolete("Use MainWindow.GlobalKeyEvent.")]
    public event Action<int, KeyAction>? GlobalKeyEvent
    {
        add => MainWindow.GlobalKeyEvent += value;
        remove => MainWindow.GlobalKeyEvent -= value;
    }

    private static AppWindow CreateWindow(Element contentRoot, PlatformHostCreateInfo hostCreateInfo)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);
        ArgumentNullException.ThrowIfNull(hostCreateInfo);
        var window = new AppWindow(hostCreateInfo.Title, hostCreateInfo.Width, hostCreateInfo.Height)
        {
            RenderBackend = hostCreateInfo.RenderBackend,
            TitleStyle = hostCreateInfo.TitleStyle,
            BorderStyle = hostCreateInfo.BorderStyle
        };
        window.Load(contentRoot);
        return window;
    }

    protected override void RunCore()
    {
        BackendRegistration.RegisterDefaults();
        Square.Controls.ControlRegistration.RegisterDefaults();

        MainWindow.RegisterGlobalCssScope(_root);
        _document.Build();
        CssStyleReconciler.ReapplyScopesToTree(_root);
        var lifecycle = (IComponentLifecycle)_root;
        // 先注册帧调度，再 OnAttached：组件在 OnAttached 里 RequestAnimationFrame 才能被调度
        _root.AddEventListener(StandardEvents.RequestFrame, HandleFrameRequest);
        _document.AddEventListener(StandardEvents.RequestFrame, HandleFrameRequest);
        lifecycle.OnAttached();
        try
        {
            _hostCreateInfo.Title = MainWindow.Title;
            _hostCreateInfo.RenderBackend = MainWindow.RenderBackend;
            _hostCreateInfo.TitleStyle = MainWindow.TitleStyle;
            _hostCreateInfo.BorderStyle = MainWindow.BorderStyle;
            _host = PlatformRegistry.Get().CreateHost(_hostCreateInfo);
            MainWindow.Attach(_host);
            AttachHostEvents(_host);

            _host.Show();
            _renderContext = _host.CreateRenderContext();
            lifecycle.OnLoaded();
            RenderFrame();
            _host.ShowAfterFirstFrame();
            _host.PumpEvents();
        }
        finally
        {
            _root.RemoveEventListener(StandardEvents.RequestFrame, HandleFrameRequest);
            _document.RemoveEventListener(StandardEvents.RequestFrame, HandleFrameRequest);
            _scheduledFrames.Clear();
            if (_root.IsLoaded) lifecycle.OnUnloaded();
            lifecycle.OnDetached();
            CssStyleReconciler.UnregisterScopesForTree(_root);
            _renderContext?.Dispose();
            _renderContext = null;
            if (_host != null) MainWindow.Detach(_host);
            _host?.Dispose();
            _host = null;
        }
    }

    private void AttachHostEvents(IPlatformHost host)
    {
        host.SizeChanged += _ => RenderFrame();
        host.WheelEvent += HandleWheel;
        host.MouseEvent += HandleMouse;
        host.KeyEvent += HandleKey;
        host.TextInput += HandleTextInput;
        host.Tick += HandleTick;
    }

    private void HandleFrameRequest(Event e)
    {
        // Target 为派发源；不要用 CurrentTarget（冒泡到 root 时已是 root）。
        // 只登记到期时间，不立刻 _renderRequested——否则会在每个 WM_TIMER(16ms)
        // 都做全窗口软件 Clear+Present，动画 CPU 极高。
        if (e is FrameRequestEvent args && e.Target is Element { IsAttached: true, IsEffectivelyVisible: true } target)
        {
            var requestedTime = _clock.Elapsed.TotalSeconds + args.IntervalSeconds;
            if (!_scheduledFrames.TryGetValue(target, out var current) || requestedTime < current)
                _scheduledFrames[target] = requestedTime;
        }

        e.StopPropagation();
    }

    /// <inheritdoc/>
    public void RequestRender() => Volatile.Write(ref _renderRequested, true);

    /// <inheritdoc/>
    public void Close()
    {
        MainWindow.Close();
    }

    /// <inheritdoc/>
    public Task InjectPointerAsync(DevToolsPointerInput input) => Dispatcher.InvokeAsync(() =>
    {
        WithDevToolsModifiers(input.Modifiers, () => HandleMouse(input.Position, input.Action, input.Button));
    });

    /// <inheritdoc/>
    public Task InjectKeyAsync(DevToolsKeyInput input) => Dispatcher.InvokeAsync(() =>
    {
        WithDevToolsModifiers(input.Modifiers, () => HandleKey(input.KeyCode, input.Action));
    });

    /// <inheritdoc/>
    public Task InjectTextAsync(string text) => Dispatcher.InvokeAsync(() => HandleTextInput(text ?? ""));

    /// <inheritdoc/>
    public Task InjectWheelAsync(DevToolsWheelInput input) => Dispatcher.InvokeAsync(() =>
    {
        WithDevToolsModifiers(input.Modifiers, () => HandleWheel(input.Position, input.Delta));
    });

    /// <inheritdoc/>
    public Task<Bitmap> CaptureRendererBitmapAsync()
    {
        var completion = new TaskCompletionSource<Bitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.Invoke(() =>
        {
            try
            {
                if (_host == null || _renderContext == null)
                    throw new InvalidOperationException(
                        "The application must be running before renderer capture is available.");

                // Prefer the live frame from the active render context. For GPU backends
                // (e.g. Vulkan) this reads back the actual presented frame, so the capture
                // reflects real GPU output instead of a software re-render — which is what
                // makes GPU-side rendering bugs visible in DevTools screenshots.
                if (_renderContext is IRenderBitmapSource { IsCaptureAvailable: true } liveSource)
                {
                    completion.SetResult(liveSource.CaptureBitmap());
                    return;
                }

                // Fallback: re-render the display tree into a software capture context.
                using var captureContext = new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
                {
                    CanvasSize = _host.ClientSize,
                    DpiScale = _host.DpiScale
                });
                captureContext.Clear(MainWindow.Background);
                _displayTree.Render(captureContext);
                RenderTextSelection(captureContext);
                RenderDiagnosticsOverlay(captureContext);
                captureContext.Flush();
                completion.SetResult(((IRenderBitmapSource)captureContext).CaptureBitmap());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    /// <inheritdoc/>
    public Task<ElementInspectionSnapshot> CaptureInspectionSnapshotAsync(bool includeSourcePaths = true,
        bool includeTextContent = true) =>
        InvokeOnDispatcherAsync(() =>
            new ElementInspectionSnapshot(CreateInspectionNode(_root, includeSourcePaths, includeTextContent,
                includeChildren: true)));

    /// <inheritdoc/>
    public Task<ElementInspectionNode?> InspectElementAsync(int debugId, bool includeSourcePaths = true,
        bool includeTextContent = true) =>
        InvokeOnDispatcherAsync(() => FindElementByDebugId(_root, debugId) is { } element
            ? CreateInspectionNode(element, includeSourcePaths, includeTextContent, includeChildren: true)
            : null);

    /// <inheritdoc/>
    public Task<ElementInspectionStyleSnapshot?> InspectElementStylesAsync(int debugId) =>
        InvokeOnDispatcherAsync(() => FindElementByDebugId(_root, debugId) is { } element
            ? new ElementInspectionStyleSnapshot(
                element.Style.GetAll(),
                element.Style.CssText,
                CssStyleReconciler.GetMatchedRules(element)
                    .Select(rule => new ElementInspectionStyleRule(
                        rule.Selector,
                        rule.Declarations
                            .Select(declaration => new ElementInspectionStyleDeclaration(
                                declaration.Property,
                                declaration.Value,
                                declaration.Important))
                            .ToArray()))
                    .ToArray())
            : null);

    /// <inheritdoc/>
    public Task<bool> SetInspectorHighlightAsync(int debugId) =>
        InvokeOnDispatcherAsync(() =>
        {
            if (FindElementByDebugId(_root, debugId) == null) return false;
            _inspectorHighlightDebugId = debugId;
            _inspectorOverlayDirty = true;
            RequestRender();
            return true;
        });

    /// <inheritdoc/>
    public Task ClearInspectorHighlightAsync() =>
        InvokeOnDispatcherAsync(() =>
        {
            if (_inspectorHighlightDebugId.HasValue)
            {
                _inspectorHighlightDebugId = null;
                _inspectorOverlayDirty = true;
                RequestRender();
            }
        });

    /// <inheritdoc/>
    public Task SetInspectorModeAsync(bool enabled) =>
        InvokeOnDispatcherAsync(() =>
        {
            _inspectorModeEnabled = enabled;
            if (!enabled) _inspectorHighlightDebugId = null;
            _inspectorOverlayDirty = true;
            RequestRender();
        });

    /// <inheritdoc/>
    public Task<ElementInspectionNode?> HitTestInspectionAsync(Point point, bool includeSourcePaths = true,
        bool includeTextContent = true) =>
        InvokeOnDispatcherAsync(() => HitTest(point) is { } element
            ? CreateInspectionNode(element, includeSourcePaths, includeTextContent, includeChildren: false)
            : null);

    private void RenderFrame()
    {
        FlushPendingSplitterMove();
        var hadPendingTextSelection = _pendingTextSelectionPoint.HasValue;
        var textSelectionChanged = FlushPendingTextSelection();
        var textSelectionOverlayDirtyBounds = _textSelectionOverlayDirtyBounds;
        _textSelectionOverlayDirtyBounds = Rect.Empty;
        Volatile.Write(ref _renderRequested, false);
        if (_host == null || _renderContext == null) return;

        RunUpdatePass();

        if (!string.IsNullOrEmpty(_document.Title) && _hostCreateInfo.Title != _document.Title)
        {
            _hostCreateInfo.Title = _document.Title;
            _host.Title = _document.Title;
            MainWindow.SynchronizeTitle(_document.Title);
        }

        var size = _host.ClientSize;
        var layoutDirty = _root.IsLayoutDirty || !AreLayoutSizesEquivalent(_root.Geometry.Size, size);
        if (layoutDirty)
        {
            _layout.MeasureAndArrange(_root, size);
            ArrangeWindowShell(size);
        }
        if (layoutDirty)
            _displayTree.Synchronize(_root);
        _displayTree.UpdateDirty();
        NativeViewSynchronizer.Synchronize(_root, _host.DpiScale);

        if (MainWindow.RenderingMode == RenderMode.FullFrame || layoutDirty || _inspectorOverlayDirty ||
            !_renderContext.SupportsPartialRendering)
        {
            MainWindow.LastRenderDiagnostics = new RenderDiagnostics(
                MainWindow.RenderingMode,
                true,
                layoutDirty ? "LayoutDirty" : !_renderContext.SupportsPartialRendering ? "BackendFullFrame" : "ModeFullFrame",
                0,
                1f,
                new Rect(0, 0, size.Width, size.Height));
            RenderFullFrame();
        }
        else
        {
            var dirty = _displayTree.CollectDirtyRects();
            if (!textSelectionOverlayDirtyBounds.IsEmpty)
                dirty.Add(textSelectionOverlayDirtyBounds);
            if (dirty.Count == 0)
            {
                if (hadPendingTextSelection && !textSelectionChanged)
                {
                    if (_focusedEditor != null)
                        _host.SetTextInputRect(MapContentRectToScreen(_focusedInput, _focusedEditor.CaretRect));
                    return;
                }
                // 无节点标脏时仍全量重绘一帧，避免“状态已变但未 InvalidatePaint”时界面卡住
                // （与脏区优化前“每次 RenderFrame 都清屏重放命令”的行为对齐）
                MainWindow.LastRenderDiagnostics = new RenderDiagnostics(
                    MainWindow.RenderingMode,
                    true,
                    "NoDirtyRects",
                    0,
                    1f,
                    new Rect(0, 0, size.Width, size.Height));
                RenderFullFrame();
            }
            else
            {
                MainWindow.LastRenderDiagnostics = RenderDecision.Decide(
                    MainWindow.RenderingMode,
                    dirty,
                    size,
                    MainWindow.MaxDirtyRectCount,
                    MainWindow.MaxDirtyAreaRatio);

                if (MainWindow.LastRenderDiagnostics.UsedFullFrame)
                {
                    RenderFullFrame();
                }
                else
                {
                    foreach (var dirtyRect in dirty)
                        _renderContext.Clear(MainWindow.Background, dirtyRect);
                    _displayTree.Render(_renderContext, dirty);
                    foreach (var dirtyRect in dirty)
                    {
                        _renderContext.PushClip(dirtyRect);
                        RenderTextSelection(_renderContext);
                        _renderContext.PopClip();
                    }
                    RenderInspectorOverlay(_renderContext);
                    RenderDiagnosticsOverlay(_renderContext);
                    _renderContext.Flush();
                    _renderContext.Present(MainWindow.ShowRenderDiagnosticsOverlay
                        ? null
                        : dirty);
                }
            }
        }

        if (_focusedEditor != null)
            _host.SetTextInputRect(MapContentRectToScreen(_focusedInput, _focusedEditor.CaretRect));
    }

    private ElementInspectionNode CreateInspectionNode(Element element, bool includeSourcePaths,
        bool includeTextContent, bool includeChildren)
    {
        var children = includeChildren
            ? element.Children.Select(child =>
                CreateInspectionNode(child, includeSourcePaths, includeTextContent, includeChildren: true)).ToArray()
            : [];
        return new ElementInspectionNode(
            element.DebugId,
            element.DebugInfo?.TagName ?? element.TagName,
            element.Id,
            element.DebugInfo?.ComponentName,
            element.Geometry,
            new ElementInspectionState(
                element.HasState(ElementState.Hover),
                element.HasState(ElementState.Focus),
                element.HasState(ElementState.Active),
                element.HasState(ElementState.Disabled)),
            CreateInspectionSource(element.DebugInfo, includeSourcePaths),
            includeTextContent ? ReadElementText(element) : null,
            element.Children.Count,
            children,
            element.ClassList.GetAll().OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
            CreateInspectionBoxModel(element));
    }

    private ElementInspectionBoxModel? CreateInspectionBoxModel(Element element)
    {
        var box = _layout.GetInspectionBoxModel(element);
        return box is { } value
            ? new ElementInspectionBoxModel(value.Content, value.Padding, value.Border, value.Margin)
            : null;
    }

    private Task InvokeOnDispatcherAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.Invoke(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    private Task<T> InvokeOnDispatcherAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.Invoke(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    private static string? ReadElementText(Element element)
    {
        var textContent = element.GetProperty<string>("TextContent");
        return string.IsNullOrEmpty(textContent) ? null : textContent;
    }

    private static ElementInspectionSource? CreateInspectionSource(ElementDebugInfo? debugInfo, bool includeSourcePaths)
    {
        if (debugInfo == null) return null;
        return new ElementInspectionSource(
            debugInfo.SourceId,
            includeSourcePaths ? debugInfo.SourcePath : null,
            debugInfo.StartLine,
            debugInfo.StartColumn,
            debugInfo.EndLine,
            debugInfo.EndColumn,
            debugInfo.Kind.ToString());
    }

    private static Element? FindElementByDebugId(Element element, int debugId)
    {
        if (element.DebugId == debugId) return element;
        foreach (var child in element.Children)
        {
            var found = FindElementByDebugId(child, debugId);
            if (found != null) return found;
        }

        return null;
    }

    private bool RunUpdatePass()
    {
        var hadWork = Dispatcher.HasWork || _document.Context.Reconciler.HasWork;
        Dispatcher.Run();
        if (_document.Context.Reconciler.HasWork)
        {
            hadWork = true;
            _document.Context.Reconciler.Flush();
        }

        if (CssStyleReconciler.HasWorkForTree(_root))
        {
            hadWork = true;
            CssStyleReconciler.Flush(_root);
        }

        return hadWork;
    }

    private Element? HitTest(Point point) =>
        _displayTree.HitTestPopups(point) ?? _displayTree.HitTestFixed(point) ?? _displayTree.HitTestRoot(point);

    private static bool AreLayoutSizesEquivalent(Size actual, Size requested) =>
        MathF.Abs(actual.Width - requested.Width) < 0.5f &&
        MathF.Abs(actual.Height - requested.Height) < 0.5f;

    private void ArrangeWindowShell(Size size)
    {
        var titleHeight = 0f;
        if (MainWindow.TitleStyle == TitleStyle.Custom && MainWindow.CustomTitleBar != null)
        {
            var titleBar = MainWindow.CustomTitleBar.Query<TitleBar>() ?? MainWindow.CustomTitleBar;
            titleHeight = Math.Clamp(
                titleBar.Measure(new Size(size.Width, size.Height)).Height,
                0,
                size.Height);
            var titleBounds = new Rect(0, 0, size.Width, titleHeight);
            _layout.Measure(_document.Head, titleBounds.Size);
            _layout.Arrange(_document.Head, titleBounds);
        }
        else
        {
            _document.Head.Geometry = new Rect(0, 0, size.Width, 0);
        }

        var bodyBounds = new Rect(
            0,
            titleHeight,
            size.Width,
            Math.Max(0, size.Height - titleHeight));
        _layout.Measure(_document.Body, bodyBounds.Size);
        _layout.Arrange(_document.Body, bodyBounds);
    }

    private void RenderFullFrame()
    {
        if (_renderContext == null) return;
        _renderContext.Clear(MainWindow.Background);
        _displayTree.Render(_renderContext);
        RenderTextSelection(_renderContext);
        RenderInspectorOverlay(_renderContext);
        RenderDiagnosticsOverlay(_renderContext);
        _renderContext.Flush();
        _renderContext.Present(null);
        _inspectorOverlayDirty = false;
    }


    private void RenderInspectorOverlay(IRenderContext context)
    {
        if (!_inspectorHighlightDebugId.HasValue) return;
        var element = FindElementByDebugId(_root, _inspectorHighlightDebugId.Value);
        if (element == null)
        {
            _inspectorHighlightDebugId = null;
            return;
        }

        var bounds = element.Geometry;
        if (bounds.IsEmpty) return;
        context.FillRect(bounds, new SolidColorBrush(Color.FromRgba(51, 144, 255, 48)));
        context.DrawRect(bounds, Pen.FromColor(Color.FromRgba(51, 144, 255, 220), 2));
    }

    private void RenderDiagnosticsOverlay(IRenderContext context)
    {
        if (!MainWindow.ShowRenderDiagnosticsOverlay) return;

        var diagnostics = MainWindow.LastRenderDiagnostics;
        var panel = new Rect(8, 8, 300, 86);
        context.FillRect(panel, new SolidColorBrush(Color.FromRgba(20, 24, 28, 220)));
        context.DrawRect(panel, Pen.FromColor(Color.FromRgb(80, 180, 255)));

        DrawOverlayText(context, $"mode: {diagnostics.Mode} / {(diagnostics.UsedFullFrame ? "full" : "dirty")}", 16,
            16);
        DrawOverlayText(context, $"reason: {diagnostics.Reason}", 16, 34);
        DrawOverlayText(context, $"dirty: {diagnostics.DirtyRectCount}, area: {diagnostics.DirtyAreaRatio:P1}", 16, 52);
        DrawOverlayText(context, $"union: {FormatRect(diagnostics.DirtyUnion)}", 16, 70);

        if (MainWindow.ShowDirtyUnionOverlay && !diagnostics.DirtyUnion.IsEmpty)
            context.DrawRect(diagnostics.DirtyUnion, Pen.FromColor(Color.FromRgba(255, 64, 64, 220), 2));
    }

    private static void DrawOverlayText(IRenderContext context, string text, float x, float y)
    {
        context.DrawText(
            new TextLayout(text, new Font("Segoe UI", 12)),
            new Point(x, y),
            new SolidColorBrush(Color.White));
    }

    private static string FormatRect(Rect rect) => rect.IsEmpty
        ? "empty"
        : $"{rect.X:0},{rect.Y:0} {rect.Width:0}x{rect.Height:0}";

    private void HandleWheel(Point point, int delta)
    {
        var hit = HitTest(point);
        UpdateHoverPath(hit);
        hit?.DispatchTrusted(StandardEvents.CreateWheel(0, -delta));
        RenderFrame();
    }

    private void HandleMouse(Point point, MouseAction action, MouseButton button = MouseButton.Left)
    {
        if (_host == null) return;

        if (button == MouseButton.Right)
        {
            if (action == MouseAction.Down && _displayTree.DismissPopupsOutside(point)) RequestRender();
            var contextTarget = HitTest(point);
            UpdateHoverPath(contextTarget);
            if (action == MouseAction.Up)
                contextTarget?.DispatchTrusted(StandardEvents.CreateContextMenu(point.X, point.Y));
            RenderFrame();
            return;
        }

        if (_draggingSplitter != null && action == MouseAction.Move)
        {
            _pendingSplitterPoint = point;
            RequestRender();
            return;
        }

        if (_draggingSplitter != null && action == MouseAction.Up)
        {
            var releaseHit = HitTest(point);
            _pendingSplitterPoint = null;
            _draggingSplitter.HandlePointerUp(point);
            _draggingSplitter = null;
            UpdateHoverPath(releaseHit);
            _host.Cursor = ResolveCursor(releaseHit, point);
            if (_pointerDownTarget != null && releaseHit == _pointerDownTarget)
                releaseHit?.DispatchTrusted(StandardEvents.CreateClick());
            _pointerDownTarget = null;
            ClearActivePath();
            RenderFrame();
            return;
        }

        var hit = HitTest(point);
        if (_inspectorModeEnabled)
        {
            if (action == MouseAction.Move)
            {
                var nextHighlight = hit?.DebugId;
                if (_inspectorHighlightDebugId != nextHighlight)
                {
                    _inspectorHighlightDebugId = nextHighlight;
                    _inspectorOverlayDirty = true;
                    RequestRender();
                }
                return;
            }

            if (action == MouseAction.Down)
            {
                if (hit != null)
                {
                    _inspectorHighlightDebugId = hit.DebugId;
                    _inspectorOverlayDirty = true;
                    InspectorNodeSelected?.Invoke(hit.DebugId);
                    RequestRender();
                }
                return;
            }

            if (action == MouseAction.Up)
                return;
        }

        if (action == MouseAction.Down && MainWindow.TitleStyle == TitleStyle.Custom &&
            _document.Head.Geometry.Contains(point) && !IsInteractiveTitleBarElement(hit))
        {
            MainWindow.BeginMoveAsync().GetAwaiter().GetResult();
            return;
        }

        if (action == MouseAction.Down && _displayTree.DismissPopupsOutside(point))
            RequestRender();
        if (action == MouseAction.Move)
        {
            var isSelectingDocumentText = _textSelection is { IsSelecting: true };
            if (!isSelectingDocumentText) UpdateHoverPath(hit);
            var needsRender = false;
            _host.Cursor = ResolveCursor(hit, point);
            if (_isSelectingText && _focusedEditor != null)
            {
                _focusedEditor.HandlePointerMove(MapPointerPoint(_focusedInput, point));
                needsRender = true;
            }
            else if (_textSelection is { IsSelecting: true })
            {
                if (_textSelection.PreserveWordSelectionUntilDrag)
                {
                    var dragDeltaX = point.X - _textSelection.PointerDownPoint.X;
                    var dragDeltaY = point.Y - _textSelection.PointerDownPoint.Y;
                    if (dragDeltaX * dragDeltaX + dragDeltaY * dragDeltaY <= 25)
                    {
                        return;
                    }

                    _textSelection.PreserveWordSelectionUntilDrag = false;
                }

                _pendingTextSelectionPoint = point;
                _pendingTextSelectionHit = hit;
                needsRender = true;
            }
            else
            {
                needsRender |= _displayTree.HandlePointerMove(point);
            }

            if (needsRender) RequestRender();
            return;
        }

        if (action == MouseAction.Up)
        {
            if (_isSelectingText && _focusedEditor != null)
            {
                _focusedEditor.HandlePointerUp(MapPointerPoint(_focusedInput, point));
                _isSelectingText = false;
            }

            if (_textSelection is { IsSelecting: true } selection)
            {
                if (!selection.PreserveWordSelectionUntilDrag)
                    UpdateTextSelection(selection, hit, point);
                _pendingTextSelectionPoint = null;
                _pendingTextSelectionHit = null;
                selection.PreserveWordSelectionUntilDrag = false;
                selection.IsSelecting = false;
                SyncDocumentSelection(selection);
                UpdateHoverPath(hit);
            }

            if (_pointerDownTarget != null && hit == _pointerDownTarget)
                hit?.DispatchTrusted(StandardEvents.CreateClick());
            _pointerDownTarget = null;
            ClearActivePath();
            RenderFrame();
            return;
        }

        if (action != MouseAction.Down) return;

        var elapsed = _clock.Elapsed.TotalSeconds - _lastClickSeconds;
        var deltaX = point.X - _lastClickPoint.X;
        var deltaY = point.Y - _lastClickPoint.Y;
        var isDoubleClick = ReferenceEquals(hit, _lastClickTarget) && elapsed <= 0.5 &&
                            deltaX * deltaX + deltaY * deltaY <= 25;
        _lastClickTarget = isDoubleClick ? null : hit;
        _lastClickPoint = point;
        _lastClickSeconds = _clock.Elapsed.TotalSeconds;
        _pointerDownTarget = hit;
        UpdateHoverPath(hit);
        UpdateActivePath(hit);
        hit?.DispatchTrusted(StandardEvents.CreatePointerDown());
        _draggingSplitter = FindAncestor<Splitter>(hit);
        _pendingSplitterPoint = null;
        _draggingSplitter?.HandlePointerDown(point);
        UpdateFocus(hit, point, isDoubleClick);

        if (hit is Select selected) selected.HandlePointerDown(point);
        RenderFrame();
    }

    private void FlushPendingSplitterMove()
    {
        if (_draggingSplitter == null || _pendingSplitterPoint is not { } point) return;
        _pendingSplitterPoint = null;
        _draggingSplitter.HandlePointerMove(point);
    }

    private void UpdateFocus(Element? hit, Point point, bool selectWord)
    {
        if (_host == null) return;

        var focusTarget = FindFocusableAncestor(hit);

        if (_focusedInput != focusTarget)
        {
            var previous = _focusedInput;
            previous?.Unfocus();
            if (previous?.IsFocused == true)
            {
                _focusedInput = previous;
                _focusedEditor = previous as ITextEditor;
                return;
            }
            _focusedInput = focusTarget;
            _focusedEditor = focusTarget as ITextEditor;
            focusTarget?.Focus();
            if (focusTarget?.IsFocused != true)
            {
                _focusedInput = null;
                _focusedEditor = null;
            }
        }

        if (hit is ITextEditor editor && hit is UIElement editorElement)
        {
            ClearDocumentSelection();
            var editorPoint = MapPointerPoint(editorElement, point);
            var startedDrag = editor.HandlePointerDown(
                editorPoint,
                CurrentModifiers.HasFlag(KeyModifiers.Shift),
                CurrentModifiers.HasFlag(KeyModifiers.Alt));
            if (selectWord && startedDrag) editor.SelectWordAt(editorPoint);
            // gutter/折叠等消费点击时返回 false：保留选区，不进入拖选
            _isSelectingText = startedDrag;
            return;
        }

        if (TryStartTextSelection(hit, point, selectWord))
        {
            _focusedInput?.Unfocus();
            _focusedInput = null;
            _focusedEditor = null;
            _isSelectingText = false;
            return;
        }

        _isSelectingText = false;
        ClearDocumentSelection();
    }

    private void ClearDocumentSelection()
    {
        if (_textSelection == null && _document.GetSelection().RangeCount == 0) return;
        if (_textSelection != null)
            InvalidateTextSelectionOverlay(GetTextSelectionBounds(_textSelection));
        _textSelection = null;
        _document.GetSelection().RemoveAllRanges();
        RequestRender();
    }

    private static bool IsFocusable(UIElement element) => element.IsEnabled &&
                                                          (element is ITextEditor or Button or CheckBox or Radio
                                                              or Select or List or Tree or Swiper or Link);

    private static Point MapPointerPoint(Element? target, Point point)
    {
        for (var current = target?.Parent; current != null; current = current.Parent)
            if (current is IPopupElement popup)
                return popup.MapPointToContent(point);
        return point;
    }

    private static Rect MapContentRectToScreen(Element? target, Rect rect)
    {
        for (var current = target?.Parent; current != null; current = current.Parent)
        {
            if (current is not IPopupElement popup) continue;
            var origin = popup.MapPointToContent(Point.Zero);
            return rect.Offset(-origin.X, -origin.Y);
        }

        return rect;
    }

    private static UIElement? FindFocusableAncestor(Element? hit)
    {
        for (var current = hit; current != null; current = current.Parent)
            if (current is UIElement element && IsFocusable(element))
                return element;
        return null;
    }

    private static T? FindAncestor<T>(Element? hit) where T : Element
    {
        for (var current = hit; current != null; current = current.Parent)
            if (current is T match) return match;
        return null;
    }

    private static CursorKind ResolveCursor(Element? hit, Point point)
    {
        for (var current = hit; current != null; current = current.Parent)
        {
            var value = current.Style.Get("cursor")?.Trim();
            if (!string.IsNullOrEmpty(value))
            {
                var cursor = value.ToLowerInvariant() switch
                {
                    "pointer" or "hand" => CursorKind.Hand,
                    "text" => CursorKind.Text,
                    "default" => CursorKind.Arrow,
                    "auto" => (CursorKind?)null,
                    _ => null
                };
                if (cursor.HasValue) return cursor.Value;
            }
        }

        for (var current = hit; current != null; current = current.Parent)
        {
            if (current is Link link) return link.IsEnabled ? CursorKind.Hand : CursorKind.Arrow;
            if (current is Splitter splitter)
                return splitter.IsVertical ? CursorKind.ResizeHorizontal : CursorKind.ResizeVertical;
            if (current is ITextEditor editor)
                return editor.ResolveCursorAt(point) ?? CursorKind.Text;
        }

        return FindUserSelectRoot(hit) != null ? CursorKind.Text : CursorKind.Arrow;
    }

    private static bool IsInteractiveTitleBarElement(Element? hit)
    {
        for (var current = hit; current != null; current = current.Parent)
        {
            if (current is Button or Input or TextArea or CheckBox or Radio or Select or Link or
                MenuBar or Menu or MenuItem or List or Tree or Swiper or ITextEditor)
                return true;
            if (current is UIHeadElement) break;
        }

        return false;
    }

    private bool UpdateHoverPath(Element? hit)
    {
        var changed = UpdateStatePath(_hoverPath, hit, ElementState.Hover);
        UpdateTooltip(hit);
        return changed;
    }

    private void UpdateTooltip(Element? hit)
    {
        var target = FindTooltipTarget(hit);
        var text = target?.Tooltip;
        if (ReferenceEquals(_tooltipTarget, target) && string.Equals(_tooltipPopup.Message, text, StringComparison.Ordinal))
            return;

        _tooltipTarget = target;
        if (target == null || string.IsNullOrWhiteSpace(text))
        {
            _tooltipPopup.Anchor = null;
            _tooltipPopup.Close();
        }
        else
        {
            _tooltipPopup.Anchor = target;
            _tooltipPopup.Message = text;
            _tooltipPopup.Open();
        }

        RequestRender();
    }

    private static UIElement? FindTooltipTarget(Element? hit)
    {
        for (var current = hit; current != null; current = current.Parent)
        {
            if (current is UIElement element && !string.IsNullOrWhiteSpace(element.Tooltip))
                return element;
        }

        return null;
    }

    private bool UpdateActivePath(Element? hit) => UpdateStatePath(_activePath, hit, ElementState.Active);

    private bool ClearActivePath() => UpdateStatePath(_activePath, null, ElementState.Active);

    private static bool UpdateStatePath(List<UIElement> currentPath, Element? hit, ElementState state)
    {
        UIElement? pathStart = null;
        for (var current = hit; current != null; current = current.Parent)
        {
            if (current is not UIElement uiElement) continue;
            pathStart = uiElement;
            break;
        }

        if ((currentPath.Count == 0 && pathStart == null) ||
            (currentPath.Count > 0 && ReferenceEquals(currentPath[0], pathStart)))
            return false;

        var nextPath = BuildElementPath(hit);
        if (PathsEqual(currentPath, nextPath)) return false;

        foreach (var element in currentPath)
        {
            if (!nextPath.Contains(element))
                element.SetState(state, false);
        }

        for (var i = nextPath.Count - 1; i >= 0; i--)
        {
            if (!currentPath.Contains(nextPath[i]))
                nextPath[i].SetState(state, true);
        }

        currentPath.Clear();
        currentPath.AddRange(nextPath);
        return true;
    }

    private static List<UIElement> BuildElementPath(Element? hit)
    {
        var path = new List<UIElement>();
        for (var current = hit; current != null; current = current.Parent)
            if (current is UIElement uiElement)
                path.Add(uiElement);
        return path;
    }

    private static bool PathsEqual(List<UIElement> left, List<UIElement> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
            if (!ReferenceEquals(left[i], right[i]))
                return false;
        return true;
    }

    private void HandleKey(int keyCode, KeyAction action)
    {
        if (_host == null) return;

        MainWindow.RaiseGlobalKeyEvent(keyCode, action);

        var shift = CurrentModifiers.HasFlag(KeyModifiers.Shift);
        var control = CurrentModifiers.HasFlag(KeyModifiers.Control);
        var alt = CurrentModifiers.HasFlag(KeyModifiers.Alt);

        if (action == KeyAction.Down && _displayTree.HandlePopupKey(keyCode, shift, control, alt))
        {
            RenderFrame();
            return;
        }

        if (action == KeyAction.Down && keyCode == 27 && _displayTree.DismissTopmostPopupOnEscape())
        {
            SyncFocusedInputFromTree();
            RenderFrame();
            return;
        }

        SyncFocusedInputFromTree();

        _focusedInput?.DispatchTrusted(
            action == KeyAction.Down
                ? StandardEvents.CreateKeyDown(keyCode, shift, control, alt)
                : StandardEvents.CreateKeyUp(keyCode, shift, control, alt));
        if (action != KeyAction.Down) return;

        if (_focusedEditor == null)
        {
            if (control && keyCode == 67)
            {
                var text = GetSelectedUserText();
                if (!string.IsNullOrEmpty(text)) _host.SetClipboardText(text);
            }

            RenderFrame();
            return;
        }

        var clipboardShortcut = !_focusedEditor.ClipboardShortcutsRequireShift || shift;
        if (control && clipboardShortcut && keyCode == 67)
        {
            if (_focusedEditor.CanCopySelection && _focusedEditor.SelectionLength > 0)
                _host.SetClipboardText(_focusedEditor.SelectedText);
            else
                _focusedEditor.HandleKey(keyCode, shift, control);
        }
        else if (control && keyCode == 88)
        {
            if (_focusedEditor.CanCutSelection && _focusedEditor.SelectionLength > 0)
            {
                _host.SetClipboardText(_focusedEditor.SelectedText);
                _focusedEditor.HandleKey(keyCode, shift, control);
            }
            else
            {
                _focusedEditor.HandleKey(keyCode, shift, control);
            }
        }
        else if (control && clipboardShortcut && keyCode == 86)
        {
            var text = _host.GetClipboardText();
            if (!string.IsNullOrEmpty(text))
                _focusedEditor.HandleTextInput(text);
        }
        else
        {
            _focusedEditor.HandleKey(keyCode, shift, control);
        }

        RenderFrame();
    }

    private void SyncFocusedInputFromTree()
    {
        var focused = _root.QueryAll<UIElement>().LastOrDefault(element => element.IsFocused);
        if (ReferenceEquals(_focusedInput, focused)) return;
        _focusedInput = focused;
        _focusedEditor = focused as ITextEditor;
    }

    private void HandleTextInput(string text)
    {
        SyncFocusedInputFromTree();
        _focusedEditor?.HandleTextInput(text);
        RenderFrame();
    }

    private KeyModifiers CurrentModifiers => _devToolsModifiers ?? _host?.Modifiers ?? KeyModifiers.None;

    private void WithDevToolsModifiers(KeyModifiers modifiers, Action action)
    {
        var previous = _devToolsModifiers;
        _devToolsModifiers = modifiers;
        try
        {
            action();
        }
        finally
        {
            _devToolsModifiers = previous;
        }
    }

    private bool TryStartTextSelection(Element? hit, Point point, bool selectWord = false)
    {
        var root = FindUserSelectRoot(hit);
        if (root == null) return false;

        var selection = new TextSelectionState(root, CollectSelectableText(root));
        var selectionPoint = FindTextSelectionPoint(selection, hit, point);
        if (selectionPoint.Index < 0) return false;

        selection.Anchor = selectionPoint;
        selection.Focus = selectionPoint;
        if (selectWord)
        {
            var item = selection.Items[selectionPoint.Index];
            var (start, end) = FindDocumentWordAt(item.Text, selectionPoint.Offset);
            selection.Anchor = new TextSelectionPoint(selectionPoint.Index, start);
            selection.Focus = new TextSelectionPoint(selectionPoint.Index, end);
        }

        selection.IsSelecting = true;
        selection.PointerDownPoint = point;
        selection.PreserveWordSelectionUntilDrag = selectWord;
        _textSelection = selection;
        SyncDocumentSelection(selection);
        InvalidateTextSelectionOverlay(GetTextSelectionBounds(selection));
        RequestRender();
        return true;
    }

    private static (int Start, int End) FindDocumentWordAt(string text, int offset)
    {
        if (text.Length == 0) return (0, 0);
        var index = Math.Clamp(offset, 0, text.Length - 1);
        if (!char.IsLetterOrDigit(text[index]) && text[index] != '_') return (index, index + 1);
        var start = index;
        var end = index + 1;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
        return (start, end);
    }

    private bool UpdateTextSelection(TextSelectionState selection, Element? hit, Point point)
    {
        var selectionPoint = FindTextSelectionPoint(selection, hit, point);
        if (selectionPoint.Index < 0 || selectionPoint == selection.Focus) return false;
        var previousFocus = selection.Focus;
        selection.Focus = selectionPoint;
        SyncDocumentSelection(selection);
        InvalidateTextSelectionOverlay(GetTextSelectionBounds(selection.Items, previousFocus, selectionPoint));
        return true;
    }

    private bool FlushPendingTextSelection()
    {
        if (_pendingTextSelectionPoint is not { } point ||
            _textSelection is not { IsSelecting: true } selection)
            return false;

        var hit = _pendingTextSelectionHit;
        _pendingTextSelectionPoint = null;
        _pendingTextSelectionHit = null;
        return UpdateTextSelection(selection, hit, point);
    }

    private static Element? FindUserSelectRoot(Element? element)
    {
        Element? candidate = null;
        for (var current = element; current != null; current = current.Parent)
        {
            var value = current.Style.Get("user-select")?.Trim();
            if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(value, "text", StringComparison.OrdinalIgnoreCase)) candidate = current;
        }

        return candidate;
    }

    private List<TextSelectionItem> CollectSelectableText(Element root)
    {
        var items = new List<TextSelectionItem>();
        var fragmentsByElement = _displayTree.CollectTextFragments(root)
            .GroupBy(fragment => fragment.Element)
            .ToDictionary(group => group.Key, group => group.ToList());
        CollectSelectableText(root, items, fragmentsByElement);
        return items;
    }

    private static void CollectSelectableText(Element element, List<TextSelectionItem> items,
        Dictionary<Element, List<TextFragment>> fragmentsByElement)
    {
        if (!element.IsVisible || !element.IsUserSelectText()) return;
        var selectableStart = items.Count;
        if (fragmentsByElement.TryGetValue(element, out var fragments))
        {
            if (element is ITextSelectable selectable && !string.IsNullOrEmpty(selectable.SelectableText))
            {
                var fragment = MergeSelectableTextFragments(element, selectable.SelectableText, fragments);
                items.Add(new TextSelectionItem(
                    element,
                    selectable.SelectableText,
                    fragment.Bounds,
                    fragment,
                    FindTextNode(element, selectable.SelectableText)));
            }
            else
            {
                foreach (var fragment in fragments)
                    items.Add(new TextSelectionItem(
                        element,
                        fragment.Text,
                        fragment.Bounds,
                        fragment,
                        FindTextNode(element, fragment.Text)));
            }
        }
        else if (element is ITextSelectable selectable && !string.IsNullOrEmpty(selectable.SelectableText))
            items.Add(new TextSelectionItem(
                element,
                selectable.SelectableText,
                selectable.SelectableTextBounds,
                null,
                FindTextNode(element, selectable.SelectableText)));

        foreach (var child in element.Children)
            CollectSelectableText(child, items, fragmentsByElement);
        if (items.Count > selectableStart + 1 && element is ITextSelectable)
            items.RemoveAt(selectableStart);
    }

    internal static TextFragment MergeSelectableTextFragments(
        Element element,
        string selectableText,
        IReadOnlyList<TextFragment> fragments)
    {
        if (fragments.Count == 1 && fragments[0].Text == selectableText)
            return fragments[0];

        var characters = new List<TextCharacterFragment>();
        var bounds = Rect.Empty;
        var searchFrom = 0;
        foreach (var fragment in fragments)
        {
            bounds = bounds.IsEmpty ? fragment.Bounds : Rect.Union(bounds, fragment.Bounds);
            var fragmentStart = selectableText.IndexOf(fragment.Text, searchFrom, StringComparison.Ordinal);
            if (fragmentStart < 0)
                fragmentStart = Math.Min(searchFrom, selectableText.Length);
            foreach (var character in fragment.Characters)
            {
                characters.Add(new TextCharacterFragment(
                    Math.Min(selectableText.Length, fragmentStart + character.StartOffset),
                    Math.Min(selectableText.Length, fragmentStart + character.EndOffset),
                    character.Bounds,
                    character.SelectionBounds)
                    {
                        Direction = character.Direction
                    });
            }
            searchFrom = Math.Min(selectableText.Length, fragmentStart + fragment.Text.Length);
        }

        return new TextFragment(element, selectableText, fragments[0].Font, bounds, characters);
    }

    private static TextSelectionPoint FindTextSelectionPoint(TextSelectionState selection, Element? hit, Point point)
    {
        var visibleBounds = selection.Items.Select(GetVisibleTextBounds).ToArray();
        for (var current = hit; current != null; current = current.Parent)
        {
            var direct = -1;
            for (var index = selection.Items.Count - 1; index >= 0; index--)
                if (ReferenceEquals(selection.Items[index].Element, current) &&
                    IsValidTextSelectionBounds(visibleBounds[index]) && visibleBounds[index].Contains(point))
                {
                    direct = index;
                    break;
                }
            if (direct >= 0) return CreateSelectionPoint(selection.Items[direct], direct, point);
        }

        var containing = visibleBounds
            .Select((bounds, index) => (bounds, index))
            .Where(pair => IsValidTextSelectionBounds(pair.bounds) && pair.bounds.Contains(point))
            .OrderBy(pair => pair.bounds.Width * pair.bounds.Height)
            .Select(pair => pair.index)
            .FirstOrDefault(-1);
        if (containing >= 0) return CreateSelectionPoint(selection.Items[containing], containing, point);
        if (selection.Items.Count == 0) return new TextSelectionPoint(-1, 0);

        var bestIndex = FindNearestTextBoundsIndex(visibleBounds, point);

        return bestIndex < 0
            ? new TextSelectionPoint(-1, 0)
            : CreateSelectionPoint(selection.Items[bestIndex], bestIndex, ClampPoint(point, visibleBounds[bestIndex]));
    }

    internal static int FindNearestTextBoundsIndex(IReadOnlyList<Rect> bounds, Point point)
    {
        var bestIndex = -1;
        var bestVerticalDistance = float.MaxValue;
        var bestHorizontalDistance = float.MaxValue;
        for (var i = 0; i < bounds.Count; i++)
        {
            var itemBounds = bounds[i];
            if (!IsValidTextSelectionBounds(itemBounds)) continue;
            var dy = point.Y < itemBounds.Top ? itemBounds.Top - point.Y :
                point.Y > itemBounds.Bottom ? point.Y - itemBounds.Bottom : 0;
            var dx = point.X < itemBounds.Left ? itemBounds.Left - point.X :
                point.X > itemBounds.Right ? point.X - itemBounds.Right : 0;
            if (dy > bestVerticalDistance || dy == bestVerticalDistance && dx >= bestHorizontalDistance)
                continue;
            bestVerticalDistance = dy;
            bestHorizontalDistance = dx;
            bestIndex = i;
        }
        return bestIndex;
    }

    internal static bool IsValidTextSelectionBounds(Rect bounds) =>
        !bounds.IsEmpty && float.IsFinite(bounds.X) && float.IsFinite(bounds.Y) &&
        float.IsFinite(bounds.Width) && float.IsFinite(bounds.Height) &&
        float.IsFinite(bounds.Left) && float.IsFinite(bounds.Top) &&
        float.IsFinite(bounds.Right) && float.IsFinite(bounds.Bottom);

    private static Point ClampPoint(Point point, Rect bounds) => new(
        Math.Clamp(point.X, bounds.Left, bounds.Right),
        Math.Clamp(point.Y, bounds.Top, bounds.Bottom));

    private static TextSelectionPoint CreateSelectionPoint(TextSelectionItem item, int index, Point point)
    {
        var offset = GetTextSelectionVisualOffset(item.Element);
        var contentPoint = new Point(point.X - offset.X, point.Y - offset.Y);
        if (item.Fragment != null)
            return new TextSelectionPoint(index, item.Fragment.HitTestOffset(contentPoint));
        var midpoint = item.Bounds.X + item.Bounds.Width / 2f;
        return new TextSelectionPoint(index, contentPoint.X < midpoint ? 0 : item.Text.Length);
    }

    private string GetSelectedUserText()
    {
        var documentSelectionText = _document.GetSelection().ToString();
        if (!string.IsNullOrEmpty(documentSelectionText)) return documentSelectionText;

        if (_textSelection == null || _textSelection.Items.Count == 0) return "";
        var (start, end) = GetOrderedSelectionPoints(_textSelection);
        if (start.Index < 0 || end.Index < 0) return "";
        if (start.Index == end.Index)
        {
            var item = _textSelection.Items[start.Index];
            return start.Offset == end.Offset ? "" : item.Text[start.Offset..end.Offset];
        }

        var selected = new List<string>();
        for (var i = start.Index; i <= end.Index; i++)
        {
            var item = _textSelection.Items[i];
            if (i == start.Index) selected.Add(item.Text[start.Offset..]);
            else if (i == end.Index) selected.Add(item.Text[..end.Offset]);
            else selected.Add(item.Text);
        }

        return string.Join(Environment.NewLine, selected.Where(text => text.Length > 0));
    }

    private void SyncDocumentSelection(TextSelectionState selection)
    {
        var documentSelection = _document.GetSelection();
        if (selection.Items.Count == 0)
        {
            documentSelection.RemoveAllRanges();
            return;
        }

        var (startPoint, endPoint) = GetOrderedSelectionPoints(selection);
        var start = startPoint.Index;
        var end = endPoint.Index;
        if (start < 0 || end < 0) return;
        var startItem = selection.Items[start];
        var endItem = selection.Items[end];
        var startElement = startItem.Element;
        var endElement = endItem.Element;
        if (startElement.OwnerDocument != _document || endElement.OwnerDocument != _document) return;

        var range = _document.CreateRange();
        if (startItem.TextNode is { } startText && endItem.TextNode is { } endText)
        {
            range.SetStart(startText, Math.Clamp(startPoint.Offset, 0, startText.Length));
            range.SetEnd(endText, Math.Clamp(endPoint.Offset, 0, endText.Length));
        }
        else if (startItem.TextNode == null || endItem.TextNode == null)
        {
            // Custom-painted selectable text has no DOM Text node. Keep the visual selection
            // as the source of truth so clipboard text includes its logical SelectableText.
            documentSelection.RemoveAllRanges();
            return;
        }
        else
        {
            range.SetStart(startElement, 0);
            range.SetEnd(endElement, endElement.ChildNodes.Count);
        }

        documentSelection.SetRange(range);
    }

    private static Square.UI.Text? FindTextNode(Element element, string text)
    {
        return element.ChildNodes.OfType<Square.UI.Text>().FirstOrDefault(node => node.Data == text)
               ?? element.ChildNodes.OfType<Square.UI.Text>().FirstOrDefault();
    }

    private void RenderTextSelection(IRenderContext context)
    {
        if (_textSelection == null || _textSelection.Items.Count == 0) return;
        var (startPoint, endPoint) = GetOrderedSelectionPoints(_textSelection);
        if (startPoint.Index < 0 || endPoint.Index < 0) return;
        for (var i = startPoint.Index; i <= endPoint.Index; i++)
        {
            var item = _textSelection.Items[i];
            var startOffset = i == startPoint.Index ? startPoint.Offset : 0;
            var endOffset = i == endPoint.Index ? endPoint.Offset : item.Text.Length;
            if (startOffset == endOffset) continue;
            var background = ResolveSelectionColor(item.Element, foreground: false);
            var foreground = ResolveSelectionColor(item.Element, foreground: true);
            var backgroundBrush = new SolidColorBrush(background);
            var foregroundBrush = new SolidColorBrush(foreground);
            var visualOffset = GetTextSelectionVisualOffset(item.Element);
            var clip = GetTextSelectionClip(item.Element);
            if (clip is { IsEmpty: true }) continue;
            if (clip is { } clipRect) context.PushClip(clipRect);
            if (item.Fragment == null)
            {
                context.FillRect(Translate(item.Bounds, visualOffset), backgroundBrush);
                if (clip != null) context.PopClip();
                continue;
            }

            RenderSelectedTextRuns(
                context,
                item.Fragment,
                startOffset,
                endOffset,
                backgroundBrush,
                foregroundBrush,
                visualOffset);
            if (clip != null) context.PopClip();
        }
    }

    private void InvalidateTextSelectionOverlay(params Rect[] bounds)
    {
        foreach (var bound in bounds)
        {
            if (bound.IsEmpty) continue;
            var dirty = bound.Inflate(1, 1);
            _textSelectionOverlayDirtyBounds = _textSelectionOverlayDirtyBounds.IsEmpty
                ? dirty
                : Rect.Union(_textSelectionOverlayDirtyBounds, dirty);
        }
    }

    private static Rect GetTextSelectionBounds(TextSelectionState selection)
    {
        var (startPoint, endPoint) = GetOrderedSelectionPoints(selection);
        return GetTextSelectionBounds(selection.Items, startPoint, endPoint);
    }

    private static Rect GetTextSelectionBounds(
        List<TextSelectionItem> items,
        TextSelectionPoint startPoint,
        TextSelectionPoint endPoint)
    {
        if (items.Count == 0) return Rect.Empty;
        if (startPoint.Index > endPoint.Index ||
            startPoint.Index == endPoint.Index && startPoint.Offset > endPoint.Offset)
            (startPoint, endPoint) = (endPoint, startPoint);
        if (startPoint.Index < 0 || endPoint.Index < 0) return Rect.Empty;

        var bounds = Rect.Empty;
        for (var i = startPoint.Index; i <= endPoint.Index; i++)
        {
            var item = items[i];
            var startOffset = i == startPoint.Index ? startPoint.Offset : 0;
            var endOffset = i == endPoint.Index ? endPoint.Offset : item.Text.Length;
            if (startOffset == endOffset) continue;

            if (item.Fragment == null)
            {
                var itemBounds = GetVisualTextBounds(item);
                bounds = bounds.IsEmpty ? itemBounds : Rect.Union(bounds, itemBounds);
                continue;
            }

            var visualOffset = GetTextSelectionVisualOffset(item.Element);
            foreach (var character in item.Fragment.Characters)
            {
                if (character.EndOffset <= startOffset || character.StartOffset >= endOffset) continue;
                var characterBounds = Translate(character.SelectionBounds, visualOffset);
                bounds = bounds.IsEmpty
                    ? characterBounds
                    : Rect.Union(bounds, characterBounds);
            }
        }

        return bounds;
    }

    private static void RenderSelectedTextRuns(
        IRenderContext context,
        TextFragment fragment,
        int startOffset,
        int endOffset,
        Brush background,
        Brush foreground,
        Point visualOffset)
    {
        var characters = fragment.Characters;
        var index = 0;
        while (index < characters.Count)
        {
            while (index < characters.Count &&
                   (characters[index].EndOffset <= startOffset || characters[index].StartOffset >= endOffset))
                index++;
            if (index >= characters.Count) break;

            var first = characters[index];
            var runStart = first.StartOffset;
            var runEnd = first.EndOffset;
            var bounds = Translate(first.SelectionBounds, visualOffset);
            var origin = new Point(first.Bounds.X + visualOffset.X, first.Bounds.Y + visualOffset.Y);
            var lineY = first.Bounds.Y;
            index++;

            while (index < characters.Count)
            {
                var character = characters[index];
                if (character.EndOffset <= startOffset || character.StartOffset >= endOffset ||
                    character.Bounds.Y != lineY || character.StartOffset != runEnd)
                    break;
                runEnd = character.EndOffset;
                bounds = Rect.Union(bounds, Translate(character.SelectionBounds, visualOffset));
                index++;
            }

            context.FillRect(bounds, background);
            context.DrawText(
                new TextLayout(fragment.Text[runStart..runEnd], fragment.Font),
                origin,
                foreground);
        }
    }

    private static Rect GetVisualTextBounds(TextSelectionItem item) =>
        Translate(item.Bounds, GetTextSelectionVisualOffset(item.Element));

    private static Rect GetVisibleTextBounds(TextSelectionItem item) =>
        GetVisibleTextSelectionBounds(item.Element, GetVisualTextBounds(item));

    internal static Rect GetVisibleTextSelectionBounds(Element element, Rect visualBounds)
    {
        if (!IsValidTextSelectionBounds(visualBounds)) return Rect.Empty;
        var clip = GetTextSelectionClip(element);
        if (clip == null) return visualBounds;
        var visible = Rect.Intersect(visualBounds, clip.Value);
        return IsValidTextSelectionBounds(visible) ? visible : Rect.Empty;
    }

    private static Point GetTextSelectionVisualOffset(Element element)
    {
        var x = 0f;
        var y = 0f;
        for (var current = element.Parent; current != null; current = current.Parent)
        {
            if (!current.MapsScrollOffsetForChildren()) continue;
            x -= current.ScrollLeft;
            y -= current.ScrollTop;
        }
        return new Point(x, y);
    }

    private static Rect? GetTextSelectionClip(Element element)
    {
        Rect? clip = null;
        for (var current = element.Parent; current != null; current = current.Parent)
        {
            if (!current.ClipsOverflow()) continue;
            var currentClip = current.GetOverflowClipRect();
            if (!IsValidTextSelectionBounds(currentClip)) return Rect.Empty;
            currentClip = Translate(currentClip, GetTextSelectionVisualOffset(current));
            if (!IsValidTextSelectionBounds(currentClip)) return Rect.Empty;
            clip = clip == null ? currentClip : Rect.Intersect(clip.Value, currentClip);
            if (!IsValidTextSelectionBounds(clip.Value)) return Rect.Empty;
        }
        return clip;
    }

    private static Rect Translate(Rect rect, Point offset) =>
        new(rect.X + offset.X, rect.Y + offset.Y, rect.Width, rect.Height);

    private static Color ResolveSelectionColor(Element element, bool foreground)
    {
        var value = foreground
            ? FindStyleInPath(element, "selection-color")
            : FindStyleInPath(element, "selection-background") ??
              FindStyleInPath(element, "selection-background-color");
        if (Color.TryParse(value, out var color)) return color;

        return foreground ? DefaultSelectionForeground : DefaultSelectionBackground;
    }

    private static string? FindStyleInPath(Element element, string property)
    {
        for (var current = element; current != null; current = current.Parent)
        {
            var value = current.Style.Get(property);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static (TextSelectionPoint Start, TextSelectionPoint End) GetOrderedSelectionPoints(
        TextSelectionState selection)
    {
        var anchor = selection.Anchor;
        var focus = selection.Focus;
        if (anchor.Index < focus.Index || anchor.Index == focus.Index && anchor.Offset <= focus.Offset)
            return (anchor, focus);
        return (focus, anchor);
    }

    private sealed class TextSelectionState(Element root, List<TextSelectionItem> items)
    {
        public Element Root { get; } = root;
        public List<TextSelectionItem> Items { get; } = items;
        public TextSelectionPoint Anchor { get; set; }
        public TextSelectionPoint Focus { get; set; }
        public bool IsSelecting { get; set; }
        public Point PointerDownPoint { get; set; }
        public bool PreserveWordSelectionUntilDrag { get; set; }
    }

    private readonly record struct TextSelectionItem(
        Element Element,
        string Text,
        Rect Bounds,
        TextFragment? Fragment,
        Square.UI.Text? TextNode);

    private readonly record struct TextSelectionPoint(int Index, int Offset);

    private sealed class TooltipPopup : Popup
    {
        private readonly Square.Controls.Text _text = new();

        public TooltipPopup()
        {
            ClassList.Add("square-tooltip");
            Style.Set("display", "flex");
            Style.Set("align-items", "center");
            Style.Set("padding", "5px 8px");
            Style.Set("background", "#263448");
            Style.Set("border", "1px solid #526783");
            Style.Set("border-radius", "4px");
            Style.Set("max-width", "320px");
            Placement = PopupPlacement.Bottom;
            Alignment = PopupAlignment.Center;
            VerticalOffset = 6;
            FlipOnOverflow = true;
            ConstrainToViewport = true;
            DismissOnPointerDownOutside = false;
            _text.Style.Set("color", "#f3f6fb");
            _text.Style.Set("font-size", "12px");
            _text.Style.Set("white-space", "nowrap");
            _text.Style.Set("user-select", "none");
            Children.Add(_text);
        }

        public string? Message
        {
            get => _text.TextContent;
            set
            {
                _text.TextContent = value ?? "";
                _text.InvalidateLayout();
            }
        }

        public override Size Measure(Size availableSize)
        {
            var textSize = ControlDrawing.MeasureText(_text, Message ?? "", 12f, new Size(320, float.MaxValue));
            return new Size(textSize.Width + 16, textSize.Height + 10);
        }

        public override Element? HitTestPopup(Point point) => null;

        public override bool ContainsPopupInteraction(Point point) => false;
    }

    private void HandleTick()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var animationDelta = (float)Math.Max(0, now - _lastAnimationTickSeconds);
        _lastAnimationTickSeconds = now;
        var animationsRunning = CssStyleReconciler.TickAnimations(_root, animationDelta);
        // 避免每 tick 分配 LINQ 数组
        List<Element>? dueTargets = null;
        List<Element>? staleTargets = null;
        foreach (var pair in _scheduledFrames)
        {
            if (!pair.Key.IsAttached || !pair.Key.IsEffectivelyVisible || !ReferenceEquals(pair.Key.OwnerDocument, _document))
            {
                staleTargets ??= [];
                staleTargets.Add(pair.Key);
                continue;
            }
            if (now < pair.Value) continue;
            dueTargets ??= [];
            dueTargets.Add(pair.Key);
        }

        if (staleTargets != null)
            foreach (var target in staleTargets)
                _scheduledFrames.Remove(target);

        if (dueTargets != null)
        {
            foreach (var target in dueTargets)
            {
                _scheduledFrames.Remove(target);
                if (target is IFrameScheduledElement scheduled)
                    scheduled.OnFrameDue();
                else
                    target.InvalidatePaint();
            }
        }

        var needsRender = (dueTargets != null && dueTargets.Count > 0)
                          || animationsRunning
                          || Volatile.Read(ref _renderRequested)
                          || _document.Context.Reconciler.HasWork
                          || CssStyleReconciler.HasWorkForTree(_root)
                          || Dispatcher.HasWork;
        if (_focusedEditor?.ToggleCaretBlink() == true) needsRender = true;
        if (needsRender && HasVisualWork()) RenderFrame();
    }

    private bool HasVisualWork()
    {
        RunUpdatePass();
        return _renderRequested || HasVisualInvalidation(_root);
    }

    private static bool HasVisualInvalidation(Element element)
    {
        if (!element.IsVisible || !element.IsCssDisplayed()) return false;
        if (element.IsLayoutDirty || element.NeedsPaint) return true;
        foreach (var child in element.Children)
            if (HasVisualInvalidation(child)) return true;
        return false;
    }
}
