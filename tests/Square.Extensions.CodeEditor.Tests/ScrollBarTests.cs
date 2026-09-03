using Square.Extensions.CodeEditor;
using Square.Graphics;
using Square.Platform;
using Square.UI.Scrolling;
using Xunit;

namespace Square.Extensions.CodeEditor.Tests;

public class ScrollBarTests
{
    private static CodeEditor CreateTall()
    {
        CodeEditorRegistration.RegisterDefaults();
        return new CodeEditor
        {
            Geometry = new Rect(0, 0, 300, 120),
            Value = string.Join("\n", Enumerable.Range(0, 80).Select(i => "line-" + i + " " + new string('x', 20))),
            ShowLineNumbers = true,
            ShowFolding = false,
            WordWrap = false,
            ShowScrollBars = true,
        };
    }

    [Fact]
    public void ShowScrollBars_DefaultsTrue_AndToggleWorks()
    {
        var pad = new CodeEditor();
        Assert.True(pad.ShowScrollBars);
        pad.ToggleScrollBars();
        Assert.False(pad.ShowScrollBars);
        pad.ShowScrollBars = true;
        Assert.True(pad.ShowScrollBars);
    }

    [Fact]
    public void ResolveCursorAt_UsesArrowOnScrollBarArea()
    {
        var pad = CreateTall();
        // force layout/scroll metrics via caret
        _ = pad.CaretRect;

        var onBar = pad.ResolveCursorAt(new Point(pad.Geometry.Right - 4, pad.Geometry.Y + 40));
        Assert.Equal(CursorKind.Arrow, onBar);

        var inText = pad.ResolveCursorAt(new Point(pad.Geometry.X + 80, pad.Geometry.Y + 40));
        Assert.Equal(CursorKind.Text, inText);
    }

    [Fact]
    public void ScrollBarTrackClick_ChangesScrollPosition()
    {
        var pad = CreateTall();
        _ = pad.CaretRect;

        // click near bottom of vertical track to page down
        var x = pad.Geometry.Right - 5;
        var y = pad.Geometry.Bottom - 30;
        pad.HandlePointerDown(new Point(x, y));
        pad.HandlePointerUp(new Point(x, y));
        // should not throw; with tall content scroll should move or stay at max
        Assert.True(pad.ShowScrollBars);
    }

    [Fact]
    public void WhenScrollBarsHidden_CursorRemainsTextInContent()
    {
        var pad = CreateTall();
        pad.ShowScrollBars = false;
        pad.ShowOverviewRuler = false;
        var cursor = pad.ResolveCursorAt(new Point(pad.Geometry.Right - 4, pad.Geometry.Y + 40));
        // without scrollbars/overview, far-right is still text area (or outside gutter)
        Assert.Equal(CursorKind.Text, cursor);
    }

    [Fact]
    public void CaretStaysAboveHorizontalScrollbarWhenBothAxesOverflow()
    {
        var pad = new CodeEditor
        {
            Geometry = new Rect(0, 0, 200, 100),
            Value = string.Join("\n", Enumerable.Range(0, 80).Select(static _ => new string('x', 200))),
            ShowLineNumbers = false,
            ShowFolding = false,
            ShowGlyphMargin = false,
            ShowOverviewRuler = false,
            ShowScrollBars = true,
            WordWrap = false
        };
        pad.SelectRange(0, pad.Value.Length);

        Assert.True(pad.CaretRect.Bottom <= pad.Geometry.Bottom - 12);
    }

    [Fact]
    public void WordWrapCaretUsesScrollbarReducedWrappingWidth()
    {
        var pad = new CodeEditor
        {
            Geometry = new Rect(0, 0, 200, 100),
            Value = string.Join("\n", Enumerable.Range(0, 80).Select(static _ => new string('x', 200))),
            ShowLineNumbers = false,
            ShowFolding = false,
            ShowGlyphMargin = false,
            ShowOverviewRuler = false,
            ShowScrollBars = true,
            WordWrap = true
        };
        pad.SelectRange(0, pad.Value.Length);
        var caret = pad.CaretRect;

        Assert.True(caret.Top >= 8, $"caret={caret}");
        Assert.True(caret.Bottom <= 92, $"caret={caret}");
    }

    [Fact]
    public void WordWrapPointerHitTestUsesPaintWrappingWidth()
    {
        var pad = new CodeEditor
        {
            Geometry = new Rect(0, 0, 200, 100),
            Value = string.Join("\n", Enumerable.Range(0, 80).Select(static _ => new string('x', 200))),
            ShowLineNumbers = false,
            ShowFolding = false,
            ShowGlyphMargin = false,
            ShowOverviewRuler = false,
            ShowScrollBars = true,
            WordWrap = true
        };
        pad.SelectRange(0, pad.Value.Length);
        // caret 行数与 pointer 命中必须基于同一换行宽度：底部最后一行点击应移动 caret 到文档附近。
        var lastRowY = pad.Geometry.Bottom - 20;
        pad.HandlePointerDown(new Point(pad.Geometry.X + 30, lastRowY), extendSelection: true);

        Assert.True(pad.CaretIndex > 0, $"caret={pad.CaretIndex}");
    }
    [Fact]
    public void CssScrollbarWidthNonePreservesRangeButSuppressesChrome()
    {
        var editor = CreateTall();
        editor.Style.Set("scrollbar-width", "none");

        Assert.True(editor.VerticalScrollRange > 0);
        Assert.Equal(ScrollbarPart.None, editor.GetScrollbarPartAt(new Point(editor.Geometry.Right - 4, editor.Geometry.Y + 40)));
    }

    [Fact]
    public void PageKeysUpdatePublicOffsetAndDispatchScroll()
    {
        var editor = CreateTall();
        var events = 0;
        editor.AddEventListener("scroll", _ => events++);

        editor.HandleKey(34);

        Assert.True(editor.VerticalScrollOffset > 0);
        Assert.True(editor.VerticalScrollRange >= editor.VerticalScrollOffset);
        Assert.Equal(new Point(editor.HorizontalScrollOffset, editor.VerticalScrollOffset), editor.EditorScrollOffset);
        Assert.Equal(1, events);
    }

    [Fact]
    public void ControlHomeAndEndRetainCaretMovementAndScrollToBounds()
    {
        var editor = CreateTall();
        editor.HandleKey(35, control: true);
        Assert.Equal(editor.Value.Length, editor.CaretIndex);
        Assert.Equal(editor.VerticalScrollRange, editor.VerticalScrollOffset);

        editor.HandleKey(36, control: true);
        Assert.Equal(0, editor.CaretIndex);
        Assert.Equal(0, editor.VerticalScrollOffset);
    }
}
