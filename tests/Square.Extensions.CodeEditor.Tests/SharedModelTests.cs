using Square.Extensions.CodeEditor;
using Square.Graphics;
using Square.Runtime;
using Xunit;

namespace Square.Extensions.CodeEditor.Tests;

public class SharedModelTests
{
    [Fact]
    public void SharedModel_EditAndUndoStayConsistentAcrossViews()
    {
        var model = new CodeEditorTextModel();
        model.SetValue(string.Join("\n", Enumerable.Repeat("hello", 80)));
        var first = Attach(new CodeEditor(model) { Geometry = new Rect(0, 0, 400, 200) });
        var second = Attach(new CodeEditor(model) { Geometry = new Rect(0, 0, 400, 200) });
        second.SetScrollOffset(0, 40);
        var secondScroll = second.VerticalScrollOffset;
        var length = model.Length;
        first.SelectRange(length, length);
        first.HandleTextInput("!");

        Assert.Equal(length + 1, first.Value.Length);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(0, second.CaretIndex);
        Assert.Equal(secondScroll, second.VerticalScrollOffset);

        Assert.True(second.Undo());
        Assert.Equal(length, first.Value.Length);
        Assert.Equal(first.Value, second.Value);
    }

    [Fact]
    public void SharedModel_DetachDoesNotBlockOtherView()
    {
        var model = new CodeEditorTextModel();
        model.SetValue("abc");
        var first = Attach(new CodeEditor(model));
        var second = Attach(new CodeEditor(model));
        ((IComponentLifecycle)first).OnDetached();

        second.SelectAll();
        second.HandleTextInput("z");
        Assert.Equal("z", model.GetValue());
        Assert.Equal("z", second.Value);
    }

    [Fact]
    public void ReplaceAll_IsSingleUndo()
    {
        var editor = Attach(new CodeEditor { Geometry = new Rect(0, 0, 400, 200), Value = "a a a" });
        Assert.Equal(3, editor.ReplaceAll("a", "b"));
        Assert.Equal("b b b", editor.Value);
        Assert.True(editor.Undo());
        Assert.Equal("a a a", editor.Value);
        Assert.False(editor.Undo());
    }

    [Fact]
    public void ApplyEdits_RejectsOverlapWithoutPartialWrite()
    {
        var model = new CodeEditorTextModel();
        model.SetValue("abcdef");
        var version = model.Version;
        Assert.Throws<ArgumentException>(() =>
            model.ApplyEdits([new TextEdit(0, 3, "X"), new TextEdit(2, 2, "Y")]));
        Assert.Equal("abcdef", model.GetValue());
        Assert.Equal(version, model.Version);
    }

    [Fact]
    public void GetText_ReadsSurrogateChineseAndLineBreaks()
    {
        var model = new CodeEditorTextModel();
        model.SetValue("汉\n😀字");
        Assert.Equal("汉", model.GetText(0, 1));
        Assert.Equal("\n", model.GetText(1, 1));
        Assert.Equal("😀", model.GetText(2, 2));
        Assert.Equal("字", model.GetText(4, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.GetText(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.GetText(0, 99));
    }

    [Fact]
    public void HistoryStateId_TracksUndoRedoAndBranch()
    {
        var model = new CodeEditorTextModel();
        model.SetValue("a");
        var saved = model.HistoryStateId;
        model.Replace(1, 0, "b");
        var afterB = model.HistoryStateId;
        Assert.NotEqual(saved, afterB);
        model.Undo(out _);
        Assert.Equal(saved, model.HistoryStateId);
        model.Redo(out _);
        Assert.Equal(afterB, model.HistoryStateId);
        model.Undo(out _);
        model.Replace(1, 0, "c");
        Assert.NotEqual(saved, model.HistoryStateId);
        Assert.NotEqual(afterB, model.HistoryStateId);
        Assert.Equal("ac", model.GetValue());
    }

    [Fact]
    public void LanguageRegistry_GuessesHtmlCssJavaScript()
    {
        CodeEditorRegistration.RegisterDefaults();
        Assert.Equal("html", LanguageRegistry.GuessLanguage("index.html"));
        Assert.Equal("css", LanguageRegistry.GuessLanguage("styles.css"));
        Assert.Equal("javascript", LanguageRegistry.GuessLanguage("app.js"));
        Assert.True(LanguageRegistry.TryGet("html", out _));
        Assert.True(LanguageRegistry.TryGet("css", out _));
        Assert.True(LanguageRegistry.TryGet("javascript", out _));
    }

    private static CodeEditor Attach(CodeEditor editor)
    {
        CodeEditorRegistration.RegisterDefaults();
        ((IComponentLifecycle)editor).OnAttached();
        return editor;
    }
}
