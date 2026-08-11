namespace Square.Extensions.CodeEditor;

/// <summary>基于 PieceTable 的文本模型，带增量 undo/redo。</summary>
internal sealed class CodeEditorTextModel : ICodeEditorTextModel
{
    private readonly PieceTable _table = new();
    private readonly EditStack _undo = new();
    private bool _suppressHistory;
    /// <summary>下一次 Replace 记录到历史时使用的编辑前光标（可选）。</summary>
    private int? _pendingPreCaret;

    public CodeEditorTextModel()
    {
        _table.SetValue("");
    }

    public int Length => _table.Length;
    public int LineCount => _table.LineCount;
    public bool CanUndo => _undo.CanUndo;
    public bool CanRedo => _undo.CanRedo;

    public string GetValue() => _table.GetValue();

    public void SetValue(string text)
    {
        var normalized = Normalize(text ?? "");
        var oldLength = _table.Length;
        if (oldLength == normalized.Length && _table.GetValue() == normalized) return;

        _table.SetValue(normalized);
        // SetValue is document loading/replacement, not an edit transaction.
        // Keeping the old and new full-document snapshots defeats the PieceTable.
        _undo.Clear();
        Changed?.Invoke(this, new ContentChangedEventArgs(0, oldLength, _table.Length));
    }

    public string GetLineContent(int lineNumber) => _table.GetLineContent(lineNumber);
    public int GetLineStart(int lineNumber) => _table.GetLineStart(lineNumber);
    public int GetLineNumberAt(int offset) => _table.GetLineNumberAt(offset);

    public (int Line, int Column) GetPositionAt(int offset)
    {
        offset = Math.Clamp(offset, 0, Length);
        var line = GetLineNumberAt(offset);
        return (line, offset - GetLineStart(line));
    }

    public int GetOffsetAt(int line, int column)
    {
        line = Math.Clamp(line, 0, LineCount - 1);
        var start = GetLineStart(line);
        var content = GetLineContent(line);
        column = Math.Clamp(column, 0, content.Length);
        return start + column;
    }

    public void ApplyEdits(IReadOnlyList<TextEdit> edits)
    {
        if (edits == null || edits.Count == 0) return;
        foreach (var edit in edits.OrderByDescending(e => e.Offset))
            Replace(edit.Offset, edit.Length, edit.Text ?? "");
    }

    /// <summary>设置下一次 <see cref="Replace"/> 写入历史时的编辑前光标。</summary>
    public void SetNextPreCaret(int caret) => _pendingPreCaret = caret;

    public void Replace(int offset, int length, string text)
    {
        text = Normalize(text ?? "");
        if (offset < 0 || offset > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0 || offset + length > Length) throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0 && text.Length == 0) return;

