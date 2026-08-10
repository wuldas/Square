using Square.Extensions.Terminal;
using Xunit;

namespace Square.Extensions.Terminal.Tests;

public sealed class TerminalParserTests
{
    [Fact]
    public void ControlCharactersMoveCursorAndEditText()
    {
        var screen = new TerminalScreen(8, 3);
        var parser = new AnsiVtParser(screen);

        parser.Feed("abc\rZ\n12\bX\tQ");

        Assert.Equal("Zbc\n 1X    Q\n", screen.GetTextSnapshot());
        Assert.Equal(1, screen.Buffer.CursorRow);
        Assert.Equal(7, screen.Buffer.CursorColumn);
    }

    [Fact]
    public void EscapeAndCsiSaveRestoreCursor()
    {
        var screen = new TerminalScreen(10, 3);
        var parser = new AnsiVtParser(screen);

        parser.Feed("ab\u001b7\x1b[3;5H!\u001b8Z\x1b[s\x1b[2;2H?\x1b[uQ");

        Assert.Equal('Z', screen.Buffer.GetCell(0, 2).Character);
        Assert.Equal('Q', screen.Buffer.GetCell(0, 3).Character);
        Assert.Equal('?', screen.Buffer.GetCell(1, 1).Character);
        Assert.Equal('!', screen.Buffer.GetCell(2, 4).Character);
    }

    [Fact]
    public void CursorMovementAndEraseCommandsAreApplied()
    {
        var screen = new TerminalScreen(6, 4);
        var parser = new AnsiVtParser(screen);

        parser.Feed("abcdef\x1b[1A\x1b[2DXY\x1b[2K\x1b[2;3Hq\x1b[1J");

        Assert.Equal("\n\n\n", screen.GetTextSnapshot(trimTrailingWhitespace: true));
        Assert.Equal(1, screen.Buffer.CursorRow);
        Assert.Equal(3, screen.Buffer.CursorColumn);
    }

    [Fact]
    public void InsertDeleteCharactersAndLinesWorkWithinRegion()
    {
        var screen = new TerminalScreen(5, 4);
        var parser = new AnsiVtParser(screen);
        parser.Feed("11111\r\n22222\r\n33333\r\n44444");

        parser.Feed("\x1b[2;2H\x1b[2@AB\x1b[1P");
        parser.Feed("\x1b[2;1H\x1b[1L");

        Assert.Equal("11111", Row(screen, 0));
        Assert.Equal("     ", Row(screen, 1));
        Assert.Equal("2AB2 ", Row(screen, 2));
        Assert.Equal("33333", Row(screen, 3));
    }

    [Fact]
    public void ScrollRegionAndExplicitScrollCommandsWork()
    {
        var screen = new TerminalScreen(4, 4);
        var parser = new AnsiVtParser(screen);
        parser.Feed("aaaa\r\nbbbb\r\ncccc\r\ndddd");

        parser.Feed("\x1b[2;4r\x1b[4;1H\n");

        Assert.Equal("aaaa", Row(screen, 0));
        Assert.Equal("cccc", Row(screen, 1));
        Assert.Equal("dddd", Row(screen, 2));
        Assert.Equal("    ", Row(screen, 3));

        parser.Feed("\x1b[1T");
        Assert.Equal("    ", Row(screen, 1));
        Assert.Equal("cccc", Row(screen, 2));
        Assert.Equal("dddd", Row(screen, 3));
    }

