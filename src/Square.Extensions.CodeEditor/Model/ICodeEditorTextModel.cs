namespace Square.Extensions.CodeEditor;

/// <summary>代码文档模型契约。</summary>
public interface ICodeEditorTextModel
{
    /// <summary>UTF-16 码元长度。</summary>
    int Length { get; }

    /// <summary>行数（至少 1）。</summary>
    int LineCount { get; }

    /// <summary>整篇文本。</summary>
    string GetValue();

    /// <summary>加载或替换整篇文本，并清空 undo/redo 历史。</summary>
    void SetValue(string text);

    /// <summary>获取一行（不含换行符）。</summary>
    string GetLineContent(int lineNumber);

    /// <summary>行起始 offset（0-based 行号）。</summary>
    int GetLineStart(int lineNumber);

    /// <summary>offset 所在行号。</summary>
    int GetLineNumberAt(int offset);

    /// <summary>offset → (line, column)。</summary>
    (int Line, int Column) GetPositionAt(int offset);

    /// <summary>(line, column) → offset。</summary>
    int GetOffsetAt(int line, int column);

    /// <summary>应用编辑（可多段，按 offset 从后往前安全）。</summary>
    void ApplyEdits(IReadOnlyList<TextEdit> edits);

    /// <summary>单次替换。</summary>
    void Replace(int offset, int length, string text);

    /// <summary>内容变化。</summary>
    event EventHandler<ContentChangedEventArgs>? Changed;
}

/// <summary>文本替换。</summary>
public readonly record struct TextEdit(int Offset, int Length, string Text);

/// <summary>内容变更参数。</summary>
public sealed class ContentChangedEventArgs : EventArgs
{
    /// <summary>初始化。</summary>
    public ContentChangedEventArgs(int offset, int oldLength, int newLength)
    {
        Offset = offset;
        OldLength = oldLength;
        NewLength = newLength;
    }

    /// <summary>起点。</summary>
    public int Offset { get; }
    /// <summary>旧长度。</summary>
    public int OldLength { get; }
    /// <summary>新长度。</summary>
    public int NewLength { get; }
}
