namespace Square.Extensions.CodeEditor;

/// <summary>基于 PieceTable 的文本模型，带增量 undo/redo。</summary>
public sealed class CodeEditorTextModel : ICodeEditorTextModel
{
    private readonly PieceTable _table = new();
    private readonly EditStack _undo = new();
    private readonly Stack<long> _undoStates = new();
    private readonly Stack<long> _redoStates = new();
    private bool _suppressHistory;
    private int? _pendingPreCaret;
    private long _version;
    private long _historyStateId;
    private long _nextHistoryId = 1;
    private object? _coalesceOwner;
    private object? _activeEditView;

    public CodeEditorTextModel()
    {
        _table.SetValue("");
    }

    public int Length => _table.Length;
    public int LineCount => _table.LineCount;
    public long Version => _version;
    public long HistoryStateId => _historyStateId;
    public bool CanUndo => _undo.CanUndo;
    public bool CanRedo => _undo.CanRedo;

    public string GetValue() => _table.GetValue();

    public string GetText(int offset, int length)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (offset > Length || (long)offset + length > Length)
            throw new ArgumentOutOfRangeException(nameof(length));
        return _table.GetText(offset, length);
    }

    public void SetValue(string text)
    {
        var normalized = Normalize(text ?? "");
        var oldLength = _table.Length;
        if (oldLength == normalized.Length && _table.GetValue() == normalized) return;

        _table.SetValue(normalized);
        _undo.Clear();
        _undoStates.Clear();
        _redoStates.Clear();
        _coalesceOwner = null;
        _historyStateId = _nextHistoryId++;
        RaiseChanged(Array.Empty<TextEdit>(), isReset: true);
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

    public void ApplyEdits(IReadOnlyList<TextEdit> edits) => ReplaceMany(edits);

    public void SetNextPreCaret(int caret) => _pendingPreCaret = caret;

    public void BeginViewEdit(object view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!ReferenceEquals(_coalesceOwner, view))
            _undo.EndCoalesce();
        _coalesceOwner = view;
        _activeEditView = view;
    }

    public void EndViewEdit() => _activeEditView = null;

    public bool IsActiveEditView(object view) => ReferenceEquals(_activeEditView, view);

    public void EndCoalesce() => _undo.EndCoalesce();

    public void Replace(int offset, int length, string text)
    {
        text = Normalize(text ?? "");
        if (offset < 0 || offset > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0 || offset + length > Length) throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0 && text.Length == 0) return;

        var deleted = length > 0 ? _table.GetText(offset, length) : "";
        var preCaret = _pendingPreCaret ?? (text.Length == 0 ? offset + length : offset);
        _pendingPreCaret = null;
        var kind = PushHistory(offset, deleted, text, preCaret);
        if (kind == HistoryPushKind.NewNode)
            AssignNewHistoryState();
        _table.Replace(offset, length, text);
        RaiseChanged([new TextEdit(offset, length, text)], isReset: false);
    }

    public bool Undo(out int caretOffset) => Undo(out caretOffset, out _);

    public bool Undo(out int caretOffset, out int[] caretOffsets)
    {
        caretOffset = 0;
        caretOffsets = [];
        if (!_undo.TryUndoItem(out var item)) return false;
        _redoStates.Push(_historyStateId);
        _historyStateId = _undoStates.Count > 0 ? _undoStates.Pop() : 0;
        _suppressHistory = true;
        try
        {
            var edits = ApplyHistory(item, forward: false, out caretOffset, out caretOffsets);
            RaiseChanged(edits, isReset: false);
            return true;
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
        _undoStates.Push(_historyStateId);
        _historyStateId = _redoStates.Count > 0 ? _redoStates.Pop() : _historyStateId;
        _suppressHistory = true;
        try
        {
            var edits = ApplyHistory(item, forward: true, out caretOffset, out caretOffsets);
            RaiseChanged(edits, isReset: false);
            return true;
        }
        finally
        {
            _suppressHistory = false;
        }
    }

    public void ReplaceMany(IReadOnlyList<TextEdit> edits, IReadOnlyList<int>? preCarets = null)
    {
        if (edits == null || edits.Count == 0) return;
        var prepared = PrepareEdits(edits);
        if (prepared.Count == 0) return;
        if (prepared.Count == 1)
        {
            if (preCarets is { Count: > 0 })
                SetNextPreCaret(preCarets[0]);
            var single = prepared[0];
            Replace(single.Offset, single.Length, single.Text);
            return;
        }

        var history = new List<EditEntry>(prepared.Count);
        var applied = new List<TextEdit>(prepared.Count);
        _suppressHistory = true;
        try
        {
            var preByOffset = new Dictionary<int, int>();
            if (preCarets != null)
            {
                for (var i = 0; i < edits.Count && i < preCarets.Count; i++)
                    preByOffset[edits[i].Offset] = preCarets[i];
            }

            foreach (var edit in prepared.OrderByDescending(e => e.Offset))
            {
                var deleted = edit.Length > 0 ? _table.GetText(edit.Offset, edit.Length) : "";
                var pre = preByOffset.TryGetValue(edit.Offset, out var p)
                    ? p
                    : (edit.Text.Length == 0 ? edit.Offset + edit.Length : edit.Offset);
                history.Add(new EditEntry(edit.Offset, deleted, edit.Text, pre));
                _table.Replace(edit.Offset, edit.Length, edit.Text);
                applied.Add(edit);
            }
        }
        finally
        {
            _suppressHistory = false;
        }

        _undo.PushCompound(history);
        AssignNewHistoryState();
        RaiseChanged(applied, isReset: false);
    }

    public event EventHandler<ContentChangedEventArgs>? Changed;

    private List<TextEdit> PrepareEdits(IReadOnlyList<TextEdit> edits)
    {
        var prepared = new List<TextEdit>(edits.Count);
        foreach (var edit in edits)
        {
            var text = Normalize(edit.Text ?? "");
            if (edit.Offset < 0 || edit.Offset > Length)
                throw new ArgumentOutOfRangeException(nameof(edits));
            if (edit.Length < 0 || edit.Offset + edit.Length > Length)
                throw new ArgumentOutOfRangeException(nameof(edits));
            if (edit.Length == 0 && text.Length == 0)
                continue;
            prepared.Add(edit with { Text = text });
        }

        var ordered = prepared.OrderBy(e => e.Offset).ThenBy(e => e.Length).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];
            var previousEnd = previous.Offset + previous.Length;
            if (previous.Length == 0 && current.Length == 0 && previous.Offset == current.Offset)
                throw new ArgumentException("Duplicate insertion points are not allowed.", nameof(edits));
            if (previousEnd > current.Offset)
                throw new ArgumentException("Overlapping edits are not allowed.", nameof(edits));
        }

        return prepared;
    }

    private IReadOnlyList<TextEdit> ApplyHistory(
        HistoryItem item,
        bool forward,
        out int caretOffset,
        out int[] caretOffsets)
    {
        switch (item)
        {
            case SingleHistory single:
                var entry = single.Entry;
                if (forward)
                    ApplyForward(entry);
                else
                    ApplyInverse(entry);
                caretOffset = Math.Clamp(
                    forward ? entry.Offset + entry.NewText.Length : entry.PreCaret,
                    0,
                    Length);
                caretOffsets = [caretOffset];
                return
                [
                    forward
                        ? new TextEdit(entry.Offset, entry.OldText.Length, entry.NewText)
                        : new TextEdit(entry.Offset, entry.NewText.Length, entry.OldText)
                ];
            case CompoundHistory compound:
                var applied = new List<TextEdit>(compound.Entries.Count);
                if (forward)
                {
                    foreach (var historyEntry in compound.Entries.OrderByDescending(e => e.Offset))
                    {
                        ApplyForward(historyEntry);
                        applied.Add(new TextEdit(historyEntry.Offset, historyEntry.OldText.Length, historyEntry.NewText));
                    }

                    caretOffsets = MapCaretsAfterForwardCompound(compound.Entries);
                }
                else
                {
                    foreach (var historyEntry in compound.Entries.OrderBy(e => e.Offset))
                    {
                        ApplyInverse(historyEntry);
                        applied.Add(new TextEdit(historyEntry.Offset, historyEntry.NewText.Length, historyEntry.OldText));
                    }

                    caretOffsets = compound.Entries
                        .Select(e => Math.Clamp(e.PreCaret, 0, Length))
                        .Distinct()
                        .OrderBy(c => c)
                        .ToArray();
                }

                if (caretOffsets.Length == 0)
                    caretOffsets = [0];
                caretOffset = caretOffsets[^1];
                return applied;
            default:
                caretOffset = 0;
                caretOffsets = [0];
                return [];
        }
    }

    private void ApplyInverse(EditEntry entry) =>
        _table.Replace(entry.Offset, entry.NewText.Length, entry.OldText);

    private void ApplyForward(EditEntry entry) =>
        _table.Replace(entry.Offset, entry.OldText.Length, entry.NewText);

    private HistoryPushKind PushHistory(int offset, string oldText, string newText, int preCaret)
    {
        if (_suppressHistory) return HistoryPushKind.Coalesced;
        return _undo.Push(new EditEntry(offset, oldText, newText, preCaret));
    }

    private void AssignNewHistoryState()
    {
        _undoStates.Push(_historyStateId);
        _redoStates.Clear();
        _historyStateId = _nextHistoryId++;
    }

    private void RaiseChanged(IReadOnlyList<TextEdit> edits, bool isReset)
    {
        _version++;
        Changed?.Invoke(this, new ContentChangedEventArgs(_version, edits, isReset));
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

internal readonly record struct EditEntry(int Offset, string OldText, string NewText, int PreCaret);

internal abstract record HistoryItem;

internal sealed record SingleHistory(EditEntry Entry) : HistoryItem;

internal sealed record CompoundHistory(IReadOnlyList<EditEntry> Entries) : HistoryItem;

internal enum HistoryPushKind
{
    Coalesced,
    NewNode
}

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

    public void EndCoalesce() => FlushCoalesce();

    public HistoryPushKind Push(EditEntry entry)
    {
        _redo.Clear();
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
            return HistoryPushKind.Coalesced;
        }

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
            return HistoryPushKind.Coalesced;
        }

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
            return HistoryPushKind.Coalesced;
        }

        FlushCoalesce();
        if ((entry.OldText.Length == 0 && entry.NewText.Length == 1) ||
            (entry.NewText.Length == 0 && entry.OldText.Length == 1))
        {
            _coalesce = entry;
            _coalesceTime = DateTime.UtcNow;
            return HistoryPushKind.NewNode;
        }

        _undo.Push(new SingleHistory(entry));
        return HistoryPushKind.NewNode;
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
