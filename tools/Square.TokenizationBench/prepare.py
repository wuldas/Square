"""Prepare pinned packages and generate legacy cache variants from the frozen baseline."""
from pathlib import Path
import hashlib
import json
import re
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "artifacts/tokenization-bench"
SOURCE = ROOT / "src/Square.Extensions.CodeEditor/Language/Tokenization/TokenizationCache.cs"
BASELINE = Path(__file__).resolve().parent / "Baseline/TokenizationCache.cs.txt"
BASELINE_SHA256 = "55b34cb46eddb80ed2f38f78d9f275ab77abdf7920d5147355e74dfdf127ed3c"
PACKAGES = {"bbtree": "0.1.1", "textmatesharp": "2.0.4", "textmatesharp.grammars": "2.0.4", "onigwrap": "1.0.11"}


def replace_once(text, old, new):
    assert text.count(old) == 1, f"Production source changed: {old[:80]}"
    return text.replace(old, new, 1)


def method(text, name, replacement):
    match = re.search(r"    private [^\n]+ " + name + r"\([^\n]*\)\n    \{", text)
    assert match, name
    depth, end = 1, match.end()
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[:match.start()] + replacement.rstrip() + text[end:]


def guard(text):
    text = replace_once(text, "    private readonly ITokenizer _tokenizer;", "    private int _checkpointUpperBound;\n    private readonly ITokenizer _tokenizer;")
    text = replace_once(text, "        _lineEndStates.Clear();", "        _checkpointUpperBound = 0;\n        _lineEndStates.Clear();")
    text = replace_once(text, "                _checkpoints[nextLine] = state;", "                _checkpointUpperBound = Math.Max(_checkpointUpperBound, nextLine);\n                _checkpoints[nextLine] = state;")
    text = replace_once(text, "        PruneCheckpoints(model.LineCount);", "        if (_checkpointUpperBound >= model.LineCount)\n            PruneCheckpoints(model.LineCount);")
    text = replace_once(text, "    private void PruneCheckpoints(int lineCount)\n    {", "    private void PruneCheckpoints(int lineCount)\n    {\n        _checkpointUpperBound = Math.Min(_checkpointUpperBound, lineCount - 1);")
    return text


