using System.Numerics;
using System.Reflection;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Controls;
using Square.Extensions.CodeEditor;
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

namespace Square.Extensions.CodeEditor.Tests;

public sealed class SharedScrollbarTests
{
    [Fact]
    public void CodeEditorUsesSharedScrollbarMetrics()
    {
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Range(0, 80).Select(static i => $"line-{i}")),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        var method = typeof(CodeEditor).GetMethod("GetScrollMetrics", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(typeof(ScrollbarMetrics), method!.ReturnType);

        var font = new Font { Family = "monospace", Size = 13 };
        var metrics = Assert.IsType<ScrollbarMetrics>(method.Invoke(editor, [font, 16f]));
        Assert.True(metrics.HasVertical);
        Assert.Equal(9, metrics.ThumbThickness);
    }

    [Fact]
    public void CodeEditorWebKitPseudoStylesStyleOwnChrome()
    {
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Range(0, 80).Select(static i => $"line-{i}")),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        var engine = new CssEngine();
        engine.LoadStyleSheet(new CssParser(new CssTokenizer("""
            CodeEditor::-webkit-scrollbar { width: 12px; }
            CodeEditor::-webkit-scrollbar-thumb { background: #123456; }
            """).Tokenize()).Parse());
        engine.ApplyStyles(editor);

        var metrics = GetEditorScrollMetrics(editor);
        Assert.Equal(12, metrics.ScrollbarThickness);
        var paintMethod = typeof(CodeEditor).GetMethod("PaintScrollBars", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(paintMethod);
        var context = new RecordingRenderContext();
        paintMethod!.Invoke(editor,
        [
            context,
            new CodeEditorTheme
            {
                ScrollBarThumb = Color.FromRgb(200, 200, 200),
                ScrollBarTrack = Color.FromRgb(80, 80, 80)
            },
            new Font { Family = "monospace", Size = 13 }, 16f, 0f, 0f, 0f, 0f
        ]);

        Assert.Contains(context.Fills, fill =>
            fill.Geometry is RoundedRectGeometry && fill.Color == Color.Parse("#123456"));
    }

    [Fact]
    public void DisplayTreeHitTestsOwnedCodeEditorScrollbarForHoverRouting()
    {
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Range(0, 80).Select(static i => $"line-{i}")),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        var root = new View { Geometry = new Rect(0, 0, 300, 120) };
        root.Children.Add(editor);
        var displayTree = new DisplayTree();
        displayTree.BuildFrom(root);
        var metrics = GetEditorScrollMetrics(editor);
        var point = metrics.VerticalThumb.Center;

        var ownedHitTest = typeof(DisplayTree).GetMethod(
            "HitTestOwnedScrollbar", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Same(editor, ownedHitTest.Invoke(displayTree, [point]));
        Assert.True(editor.UpdateScrollbarHover(point));
        Assert.Equal(ScrollbarPart.VerticalThumb,
            typeof(CodeEditor).GetField("_scrollbarHoverPart", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(editor));
        editor.ClearScrollbarHover();
    }

    [Fact]
    public void CodeEditorMobileScrollbarFadesAfterWheelIdle()
    {
        var window = new AppWindow("editor-scrollbar")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Mobile
        };
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Range(0, 80).Select(static i => $"line-{i}")),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        window.Load(editor);

        editor.DispatchTrusted(StandardEvents.CreateWheel(0, 50));

