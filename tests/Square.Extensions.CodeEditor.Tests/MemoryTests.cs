using System.Reflection;
using Square.Extensions.CodeEditor;
using Xunit;

namespace Square.Extensions.CodeEditor.Tests;

public sealed class MemoryTests
{
    [Fact]
    public void SetValue_DoesNotCreateUndoSnapshotForLoadedDocument()
    {
        var editor = new CodeEditor();
        var text = string.Join('\n', Enumerable.Range(0, 20_000).Select(static i => $"line-{i}"));

        editor.Model.SetValue(text);

        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void SetValue_ClearsExistingEditHistory()
    {
        var editor = new CodeEditor { Value = "before" };
        editor.SelectAll();
        editor.HandleTextInput("edited");
        Assert.True(editor.CanUndo);

        editor.Value = "loaded";

        Assert.False(editor.CanUndo);
        Assert.False(editor.CanRedo);
        Assert.Equal("loaded", editor.Value);
    }

    [Fact]
    public void PieceTable_DoesNotMaterializeWholeAddBufferForEveryAppend()
    {
        var editor = new CodeEditor();
        for (var i = 0; i < 5_000; i++)
            editor.Model.Replace(editor.Model.Length, 0, "x");

        var table = typeof(CodeEditorTextModel)
            .GetField("_table", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(editor.Model)!;
        var cacheField = table.GetType().GetField("_addCache", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.True(cacheField == null || cacheField.GetValue(table) is null);
        Assert.Equal(new string('x', 5_000), editor.Value);
    }

    [Fact]
    public void TokenizationCache_BoundsLineTokenCache()
    {
        var model = new CodeEditor
        {
            Value = string.Join('\n', Enumerable.Range(0, 5_000).Select(static i => $"line-{i}")),
        }.Model;
        var cache = new TokenizationCache(new PlainTextTokenizer());

        cache.GetLineTokens(model, model.LineCount - 1);

        var lines = (System.Collections.ICollection)typeof(TokenizationCache)
            .GetField("_lines", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cache)!;
        Assert.True(lines.Count <= 256, $"cached lines: {lines.Count}");
    }
}