    [Fact]
    public void SgrSupportsAttributesAndAllRequiredColorForms()
    {
        var screen = new TerminalScreen(8, 2);
        var parser = new AnsiVtParser(screen);

        parser.Feed("\x1b[1;2;3;4;7;9;31;104mA");
        parser.Feed("\x1b[38;5;200;48;2;1;2;3mB\x1b[0mC");

        var first = screen.Buffer.GetCell(0, 0).Style;
        Assert.True(first.Bold);
        Assert.True(first.Dim);
        Assert.True(first.Italic);
        Assert.True(first.Underline);
        Assert.True(first.Inverse);
        Assert.True(first.Strike);
        Assert.Equal(TerminalColor.FromIndex(1), first.Foreground);
        Assert.Equal(TerminalColor.FromIndex(12), first.Background);

        var second = screen.Buffer.GetCell(0, 1).Style;
        Assert.Equal(TerminalColor.FromIndex(200), second.Foreground);
        Assert.Equal(TerminalColor.FromRgb(1, 2, 3), second.Background);
        Assert.Equal(TerminalStyle.Default, screen.Buffer.GetCell(0, 2).Style);
    }

    [Fact]
    public void DecModesControlCursorAndAlternateScreen()
    {
        var screen = new TerminalScreen(8, 2);
        var parser = new AnsiVtParser(screen);
        parser.Feed("primary");

        parser.Feed("\x1b[?25l\x1b[?1049halt");

        Assert.False(screen.CursorVisible);
        Assert.True(screen.IsAlternateBuffer);
        Assert.StartsWith("alt", screen.GetTextSnapshot());

        parser.Feed("\x1b[?1049l\x1b[?25h");
        Assert.False(screen.IsAlternateBuffer);
        Assert.True(screen.CursorVisible);
        Assert.StartsWith("primary", screen.GetTextSnapshot());
    }

    [Fact]
    public void ParserKeepsIncompleteCsiAcrossFeedCalls()
    {
        var screen = new TerminalScreen(4, 2);
        var parser = new AnsiVtParser(screen);

        parser.Feed("\x1b[3");
        parser.Feed("1mR");

        Assert.Equal('R', screen.Buffer.GetCell(0, 0).Character);
        Assert.Equal(TerminalColor.FromIndex(1), screen.Buffer.GetCell(0, 0).Style.Foreground);
    }

    [Fact]
    public void WideCharactersOccupyTwoColumnsAndAdvanceTheCursor()
    {
        var screen = new TerminalScreen(8, 2);
        var parser = new AnsiVtParser(screen);

        parser.Feed("A中B");

        Assert.Equal("A中B\n", screen.GetTextSnapshot());
        Assert.Equal("中", screen.Buffer.GetCell(0, 1).Text);
        Assert.Equal(2, screen.Buffer.GetCell(0, 1).ColumnSpan);
        Assert.True(screen.Buffer.GetCell(0, 2).IsContinuation);
        Assert.Equal("B", screen.Buffer.GetCell(0, 3).Text);
        Assert.Equal(4, screen.Buffer.CursorColumn);
    }

    [Fact]
    public void CombiningMarksAndSplitSurrogatesDoNotConsumeExtraCells()
    {
        var screen = new TerminalScreen(8, 2);
        var parser = new AnsiVtParser(screen);

        parser.Feed("e\u0301");
        parser.Feed("\ud83d");
        parser.Feed("\ude00X");

        Assert.Equal("e\u0301😀X\n", screen.GetTextSnapshot());
        Assert.Equal("e\u0301", screen.Buffer.GetCell(0, 0).Text);
        Assert.Equal("😀", screen.Buffer.GetCell(0, 1).Text);
        Assert.True(screen.Buffer.GetCell(0, 2).IsContinuation);
        Assert.Equal(4, screen.Buffer.CursorColumn);
    }

    [Fact]
    public void ErasingPartOfAWideCharacterClearsBothColumns()
    {
        var screen = new TerminalScreen(6, 2);
        var parser = new AnsiVtParser(screen);
        parser.Feed("A中B\x1b[1;3H\x1b[K");

        Assert.Equal("A\n", screen.GetTextSnapshot());
        Assert.True(screen.Buffer.GetCell(0, 1).IsBlank);
        Assert.True(screen.Buffer.GetCell(0, 2).IsBlank);
    }

    private static string Row(TerminalScreen screen, int row) =>
        new(Enumerable.Range(0, screen.Buffer.Columns).Select(column => screen.Buffer.GetCell(row, column).Character).ToArray());
}
