using System.Reflection;
using Square.Controls;
using Square.Graphics;
using Square.Hosting;
using Square.Html;
using Square.Platform;
using Square.Runtime;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

/// <summary>
/// 真实 Hot Reload 重建路径与真实自定义元素生命周期上的窗口诊断事件：
/// 通过实际 DesktopApplication.ApplyHotReload 与实际 attach 驱动失败，
/// 观察所属窗口 DiagnosticMessage 的级别与完整消息，以及失败后后续元素照常推进。
/// 不使用伪造的 Emit 或只验证转发的接线替身。
/// </summary>
public sealed class NativeDiagnosticTests
{
    [Fact]
    public void HotReloadRebuildFailureForwardsErrorDiagnosticAndLaterRebuildsContinue()
    {
        var (window, application, component) = CreateAppWithReloadableComponent();
        var diagnostics = new List<(System.Diagnostics.TraceEventType Level, string Text)>();
        window.DiagnosticMessage += (level, text) => diagnostics.Add((level, text));
        application.PrepareSession();
        SetPrivateField(application, "_host", new TestHost());
        SetPrivateField(application, "_renderContext", new RecordingRenderContext());

        var failure = new InvalidOperationException("rebuild exploded");
        component.RebuildFailure = failure;
        InvokePrivate(application, "ApplyHotReload");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(System.Diagnostics.TraceEventType.Error, diagnostic.Level);
        Assert.Equal(
            $"Square Hot Reload failed to rebuild {typeof(ReloadableComponent).FullName}: {failure}",
            diagnostic.Text);
        Assert.Equal(1, component.Rebuilds);
        Assert.Same(component, window.Content);

        // 失败被吞掉后组件实例保留；清除故障后下一次真实重建照常完成，不再产生诊断。
        component.RebuildFailure = null;
        InvokePrivate(application, "ApplyHotReload");

        Assert.Equal(2, component.Rebuilds);
        Assert.Single(diagnostics);
        Assert.True(component.IsAttached);
    }

    [Fact]
    public void HotReloadLifecycleRestoreFailureForwardsErrorDiagnosticAndRebuildStillApplied()
    {
        var (window, application, component) = CreateAppWithReloadableComponent();
        application.PrepareSession();
        SetPrivateField(application, "_host", new TestHost());
        SetPrivateField(application, "_renderContext", new RecordingRenderContext());
        ((IComponentLifecycle)component).OnLoaded();

        var diagnostics = new List<(System.Diagnostics.TraceEventType Level, string Text)>();
        window.DiagnosticMessage += (level, text) => diagnostics.Add((level, text));
        var failure = new InvalidOperationException("restore exploded");
        component.RestoreFailure = failure;
        InvokePrivate(application, "ApplyHotReload");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(System.Diagnostics.TraceEventType.Error, diagnostic.Level);
        Assert.Equal(
            $"Square Hot Reload failed to restore lifecycle for {typeof(ReloadableComponent).FullName}: {failure}",
            diagnostic.Text);
        Assert.Equal(1, component.Rebuilds);

        // 生命周期恢复失败不影响组件继续参与后续重建。
        component.RestoreFailure = null;
        InvokePrivate(application, "ApplyHotReload");

        Assert.Equal(2, component.Rebuilds);
        Assert.Single(diagnostics);
    }

    [Fact]
    public void HotReloadWithoutGeneratedTopLevelComponentWarnsAndLeavesNestedComponentUntouched()
    {
        var window = new AppWindow("Diagnostics: hot reload warning");
        var root = new View();
        var nested = new ReloadableComponent();
        root.Children.Add(nested);
        window.Load(root);
        var application = new DesktopApplication(window);
        var diagnostics = new List<(System.Diagnostics.TraceEventType Level, string Text)>();
        window.DiagnosticMessage += (level, text) => diagnostics.Add((level, text));
        application.PrepareSession();
        SetPrivateField(application, "_host", new TestHost());
        SetPrivateField(application, "_renderContext", new RecordingRenderContext());

        InvokePrivate(application, "ApplyHotReload");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(System.Diagnostics.TraceEventType.Warning, diagnostic.Level);
        Assert.Equal(
            $"Square Hot Reload requires a generated top-level component: {typeof(View).FullName}",
            diagnostic.Text);
        Assert.Equal(0, nested.Rebuilds);
    }