def generate():
    source = BASELINE.read_text(encoding="utf-8-sig")
    assert hashlib.sha256(source.encode()).hexdigest() == BASELINE_SHA256, "Frozen baseline changed"
    variants = []
    def add(name, text):
        text = text.replace("TokenizationCache", name)
        variants.append(text.removeprefix("namespace Square.Extensions.CodeEditor;\n"))
    checkpoint_source = (Path(__file__).resolve().parent / "Baseline/CheckpointBaseline.cs.txt").read_text(encoding="utf-8-sig")
    assert hashlib.sha256(checkpoint_source.encode()).hexdigest() == "91b34b30ba033cef4e4f8ae31bdd50c1bf8a930f8364f80803f70439d24d800d"
    add("CheckpointBaselineTokenizationCache", checkpoint_source)
    add("BaselineTokenizationCache", source)
    field = "SortedDictionary<int, object?> _checkpoints = new() { [0] = null }"
    add("BBTreeDropInCache", replace_once(source, field, "BBTree.IntegerKey<int>.Map<object?> _checkpoints = new() { [0] = null }"))
    add("GuardedDictionaryCache", guard(source))
    sorted_list = replace_once(guard(source), field, "SortedList<int, object?> _checkpoints = new() { [0] = null }")
    sorted_list = method(sorted_list, "FindCheckpoint", """    private (int Line, object? State) FindCheckpoint(int line)
    {
        var low = 0;
        var high = _checkpoints.Count;
        while (low < high)
        {
            var mid = (low + high) >> 1;
            if (_checkpoints.Keys[mid] <= line) low = mid + 1;
            else high = mid;
        }
        return (_checkpoints.Keys[low - 1], _checkpoints.Values[low - 1]);
    }""")
    sorted_list = replace_once(sorted_list,
        "        foreach (var checkpoint in _checkpoints.Keys.Where(key => key >= line && key != 0).ToArray())\n            _checkpoints.Remove(checkpoint);",
        "        RemoveCheckpointSuffix(line);")
    sorted_list = method(sorted_list, "PruneCheckpoints", """    private void PruneCheckpoints(int lineCount)
    {
        _checkpointUpperBound = Math.Min(_checkpointUpperBound, lineCount - 1);
        RemoveCheckpointSuffix(lineCount);
        TrimCheckpoints();
    }

    private void RemoveCheckpointSuffix(int line)
    {
        while (_checkpoints.Count > 1 && _checkpoints.Keys[^1] >= line)
            _checkpoints.RemoveAt(_checkpoints.Count - 1);
    }""")
    sorted_list = method(sorted_list, "TrimCheckpoints", """    private void TrimCheckpoints()
    {
        while (_checkpoints.Count > MaxStateCheckpoints + 1)
            _checkpoints.RemoveAt(1);
    }""")
    add("SortedListCache", sorted_list)
    bbtree = replace_once(guard(source), field, "BBTree.IntegerKey<int>.Map<object?> _checkpoints = new()")
    bbtree = replace_once(bbtree, "        _checkpoints[0] = null;\n", "")
    bbtree = replace_once(bbtree, "                _checkpoints[nextLine] = state;", "                _checkpoints[-nextLine] = state;")
    bbtree = replace_once(bbtree,
        "        foreach (var checkpoint in _checkpoints.Keys.Where(key => key >= line && key != 0).ToArray())\n            _checkpoints.Remove(checkpoint);",
        "        RemoveCheckpointSuffix(line);")
    bbtree = method(bbtree, "FindCheckpoint", """    private (int Line, object? State) FindCheckpoint(int line)
    {
        // BBTree v0.1.1 BBTreeMap.cs:188: EnumerateFrom is inclusive and ascending.
        // Negated line numbers turn that lower bound into a logical predecessor.
        // The logical (0, null) sentinel is implicit, so TryGetLast finds the oldest checkpoint.
        foreach (var checkpoint in _checkpoints.EnumerateFrom(-line))
            return (-checkpoint.Key, checkpoint.Value);
        return (0, null);
    }""")
    bbtree = method(bbtree, "PruneCheckpoints", """    private void PruneCheckpoints(int lineCount)
    {
        _checkpointUpperBound = Math.Min(_checkpointUpperBound, lineCount - 1);
        RemoveCheckpointSuffix(lineCount);
        TrimCheckpoints();
    }

    private void RemoveCheckpointSuffix(int line)
    {
        while (_checkpoints.TryGetFirst(out var key, out _) && key <= -line)
            _checkpoints.TryRemoveFirst(out _, out _);
    }""")
    bbtree = method(bbtree, "TrimCheckpoints", """    private void TrimCheckpoints()
    {
        while (_checkpoints.Count > MaxStateCheckpoints && _checkpoints.TryGetLast(out var key, out _))
            _checkpoints.Remove(key);
    }""")
    add("BBTreeOptimizedCache", bbtree)
    target = OUT / "generated/Caches.g.cs"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text("// Generated by prepare.py from the frozen baseline.\n#nullable enable\nnamespace Square.Extensions.CodeEditor;\n" + "\n".join(variants), encoding="utf-8")
    return hashlib.sha256(source.encode()).hexdigest()


if __name__ == "__main__":
    feed = OUT / "feed"
    feed.mkdir(parents=True, exist_ok=True)
    hashes = {}
    for package, version in PACKAGES.items():
        name = f"{package}.{version}.nupkg"
        target = feed / name
        if not target.exists():
            url = f"https://api.nuget.org/v3-flatcontainer/{package}/{version}/{name}"
            print(f"Download {url}", flush=True)
            with urllib.request.urlopen(url, timeout=60) as response:
                target.write_bytes(response.read())
        hashes[name] = hashlib.sha256(target.read_bytes()).hexdigest()
    metadata = {"baselineSourceSha256": generate(),
                "baselineCommit": "1912e3319080441a94a1a336a676e5cf5977c2f4",
                "checkpointBaselineSha256": "91b34b30ba033cef4e4f8ae31bdd50c1bf8a930f8364f80803f70439d24d800d",
                "productionSourceSha256": hashlib.sha256(SOURCE.read_text(encoding="utf-8-sig").encode()).hexdigest(),
                "packagesSha256": hashes}
    (OUT / "preparation.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    print(f"Prepared {OUT}", flush=True)
