using Square.Extensions.Terminal;
using Xunit;

namespace Square.Extensions.Terminal.Tests;

public sealed class TerminalBufferTests
{
    [Fact]
    public void ScrollbackIsBoundedAndIncludedInSnapshot()
    {
        var screen = new TerminalScreen(3, 2, maxScrollback: 2);
        var parser = new AnsiVtParser(screen);

        parser.Feed("one\r\ntwo\r\ntri\r\nfou");

        Assert.Equal(2, screen.PrimaryBuffer.ScrollbackCount);
        Assert.Equal("one\ntwo\ntri\nfou", screen.GetTextSnapshot(includeScrollback: true));
    }

    [Fact]
    public void ResizePreservesTopLeftAndClampsCursor()
    {
        var screen = new TerminalScreen(4, 3);
        var parser = new AnsiVtParser(screen);
        parser.Feed("abcd\r\nefgh\x1b[3;4H");

        screen.Resize(3, 2);

        Assert.Equal(3, screen.Buffer.Columns);
        Assert.Equal(2, screen.Buffer.Rows);
        Assert.Equal("abc\nefg", screen.GetTextSnapshot(trimTrailingWhitespace: false));
        Assert.Equal(1, screen.Buffer.CursorRow);
        Assert.Equal(2, screen.Buffer.CursorColumn);
    }

    [Fact]
    public void EraseDisplayModeThreeClearsScrollback()
    {
        var screen = new TerminalScreen(3, 2, maxScrollback: 10);
        var parser = new AnsiVtParser(screen);
        parser.Feed("one\r\ntwo\r\ntri");
        Assert.NotEqual(0, screen.PrimaryBuffer.ScrollbackCount);

        parser.Feed("\x1b[3J");

        Assert.Equal(0, screen.PrimaryBuffer.ScrollbackCount);
    }
}
