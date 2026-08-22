using Square.Extensions.Terminal;
using Square.Events;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace Square.Extensions.Terminal.Tests;

public sealed class TerminalViewTests
{
    [Fact]
    public void FeedAndSnapshotExposeTerminalState()
    {
        var view = new TerminalView(8, 2) { AutoResize = false };

        view.Feed("hello\r\nworld".AsSpan());

        Assert.Equal("hello\nworld", view.GetTextSnapshot());
    }

    [Fact]
    public void KeyAndTextInputRaiseTransportData()
    {
        var view = new TerminalView(8, 2);
        var received = new List<string>();
        view.Input += (_, e) => received.Add(e.Data);

        view.HandleKey(67, control: true);
        view.HandleKey(38, shift: true, control: true);
        view.HandleKey(46);
        view.HandleTextInput("paste中");

        Assert.Equal(["\x03", "\x1b[1;6A", "\x1b[3~", "paste中"], received);
    }

    [Fact]
    public void TerminalClipboardShortcutsRequireControlAndShift()
    {
        var view = new TerminalView(10, 2)
        {
            AutoResize = false,
            Geometry = new Rect(0, 0, 200, 80),
        };
        var host = new ClipboardHost();
        var application = AttachApplication(view, host);
        var received = new List<string>();
        view.Input += (_, e) => received.Add(e.Data);
        view.Feed("hello world");
        view.HandlePointerDown(new Point(6, 8));
        view.HandlePointerMove(new Point(50, 8));
        view.HandlePointerUp(new Point(50, 8));

        host.Modifiers = KeyModifiers.Control | KeyModifiers.Shift;
        InvokeHandleKey(application, 67);
        host.Modifiers = KeyModifiers.Control;
        InvokeHandleKey(application, 67);
        host.ClipboardText = "paste中";
        host.Modifiers = KeyModifiers.Control | KeyModifiers.Shift;
        InvokeHandleKey(application, 86);
        host.Modifiers = KeyModifiers.Control;
        InvokeHandleKey(application, 86);

        Assert.StartsWith("hello", host.CopiedText);
        Assert.Equal(["\x03", "paste中", "\x16"], received);
    }

    [Fact]
    public void PointerSelectionProducesCopyableText()
    {
        var view = new TerminalView(10, 2)
        {
            AutoResize = false,
            Geometry = new Rect(0, 0, 200, 80),
        };
        view.Feed("hello world");

        view.HandlePointerDown(new Point(6, 8));
        view.HandlePointerMove(new Point(120, 8));
        view.HandlePointerUp(new Point(120, 8));

        Assert.True(view.SelectionLength > 0);
        Assert.StartsWith("hello", view.SelectedText);
        Assert.True(view.CanCopySelection);
        Assert.False(view.CanCutSelection);
    }

    [Fact]
    public void ContextMenuCopiesSelectedText()
    {
        var view = new TerminalView(10, 2)
        {
            AutoResize = false,
            Geometry = new Rect(0, 0, 200, 80),
        };
        var host = AttachToWindow(view);
        view.Feed("hello world");
        view.HandlePointerDown(new Point(6, 8));
        view.HandlePointerMove(new Point(50, 8));
        view.HandlePointerUp(new Point(50, 8));

        var dispatched = view.DispatchEvent(StandardEvents.CreateContextMenu(50, 8));

        Assert.False(dispatched);
        Assert.StartsWith("hello", host.ClipboardText);
        Assert.Equal(0, view.SelectionLength);
    }

    [Fact]
    public void ContextMenuPastesClipboardWhenSelectionIsEmpty()
    {
        var view = new TerminalView(10, 2);
        var host = AttachToWindow(view);
        var received = new List<string>();
        view.Input += (_, e) => received.Add(e.Data);

        host.ClipboardText = "paste中";
        var dispatched = view.DispatchEvent(StandardEvents.CreateContextMenu(0, 0));
        host.ClipboardText = "";
        view.DispatchEvent(StandardEvents.CreateContextMenu(0, 0));

        Assert.False(dispatched);
        Assert.Equal(["paste中"], received);
    }

    [Fact]
    public void PreventingContextMenuSkipsDefaultCopyAndPaste()
    {
        var view = new TerminalView(10, 2)
        {
            AutoResize = false,
            Geometry = new Rect(0, 0, 200, 80),
        };
        var host = AttachToWindow(view);
        var received = new List<string>();
        view.Input += (_, e) => received.Add(e.Data);
        view.AddEventListener(StandardEvents.ContextMenu, e => e.PreventDefault());

        view.Feed("hello world");
        view.HandlePointerDown(new Point(6, 8));
        view.HandlePointerMove(new Point(50, 8));
        view.HandlePointerUp(new Point(50, 8));
        host.ClipboardText = "unchanged";
        var copyDispatched = view.DispatchEvent(StandardEvents.CreateContextMenu(50, 8));

        Assert.False(copyDispatched);
        Assert.Equal("unchanged", host.ClipboardText);
        Assert.True(view.SelectionLength > 0);

        view.HandlePointerDown(new Point(6, 8));
        view.HandlePointerUp(new Point(6, 8));
        host.ClipboardText = "paste中";
        var pasteDispatched = view.DispatchEvent(StandardEvents.CreateContextMenu(6, 8));

        Assert.False(pasteDispatched);
        Assert.Empty(received);
    }