        Assert.Equal(1, editor.ScrollbarOpacity);
        editor.AdvanceScrollbarFade(0.5f);
        Assert.Equal(1, editor.ScrollbarOpacity);
        editor.AdvanceScrollbarFade(0.2f);
        Assert.Equal(0, editor.ScrollbarOpacity);
    }

    [Fact]
    public void CodeEditorMobileFadeRequestedBeforeAttachIsRescheduledOnAttach()
    {
        var window = new AppWindow("editor-fade-before-attach")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Mobile
        };
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Range(0, 80).Select(static i => $"line-{i}")),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        window.Load(editor);
        editor.DispatchTrusted(StandardEvents.CreateWheel(0, 50));
        var application = new DesktopApplication(window);
        editor.AddEventListener(StandardEvents.RequestFrame, e => InvokeHandleFrameRequest(application, e));

        ((IComponentLifecycle)editor).OnAttached();

        var scheduled = Assert.IsType<Dictionary<Element, double>>(
            GetPrivateField<Dictionary<Element, double>>(application, "_scheduledFrames"));
        Assert.True(scheduled.ContainsKey(editor));
    }

    [Fact]
    public void CodeEditorWheelPreventsAncestorDefaultScrolling()
    {
        var parent = new Square.Controls.View { Geometry = new Rect(0, 0, 300, 120) };
        parent.Style.Set("overflow-y", "auto");
        parent.SetScrollContentSize(new Size(300, 400));
        var editor = new CodeEditor
        {
            Geometry = parent.Geometry,
            Value = string.Join("\n", Enumerable.Range(0, 80).Select(static i => $"line-{i}")),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        parent.Children.Add(editor);
        var parentFrameRequests = 0;
        parent.AddEventListener(StandardEvents.RequestFrame, _ => parentFrameRequests++);
        var wheel = StandardEvents.CreateWheel(0, 50);

        editor.DispatchTrusted(wheel);

        Assert.True(wheel.DefaultPrevented);
        Assert.Equal(0, parentFrameRequests);
    }

    [Fact]
    public void CodeEditorWheelAtBoundaryDoesNotPreventAncestorDefaultScrolling()
    {
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Range(0, 80).Select(static i => $"line-{i}")),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        var wheel = StandardEvents.CreateWheel(0, -50);

        editor.DispatchTrusted(wheel);

        Assert.False(wheel.DefaultPrevented);
    }

    [Fact]
    public void CodeEditorThumbPointerDownStartsHostDragContract()
    {
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Range(0, 80).Select(static i => $"line-{i}")),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        var method = typeof(CodeEditor).GetMethod("GetScrollMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var font = new Font { Family = "monospace", Size = 13 };
        var before = Assert.IsType<ScrollbarMetrics>(method.Invoke(editor, [font, 16f]));

        Assert.True(editor.HandlePointerDown(before.VerticalThumb.Center));
        editor.HandlePointerMove(new Point(before.VerticalThumb.Center.X, before.VerticalTrack.Bottom));
        editor.HandlePointerUp(before.VerticalTrack.Center);

        var after = Assert.IsType<ScrollbarMetrics>(method.Invoke(editor, [font, 16f]));
        Assert.True(after.VerticalThumb.Y > before.VerticalThumb.Y);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CodeEditorPaintsOnlyDraggedAxisThumbAsActive(bool vertical)
    {
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Repeat(new string('x', 200), 80)),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        var font = new Font { Family = "monospace", Size = 13 };
        var metricsMethod = typeof(CodeEditor).GetMethod("GetScrollMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var metrics = Assert.IsType<ScrollbarMetrics>(metricsMethod.Invoke(editor, [font, 16f]));
        Assert.True(metrics.HasVertical);
        Assert.True(metrics.HasHorizontal);
        Assert.True(editor.HandlePointerDown(vertical ? metrics.VerticalThumb.Center : metrics.HorizontalThumb.Center));
        var normal = Color.FromRgb(20, 40, 60);
        var active = Color.FromRgb(180, 200, 220);
        var theme = new CodeEditorTheme
        {
            ScrollBarThumb = normal,
            ScrollBarThumbActive = active,
            ScrollBarTrack = Color.FromRgb(80, 80, 80)
        };
        var context = new RecordingRenderContext();
        var paintMethod = typeof(CodeEditor).GetMethod("PaintScrollBars", BindingFlags.Instance | BindingFlags.NonPublic)!;

        paintMethod.Invoke(editor, [context, theme, font, 16f, 0f, 0f, 0f, 0f]);

        var verticalColor = Assert.Single(context.Fills,
            fill => fill.Geometry is RoundedRectGeometry rounded && rounded.Rect == metrics.VerticalThumb).Color;
        var horizontalColor = Assert.Single(context.Fills,
            fill => fill.Geometry is RoundedRectGeometry rounded && rounded.Rect == metrics.HorizontalThumb).Color;
        Assert.Equal(vertical ? active : normal, verticalColor);
        Assert.Equal(vertical ? normal : active, horizontalColor);
    }

    [Fact]
    public void CodeEditorScrollbarCornerConsumesPointerWithoutSelectingText()
    {
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Repeat(new string('x', 200), 80)),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        editor.SelectAll();
        var selectedBefore = editor.SelectedText;
        var metricsMethod = typeof(CodeEditor).GetMethod("GetScrollMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var metrics = Assert.IsType<ScrollbarMetrics>(metricsMethod.Invoke(
            editor, [new Font { Family = "monospace", Size = 13 }, 16f]));

        Assert.False(editor.HandlePointerDown(metrics.Corner.Center));
        editor.HandlePointerUp(metrics.Corner.Center);

        Assert.Equal(selectedBefore, editor.SelectedText);
    }

    [Fact]
    public void CodeEditorScrollbarCaptureIsClearedOnDetach()
    {
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Repeat(new string('x', 200), 80)),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        var window = new AppWindow("editor-scrollbar-detach");
        window.Load(editor);
        ((IComponentLifecycle)editor).OnAttached();
        var metricsMethod = typeof(CodeEditor).GetMethod("GetScrollMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var metrics = Assert.IsType<ScrollbarMetrics>(metricsMethod.Invoke(
            editor, [new Font { Family = "monospace", Size = 13 }, 16f]));

        Assert.True(editor.HandlePointerDown(metrics.VerticalThumb.Center));
        ((IComponentLifecycle)editor).OnDetached();

        var verticalDragging = typeof(CodeEditor).GetField(
            "_draggingVScroll", BindingFlags.Instance | BindingFlags.NonPublic);
        var horizontalDragging = typeof(CodeEditor).GetField(
            "_draggingHScroll", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(verticalDragging);
        Assert.NotNull(horizontalDragging);
        Assert.False((bool)verticalDragging!.GetValue(editor)!);
        Assert.False((bool)horizontalDragging!.GetValue(editor)!);
    }

    [Fact]
    public void CodeEditorScrollbarCaptureIsClearedWhenHidden()
    {
        var editor = CreateAttachedScrollableEditor(new AppWindow("editor-scrollbar-hidden"));
        var metrics = GetEditorScrollMetrics(editor);

        Assert.True(editor.HandlePointerDown(metrics.VerticalThumb.Center));
        editor.IsVisible = false;

        Assert.False(GetPrivateBool(editor, "_draggingVScroll"));
        Assert.False(GetPrivateBool(editor, "_draggingHScroll"));
    }

    [Fact]
    public void CodeEditorScrollbarCaptureIsClearedWhenDisplayNone()
    {
        var editor = CreateAttachedScrollableEditor(new AppWindow("editor-scrollbar-display-none"));
        var metrics = GetEditorScrollMetrics(editor);

        Assert.True(editor.HandlePointerDown(metrics.VerticalThumb.Center));
        editor.Style.Set("display", "none");
        editor.HandlePointerMove(new Point(metrics.VerticalThumb.Center.X, metrics.VerticalTrack.Bottom));

        Assert.False(GetPrivateBool(editor, "_draggingVScroll"));
        Assert.False(GetPrivateBool(editor, "_draggingHScroll"));
    }

    [Fact]
    public void CodeEditorScrollbarCaptureIsClearedWhenSwitchingToMobile()
    {
        var window = new AppWindow("editor-scrollbar-profile")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Desktop
        };
        var editor = CreateAttachedScrollableEditor(window);
        var metrics = GetEditorScrollMetrics(editor);

        Assert.True(editor.HandlePointerDown(metrics.VerticalThumb.Center));
        window.ScrollbarProfile = ScrollbarDeviceProfile.Mobile;
        editor.HandlePointerMove(new Point(metrics.VerticalThumb.Center.X, metrics.VerticalTrack.Bottom));

        Assert.False(GetPrivateBool(editor, "_draggingVScroll"));
        Assert.False(GetPrivateBool(editor, "_draggingHScroll"));
    }

    [Fact]
    public void CodeEditorScrollbarPointerDoesNotDispatchDocumentEvents()
    {
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Repeat(new string('x', 200), 80)),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        var window = new AppWindow("editor-scrollbar-events");
        window.Load(editor);
        ((IComponentLifecycle)editor).OnAttached();
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(editor);
        var events = new List<string>();
        editor.AddEventListener(StandardEvents.PointerDown, _ => events.Add(StandardEvents.PointerDown));
        editor.AddEventListener(StandardEvents.Click, _ => events.Add(StandardEvents.Click));
        var thumb = GetEditorScrollMetrics(editor).VerticalThumb.Center;

        InvokeHandleMouse(application, thumb, MouseAction.Down);
        InvokeHandleMouse(application, thumb, MouseAction.Up);

        Assert.Empty(events);
    }

    [Fact]
    public void CodeEditorScrollbarButtonRepeatsThroughDesktopHost()
    {
        var window = new AppWindow("editor-scrollbar-repeat");
        var editor = CreateAttachedScrollableEditor(window);
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(editor);
        var metrics = GetEditorScrollMetrics(editor);
        InvokeHandleMouse(application, metrics.VerticalForwardButton.Center, MouseAction.Down);
        var afterPress = GetPrivateFloat(editor, "_scrollY");
        Assert.Same(editor, GetPrivateField<Element>(application, "_pressedScrollbar"));
        SetPrivateField(application, "_nextScrollbarRepeatSeconds", 0d);
        InvokeHandleTick(application);

        var afterRepeat = GetPrivateFloat(editor, "_scrollY");
        Assert.True(afterRepeat > afterPress);
        InvokeHandleMouse(application, new Point(500, 500), MouseAction.Move, MouseButton.None);
        SetPrivateField(application, "_nextScrollbarRepeatSeconds", 0d);
        InvokeHandleTick(application);
        Assert.Equal(afterRepeat, GetPrivateFloat(editor, "_scrollY"));
        InvokeHandleMouse(application, metrics.VerticalForwardButton.Center, MouseAction.Up);
    }

    [Fact]
    public void CodeEditorCssOverflowDoesNotPaintDuplicateScrollbarChrome()
    {
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Repeat(new string('x', 200), 80)),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        editor.Style.Set("overflow", "auto");
        var context = new RecordingRenderContext();

        new DisplayNode { Element = editor }.Render(context);

        Assert.Equal(2, context.Fills.Count(fill => fill.Geometry is RoundedRectGeometry));
    }

    [Fact]
    public void CodeEditorCssOverflowDoesNotExposeGenericScrollbarHitZones()
    {
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Repeat(new string('x', 200), 80)),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        editor.Style.Set("overflow", "auto");
        editor.Paint(new RecordingRenderContext());
        var metricsMethod = typeof(Element).GetMethod(
            "GetScrollbarMetrics", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(metricsMethod);
        var genericMetrics = Assert.IsType<ScrollbarMetrics>(metricsMethod!.Invoke(editor, null));
        var customMetrics = GetEditorScrollMetrics(editor);
        var tree = new DisplayTree();
        tree.BuildFrom(editor);

        Assert.Same(editor, tree.HitTestRoot(customMetrics.VerticalThumb.Center));
        Assert.Null(tree.HitTestScrollbar(genericMetrics.VerticalThumb.Center));
    }

    private static CodeEditor CreateAttachedScrollableEditor(AppWindow window)
    {
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Repeat(new string('x', 200), 80)),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        window.Load(editor);
        ((IComponentLifecycle)editor).OnAttached();
        return editor;
    }

    private static ScrollbarMetrics GetEditorScrollMetrics(CodeEditor editor)
    {
        var method = typeof(CodeEditor).GetMethod("GetScrollMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Assert.IsType<ScrollbarMetrics>(method.Invoke(
            editor, [new Font { Family = "monospace", Size = 13 }, 16f]));
    }

    private static bool GetPrivateBool(CodeEditor editor, string name)
    {
        var field = typeof(CodeEditor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (bool)field!.GetValue(editor)!;
    }

    private static float GetPrivateFloat(CodeEditor editor, string name)
    {
        var field = typeof(CodeEditor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (float)field!.GetValue(editor)!;
    }

    [Fact]
    public void DoubleClickingCodeEditorThumbKeepsHostDragWithoutSelectingWord()
    {
        const string value = "alpha beta gamma";
        var editor = new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Repeat(value, 80)),
            ShowScrollBars = true,
            ShowFolding = false,
            WordWrap = false
        };
        editor.SelectAll();
        var selectedBefore = editor.SelectedText;
        var window = new AppWindow("editor-thumb-double-click");
        window.Load(editor);
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(editor);
        var method = typeof(CodeEditor).GetMethod("GetScrollMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var metrics = Assert.IsType<ScrollbarMetrics>(method.Invoke(editor, [new Font { Family = "monospace", Size = 13 }, 16f]));
        var thumb = metrics.VerticalThumb.Center;

        InvokeHandleMouse(application, thumb, MouseAction.Down);
        InvokeHandleMouse(application, thumb, MouseAction.Up);
        InvokeHandleMouse(application, thumb, MouseAction.Down);

        Assert.True(GetPrivateField<bool>(application, "_isSelectingText"));
        Assert.Equal(selectedBefore, editor.SelectedText);
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

    private static void InvokeHandleFrameRequest(DesktopApplication application, Event request)
    {
        var method = typeof(DesktopApplication).GetMethod(
            "HandleFrameRequest",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(application, [request]);
    }

    private static void InvokeHandleTick(DesktopApplication application)
    {
        var method = typeof(DesktopApplication).GetMethod("HandleTick", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(application, null);
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

    private sealed class RecordingRenderContext : IRenderContext
    {
        public readonly List<FillRecord> Fills = [];
        public Size CanvasSize => new(300, 120);
        public float DpiScale => 1;
        public void PushTransform(Matrix3x2 matrix) { }
        public void PopTransform() { }
        public void PushClip(Rect rect) { }
        public void PushClip(Geometry geometry) { }
        public void PopClip() { }
        public void FillRect(Rect rect, Brush brush) => Record(new RectGeometry(rect), brush);
        public void DrawRect(Rect rect, Pen pen) { }
        public void FillPath(PathGeometry path, Brush brush) { }
        public void DrawPath(PathGeometry path, Pen pen) { }
        public void FillGeometry(Geometry geometry, Brush brush) => Record(geometry, brush);
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

        private void Record(Geometry geometry, Brush brush)
        {
            if (brush is SolidColorBrush solid)
                Fills.Add(new FillRecord(geometry, solid.Color));
        }
    }

    private readonly record struct FillRecord(Geometry Geometry, Color Color);

    private sealed class TestHost : IPlatformHost
    {
        public Size ClientSize => new(300, 120);
        public float DpiScale => 1;
        public bool IsRunning => true;
        public string Title { get; set; } = "editor-thumb-double-click";
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