    [Fact]
    public void CustomElementCallbackFailureForwardsOwningWindowDiagnosticAndLaterElementsContinue()
    {
        var window = new AppWindow("Diagnostics: custom elements");
        var otherWindow = new AppWindow("Diagnostics: other window");
        var document = (UIDocument)window.Document;
        document.CustomElements.DefineAutonomous("acme-failing", static () => new FailingBadge());
        document.CustomElements.DefineAutonomous("acme-badge", static () => new StatusBadge(), "status");

        var diagnostics = new List<(System.Diagnostics.TraceEventType Level, string Text)>();
        var otherDiagnostics = new List<(System.Diagnostics.TraceEventType Level, string Text)>();
        window.DiagnosticMessage += (level, text) => diagnostics.Add((level, text));
        otherWindow.DiagnosticMessage += (level, text) => otherDiagnostics.Add((level, text));

        var root = new View();
        var failing = Assert.IsType<FailingBadge>(document.CreateElement("acme-failing"));
        var badge = Assert.IsType<StatusBadge>(document.CreateElement("acme-badge"));
        root.Children.Add(failing);
        root.Children.Add(badge);
        window.Load(root);

        var application = new DesktopApplication(window);
        application.PrepareSession();
        SetPrivateField(application, "_host", new TestHost());
        SetPrivateField(application, "_renderContext", new RecordingRenderContext());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(System.Diagnostics.TraceEventType.Error, diagnostic.Level);
        Assert.Equal("Custom element 'acme-failing' callback failed: connect exploded", diagnostic.Text);
        Assert.Single(document.CustomElements.Diagnostics);
        // 所属窗口之外收不到；后续元素照常连接与更新。
        Assert.Empty(otherDiagnostics);
        Assert.Equal(1, badge.Connected);
        Assert.Equal(0, badge.Disconnected);

        badge.SetAttribute("status", "ready");
        var attributeChange = Assert.Single(badge.Changes);
        Assert.Equal(("status", null, "ready"), attributeChange);

        var failingLater = Assert.IsType<FailingBadge>(document.CreateElement("acme-failing"));
        root.Children.Add(failingLater);

        Assert.Equal(2, diagnostics.Count);
        Assert.Equal("Custom element 'acme-failing' callback failed: connect exploded", diagnostics[1].Text);
        Assert.Equal(2, document.CustomElements.Diagnostics.Count);
        Assert.Empty(otherDiagnostics);
    }

    private static (AppWindow Window, DesktopApplication Application, ReloadableComponent Component)
        CreateAppWithReloadableComponent()
    {
        var window = new AppWindow("Diagnostics: hot reload");
        var component = new ReloadableComponent();
        window.Load(component);
        var application = new DesktopApplication(window);
        return (window, application, component);
    }

    private static void InvokePrivate(DesktopApplication application, string name, params object?[] args)
    {
        var method = typeof(DesktopApplication).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(application, args);
    }

    private static void SetPrivateField<T>(DesktopApplication application, string name, T value)
    {
        var field = typeof(DesktopApplication).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(application, value);
    }

    public sealed class ReloadableComponent : View, ISquareHotReloadComponent
    {
        public Exception? RebuildFailure { get; set; }
        public Exception? RestoreFailure { get; set; }
        public int Rebuilds { get; private set; }

        public bool HasHotReloadChanges => true;

        public void RebuildAfterHotReload()
        {
            Rebuilds++;
            if (RebuildFailure != null) throw RebuildFailure;
        }

        protected override void OnLoadedCore()
        {
            if (RestoreFailure != null) throw RestoreFailure;
        }
    }

    public sealed class StatusBadge : HTMLElement
    {
        public List<(string Name, string? OldValue, string? NewValue)> Changes { get; } = [];
        public int Connected;
        public int Disconnected;

        public StatusBadge() : base("acme-badge") { }

        protected override void ConnectedCallback() => Connected++;
        protected override void DisconnectedCallback() => Disconnected++;
        protected override void AttributeChangedCallback(string name, string? oldValue, string? newValue) =>
            Changes.Add((name, oldValue, newValue));
    }

    public sealed class FailingBadge : HTMLElement
    {
        public FailingBadge() : base("acme-failing") { }

        protected override void ConnectedCallback() => throw new InvalidOperationException("connect exploded");
    }

    private sealed class RecordingRenderContext : IRenderContext
    {
        public Size CanvasSize => new(320, 240);
        public float DpiScale => 1;

        public void PushTransform(System.Numerics.Matrix3x2 matrix) { }
        public void PopTransform() { }
        public void PushClip(Rect rect) { }
        public void PushClip(Geometry geometry) { }
        public void PopClip() { }
        public void FillRect(Rect rect, Brush brush) { }
        public void DrawRect(Rect rect, Pen pen) { }
        public void FillPath(PathGeometry path, Brush brush) { }
        public void DrawPath(PathGeometry path, Pen pen) { }
        public void FillGeometry(Geometry geometry, Brush brush) { }
        public void DrawGeometry(Geometry geometry, Pen pen) { }
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
        public Size ClientSize => new(320, 240);
        public float DpiScale => 1;
        public bool IsRunning => true;
        public string Title { get; set; } = "native-diagnostics";
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
}
