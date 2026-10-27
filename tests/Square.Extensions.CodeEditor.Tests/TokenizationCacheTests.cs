using System.Reflection;
using Square.Extensions.CodeEditor;
using Xunit;

namespace Square.Extensions.CodeEditor.Tests;

public sealed class TokenizationCacheTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestsMatchSequentialTokensAcrossCheckpointsAndJumps(bool objectState)
    {
        var model = CreateModel(4097);
        var cache = new TokenizationCache(CreateTokenizer(objectState));
        foreach (var line in new[] { 0, 63, 64, 65, 127, 128, 129, 1023, 500, 4096, 7, 2048 })
        {
            Assert.Equal(Expected(model, line, objectState), cache.GetLineTokens(model, line));
            AssertCache(cache, line);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedRequestsDoNotTokenizeAgain(bool objectState)
    {
        var model = CreateModel(2049);
        var tokenizer = CreateTokenizer(objectState);
        var cache = new TokenizationCache(tokenizer);
        var tokens = cache.GetLineTokens(model, 1023);
        tokenizer.Seen.Clear();

        for (var i = 0; i < 32; i++)
            Assert.Same(tokens, cache.GetLineTokens(model, 1023));

        Assert.Empty(tokenizer.Seen);
        AssertCache(cache, 1023);
    }

    [Fact]
    public void WarmCachedRequestsHaveNoManagedAllocations()
    {
        var model = CreateModel(129);
        var cache = new TokenizationCache(new ObjectTokenizer());
        cache.GetLineTokens(model, 63);
        for (var i = 0; i < 32; i++) _ = cache.GetLineTokens(model, 63);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var tokenCount = 0;
        for (var i = 0; i < 4096; i++) tokenCount += cache.GetLineTokens(model, 63).Count;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        Assert.Equal(4096, tokenCount);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplayReusesOldWindowUntilTheBatchCompletes(bool objectState)
    {
        var model = CreateModel(2049);
        var tokenizer = CreateTokenizer(objectState);
        var cache = new TokenizationCache(tokenizer);
        cache.GetLineTokens(model, 511);
        cache.InvalidateFromLine(512);
        tokenizer.Seen.Clear();

        var actual = cache.GetLineTokens(model, 671);

        Assert.Equal(Enumerable.Range(512, 160), tokenizer.Seen);
        Assert.Equal(Expected(model, 671, objectState), actual);
        AssertCache(cache, 671);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidatingTailDoesNotRetokenizeEarlierCachedLines(bool objectState)
    {
        var model = CreateModel(2049);
        var tokenizer = CreateTokenizer(objectState);
        var cache = new TokenizationCache(tokenizer);
        cache.GetLineTokens(model, 1023);
        cache.InvalidateFromLine(927);
        tokenizer.Seen.Clear();

        var actual = cache.GetLineTokens(model, 1039);

        Assert.Equal(Enumerable.Range(927, 113), tokenizer.Seen);
        Assert.Equal(Expected(model, 1039, objectState), actual);
        AssertCache(cache, 1039);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameLineCountEditAndNegativeInvalidationPreserveState(bool objectState)
    {
        var model = CreateModel(1025);
        var cache = new TokenizationCache(CreateTokenizer(objectState));
        cache.GetLineTokens(model, 900);
        model.Replace(model.GetLineStart(64), model.GetLineContent(64).Length, "64: */ changed");
        cache.InvalidateFromLine(64);

        foreach (var line in new[] { 64, 65, 127, 511, 901 })
        {
            Assert.Equal(Expected(model, line, objectState), cache.GetLineTokens(model, line));
            AssertCache(cache, line);
        }

        cache.InvalidateFromLine(-12);
        Assert.Empty(Lines(cache));
        Assert.Empty(States(cache));
        Assert.Equal(new[] { 0 }, Checkpoints(cache).Select(pair => pair.Key));
        Assert.Equal(Expected(model, 65, objectState), cache.GetLineTokens(model, 65));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShrinkPrunesOnCacheHitAndGrowthCanResume(bool objectState)
    {
        var model = CreateModel(256);
        var original = model.GetValue();
        var tokenizer = CreateTokenizer(objectState);
        var cache = new TokenizationCache(tokenizer);
        cache.GetLineTokens(model, 255);
        var prefix = model.GetText(0, model.GetLineStart(128) - 1);
        model.SetValue(prefix);
        tokenizer.Seen.Clear();

        Assert.Equal(Expected(model, 127, objectState), cache.GetLineTokens(model, 127));
        Assert.Empty(tokenizer.Seen);
        Assert.All(Checkpoints(cache), pair => Assert.True(pair.Key < model.LineCount));

        model.SetValue(original);
        cache.InvalidateFromLine(128);
        Assert.Equal(Expected(model, 255, objectState), cache.GetLineTokens(model, 255));
        AssertCache(cache, 255);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SecondEofRequestPrunesBoundaryWithoutTokenizing(bool objectState)
    {
        var model = CreateModel(128);
        var tokenizer = CreateTokenizer(objectState);
        var cache = new TokenizationCache(tokenizer);
        var first = cache.GetLineTokens(model, 127);
        Assert.Contains(Checkpoints(cache), pair => pair.Key == 128);
        tokenizer.Seen.Clear();

        Assert.Same(first, cache.GetLineTokens(model, 127));

        Assert.Empty(tokenizer.Seen);
        Assert.DoesNotContain(Checkpoints(cache), pair => pair.Key == 128);
        AssertCache(cache, 127);
    }

    [Fact]
    public void CapacityEvictsSmallestNonzeroCheckpointAndPreservesSentinel()
    {
        var model = CreateModel(65601);
        var cache = new TokenizationCache(new ObjectTokenizer());
        cache.GetLineTokens(model, 65599);
        var checkpoints = Checkpoints(cache);

        Assert.Equal(new[] { 0 }.Concat(Enumerable.Range(2, 1024).Select(index => index * 64)), checkpoints.Select(pair => pair.Key));
        Assert.Null(checkpoints[0].Value);
        AssertCache(cache, 65599);
    }

    [Fact]
    public void LongReplayDoesNotAccumulateTheWholeDocumentInTheCache()
    {
        var model = CreateModel(10001);
        var tokenizer = new ObjectTokenizer();
        var cache = new TokenizationCache(tokenizer);
        cache.GetLineTokens(model, 511);
        var peak = Lines(cache).Count;
        tokenizer.OnTokenize = _ => peak = Math.Max(peak, Lines(cache).Count);

        cache.GetLineTokens(model, 9999);

        Assert.InRange(peak, 1, 257 + 129);
        AssertCache(cache, 9999);
    }

    [Fact]
    public void TokenizerFailureDoesNotPublishFailedLineOrLeaveUnboundedCaches()
    {
        var model = CreateModel(2049);
        var tokenizer = new ObjectTokenizer();
        var cache = new TokenizationCache(tokenizer);
        cache.GetLineTokens(model, 511);
        tokenizer.ThrowOnLine = 800;

        Assert.Throws<InvalidOperationException>(() => cache.GetLineTokens(model, 1000));

        AssertCache(cache, 1000);
        Assert.DoesNotContain(800, Lines(cache).Keys);
        Assert.All(Checkpoints(cache), pair => Assert.True(pair.Key <= 800));
        tokenizer.ThrowOnLine = null;
        Assert.Equal(Expected(model, 1000, true), cache.GetLineTokens(model, 1000));
        AssertCache(cache, 1000);
    }

    [Fact]
    public void ResetClearsStateAndInvalidRequestsDoNotTokenize()
    {
        var model = CreateModel(300);
        var tokenizer = new ObjectTokenizer();
        var cache = new TokenizationCache(tokenizer);
        cache.GetLineTokens(model, 200);
        cache.Reset();
        tokenizer.Seen.Clear();

        Assert.Empty(Lines(cache));
        Assert.Empty(States(cache));
        Assert.Equal(new[] { 0 }, Checkpoints(cache).Select(pair => pair.Key));
        Assert.Empty(cache.GetLineTokens(model, -1));
        Assert.Empty(cache.GetLineTokens(model, model.LineCount));
        Assert.Empty(tokenizer.Seen);
        Assert.Equal(Expected(model, 65, true), cache.GetLineTokens(model, 65));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalCheckpointsBoundRewindsAndRetainTheNewViewport(bool objectState)
    {
        var model = CreateModel(200001);
        var tokenizer = CreateTokenizer(objectState);
        var cache = new TokenizationCache(tokenizer);
        cache.GetLineTokens(model, 199999);
        AssertGlobalGrid(cache, 200000);
        var interval = Field<int>(cache, "_globalCheckpointInterval");

        foreach (var line in new[] { 8191, 100123, 160127, 4095, 198655 })
        {
            tokenizer.Seen.Clear();
            Assert.Equal(Expected(model, line, objectState), cache.GetLineTokens(model, line));
            Assert.InRange(tokenizer.Seen.Count, 0, interval);
            AssertCache(cache, line);
            AssertGlobalGrid(cache, 200000);
        }

        cache.GetLineTokens(model, 8191);
        Assert.Contains(Checkpoints(cache), pair => pair.Key == 8128);
        tokenizer.Seen.Clear();
        Assert.Equal(Expected(model, 8351, objectState), cache.GetLineTokens(model, 8351));
        Assert.InRange(tokenizer.Seen.Count, 0, 160);
        AssertGlobalGrid(cache, 200000);
    }

    [Fact]
    public void CheckpointIntervalOnlyGrowsUntilResetIncludingOnCacheHits()
    {
        var model = CreateModel(32768);
        var tokenizer = new ObjectTokenizer();
        var cache = new TokenizationCache(tokenizer);
        cache.GetLineTokens(model, 0);
        foreach (var (count, interval) in new[] { (32768, 64), (32769, 128), (65536, 128), (65537, 256), (129, 256) })
        {
            model.SetValue(CreateModel(count).GetValue());
            tokenizer.Seen.Clear();
            cache.GetLineTokens(model, 0);
            Assert.Empty(tokenizer.Seen);
            Assert.Equal(interval, Field<int>(cache, "_globalCheckpointInterval"));
        }
        cache.Reset();
        Assert.Equal(64, Field<int>(cache, "_globalCheckpointInterval"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void EditingAroundGlobalBoundaryPreservesOnlyValidPrefix(int delta)
    {
        var model = CreateModel(100001);
        var cache = new TokenizationCache(new ObjectTokenizer());
        cache.GetLineTokens(model, 99999);
        var cutoff = 32768 + delta;
        model.Replace(model.GetLineStart(cutoff), model.GetLineContent(cutoff).Length, $"{cutoff}: /* changed");
        cache.InvalidateFromLine(cutoff);
        Assert.All(Checkpoints(cache), pair => Assert.True(pair.Key < cutoff));
        AssertGlobalGrid(cache, cutoff - 1);
        var target = cutoff + 300;
        Assert.Equal(Expected(model, target, true), cache.GetLineTokens(model, target));
        AssertGlobalGrid(cache, (target + 1) / 64 * 64);
        AssertCache(cache, target);
    }

    [Fact]
    public void RetentionMatchesFarthestUnprotectedReferencePolicy()
    {
        var cache = new TokenizationCache(new ObjectTokenizer());
        var configure = typeof(TokenizationCache).GetMethod("EnsureCheckpointInterval", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var store = typeof(TokenizationCache).GetMethod("StoreCheckpoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
        configure.Invoke(cache, [1_000_000]);
        var interval = Field<int>(cache, "_globalCheckpointInterval");
        var expected = new SortedSet<int> { 0 };
        var operations = 0;
        void Add(int key, int center)
        {
            expected.Add(key);
            if (expected.Count > 1025)
            {
                var evicted = expected.Where(value => value != 0 && value % interval != 0)
                    .OrderByDescending(value => Math.Abs((long)value - center)).ThenBy(value => value).First();
                expected.Remove(evicted);
            }
            store.Invoke(cache, [key, null, center]);
            if (++operations % 257 == 0) Assert.Equal(expected, Checkpoints(cache).Select(pair => pair.Key));
        }
        for (var key = 64; key <= 995904; key += 64) Add(key, 995903);
        foreach (var center in new[] { 8191, 500031, 200063, 999999, 65535 })
        {
            var start = expected.GetViewBetween(0, center).Max;
            for (var key = start + 64; key <= center + 1; key += 64) Add(key, center);
            Assert.Equal(expected, Checkpoints(cache).Select(pair => pair.Key));
        }
        configure.Invoke(cache, [2_000_000]);
        interval = Field<int>(cache, "_globalCheckpointInterval");
        for (var key = 1_000_000; key <= 1_999_936; key += 64) Add(key, 1_999_999);
        Assert.Equal(expected, Checkpoints(cache).Select(pair => pair.Key));
    }

    private static void AssertGlobalGrid(TokenizationCache cache, int lastValidCheckpoint)
    {
        var interval = Field<int>(cache, "_globalCheckpointInterval");
        Assert.True(interval >= 64 && (interval & (interval - 1)) == 0);
        var keys = Checkpoints(cache).Select(pair => pair.Key).ToHashSet();
        for (var key = interval; key <= lastValidCheckpoint; key += interval)
            Assert.Contains(key, keys);
    }

    private static CodeEditorTextModel CreateModel(int count)
    {
        var model = new CodeEditorTextModel();
        model.SetValue(string.Join('\n', Enumerable.Range(0, count).Select(line => $"{line}: " + ((line % 256) switch
        {
            61 or 125 => "/* open",
            67 or 133 => "*/ close",
            _ => "code"
        }))));
        return model;
    }

    private static RecordingTokenizer CreateTokenizer(bool objectState) => objectState ? new ObjectTokenizer() : new StringTokenizer();

    private static IReadOnlyList<TokenSpan> Expected(ICodeEditorTextModel model, int line, bool objectState)
    {
        var tokenizer = CreateTokenizer(objectState);
        object? state = null;
        var stringState = "root";
        IReadOnlyList<TokenSpan> tokens = [];
        for (var current = 0; current <= line; current++)
            tokens = tokenizer is IStatefulTokenizer stateful
                ? stateful.TokenizeLine(model.GetLineContent(current), ref state)
                : tokenizer.TokenizeLine(model.GetLineContent(current), ref stringState);
        return tokens;
    }

    private static T Field<T>(TokenizationCache cache, string name) =>
        (T)typeof(TokenizationCache).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;

    private static Dictionary<int, IReadOnlyList<TokenSpan>> Lines(TokenizationCache cache) => Field<Dictionary<int, IReadOnlyList<TokenSpan>>>(cache, "_lines");
    private static Dictionary<int, object?> States(TokenizationCache cache) => Field<Dictionary<int, object?>>(cache, "_lineEndStates");
    private static KeyValuePair<int, object?>[] Checkpoints(TokenizationCache cache) =>
        Field<IEnumerable<KeyValuePair<int, object?>>>(cache, "_checkpoints").OrderBy(pair => pair.Key).ToArray();

    private static void AssertCache(TokenizationCache cache, int center)
    {
        var lines = Lines(cache);
        Assert.Equal(lines.Keys.Order(), States(cache).Keys.Order());
        Assert.InRange(lines.Count, 0, 257);
        Assert.All(lines.Keys, line => Assert.InRange((long)line, Math.Max(0, center - 128), (long)center + 128));
        var checkpoints = Checkpoints(cache);
        Assert.InRange(checkpoints.Length, 1, 1025);
        Assert.Equal(0, checkpoints[0].Key);
        Assert.Null(checkpoints[0].Value);
        Assert.All(checkpoints, pair => Assert.Equal(0, pair.Key % 64));
    }

    private abstract class RecordingTokenizer : ITokenizer
    {
        internal readonly List<int> Seen = [];
        internal Action<int>? OnTokenize;
        internal int? ThrowOnLine;
        public abstract IReadOnlyList<TokenSpan> TokenizeLine(string line, ref string state);

        protected IReadOnlyList<TokenSpan> Read(string line, ref bool insideComment)
        {
            var number = int.Parse(line.AsSpan(0, line.IndexOf(':')));
            Seen.Add(number);
            OnTokenize?.Invoke(number);
            if (number == ThrowOnLine) throw new InvalidOperationException("Tokenizer test failure.");
            var isComment = insideComment || line.Contains("/*", StringComparison.Ordinal);
            insideComment = !line.Contains("*/", StringComparison.Ordinal) && isComment;
            return [new TokenSpan(0, line.Length, isComment ? "comment" : "source")];
        }
    }

    private sealed class StringTokenizer : RecordingTokenizer
    {
        public override IReadOnlyList<TokenSpan> TokenizeLine(string line, ref string state)
        {
            var inside = state == "comment";
            var result = Read(line, ref inside);
            state = inside ? "comment" : "root";
            return result;
        }
    }

    private sealed class ObjectTokenizer : RecordingTokenizer, IStatefulTokenizer
    {
        private sealed record LexerState(bool InsideComment);
        private static readonly LexerState Open = new(true);
        private static readonly LexerState Closed = new(false);
        public override IReadOnlyList<TokenSpan> TokenizeLine(string line, ref string state) => throw new InvalidOperationException("Object state overload required.");
        public IReadOnlyList<TokenSpan> TokenizeLine(string line, ref object? state)
        {
            var inside = state is LexerState { InsideComment: true };
            var result = Read(line, ref inside);
            state = inside ? Open : Closed;
            return result;
        }
    }
}
