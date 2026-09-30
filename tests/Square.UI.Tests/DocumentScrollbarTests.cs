using System.Reflection;
using Square.Events;
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

    private static string Desc(Element? element) =>
        element == null ? "null" : $"{element.GetType().Name}@{element.Geometry}";

    private static (AppWindow window, UIBodyElement body) CreateWindowBodyWithTallContent()
    {
        var (window, body) = CreateWindowBody();
        window.Content!.Style.Set("height", "400px");
        return (window, body);
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
