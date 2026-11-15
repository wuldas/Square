using Square.Controls;
using Square.Events;
using Square.Graphics;
using Square.Backends;
using Square.Hosting;
using Square.Extensions.Routing;
using Square.Platform;
using Square.Rendering;
using Square.Runtime;
using Square.Sample;
using Square.Sample.Components;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

public class SampleTabsScrollingTests
{
    [Theory]
    [InlineData(false, 900, 942)]
    [InlineData(true, 900, 942)]
    [InlineData(false, 885.3333f, 942.6667f)]
    [InlineData(true, 885.3333f, 942.6667f)]
    [InlineData(false, 640.6667f, 400.6667f)]
    [InlineData(true, 640.6667f, 400.6667f)]
    public void SampleWindowFitsViewportAndScrollsOnlyItsTabPanel(bool vue, float width, float height)
    {
        UIElement component = vue ? new Square.Sample.Vue.Components.Main() : new Main();
        var window = new AppWindow("sample-scroll", 900, 980);
        window.UseRouter(routes => routes.Map("/", static () => new RouteHomePage()));
        window.Load(component);
        using var session = new ApplicationSession(window, new SampleHost(new Size(width, height)));
        session.Attach();
        session.ProcessFrame();
        var document = (UIDocument)window.Document;
        var panel = Assert.Single(component.QueryAll<View>(), view => view.ClassList.Contains("tab-panels"));

        Assert.False(document.Body.GetScrollbarMetrics().HasVertical,
            $"vue={vue}, viewport={width}x{height}, body={document.Body.Geometry}, content={document.Body.ScrollContentSize}, component={component.Geometry}");
        Assert.False(document.Body.GetScrollbarMetrics().HasHorizontal);
        Assert.True(panel.Geometry.Bottom <= document.Body.Geometry.Bottom);
        if (height < 500)
        {
            Assert.True(panel.GetScrollbarMetrics().HasVertical);
            var target = panel.Children.First(child => child.IsVisible);
            target.DispatchTrusted(StandardEvents.CreateWheel(0, 120));
            panel.AdvanceSmoothScroll(0.2f);
            Assert.True(panel.ScrollTop > 0);
            Assert.Equal(0, document.Body.ScrollTop);
        }
    }

    private sealed class SampleHost(Size size) : IPlatformHost
    {
        public Size ClientSize => size;
        public float DpiScale => 1.5f;
        public bool IsRunning { get; private set; }
        public string Title { get; set; } = "sample-scroll";
        public CursorKind Cursor { get; set; }
        public KeyModifiers Modifiers => KeyModifiers.None;
        public event Action<Size>? SizeChanged { add { } remove { } }
        public event Action<Point, MouseAction, MouseButton>? MouseEvent { add { } remove { } }
        public event Action<WheelInput>? WheelEvent { add { } remove { } }
        public event Action<int, KeyAction>? KeyEvent { add { } remove { } }
        public event Action<string>? TextInput { add { } remove { } }
        public event Action? Tick { add { } remove { } }
        public void Show() => IsRunning = true;
        public void Close() => IsRunning = false;
        public IRenderContext CreateRenderContext() => new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = size,
            DpiScale = DpiScale
        });
        public void PumpEvents() => throw new InvalidOperationException("External sample test must not pump native events.");
        public void SetTextInputRect(Rect rect) { }
        public string GetClipboardText() => "";
        public void SetClipboardText(string text) { }
        public void Dispose() => IsRunning = false;
    }

    [Fact]
    public void SmallWindowScrollsOnlyTheActiveTabPanel()
    {
        var component = new Main();
        component.BuildElementTree();
        ((IComponentLifecycle)component).OnAttached();
        var app = Assert.IsType<View>(Assert.Single(component.Children));
        var tabList = Assert.Single(app.QueryAll<View>(), view => view.ClassList.Contains("tab-list"));
        var tabPanels = Assert.Single(app.QueryAll<View>(), view => view.ClassList.Contains("tab-panels"));
        var textPage = Assert.Single(app.QueryAll<TextSamplesPage>());
        var layout = new LayoutEngine();

        layout.Measure(app, new Size(900, 400));
        layout.Arrange(app, new Rect(0, 0, 900, 400));

        Assert.Equal(42, tabList.Geometry.Height);
        Assert.True(tabPanels.Geometry.Bottom <= app.Geometry.Bottom);
        Assert.True(tabPanels.ScrollContentSize.Height > tabPanels.Geometry.Height);
        var appTop = app.Geometry.Top;
        var tabListTop = tabList.Geometry.Top;

        textPage.DispatchTrusted(StandardEvents.CreateWheel(0, 120));

        tabPanels.AdvanceSmoothScroll(0.08f);
        Assert.True(tabPanels.ScrollTop > 0);
        Assert.Equal(appTop, app.Geometry.Top);
        Assert.Equal(tabListTop, tabList.Geometry.Top);
        ((IComponentLifecycle)component).OnDetached();
    }
}
