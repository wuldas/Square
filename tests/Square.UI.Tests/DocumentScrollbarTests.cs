using System.Reflection;
using Square.Events;
using Square.Html;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using Square.Rendering;
using Square.Rendering.Tree;
using Square.Runtime;
using Square.UI;
using Square.UI.Scrolling;
using Xunit;

namespace Square.UI.Tests;

/// <summary>文档视口滚动：窗口客户区内容超出时 Body 充当浏览器 document scroller。</summary>
public sealed class DocumentScrollbarTests
{
    [Fact]
    public void WindowBodyBecomesDocumentScrollerWhenContentExceedsViewport()
    {
        var (window, body) = CreateWindowBody();
        body.Geometry = new Rect(0, 0, 100, 100);
        body.SetScrollContentSize(new Size(100, 400));

        var metrics = body.GetScrollbarMetrics();

        Assert.True(metrics.HasVertical);
        Assert.True(body.ScrollBy(0, 50));
        Assert.Equal(50, body.ScrollOffset.Y);
    }

    [Fact]
    public void DocumentWithoutOverflowDoesNotShowScrollbar()
    {
        var (window, body) = CreateWindowBody();
        body.Geometry = new Rect(0, 0, 100, 100);
        body.SetScrollContentSize(new Size(100, 100));

        Assert.False(body.GetScrollbarMetrics().HasVertical);
    }

    [Fact]
    public void BodyOverflowHiddenDisablesDocumentScrollbar()
    {
        var (window, body) = CreateWindowBody();
        body.Geometry = new Rect(0, 0, 100, 100);
        body.SetScrollContentSize(new Size(100, 400));
        body.Style.Set("overflow", "hidden");

        Assert.False(body.GetScrollbarMetrics().HasVertical);
        Assert.False(body.ScrollBy(0, 50));
    }

    [Fact]
    public void RootOverflowHiddenDisablesDocumentScrollbar()
    {
        var (window, body) = CreateWindowBody();
        body.Geometry = new Rect(0, 0, 100, 100);
        body.SetScrollContentSize(new Size(100, 400));
        window.Document.DocumentElement.Style.Set("overflow", "hidden");

        Assert.False(body.GetScrollbarMetrics().HasVertical);
    }

    [Fact]
    public void ExplicitBodyOverflowScrollStillShowsBothAxes()
    {
        var (window, body) = CreateWindowBody();
        body.Geometry = new Rect(0, 0, 100, 100);
        body.SetScrollContentSize(new Size(100, 100));
        body.Style.Set("overflow", "scroll");

        Assert.True(body.GetScrollbarMetrics().HasVertical);
    }

    [Fact]
    public void DocumentScrollbarThumbDragScrollsBody()
    {
        var (application, body, tree) = CreateApplicationBody();
        var thumbCenter = body.GetScrollbarMetrics().VerticalThumb.Center;

        Assert.Same(body, tree.HitTestScrollbar(thumbCenter));

        InvokeHandleMouse(application, thumbCenter, MouseAction.Down);
        Assert.Equal(ScrollbarPart.VerticalThumb, body.ScrollbarInteractionPart);
        Assert.Same(body, GetPrivateField<Element>(application, "_draggingScrollbar"));

        InvokeHandleMouse(application, new Point(50, 500), MouseAction.Move);
        Assert.True(body.ScrollTop > 0);
    }

    [Fact]
    public void WheelScrollsDocumentBody()
    {
        var (application, body, tree) = CreateApplicationBody();

        InvokeHandleWheel(application, new WheelInput(new Point(50, 50), 0, 100, isPrecise: true));

        Assert.True(body.ScrollTop > 0);
    }

    private static (DesktopApplication application, UIBodyElement body, DisplayTree tree) CreateApplicationBody()
    {
        var (window, body) = CreateWindowBody();
        window.Content!.Geometry = new Rect(0, 0, 85, 400);
        body.Geometry = new Rect(0, 0, 100, 100);
        body.SetScrollContentSize(new Size(100, 400));
        ((IComponentLifecycle)body).OnAttached();
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(window.Document.DocumentElement);
        Assert.True(body.GetScrollbarMetrics().HasVertical);
        return (application, body, tree);
    }

