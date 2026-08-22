using Square.Extensions.Terminal;
using Square.Graphics;
using Square.Hosting;
using System.Numerics;
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
