namespace Square.Extensions.CodeEditor;

/// <summary>按文档缓存行 token 与跨行状态。</summary>
internal sealed class TokenizationCache
{
    private const int MaxCachedLines = 256;
    private const int StateCheckpointInterval = 64;
    private const int MaxStateCheckpoints = 1024;
    private const int MaxGlobalCheckpoints = MaxStateCheckpoints / 2;
    private int _globalCheckpointInterval = StateCheckpointInterval;

    private readonly ITokenizer _tokenizer;
    private readonly Dictionary<int, object?> _lineEndStates = [];
    private readonly Dictionary<int, IReadOnlyList<TokenSpan>> _lines = [];
    private readonly SortedList<int, object?> _checkpoints = new() { [0] = null };
    private readonly List<int> _linesToRemove = [];

    public TokenizationCache(ITokenizer tokenizer) => _tokenizer = tokenizer;

    public void InvalidateFromLine(int line)
    {
        line = Math.Max(0, line);
        RemoveLinesFrom(line);
        RemoveCheckpointSuffix(line);
    }

    public void Reset()
    {
        _lineEndStates.Clear();
        _lines.Clear();
        _linesToRemove.Clear();
        _globalCheckpointInterval = StateCheckpointInterval;
        _checkpoints.Clear();
        _checkpoints[0] = null;
    }

    public IReadOnlyList<TokenSpan> GetLineTokens(ICodeEditorTextModel model, int line)
    {
        if (line < 0 || line >= model.LineCount) return [];
        // A previous request at EOF can have stored a checkpoint equal to LineCount.
        RemoveCheckpointSuffix(model.LineCount);
        EnsureCheckpointInterval(model.LineCount);
        if (_lines.TryGetValue(line, out var tokens)) return tokens;
        EnsureLine(model, line);
        return _lines.TryGetValue(line, out tokens) ? tokens : [];
    }

    private void EnsureLine(ICodeEditorTextModel model, int line)
    {
        var minCachedLine = Math.Max(0, line - MaxCachedLines / 2);
        var (start, state) = FindCheckpoint(line);
        try
        {
            for (var current = start; current <= line; current++)
            {
                if (_lineEndStates.TryGetValue(current, out var cachedState))
                {
                    state = cachedState;
                }
                else
                {
                    var content = model.GetLineContent(current);
                    IReadOnlyList<TokenSpan> tokens;
                    if (_tokenizer is IStatefulTokenizer stateful)
                    {
                        tokens = stateful.TokenizeLine(content, ref state);
                    }
                    else
                    {
                        var stringState = state as string ?? "root";
                        tokens = _tokenizer.TokenizeLine(content, ref stringState);
                        state = stringState;
                    }

                    // Intermediate lines advance state without entering the token cache.
                    if (current >= minCachedLine)
                    {
                        _lines[current] = tokens;
                        _lineEndStates[current] = state;
                    }
                }

                var nextLine = current + 1;
                if (nextLine % StateCheckpointInterval == 0)
                {
                    StoreCheckpoint(nextLine, state, line);
                }
            }
        }
        finally
        {
            // Retain the old window during replay so its states can still be reused.
            TrimLineCache(line);
        }
    }

    private (int Line, object? State) FindCheckpoint(int line)
    {
        var low = 0;
        var high = _checkpoints.Count;
        var keys = _checkpoints.Keys;
        while (low < high)
        {
            var mid = (low + high) >> 1;
            if (keys[mid] <= line) low = mid + 1;
            else high = mid;
        }
        var index = low - 1;
        return (keys[index], _checkpoints.Values[index]);
    }

    private void TrimLineCache(int centerLine)
    {
        var radius = MaxCachedLines / 2;
        var min = Math.Max(0, centerLine - radius);
        var max = (long)centerLine + radius;
        foreach (var line in _lines.Keys)
            if (line < min || line > max)
                _linesToRemove.Add(line);
        RemoveCachedLines();
    }

    private void RemoveLinesFrom(int line)
    {
        foreach (var cachedLine in _lines.Keys)
            if (cachedLine >= line)
                _linesToRemove.Add(cachedLine);
        RemoveCachedLines();
    }

    private void RemoveCachedLines()
    {
        foreach (var line in _linesToRemove)
        {
            _lines.Remove(line);
            _lineEndStates.Remove(line);
        }
        _linesToRemove.Clear();
    }

    private void RemoveCheckpointSuffix(int line)
    {
        while (_checkpoints.Count > 1 && _checkpoints.Keys[^1] >= line)
            _checkpoints.RemoveAt(_checkpoints.Count - 1);
    }

    private void EnsureCheckpointInterval(int lineCount)
    {
        // Doubling preserves the new global grid as a subset of the old grid.
        while ((long)_globalCheckpointInterval * MaxGlobalCheckpoints < lineCount)
            _globalCheckpointInterval *= 2;
    }

    private void StoreCheckpoint(int line, object? state, int centerLine)
    {
        _checkpoints[line] = state;
        if (_checkpoints.Count <= MaxStateCheckpoints + 1) return;

        var first = FirstLocalCheckpoint();
        // During forward scans every checkpoint is behind the target, except possibly target+1.
        // With 64-line spacing that final boundary cannot be farther than the first local point.
        if (_checkpoints.Keys[^1] <= (long)centerLine + 1)
        {
            _checkpoints.RemoveAt(first);
            return;
        }
        var last = LastLocalCheckpoint();
        var firstDistance = Math.Abs((long)_checkpoints.Keys[first] - centerLine);
        var lastDistance = Math.Abs((long)_checkpoints.Keys[last] - centerLine);
        _checkpoints.RemoveAt(firstDistance >= lastDistance ? first : last);
    }

    private int FirstLocalCheckpoint()
    {
        var low = 1;
        var high = _checkpoints.Count;
        // A prefix containing only global points is exactly 0, interval, 2*interval, ...
        while (low < high)
        {
            var mid = (low + high) >> 1;
            if (_checkpoints.Keys[mid] == (long)mid * _globalCheckpointInterval) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    private int LastLocalCheckpoint()
    {
        var low = 0;
        var high = _checkpoints.Count - 1;
        var lastKey = _checkpoints.Keys[high];
        if ((lastKey & (_globalCheckpointInterval - 1)) != 0) return high;
        // The global-only suffix is also a complete interval-spaced sequence.
        while (low < high)
        {
            var mid = (low + high) >> 1;
            var expected = lastKey - (long)(_checkpoints.Count - 1 - mid) * _globalCheckpointInterval;
            if (_checkpoints.Keys[mid] == expected) high = mid;
            else low = mid + 1;
        }
        return low - 1;
    }
}