        var deleted = length > 0 ? _table.GetText(offset, length) : "";
        var preCaret = _pendingPreCaret ?? (text.Length == 0 ? offset + length : offset);
        _pendingPreCaret = null;
        PushHistory(offset, deleted, text, preCaret);
        _table.Replace(offset, length, text);
        Changed?.Invoke(this, new ContentChangedEventArgs(offset, length, text.Length));
    }

    public bool Undo(out int caretOffset) => Undo(out caretOffset, out _);

    public bool Undo(out int caretOffset, out int[] caretOffsets)
    {
        caretOffset = 0;
        caretOffsets = [];
        if (!_undo.TryUndoItem(out var item)) return false;
        _suppressHistory = true;
        try
        {
            switch (item)
            {
                case SingleHistory single:
                    ApplyInverse(single.Entry);
                    caretOffset = Math.Clamp(single.Entry.PreCaret, 0, Length);
                    caretOffsets = [caretOffset];
                    return true;
                case CompoundHistory compound:
                    foreach (var entry in compound.Entries.OrderBy(e => e.Offset))
                        ApplyInverse(entry);
                    caretOffsets = compound.Entries
                        .Select(e => Math.Clamp(e.PreCaret, 0, Length))
                        .Distinct()
                        .OrderBy(c => c)
                        .ToArray();
                    if (caretOffsets.Length == 0)
                        caretOffsets = [0];
                    caretOffset = caretOffsets[^1];
                    return true;
                default:
                    return false;
            }
        }
        finally
        {
            _suppressHistory = false;
        }
    }

    public bool Redo(out int caretOffset) => Redo(out caretOffset, out _);

    public bool Redo(out int caretOffset, out int[] caretOffsets)
    {
        caretOffset = 0;
        caretOffsets = [];
        if (!_undo.TryRedoItem(out var item)) return false;
        _suppressHistory = true;
        try
        {
            switch (item)
            {
                case SingleHistory single:
                    ApplyForward(single.Entry);
                    caretOffset = Math.Clamp(single.Entry.Offset + single.Entry.NewText.Length, 0, Length);
                    caretOffsets = [caretOffset];
                    return true;
                case CompoundHistory compound:
                    foreach (var entry in compound.Entries.OrderByDescending(e => e.Offset))
                        ApplyForward(entry);
                    caretOffsets = MapCaretsAfterForwardCompound(compound.Entries);
                    caretOffset = caretOffsets.Length > 0 ? caretOffsets[^1] : 0;
                    return true;
                default:
                    return false;
            }
        }
        finally
        {
            _suppressHistory = false;
        }
    }

    private void ApplyInverse(EditEntry entry)
    {
        _table.Replace(entry.Offset, entry.NewText.Length, entry.OldText);
        Changed?.Invoke(this, new ContentChangedEventArgs(entry.Offset, entry.NewText.Length, entry.OldText.Length));
    }

    private void ApplyForward(EditEntry entry)
    {
        _table.Replace(entry.Offset, entry.OldText.Length, entry.NewText);
        Changed?.Invoke(this, new ContentChangedEventArgs(entry.Offset, entry.OldText.Length, entry.NewText.Length));
    }

    /// <summary>
    /// 批量替换（预编辑坐标），并作为一次 undo 提交。
    /// <paramref name="preCarets"/> 与 edits 对应的编辑前光标（用于撤销恢复多光标）。
    /// </summary>
    public void ReplaceMany(IReadOnlyList<TextEdit> edits, IReadOnlyList<int>? preCarets = null)
    {
        if (edits == null || edits.Count == 0) return;
        var ordered = edits.OrderByDescending(e => e.Offset).ToArray();
        if (ordered.Length == 1)
        {
            if (preCarets is { Count: > 0 })
                SetNextPreCaret(preCarets[0]);
            Replace(ordered[0].Offset, ordered[0].Length, ordered[0].Text ?? "");
            return;
        }

        var history = new List<EditEntry>(ordered.Length);
        _suppressHistory = true;
        try
        {
            // map preCarets by matching offset when provided
            var preByOffset = new Dictionary<int, int>();
            if (preCarets != null)
            {
                for (var i = 0; i < edits.Count && i < preCarets.Count; i++)
                    preByOffset[edits[i].Offset] = preCarets[i];
            }

            foreach (var edit in ordered)
            {
                var text = Normalize(edit.Text ?? "");
                var offset = edit.Offset;
                var length = edit.Length;
                if (offset < 0 || offset > Length) throw new ArgumentOutOfRangeException(nameof(edits));
                if (length < 0 || offset + length > Length) throw new ArgumentOutOfRangeException(nameof(edits));
                if (length == 0 && text.Length == 0) continue;
                var deleted = length > 0 ? _table.GetText(offset, length) : "";
                var pre = preByOffset.TryGetValue(offset, out var p)
                    ? p
                    : (text.Length == 0 ? offset + length : offset);
                history.Add(new EditEntry(offset, deleted, text, pre));
                _table.Replace(offset, length, text);
                Changed?.Invoke(this, new ContentChangedEventArgs(offset, length, text.Length));
            }
        }
        finally
        {
            _suppressHistory = false;
        }

        if (history.Count > 0)
            _undo.PushCompound(history);
    }

    public event EventHandler<ContentChangedEventArgs>? Changed;

    private void PushHistory(int offset, string oldText, string newText, int preCaret)
    {
        if (_suppressHistory) return;
        _undo.Push(new EditEntry(offset, oldText, newText, preCaret));
    }

    private int[] MapCaretsAfterForwardCompound(IReadOnlyList<EditEntry> entries)
    {
        var pre = entries.Select(e => e.Offset + e.NewText.Length).ToArray();
        var result = new int[pre.Length];
        for (var i = 0; i < pre.Length; i++)
        {
            var c = pre[i];
            foreach (var e in entries)
            {
                if (e.Offset < pre[i])
                    c += e.NewText.Length - e.OldText.Length;
            }
            result[i] = Math.Clamp(c, 0, Length);
        }
        Array.Sort(result);
        return result.Distinct().ToArray();
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}

