namespace Square.Extensions.CodeEditor;

/// <summary>代码文档模型契约。</summary>
public interface ICodeEditorTextModel
{
    /// <summary>UTF-16 码元长度。</summary>
    int Length { get; }

    /// <summary>行数（至少 1）。</summary>
    int LineCount { get; }

    /// <summary>每次有效事务严格递增的版本。</summary>
    long Version { get; }

    /// <summary>当前撤销历史节点，供保存点比较。</summary>
    long HistoryStateId { get; }

    /// <summary>整篇文本。</summary>
    string GetValue();

    /// <summary>读取一段文本；offset/length 越界抛出。</summary>
    string GetText(int offset, int length);

    /// <summary>加载或替换整篇文本，并清空 undo/redo 历史。不能用于普通替换全部。</summary>
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

    /// <summary>应用编辑（校验全部范围后一次性提交）。</summary>
    void ApplyEdits(IReadOnlyList<TextEdit> edits);

    /// <summary>单次替换。</summary>
    void Replace(int offset, int length, string text);

    /// <summary>内容变化。</summary>
    event EventHandler<ContentChangedEventArgs>? Changed;
}

/// <summary>文本替换。Offset/Length 指向该次编辑应用前正文。</summary>
public readonly record struct TextEdit(int Offset, int Length, string Text);

/// <summary>一次模型事务通知。</summary>
public sealed class ContentChangedEventArgs : EventArgs
{
    /// <summary>初始化事务消息。</summary>
    public ContentChangedEventArgs(long version, IReadOnlyList<TextEdit> edits, bool isReset)
    {
        Version = version;
        Edits = edits ?? Array.Empty<TextEdit>();
        IsReset = isReset;
    }

    /// <summary>事务完成后的模型版本。</summary>
    public long Version { get; }

    /// <summary>按实际应用次序排列的编辑。</summary>
    public IReadOnlyList<TextEdit> Edits { get; }

    /// <summary>是否为 SetValue 加载/重载。</summary>
    public bool IsReset { get; }
}
