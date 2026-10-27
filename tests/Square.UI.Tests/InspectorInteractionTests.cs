using System.Reflection;
using Square.Controls;
using Square.Events;
using Square.Graphics;
using Square.Hosting;
using Square.Html;
using Square.Platform;
using Square.Runtime;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

/// <summary>
/// 真实 DesktopApplication 上的 Inspector 点选/高亮行为：输入互斥、所有权接管、
/// 取消防复活与盒模型四层环绘制，均通过输入、事件与渲染观察验证。
/// </summary>
public sealed class InspectorInteractionTests
{
    private static readonly ElementInspectionHighlightOptions InspectorOptions = new(
        Color.FromRgba(255, 0, 0, 200),
        Color.FromRgba(0, 255, 0, 200),
        Color.FromRgba(0, 0, 255, 200),
        Color.FromRgba(255, 0, 255, 200));

    [Fact]
    public async Task EscapeInInspectModeDoesNotReachGlobalShortcuts()
    {
        var window = new AppWindow("Inspector input");
        window.Load(new View());
        var application = CreateRealApplication(window, out _);
        try
        {
            var globalKeyDowns = 0;
            window.GlobalKeyEvent += (_, action) => { if (action == KeyAction.Down) globalKeyDowns++; };
            await DrainAsync(application, window.SetInspectorModeAsync(true));

            InvokeKey(application, 27);
            Assert.Equal(0, globalKeyDowns);
            InvokeKey(application, 65);
            Assert.Equal(1, globalKeyDowns);
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task InspectSelectionConsumesGestureAndNextNormalClickClicksButton()
    {
        var button = new Button("Add activity");
        var clicks = 0;
        button.AddEventListener(StandardEvents.Click, _ => clicks++);
        var window = new AppWindow("Inspector selection");
        window.Load(button);
        var application = CreateRealApplication(window, out _);
        try
        {
            InvokeRenderFrame(application);
            var selected = new List<(long OwnerId, int DebugId)>();
            window.InspectorNodeSelected += (owner, id) => selected.Add((owner, id));

            await DrainAsync(application, window.SetInspectorModeAsync(true, InspectorOptions, 55));

            var center = button.Geometry.Center;
            InvokeHandleMouse(application, center, MouseAction.Down);
            InvokeHandleMouse(application, center, MouseAction.Up);

            var selection = Assert.Single(selected);
            Assert.Equal(55, selection.OwnerId);
            Assert.Equal(button.DebugId, selection.DebugId);
            Assert.Equal(0, clicks);

            // 点选一次性退出后，下一次正常点击必须恢复执行按钮。
            InvokeHandleMouse(application, center, MouseAction.Down);
            InvokeHandleMouse(application, center, MouseAction.Up);
            Assert.Equal(1, clicks);
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task TouchInspectSelectionConsumesGestureWithoutAppPointerEvents()
    {
        var button = new Button("Add activity");
        var clicks = 0;
        var pointerDowns = 0;
        var pointerMoves = 0;
        button.AddEventListener(StandardEvents.Click, _ => clicks++);
        button.AddEventListener(StandardEvents.PointerDown, _ => pointerDowns++);
        button.AddEventListener(StandardEvents.PointerMove, _ => pointerMoves++);
        var window = new AppWindow("Inspector touch");
        window.Load(button);
        var application = CreateRealApplication(window, out _);
        try
        {
            InvokeRenderFrame(application);
            var selected = new List<long>();
            window.InspectorNodeSelected += (owner, _) => selected.Add(owner);
            await DrainAsync(application, window.SetInspectorModeAsync(true, InspectorOptions, 66));

            var center = button.Geometry.Center;
            InvokeHandlePointer(application,
                new PointerInput(center, PointerAction.Move, 7, PointerDeviceKind.Touch));
            InvokeHandlePointer(application,
                new PointerInput(center, PointerAction.Down, 7, PointerDeviceKind.Touch, MouseButton.Left));
            InvokeHandlePointer(application,
                new PointerInput(center, PointerAction.Up, 7, PointerDeviceKind.Touch, MouseButton.Left));

            Assert.Equal([66], selected);
            Assert.Equal(0, pointerDowns);
            Assert.Equal(0, pointerMoves);
            Assert.Equal(0, clicks);

            InvokeHandleMouse(application, center, MouseAction.Down);
            InvokeHandleMouse(application, center, MouseAction.Up);
            Assert.Equal(1, clicks);
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task EscapeInInspectModeCancelsInspectionBeforePopupAndGlobalShortcuts()
    {
        var content = new View();
        var select = new Select { Options = ["red", "green", "blue"] };
        content.Children.Add(select);
        var window = new AppWindow("Inspector escape popup");
        window.Load(content);
        var application = CreateRealApplication(window, out _);
        try
        {
            InvokeRenderFrame(application);
            var globalKeyDowns = 0;
            window.GlobalKeyEvent += (_, action) => { if (action == KeyAction.Down) globalKeyDowns++; };
            var canceledOwners = new List<long>();
            window.InspectorModeCanceled += canceledOwners.Add;

            var center = select.Geometry.Center;
            InvokeHandleMouse(application, center, MouseAction.Down);
            InvokeHandleMouse(application, center, MouseAction.Up);
            Assert.True(select.IsPopupOpen);

            await DrainAsync(application, window.SetInspectorModeAsync(true, InspectorOptions, 55));
            InvokeKey(application, 27);

            Assert.Equal(0, globalKeyDowns);
            Assert.True(select.IsPopupOpen);
            Assert.Equal([55], canceledOwners);

            // 普通输入恢复：Escape 重新按 popup 优先级工作（先通知全局、再关 popup），
            // 随后按键继续到达全局快捷键。恢复后的两次按键都可见。
            InvokeKey(application, 27);
            Assert.False(select.IsPopupOpen);
            InvokeKey(application, 65);
            Assert.Equal(2, globalKeyDowns);
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task EscapeInInspectModeDoesNotReachFocusedEditor()
    {
        var content = new View();
        var editor = new TextArea();
        editor.Style.Set("width", "200px");
        editor.Style.Set("height", "60px");
        content.Children.Add(editor);
        var window = new AppWindow("Inspector escape editor");
        window.Load(content);
        var application = CreateRealApplication(window, out _);
        try
        {
            InvokeRenderFrame(application);
            var editorKeyDowns = 0;
            editor.AddEventListener(StandardEvents.KeyDown, _ => editorKeyDowns++);
            var globalKeyDowns = 0;
            window.GlobalKeyEvent += (_, action) => { if (action == KeyAction.Down) globalKeyDowns++; };

            var center = editor.Geometry.Center;
            InvokeHandleMouse(application, center, MouseAction.Down);
            InvokeHandleMouse(application, center, MouseAction.Up);
            InvokePrivate(application, "HandleTextInput", "abc");
            Assert.Equal("abc", editor.Value);
            InvokeKey(application, 65);
            Assert.Equal(1, editorKeyDowns);
            Assert.Equal(1, globalKeyDowns);

            await DrainAsync(application, window.SetInspectorModeAsync(true, InspectorOptions, 55));
            InvokeKey(application, 27);

            // Escape 被点选取消消费：不进入焦点编辑器，也不进入全局快捷键。
            Assert.Equal(1, editorKeyDowns);
            Assert.Equal(1, globalKeyDowns);
            Assert.Equal("abc", editor.Value);

            // 普通输入恢复：按键与文本重新到达仍持焦点的编辑器。
            InvokeKey(application, 66);
            Assert.Equal(2, editorKeyDowns);
            Assert.Equal(2, globalKeyDowns);
            InvokePrivate(application, "HandleTextInput", "d");
            Assert.Equal("abcd", editor.Value);
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task InspectorOwnershipTakeoverAndForeignReleaseAreIsolated()
    {
        var content = new View();
        var box = new HTMLDivElement();
        box.Style.Set("width", "80px");
        box.Style.Set("height", "40px");
        content.Children.Add(box);
        var window = new AppWindow("Inspector ownership");
        window.Load(content);
        var application = CreateRealApplication(window, out var recording);
        try
        {
            InvokeRenderFrame(application);
            var canceledOwners = new List<long>();
            window.InspectorModeCanceled += canceledOwners.Add;

            await DrainAsync(application, window.SetInspectorModeAsync(true, InspectorOptions, 55));
            await DrainAsync(application, window.SetInspectorHighlightAsync(box.DebugId, InspectorOptions, 66));

            // B 接管时 A 的活跃点选被结束并通知。
            Assert.Equal([55], canceledOwners);

            InvokeRenderFrame(application);
            AssertInspectorOverlayDrawn(recording);

            // A 的 clear/hide/release 都是外部操作：不得影响 B 的状态。
            await DrainAsync(application, window.ClearInspectorHighlightAsync(55));
            await DrainAsync(application, window.SetInspectorModeAsync(false, options: null, ownerId: 55));
            InvokeRenderFrame(application);
            AssertInspectorOverlayDrawn(recording);

            var oldRelease = new Thread(() => window.ReleaseInspectorSession(55));
            oldRelease.Start();
            Assert.True(oldRelease.Join(TimeSpan.FromSeconds(5)));
            application.Dispatcher.Run();
            InvokeRenderFrame(application);
            AssertInspectorOverlayDrawn(recording);

            // B 断开：下个 Dispatcher turn 清理自己的状态。
            var currentRelease = new Thread(() => window.ReleaseInspectorSession(66));
            currentRelease.Start();
            Assert.True(currentRelease.Join(TimeSpan.FromSeconds(5)));
            application.Dispatcher.Run();
            recording.Fills.Clear();
            recording.Strokes.Clear();
            InvokeRenderFrame(application);
            AssertInspectorOverlayNotDrawn(recording);
            Assert.Equal([55], canceledOwners);
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task CanceledQueuedHighlightCannotResurrectReleasedState()
    {
        var content = new View();
        var first = new HTMLDivElement();
        first.Style.Set("width", "60px");
        first.Style.Set("height", "30px");
        var second = new HTMLDivElement();
        second.Style.Set("width", "60px");
        second.Style.Set("height", "30px");
        content.Children.Add(first);
        content.Children.Add(second);
        var window = new AppWindow("Inspector cancel queue");
        window.Load(content);
        var application = CreateRealApplication(window, out var recording);
        try
        {
            InvokeRenderFrame(application);
            await DrainAsync(application, window.SetInspectorHighlightAsync(first.DebugId, InspectorOptions, 77));
            InvokeRenderFrame(application);
            AssertInspectorOverlayDrawn(recording);

            using var cancellation = new CancellationTokenSource();
            var queued = window.SetInspectorHighlightAsync(second.DebugId, InspectorOptions, 77, cancellation.Token);
            cancellation.Cancel();
            window.ReleaseInspectorSession(77);
            application.Dispatcher.Run();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

            recording.Fills.Clear();
            recording.Strokes.Clear();
            InvokeRenderFrame(application);
            AssertInspectorOverlayNotDrawn(recording);
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task HighlightRingsUseConfiguredColorsAsNonOverlappingLayers()
    {
        var content = new View();
        var box = new HTMLDivElement();
        box.Style.Set("width", "120px");
        box.Style.Set("height", "60px");
        box.Style.Set("margin", "10px");
        box.Style.Set("border", "6px solid black");
        box.Style.Set("padding", "8px");
        content.Children.Add(box);
        var window = new AppWindow("Inspector overlay rings");
        window.Load(content);
        var application = CreateRealApplication(window, out var recording);
        try
        {
            InvokeRenderFrame(application);
            await DrainAsync(application, window.SetInspectorHighlightAsync(box.DebugId, InspectorOptions, 0));
            recording.Fills.Clear();
            recording.Strokes.Clear();
            InvokeRenderFrame(application);

            var fills = recording.Fills.Where(f => IsInspectorLayer(f.Color)).ToList();

            // 不同层的填充矩形两两不相交。
            for (var i = 0; i < fills.Count; i++)
            {
                for (var j = i + 1; j < fills.Count; j++)
                {
                    if (fills[i].Color == fills[j].Color) continue;
                    Assert.False(fills[i].Rect.IntersectsWith(fills[j].Rect),
                        $"layers must not overlap: {fills[i].Rect} vs {fills[j].Rect}");
                }
            }

            // 面积精确：各层条带面积之和等于真实几何差。
            var geometry = box.Geometry;
            var paddingRect = new Rect(
                geometry.X + 6, geometry.Y + 6, geometry.Width - 12, geometry.Height - 12);
            var contentRect = new Rect(
                paddingRect.X + 8, paddingRect.Y + 8, paddingRect.Width - 16, paddingRect.Height - 16);
            var marginRect = new Rect(
                geometry.X - 10, geometry.Y - 10, geometry.Width + 20, geometry.Height + 20);
            AssertLayerArea(fills, InspectorOptions.ContentColor, Area(contentRect));
            AssertLayerArea(fills, InspectorOptions.PaddingColor, Area(paddingRect) - Area(contentRect));
            AssertLayerArea(fills, InspectorOptions.BorderColor, Area(geometry) - Area(paddingRect));
            AssertLayerArea(fills, InspectorOptions.MarginColor, Area(marginRect) - Area(geometry));

            // 绘制位置等于真实盒模型几何（无滚动/弹层时客户区偏移为零）。
            Assert.Equal(marginRect, BoundingBox(fills.Where(f => f.Color == InspectorOptions.MarginColor)));
            Assert.Equal(geometry, BoundingBox(fills.Where(f => f.Color == InspectorOptions.BorderColor)));
            Assert.Equal(paddingRect, BoundingBox(fills.Where(f => f.Color == InspectorOptions.PaddingColor)));
            Assert.Equal(contentRect, BoundingBox(fills.Where(f => f.Color == InspectorOptions.ContentColor)));
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public async Task HighlightWithoutProvableBoxModelDrawsOnlyRealBorderOutline()
    {
        var content = new View();
        var box = new HTMLDivElement();
        box.Style.Set("width", "80px");
        box.Style.Set("height", "40px");
        // auto/% 导致四层边不可证明：不得伪造 edges。
        box.Style.Set("margin", "5%");
        content.Children.Add(box);
        var window = new AppWindow("Inspector overlay fallback");
        window.Load(content);
        var application = CreateRealApplication(window, out var recording);
        try
        {
            InvokeRenderFrame(application);
            await DrainAsync(application, window.SetInspectorHighlightAsync(box.DebugId, InspectorOptions, 0));
            recording.Fills.Clear();
            recording.Strokes.Clear();
            InvokeRenderFrame(application);

            Assert.DoesNotContain(recording.Fills, f => IsInspectorLayer(f.Color));
            var outlines = recording.Strokes
                .Where(s => s.Color == InspectorOptions.BorderColor && MathF.Abs(s.Width - 2) < 0.01f)
                .ToList();
            var outline = Assert.Single(outlines);
            Assert.Equal(box.Geometry, outline.Rect);
        }
        finally
        {
            application.DetachSession();
        }
    }

    private static void AssertInspectorOverlayDrawn(RecordingRenderContext recording) =>
        Assert.Contains(recording.Fills, f => f.Color == InspectorOptions.ContentColor);

    private static void AssertInspectorOverlayNotDrawn(RecordingRenderContext recording)
    {
        Assert.DoesNotContain(recording.Fills, f => IsInspectorLayer(f.Color));
        Assert.DoesNotContain(recording.Strokes, s => IsInspectorLayer(s.Color));
    }

    private static bool IsInspectorLayer(Color color) =>
        color == InspectorOptions.ContentColor || color == InspectorOptions.PaddingColor ||
        color == InspectorOptions.BorderColor || color == InspectorOptions.MarginColor;

    private static float Area(Rect rect) => rect.Width * rect.Height;

    private static Rect BoundingBox(IEnumerable<RecordedFill> fills)
    {
        var bounds = Rect.Empty;
        foreach (var fill in fills)
            bounds = bounds == Rect.Empty ? fill.Rect : Rect.Union(bounds, fill.Rect);
        return bounds;
    }

    private static void AssertLayerArea(List<RecordedFill> fills, Color color, float expectedArea)
    {
        var actual = fills.Where(f => f.Color == color).Sum(f => Area(f.Rect));
        Assert.True(MathF.Abs(actual - expectedArea) < 0.5f,
            $"layer {color} area {actual} != expected {expectedArea}");
    }

    private static async Task DrainAsync(DesktopApplication application, Task task)
    {
        application.Dispatcher.Run();
        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static DesktopApplication CreateRealApplication(AppWindow window, out RecordingRenderContext recording)
    {
        var application = new DesktopApplication(window);
        application.PrepareSession();
        SetPrivateField(application, "_host", new TestHost());
        recording = new RecordingRenderContext();
        SetPrivateField(application, "_renderContext", recording);
        return application;
    }

    private static void InvokeRenderFrame(DesktopApplication application) =>
        InvokePrivate(application, "RenderFrame");

    private static void InvokeHandleMouse(
        DesktopApplication application,
        Point point,
        MouseAction action,
        MouseButton button = MouseButton.Left) =>
        InvokePrivate(application, "HandleMouse", point, action, button);

    private static void InvokeHandlePointer(DesktopApplication application, PointerInput input) =>
        InvokePrivate(application, "HandlePointerInput", input);

    private static void InvokeKey(DesktopApplication application, int keyCode) =>
        InvokePrivate(application, "HandleKey", keyCode, KeyAction.Down);

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

    private sealed record RecordedFill(Rect Rect, Color Color);

    private sealed record RecordedStroke(Rect Rect, Color Color, float Width);

    private sealed class RecordingRenderContext : IRenderContext
    {
        public readonly List<RecordedFill> Fills = [];
        public readonly List<RecordedStroke> Strokes = [];

        public Size CanvasSize => new(320, 240);
        public float DpiScale => 1;

        public void PushTransform(System.Numerics.Matrix3x2 matrix) { }
        public void PopTransform() { }
        public void PushClip(Rect rect) { }
        public void PushClip(Geometry geometry) { }
        public void PopClip() { }

        public void FillRect(Rect rect, Brush brush)
        {
            if (brush is SolidColorBrush solid) Fills.Add(new RecordedFill(rect, solid.Color));
        }

        public void DrawRect(Rect rect, Pen pen)
        {
            if (pen.Brush is SolidColorBrush solid) Strokes.Add(new RecordedStroke(rect, solid.Color, pen.Width));
        }

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
        public string Title { get; set; } = "inspector-interaction";
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