/// <param name="PreCaret">编辑前光标位置（撤销后应恢复到此）。</param>
internal readonly record struct EditEntry(int Offset, string OldText, string NewText, int PreCaret);

internal abstract record HistoryItem;

internal sealed record SingleHistory(EditEntry Entry) : HistoryItem;

internal sealed record CompoundHistory(IReadOnlyList<EditEntry> Entries) : HistoryItem;

internal sealed class EditStack
{
    private readonly Stack<HistoryItem> _undo = new();
    private readonly Stack<HistoryItem> _redo = new();
    private EditEntry? _coalesce;
    private DateTime _coalesceTime;

    public bool CanUndo => _undo.Count > 0 || _coalesce.HasValue;
    public bool CanRedo => _redo.Count > 0;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _coalesce = null;
    }

    public void Push(EditEntry entry)
    {
        _redo.Clear();
        // coalesce consecutive single-char inserts (typing) — keep first PreCaret
        if (_coalesce.HasValue &&
            entry.OldText.Length == 0 && entry.NewText.Length == 1 &&
            _coalesce.Value.OldText.Length == 0 &&
            _coalesce.Value.Offset + _coalesce.Value.NewText.Length == entry.Offset &&
            (DateTime.UtcNow - _coalesceTime).TotalMilliseconds < 800)
        {
            _coalesce = new EditEntry(
                _coalesce.Value.Offset, "",
                _coalesce.Value.NewText + entry.NewText,
                _coalesce.Value.PreCaret);
            _coalesceTime = DateTime.UtcNow;
            return;
        }

        // coalesce consecutive single-char Delete (same offset) — keep first PreCaret
        if (_coalesce.HasValue &&
            entry.NewText.Length == 0 && entry.OldText.Length == 1 &&
            _coalesce.Value.NewText.Length == 0 &&
            _coalesce.Value.Offset == entry.Offset &&
            (DateTime.UtcNow - _coalesceTime).TotalMilliseconds < 800)
        {
            _coalesce = new EditEntry(
                _coalesce.Value.Offset,
                _coalesce.Value.OldText + entry.OldText,
                "",
                _coalesce.Value.PreCaret);
            _coalesceTime = DateTime.UtcNow;
            return;
        }

        // coalesce consecutive single-char Backspace — keep original PreCaret (rightmost / first)
        if (_coalesce.HasValue &&
            entry.NewText.Length == 0 && entry.OldText.Length == 1 &&
            _coalesce.Value.NewText.Length == 0 &&
            entry.Offset + 1 == _coalesce.Value.Offset &&
            (DateTime.UtcNow - _coalesceTime).TotalMilliseconds < 800)
        {
            _coalesce = new EditEntry(
                entry.Offset,
                entry.OldText + _coalesce.Value.OldText,
                "",
                _coalesce.Value.PreCaret);
            _coalesceTime = DateTime.UtcNow;
            return;
        }

        FlushCoalesce();
        if ((entry.OldText.Length == 0 && entry.NewText.Length == 1) ||
            (entry.NewText.Length == 0 && entry.OldText.Length == 1))
        {
            _coalesce = entry;
            _coalesceTime = DateTime.UtcNow;
            return;
        }

        _undo.Push(new SingleHistory(entry));
    }

    public void PushCompound(IReadOnlyList<EditEntry> entries)
    {
        if (entries == null || entries.Count == 0) return;
        FlushCoalesce();
        _redo.Clear();
        if (entries.Count == 1)
        {
            _undo.Push(new SingleHistory(entries[0]));
            return;
        }
        _undo.Push(new CompoundHistory(entries.ToArray()));
    }

    public bool TryUndoItem(out HistoryItem item)
    {
        FlushCoalesce();
        if (_undo.Count == 0)
        {
            item = null!;
            return false;
        }
        item = _undo.Pop();
        _redo.Push(item);
        return true;
    }

    public bool TryRedoItem(out HistoryItem item)
    {
        FlushCoalesce();
        if (_redo.Count == 0)
        {
            item = null!;
            return false;
        }
        item = _redo.Pop();
        _undo.Push(item);
        return true;
    }

    private void FlushCoalesce()
    {
        if (!_coalesce.HasValue) return;
        _undo.Push(new SingleHistory(_coalesce.Value));
        _coalesce = null;
    }
}