    private static void InvokeHandleMouse(
        DesktopApplication application,
        Point point,
        MouseAction action,
        MouseButton button = MouseButton.Left)
    {
        var method = typeof(DesktopApplication).GetMethod("HandleMouse", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(application, [point, action, button]);
    }

    private static void InvokeHandleWheel(DesktopApplication application, WheelInput input)
    {
        var method = typeof(DesktopApplication).GetMethod("HandleWheel", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(application, [input]);
    }

    private static void SetPrivateField<T>(DesktopApplication application, string name, T value)
    {
        var field = typeof(DesktopApplication).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(application, value);
    }

    private static T? GetPrivateField<T>(DesktopApplication application, string name)
    {
        var field = typeof(DesktopApplication).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T?)field!.GetValue(application);
    }

    [Fact]
    public void RealLayoutPipelineKeepsDocumentScrollbarInteractive()
    {
        var (window, body) = CreateWindowBodyWithTallContent();
        var application = CreateRealApplication(window);

        InvokeRenderFrame(application);
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));

        var metrics = body.GetScrollbarMetrics();
        Assert.True(metrics.HasVertical);
        var thumbCenter = metrics.VerticalThumb.Center;
        Assert.Same(body, tree.HitTestScrollbar(thumbCenter));

        InvokeHandleMouse(application, thumbCenter, MouseAction.Down);
        Assert.Equal(ScrollbarPart.VerticalThumb, body.ScrollbarInteractionPart);
        Assert.Same(body, GetPrivateField<Element>(application, "_draggingScrollbar"));

