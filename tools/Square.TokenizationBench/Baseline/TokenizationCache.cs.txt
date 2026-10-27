namespace Square.Extensions.CodeEditor;

/// <summary>按文档缓存行 token 与跨行状态。</summary>
internal sealed class TokenizationCache
{
    private const int MaxCachedLines = 256;
    private const int StateCheckpointInterval = 64;
    private const int MaxStateCheckpoints = 1024;

    private readonly ITokenizer _tokenizer;
    private readonly Dictionary<int, object?> _lineEndStates = [];
    private readonly Dictionary<int, IReadOnlyList<TokenSpan>> _lines = [];
    private readonly SortedDictionary<int, object?> _checkpoints = new() { [0] = null };

    public TokenizationCache(ITokenizer tokenizer) => _tokenizer = tokenizer;

    public void InvalidateFromLine(int line)
    {
        line = Math.Max(0, line);
        RemoveLinesFrom(line);
        foreach (var checkpoint in _checkpoints.Keys.Where(key => key >= line && key != 0).ToArray())
            _checkpoints.Remove(checkpoint);
    }

    public void Reset()
    {
        _lineEndStates.Clear();
        _lines.Clear();
        _checkpoints.Clear();
        _checkpoints[0] = null;
    }

    public IReadOnlyList<TokenSpan> GetLineTokens(ICodeEditorTextModel model, int line)
    {
        if (line < 0 || line >= model.LineCount) return [];
        EnsureLine(model, line);
        return _lines.TryGetValue(line, out var tokens) ? tokens : [];
    }

    private void EnsureLine(ICodeEditorTextModel model, int line)
    {
        PruneCheckpoints(model.LineCount);
        if (_lines.ContainsKey(line)) return;

        var (start, state) = FindCheckpoint(line);
        for (var current = start; current <= line; current++)
        {
            if (_lines.ContainsKey(current) && _lineEndStates.TryGetValue(current, out var cachedState))
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
                _lines[current] = tokens;
                _lineEndStates[current] = state;
            }

            var nextLine = current + 1;
            if (nextLine % StateCheckpointInterval == 0)
            {
                _checkpoints[nextLine] = state;
                TrimCheckpoints();
            }

            // Keep token spans local to the viewport-sized working set while
            // carrying the current grammar state through long jumps.
            TrimLineCache(line);
        }

        TrimLineCache(line);
    }

    private (int Line, object? State) FindCheckpoint(int line)
    {
        var checkpointLine = 0;
        object? state = null;
        foreach (var checkpoint in _checkpoints)
        {
            if (checkpoint.Key > line) break;
            checkpointLine = checkpoint.Key;
            state = checkpoint.Value;
        }
        return (checkpointLine, state);
    }

    private void TrimLineCache(int centerLine)
    {
        var radius = MaxCachedLines / 2;
        var min = Math.Max(0, centerLine - radius);
        var max = centerLine + radius;
        foreach (var line in _lines.Keys.Where(key => key < min || key > max).ToArray())
        {
            _lines.Remove(line);
            _lineEndStates.Remove(line);
        }
    }

    private void RemoveLinesFrom(int line)
    {
        foreach (var cachedLine in _lines.Keys.Where(key => key >= line).ToArray())
        {
            _lines.Remove(cachedLine);
            _lineEndStates.Remove(cachedLine);
        }
    }

    private void PruneCheckpoints(int lineCount)
    {
        foreach (var checkpoint in _checkpoints.Keys.Where(key => key >= lineCount && key != 0).ToArray())
            _checkpoints.Remove(checkpoint);

        TrimCheckpoints();
    }

    private void TrimCheckpoints()
    {
        while (_checkpoints.Count > MaxStateCheckpoints + 1)
        {
            var oldest = _checkpoints.Keys.First(key => key != 0);
            _checkpoints.Remove(oldest);
        }
    }
}