    [Fact]
    public void RegistrationCreatesTerminalViewTag()
    {
        TerminalRegistration.RegisterDefaults();
        TerminalRegistration.RegisterDefaults();
        var window = new AppWindow("terminal-test");

        var createElement = window.Document.GetType().GetMethod("CreateElement", [typeof(string)]);
        Assert.NotNull(createElement);
        var element = createElement.Invoke(window.Document, ["TerminalView"]);

        Assert.IsType<TerminalView>(element);
    }

    [Fact]
    public void CaretBlinkDoesNotToggleEveryFrame()
    {
        var view = new TerminalView(8, 2);
        view.Focus();

        Assert.False(view.ToggleCaretBlink());
        Assert.False(view.ToggleCaretBlink());
    }

    [Fact]
    public void ResizeRaisesGridSizeChangedOnlyWhenDimensionsChange()
    {
        var view = new TerminalView(8, 2) { AutoResize = false };
        var changes = new List<(int Columns, int Rows)>();
        view.GridSizeChanged += (_, e) => changes.Add((e.Columns, e.Rows));

        view.Resize(8, 2);
        view.Resize(12, 4);

        Assert.Equal([(12, 4)], changes);
    }

    [Fact]
    public void PaintOnlyIncludesRowsThatFitCompletelyInTheViewport()
    {
        var view = new TerminalView(8, 3)
        {
            AutoResize = false,
            Geometry = new Rect(0, 0, 200, 38),
        };
        view.Feed("first\r\nsecond\r\nthird");
        using var context = new RecordingRenderContext();

        view.Paint(context);

        Assert.Equal("third", string.Concat(context.DrawnText));
    }

    private static ClipboardHost AttachToWindow(TerminalView view)
    {
        var window = new AppWindow("terminal-test");
        window.Load(view);
        var host = new ClipboardHost();
        var attach = typeof(AppWindow).GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(attach);
        attach.Invoke(window, [host]);
        return host;
    }

    private static DesktopApplication AttachApplication(TerminalView view, ClipboardHost host)
    {
        var window = new AppWindow("terminal-test");
        window.Load(view);
        var application = new DesktopApplication(window);
        var hostField = typeof(DesktopApplication).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(hostField);
        hostField.SetValue(application, host);
        view.Focus();
        return application;
    }

    private static void InvokeHandleKey(DesktopApplication application, int keyCode)
    {
        var handleKey = typeof(DesktopApplication).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(handleKey);
        handleKey.Invoke(application, [keyCode, KeyAction.Down]);
    }

    private sealed class ClipboardHost : IPlatformHost
    {
        public string ClipboardText { get; set; } = "";
        public string CopiedText { get; private set; } = "";
        public Size ClientSize => new(800, 600);
        public float DpiScale => 1;
        public bool IsRunning => true;
        public string Title { get; set; } = "";
        public CursorKind Cursor { get; set; }
        public KeyModifiers Modifiers { get; set; }
        public event Action<Size>? SizeChanged { add { } remove { } }
        public event Action<Point, MouseAction, MouseButton>? MouseEvent { add { } remove { } }
        public event Action<Point, int>? WheelEvent { add { } remove { } }
        public event Action<int, KeyAction>? KeyEvent { add { } remove { } }
        public event Action<string>? TextInput { add { } remove { } }
        public event Action? Tick { add { } remove { } }
        public void Show() { }
        public void Close() { }
        public IRenderContext CreateRenderContext() => throw new NotSupportedException();
        public void PumpEvents() { }
        public void SetTextInputRect(Rect rect) { }
        public string GetClipboardText() => ClipboardText;
        public void SetClipboardText(string text) => ClipboardText = CopiedText = text;
        public void Dispose() { }
    }

    private sealed class RecordingRenderContext : IRenderContext
    {
        public List<string> DrawnText { get; } = [];
        public Size CanvasSize => new(200, 38);
        public float DpiScale => 1;
        public void PushTransform(Matrix3x2 matrix) { }
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
        public void DrawText(TextLayout text, Point origin, Brush brush) => DrawnText.Add(text.Text);
        public void DrawImage(Image image, Rect dest, Rect? source = null) { }
        public void PushLayer(Rect bounds, float opacity) { }
        public void PopLayer() { }
        public void Clear(Color color) { }
        public void Clear(Color color, Rect rect) { }
        public void Flush() { }
        public void Present() { }
        public void Present(IReadOnlyList<Rect>? dirtyRects) { }
        public void Dispose() { }
    }
}