        InvokeHandleMouse(application, new Point(50, 500), MouseAction.Move);
        Assert.True(ReferenceEquals(body, GetPrivateField<Element>(application, "_draggingScrollbar")),
            $"Move 后拖拽捕获不应丢失：attached={body.IsAttached} scrollTop={body.ScrollTop}");
        Assert.True(body.ScrollTop > 0);
    }

    [Fact]
    public void HtmlBodyKeepsWheelPositionWhenContentRelayouts()
    {
        var window = new AppWindow("HTML document scrollbar")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Desktop
        };
        var article = new HTMLArticleElement();
        var content = new HTMLDivElement();
        content.Style.Set("height", "400px");
        article.Children.Add(content);
        window.Load(article);
        var body = ((UIDocument)window.Document).Body;
        var application = CreateRealApplication(window);

        InvokeRenderFrame(application);
        Assert.True(body.GetScrollbarMetrics().HasVertical);
        InvokeHandleWheel(application, new WheelInput(new Point(50, 50), 0, 50, isPrecise: true));
        Assert.Equal(50, body.ScrollTop);

        content.InvalidateLayout();
        InvokeRenderFrame(application);
        Assert.Equal(50, body.ScrollTop);
        Assert.Equal(100, body.Geometry.Height);
    }

    [Fact]
    public void RecomputingScrollContentSizeDoesNotCancelSmoothWheel()
    {
        var (window, body) = CreateWindowBodyWithTallContent();
        var application = CreateRealApplication(window);
        InvokeRenderFrame(application);

        InvokeHandleWheel(application, new WheelInput(new Point(50, 50), 0, 30));
        body.SetScrollContentSize(body.ScrollContentSize);
        Thread.Sleep(220);
        InvokeHandleTick(application);

        Assert.Equal(30, body.ScrollTop);
    }

    [Fact]
    public void SmoothWheelScrollsDocumentBodyThroughFrameQueue()
    {
        var (window, body) = CreateWindowBodyWithTallContent();
        var application = CreateRealApplication(window);
        InvokeRenderFrame(application);

        InvokeHandleWheel(application, new WheelInput(new Point(50, 50), 0, 30));
        Assert.Equal(0, body.ScrollTop);
        Thread.Sleep(220);
        InvokeHandleTick(application);

        Assert.Equal(30, body.ScrollTop);
    }

    [Fact]
    public void SystemTitleShellKeepsHeadAtZeroAndBodyAtClientOrigin()
    {
        var (window, body) = CreateWindowBodyWithTallContent();
        var head = ((UIDocument)window.Document).Head;
        var application = CreateRealApplication(window);

        InvokeRenderFrame(application);

        Assert.Equal(new Rect(0, 0, 100, 0), head.Geometry);
        Assert.Equal(new Rect(0, 0, 100, 100), body.Geometry);
        var metrics = body.GetScrollbarMetrics();
        Assert.True(metrics.HasVertical);
        Assert.Equal(0, metrics.ViewportRect.Y);
    }

    [Fact]
    public void CustomTitleShellReservesHeadAndOffsetsBodyAndScrollbarViewport()
    {
        var (window, body, titleBar) = CreateWindowWithCustomTitle(42);
        window.Content!.Style.Set("height", "400px");
        var head = ((UIDocument)window.Document).Head;
        var application = CreateRealApplication(window);

        InvokeRenderFrame(application);

        Assert.Equal(new Rect(0, 0, 100, 42), head.Geometry);
        Assert.Equal(new Rect(0, 0, 100, 42), titleBar.Geometry);
        Assert.Equal(new Rect(0, 42, 100, 58), body.Geometry);
        var metrics = body.GetScrollbarMetrics();
        Assert.True(metrics.HasVertical);
        Assert.Equal(42, metrics.ViewportRect.Y);
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        Assert.Same(body, tree.HitTestScrollbar(metrics.VerticalThumb.Center));
    }

    [Fact]
    public void ChangingTitleBarPreferredHeightRecomputesShellOriginsAndKeepsScrollTop()
    {
        var (window, body, titleBar) = CreateWindowWithCustomTitle(42);
        window.Content!.Style.Set("height", "400px");
        var head = ((UIDocument)window.Document).Head;
        var application = CreateRealApplication(window);

        InvokeRenderFrame(application);
        Assert.Equal(new Rect(0, 42, 100, 58), body.Geometry);
        InvokeHandleWheel(application, new WheelInput(new Point(50, 70), 0, 50, isPrecise: true));
        Assert.Equal(50, body.ScrollTop);

        titleBar.PreferredHeight = 60;
        titleBar.InvalidateLayout();
        InvokeRenderFrame(application);

        Assert.Equal(new Rect(0, 0, 100, 60), head.Geometry);
        Assert.Equal(new Rect(0, 0, 100, 60), titleBar.Geometry);
        Assert.Equal(new Rect(0, 60, 100, 40), body.Geometry);
        Assert.Equal(50, body.ScrollTop);
    }

    [Fact]
    public void CustomTitlePlacesGridBodyChildrenAtAbsoluteClientPositions()
    {
        var (window, body, _) = CreateWindowWithCustomTitle(42);
        var grid = new Controls.View();
        grid.Style.Set("display", "grid");
        grid.Style.Set("width", "100px");
        grid.Style.Set("grid-template-columns", "60px 40px");
        grid.Style.Set("grid-template-rows", "20px 20px");
        var first = new Controls.View();
        var second = new Controls.View();
        var third = new Controls.View();
        grid.Children.Add(first);
        grid.Children.Add(second);
        grid.Children.Add(third);
        window.Load(grid);
        var application = CreateRealApplication(window);

        InvokeRenderFrame(application);

        Assert.Equal(new Rect(0, 42, 100, 58), body.Geometry);
        Assert.Equal(new Rect(0, 42, 60, 20), first.Geometry);
        Assert.Equal(new Rect(60, 42, 40, 20), second.Geometry);
        Assert.Equal(new Rect(0, 62, 60, 20), third.Geometry);
    }

    [Fact]
    public void CustomTitlePlacesTableBodyChildrenAtAbsoluteClientPositions()
    {
        var (window, body, _) = CreateWindowWithCustomTitle(42);
        var table = new Controls.View();
        table.Style.Set("display", "table");
        table.Style.Set("width", "100px");
        var row = new Controls.View();
        row.Style.Set("display", "table-row");
        var cell = new Controls.View();
        cell.Style.Set("display", "table-cell");
        cell.Style.Set("width", "100px");
        cell.Style.Set("height", "20px");
        row.Children.Add(cell);
        table.Children.Add(row);
        window.Load(table);
        var application = CreateRealApplication(window);

        InvokeRenderFrame(application);

        Assert.Equal(new Rect(0, 42, 100, 58), body.Geometry);
        Assert.Equal(new Rect(0, 42, 100, 20), row.Geometry);
        Assert.Equal(new Rect(0, 42, 100, 20), cell.Geometry);
    }

    [Fact]
    public void ExtraRootChildFallsBackToRootLayoutWhileShellReservesCustomTitle()
    {
        var (window, body, titleBar) = CreateWindowWithCustomTitle(42);
        var extra = new Controls.View();
        extra.Style.Set("height", "20px");
        ((UIDocument)window.Document).Ui.Children.Add(extra);
        var head = ((UIDocument)window.Document).Head;
        var application = CreateRealApplication(window);
        var ui = ((UIDocument)window.Document).Ui;
        new LayoutEngine().MeasureAndArrange(ui, new Size(100, 100));
        var expectedExtra = extra.Geometry;
        extra.Geometry = Rect.Empty;
        ui.InvalidateLayout();

        InvokeRenderFrame(application);

        Assert.Equal(expectedExtra, extra.Geometry);
        Assert.Equal(new Rect(0, 0, 100, 42), head.Geometry);
        Assert.Equal(new Rect(0, 42, 100, 58), body.Geometry);
        Assert.Equal(new Rect(0, 0, 100, 42), titleBar.Geometry);
    }

    [Fact]
    public void RootScrollContainerKeepsItsOwnScrollBehaviorInShellFallback()
    {
        var (window, body) = CreateWindowBody();
        var ui = ((UIDocument)window.Document).Ui;
        ui.Style.Set("overflow", "auto");
        var extra = new Controls.View();
        extra.Style.Set("height", "400px");
        ui.Children.Add(extra);
        var application = CreateRealApplication(window);

        InvokeRenderFrame(application);

        Assert.True(ui.IsScrollContainer());
        Assert.True(ui.GetScrollbarMetrics().HasVertical);
        Assert.True(ui.ScrollBy(0, 60));
        Assert.Equal(60, ui.ScrollTop);
    }

    [Fact]
    public void AuthoredRootStyleForcesRootLayoutSemanticsOverShellShortcut()
    {
        var (window, body) = CreateWindowBody();
        var ui = ((UIDocument)window.Document).Ui;
        ui.Style.Set("display", "none");
        var head = ((UIDocument)window.Document).Head;
        var application = CreateRealApplication(window);

        InvokeRenderFrame(application);

        Assert.Equal(new Rect(0, 0, 0, 0), ui.Geometry);
        Assert.Equal(new Rect(0, 0, 100, 0), head.Geometry);
        Assert.Equal(new Rect(0, 0, 100, 100), body.Geometry);
    }

    [Fact]
    public void HiddenBodyKeepsZeroSizeAtShellOriginWithCustomTitle()
    {
        var (window, body, _) = CreateWindowWithCustomTitle(42);
        window.Content!.Style.Set("height", "400px");
        body.Style.Set("display", "none");
        var head = ((UIDocument)window.Document).Head;
        var application = CreateRealApplication(window);

        InvokeRenderFrame(application);

        Assert.Equal(new Rect(0, 0, 100, 42), head.Geometry);
        Assert.Equal(new Rect(0, 42, 0, 0), body.Geometry);
    }

    [Fact]
    public void ScrollGutterConvergenceDeflatesContentWithinSingleArrangeAcrossEngines()
    {
        // 内容尺寸在首帧排列中才越过视口：gutter 必须在同一次 arrange 的收敛 pass 内出现，
        // 内容宽度按桌面滚动条厚度（15px）扣减，后代几何保持传入 finalRect 的原点；
        // 隐藏/恢复超内容后 gutter 反向收敛。普通引擎与开启保留 flex 缓存的引擎必须一致，
        // 且嵌套外部 flex 的收敛快照在父 pass 之间正确嵌套。
        CreateGutterFixture(out var tRoot, out var tContent, out var tRow, out var tLeafA, out var tLeafB, out var tExtra);
        new LayoutEngine().MeasureAndArrange(tRoot, new Rect(7, 9, 100, 100));
        AssertGutterFixture(tRoot, tContent, tRow, tLeafA, tLeafB, tExtra, guttered: true);

        // 稳态：无变化的再次排列不得漂移。
        new LayoutEngine().MeasureAndArrange(tRoot, new Rect(7, 9, 100, 100));
        AssertGutterFixture(tRoot, tContent, tRow, tLeafA, tLeafB, tExtra, guttered: true);

        // gutter 消失方向：隐藏超内容并插入可见短块，pass 间 gutter 从 15 收敛到 0。
        tContent.Style.Set("display", "none");
        new LayoutEngine().MeasureAndArrange(tRoot, new Rect(7, 9, 100, 100));
        AssertGutterFixture(tRoot, tContent, tRow, tLeafA, tLeafB, tExtra, guttered: false);

        // 重新显示后 gutter 回归。
        tContent.Style.Remove("display");
        new LayoutEngine().MeasureAndArrange(tRoot, new Rect(7, 9, 100, 100));
        AssertGutterFixture(tRoot, tContent, tRow, tLeafA, tLeafB, tExtra, guttered: true);

        var retained = new LayoutEngine();
        retained.EnableRetainedFlexLayout();
        CreateGutterFixture(out var rRoot, out var rContent, out var rRow, out var rLeafA, out var rLeafB, out var rExtra);
        retained.MeasureAndArrange(rRoot, new Rect(7, 9, 100, 100));
        AssertGutterFixture(rRoot, rContent, rRow, rLeafA, rLeafB, rExtra, guttered: true);
        rContent.Style.Set("display", "none");
        retained.MeasureAndArrange(rRoot, new Rect(7, 9, 100, 100));
        AssertGutterFixture(rRoot, rContent, rRow, rLeafA, rLeafB, rExtra, guttered: false);
        rContent.Style.Remove("display");
        retained.MeasureAndArrange(rRoot, new Rect(7, 9, 100, 100));
        AssertGutterFixture(rRoot, rContent, rRow, rLeafA, rLeafB, rExtra, guttered: true);
        retained.ClearCachedYoga();

        Assert.Equal(tContent.Geometry, rContent.Geometry);
        Assert.Equal(tRow.Geometry, rRow.Geometry);
        Assert.Equal(tLeafA.Geometry, rLeafA.Geometry);
        Assert.Equal(tLeafB.Geometry, rLeafB.Geometry);
        Assert.Equal(tExtra.Geometry, rExtra.Geometry);
    }

    private static void CreateGutterFixture(
        out HTMLDivElement root, out HTMLDivElement content, out HTMLDivElement row,
        out HTMLDivElement leafA, out HTMLDivElement leafB, out HTMLDivElement extra)
    {
        // div 默认 display:block → 根走 CSS 正常流收敛循环；flex 行是外部 Yoga 根，
        // 在父收敛 pass 内嵌套完成自己的 arrange。
        root = new HTMLDivElement();
        root.Style.Set("overflow-y", "auto");
        root.Style.Set("height", "100px");
        content = new HTMLDivElement();
        content.Style.Set("height", "400px");
        root.Children.Add(content);
        row = new HTMLDivElement();
        row.Style.Set("display", "flex");
        leafA = new HTMLDivElement();
        leafA.Style.Set("width", "40px");
        leafA.Style.Set("height", "40px");
        leafB = new HTMLDivElement();
        leafB.Style.Set("width", "40px");
        leafB.Style.Set("height", "40px");
        row.Children.Add(leafA);
        row.Children.Add(leafB);
        root.Children.Add(row);
        extra = new HTMLDivElement();
        extra.Style.Set("height", "50px");
        root.Children.Add(extra);
    }

    private static void AssertGutterFixture(
        HTMLDivElement root, HTMLDivElement content, HTMLDivElement row,
        HTMLDivElement leafA, HTMLDivElement leafB, HTMLDivElement extra,
        bool guttered)
    {
        // guttered=true：内容超视口，桌面滚动条保留 15px 垂直 gutter，内容宽 100-15=85；
        // guttered=false：可见内容不超视口，无保留 gutter，内容满宽且行/后继块上移。
        var width = guttered ? 85f : 100f;
        var rowY = guttered ? 409f : 9f;
        Assert.Equal(new Rect(7, 9, 100, 100), root.Geometry);
        Assert.Equal(guttered, root.GetReservedScrollbarGutter() != default);
        if (guttered) Assert.Equal(new Rect(7, 9, width, 400), content.Geometry);
        Assert.Equal(new Rect(7, rowY, width, 40), row.Geometry);
        Assert.Equal(new Rect(7, rowY, 40, 40), leafA.Geometry);
        Assert.Equal(new Rect(47, rowY, 40, 40), leafB.Geometry);
        Assert.Equal(new Rect(7, rowY + 40, width, 50), extra.Geometry);
    }

    private static string Desc(Element? element) =>
        element == null ? "null" : $"{element.GetType().Name}@{element.Geometry}";

    private static (AppWindow window, UIBodyElement body) CreateWindowBodyWithTallContent()
    {
        var (window, body) = CreateWindowBody();
        window.Content!.Style.Set("height", "400px");
        return (window, body);
    }

    private static (AppWindow window, UIBodyElement body, Controls.TitleBar titleBar) CreateWindowWithCustomTitle(
        float preferredTitleHeight)
    {
        var (window, body) = CreateWindowBody();
        var titleBar = new Controls.TitleBar { PreferredHeight = preferredTitleHeight };
        window.LoadCustomTitleBar(titleBar);
        return (window, body, titleBar);
    }

    private static DesktopApplication CreateRealApplication(AppWindow window)
    {
        var application = new DesktopApplication(window);
        application.PrepareSession();
        SetPrivateField(application, "_host", new TestHost());
        SetPrivateField(application, "_renderContext", new CountingRenderContext());
        return application;
    }

    private static void InvokeRenderFrame(DesktopApplication application) =>
        Invoke(application, "RenderFrame");

    private static void InvokeHandleTick(DesktopApplication application) =>
        Invoke(application, "HandleTick");

    private static void InvokeHandleFrameRequest(DesktopApplication application, Event request) =>
        Invoke(application, "HandleFrameRequest", request);

    private static void Invoke(DesktopApplication application, string name, params object[] args)
    {
        var method = typeof(DesktopApplication).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(application, args);
    }

    private sealed class CountingRenderContext : IRenderContext
    {
        public Size CanvasSize => new(100, 100);
        public float DpiScale => 1;
        public void PushTransform(System.Numerics.Matrix3x2 matrix) { }
        public void PopTransform() { }
        public void PushClip(Rect rect) { }
        public void PushClip(Square.Graphics.Geometry geometry) { }
        public void PopClip() { }
        public void FillRect(Rect rect, Brush brush) { }
        public void DrawRect(Rect rect, Pen pen) { }
        public void FillPath(PathGeometry path, Brush brush) { }
        public void DrawPath(PathGeometry path, Pen pen) { }
        public void FillGeometry(Square.Graphics.Geometry geometry, Brush brush) { }
        public void DrawGeometry(Square.Graphics.Geometry geometry, Pen pen) { }
        public void DrawText(TextLayout text, Point origin, Brush brush) { }
        public void DrawImage(Square.Graphics.Image image, Rect dest, Rect? source = null) { }
        public void PushLayer(Rect bounds, float opacity) { }
        public void PopLayer() { }
        public void Clear(Color color) { }
        public void Clear(Color color, Rect rect) { }
        public void Flush() { }
        public void Present() { }
        public void Present(IReadOnlyList<Rect>? dirtyRects) { }
        public void Dispose() { }
    }

    private sealed class TestHost : IPlatformHost
    {
        public Size ClientSize => new(100, 100);
        public float DpiScale => 1;
        public bool IsRunning => true;
        public string Title { get; set; } = "document-scrollbar";
        public CursorKind Cursor { get; set; }
        public KeyModifiers Modifiers => KeyModifiers.None;
        public event Action<Size>? SizeChanged { add { } remove { } }
        public event Action<Point, MouseAction, MouseButton>? MouseEvent { add { } remove { } }
        public event Action<WheelInput>? WheelEvent { add { } remove { } }
        public event Action<int, KeyAction>? KeyEvent { add { } remove { } }
        public event Action<string>? TextInput { add { } remove { } }
        public event Action? Tick { add { } remove { } }
        public void Show() { }
        public void Close() { }
        public IRenderContext CreateRenderContext() => throw new NotSupportedException();
        public void PumpEvents() { }
        public void SetTextInputRect(Rect rect) { }
        public string GetClipboardText() => "";
        public void SetClipboardText(string text) { }
        public void Dispose() { }
    }

    private static (AppWindow window, UIBodyElement body) CreateWindowBody()
    {
        var window = new AppWindow("Document scrollbar")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Desktop
        };
        window.Load(new Controls.View());
        return (window, ((UIDocument)window.Document).Body);
    }
}
