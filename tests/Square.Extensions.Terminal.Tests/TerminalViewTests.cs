using Square.Extensions.Terminal;
using Square.Graphics;
using Square.Hosting;
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
        view.HandleTextInput("paste");

        Assert.Equal(["\x03", "\x1b[1;6A", "\x1b[3~", "paste"], received);
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
        view.HandlePointerMove(new Point(50, 8));
        view.HandlePointerUp(new Point(50, 8));

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
}
