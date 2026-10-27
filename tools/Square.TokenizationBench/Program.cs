using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using Square.Extensions.CodeEditor;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Square.TokenizationBench;

internal static class Program
{
    private static Type[] Variants = [typeof(BaselineTokenizationCache), typeof(TokenizationCache)];
    private static Type ReferenceType => Variants[0];
    private static long _sink;

    private static void Main(string[] args)
    {
        var output = Option(args, "--output", "artifacts/tokenization-bench/results");
        var rounds = int.Parse(Option(args, "--rounds", "9"), CultureInfo.InvariantCulture);
        var profile = Option(args, "--profile", "all");
        var comparison = Option(args, "--comparison", "production");
        if (comparison == "checkpoints")
            Variants = [typeof(CheckpointBaselineTokenizationCache), typeof(TokenizationCache)];
        else if (comparison == "legacy")
            Variants = [typeof(BaselineTokenizationCache), typeof(BBTreeDropInCache),
                typeof(GuardedDictionaryCache), typeof(SortedListCache), typeof(BBTreeOptimizedCache)];
        else if (comparison != "production")
            throw new ArgumentException("Comparison must be production, checkpoints or legacy.");
        var warmupRounds = profile == "large" ? 1 : 2;
        var seeds = new List<SeedMeasurement>();
        Directory.CreateDirectory(output);
        var metadata = new { Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
            Avx2 = Avx2.IsSupported, ServerGC = GCSettings.IsServerGC,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), BBTree = "0.1.1", Rounds = rounds,
            CorpusVersion = "mixed-csharp-v1", WarmupRounds = warmupRounds, Comparison = comparison, HarnessVersion = "checkpoint-retention-v2", ReferenceImplementation = ReferenceType.Name,
            Preparation = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText("artifacts/tokenization-bench/preparation.json")),
            AvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            TimestampUtc = DateTimeOffset.UtcNow };
        Console.WriteLine(JsonSerializer.Serialize(metadata));
        Validate();
        IGrammar? grammar = null;
        if (profile is "all" or "textmate" or "large")
        {
            var options = new RegistryOptions(ThemeName.DarkPlus);
            var registry = new Registry(options);
            grammar = registry.LoadGrammar(options.GetScopeByLanguageId("csharp"))
                ?? throw new InvalidOperationException("Cannot load C# grammar.");
            ValidateTextMate(grammar);
        }
        Console.WriteLine("Semantic checks passed (tokens, checkpoints, cache keys, edits, shrink/grow/reset).");
        if (args.Contains("--validate-only")) return;
        var results = new List<Result>();
        if (profile is "all" or "synthetic")
            foreach (var count in new[] { 64, 512, 1024 })
                RunProfile("synthetic", count, new ArrayModel(MakeLines(count * 64 + 4097)),
                    () => new SyntheticTokenizer(), Cases(count, includeCold: true), rounds, results);
        if (profile is "all" or "textmate")
        {
            var model = new CodeEditorTextModel();
            model.SetValue(string.Join('\n', MakeCodeLines(100_000)));
            RunProfile("textmate", 1024, model, () => new TextMateTokenizer(grammar!),
                Cases(1024, includeCold: false), rounds, results);
        }
        if (profile == "large")
        {
            Console.WriteLine("Creating 1,000,000-line production text model...");
            var model = new CodeEditorTextModel();
            model.SetValue(string.Join('\n', MakeCodeLines(1_000_000)));
            var end = (model.LineCount - 4096) / 64 * 64 - 1;
            Console.WriteLine($"Large model: {model.LineCount:N0} lines, {model.Length:N0} UTF-16 units, {model.Length * 2L / 1048576d:F2} MiB character payload.");
            RunProfile("large-synthetic", 1024, model, () => new SyntheticTokenizer(),
                LargeCases(model.LineCount, realTokenizer: false), rounds, results, seeds, end, warmupRounds);
            RunProfile("large-textmate", 1024, model, () => new TextMateTokenizer(grammar!),
                LargeCases(model.LineCount, realTokenizer: true), rounds, results, seeds, end, warmupRounds);
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { metadata, seeds, results }, new JsonSerializerOptions { WriteIndented = true }));
        using var csv = new StreamWriter(Path.Combine(output, "results.csv"));
        csv.WriteLine("profile,checkpoints,scenario,variant,unit,median_ns,min_ns,max_ns,bytes_per_unit,tokenizer_calls,tokenized_characters,operations");
        foreach (var row in results)
            csv.WriteLine(FormattableString.Invariant($"{row.Profile},{row.Checkpoints},{row.Scenario},{row.Variant},{row.Unit},{row.MedianNs:F2},{row.MinNs:F2},{row.MaxNs:F2},{row.BytesPerUnit:F2},{row.TokenizerCalls},{row.TokenizedCharacters},{row.Operations}"));
        Console.WriteLine($"Results: {Path.GetFullPath(output)}; checksum={_sink}");
    }

    private static string Option(string[] args, string name, string fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 ? args[index + 1] : fallback;
    }

    private static Scenario[] Cases(int checkpoints, bool includeCold)
    {
        var end = checkpoints * 64 - 1;
        var random = new Random(14039);
        var items = new List<Scenario>
        {
            new("cached-line", "request", Enumerable.Repeat(end, 32768).ToArray(), -1, false),
            new("cached-viewport", "request", Enumerable.Range(0, 8192).Select(i => end - 63 + i % 64).ToArray(), -1, false),
            new("scroll", "request", Enumerable.Range(end + 1, 512).ToArray(), -1, false),
            new("jumps", "request", Enumerable.Range(0, 128).Select(_ => Math.Max(0, end - 2048 + random.Next(4096))).ToArray(), -1, false),
            new("invalidate-tail", "event", [end + 16], end - 96, false),
            new("invalidate-middle", "event", [end / 2 + 64], end / 2, false)
        };
        if (includeCold) items.Add(new("cold-jump", "event", [end], -1, true));
        return items.ToArray();
    }

    private static Scenario[] LargeCases(int lineCount, bool realTokenizer)
    {
        var end = (lineCount - 4096) / 64 * 64 - 1;
        var random = new Random(14039);
        var items = new List<Scenario>
        {
            new("cached-line", "request", Enumerable.Repeat(end, 32768).ToArray(), -1, false),
            new("cached-viewport", "request", Enumerable.Range(0, 8192).Select(i => end - 63 + i % 64).ToArray(), -1, false),
            new("scroll", "request", Enumerable.Range(end + 1, realTokenizer ? 128 : 512).ToArray(), -1, false),
            new("local-jumps", "request", Enumerable.Range(0, realTokenizer ? 16 : 128).Select(_ => end - 2048 + random.Next(4096)).ToArray(), -1, false),
            new("invalidate-tail", "event", [end + 16], end - 96, false),
            new("invalidate-retained-middle", "event", [end - 32768 + 64], end - 32768, false),
            new(realTokenizer ? "rewind-8192" : "rewind-document-middle", "event", [realTokenizer ? 8191 : lineCount / 2 - 1], -1, false)
        };
        if (!realTokenizer)
        {
            items.Add(new("invalidate-document-middle", "event", [lineCount / 2 + 64], lineCount / 2, false));
            items.Add(new("cold-eof", "event", [lineCount - 1], -1, true));
        }
        return items.Select(item => item with
        {
            ExpectedCalls = item.Name switch
            {
                "cached-line" or "cached-viewport" => 0,
                "scroll" => realTokenizer ? 128 : 512,
                "invalidate-tail" => 127,
                "invalidate-retained-middle" => 128,
                "rewind-8192" => 8192,
                "rewind-document-middle" => lineCount / 2,
                "invalidate-document-middle" => lineCount / 2 + 65,
                "cold-eof" => lineCount,
                _ => null
            }
        }).ToArray();
    }

    private static void RunProfile(string profile, int count, ICodeEditorTextModel model, Func<ITokenizer> tokenizer,
        Scenario[] scenarios, int rounds, List<Result> results, List<SeedMeasurement>? seeds = null,
        int? seedLine = null, int warmupRounds = 2)
    {
        // Build one canonical state through the unmodified production algorithm. Restore it before every sample.
        // TextMate rule stacks and token lists are immutable to the cache and can be shared between snapshots.
        var checkpointStates = Variants.Contains(typeof(TokenizationCache)) ? new List<KeyValuePair<int, object?>>() : null;
        var seedTokenizer = new CountingTokenizer(tokenizer(), reportProgress: seeds != null, checkpointStates);
        var seedCache = new Cache(ReferenceType, seedTokenizer);
        var lastSeedLine = seedLine ?? count * 64 - 1;
        if (seeds != null) Console.WriteLine($"Preparing {profile} seed: reading through line {lastSeedLine:N0} from an empty cache...");
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        var seedBytes = GC.GetAllocatedBytesForCurrentThread();
        var seedStarted = Stopwatch.GetTimestamp();
        _ = seedCache.Get(model, lastSeedLine);
        var seedMs = Stopwatch.GetElapsedTime(seedStarted).TotalMilliseconds;
        seedBytes = GC.GetAllocatedBytesForCurrentThread() - seedBytes;
        var seed = seedCache.Snapshot();
        if (seed.Checkpoints.Length != count + 1) throw new InvalidOperationException("Unexpected seed checkpoint count.");
        var lastCheckpoint = (lastSeedLine + 1) / 64 * 64;
        var firstCheckpoint = lastCheckpoint - (count - 1) * 64;
        if (!seed.Checkpoints.Select(pair => pair.Key).SequenceEqual(new[] { 0 }.Concat(Enumerable.Range(0, count).Select(i => firstCheckpoint + i * 64))))
            throw new InvalidOperationException("Seed checkpoint retention range mismatch.");
        if (seeds != null)
        {
            if (seedTokenizer.Calls != lastSeedLine + 1L || seedTokenizer.Characters != 80L * (lastSeedLine + 1) ||
                !seed.Lines.Select(pair => pair.Key).Order().SequenceEqual(Enumerable.Range(lastSeedLine - 128, 129)))
                throw new InvalidOperationException("Large seed work/cache rows mismatch.");
            seeds.Add(new(profile, model.LineCount, model.Length, lastSeedLine, firstCheckpoint, lastCheckpoint,
                seed.Checkpoints.Length, seedTokenizer.Calls, seedTokenizer.Characters, seedMs, seedBytes));
            Console.WriteLine($"Prepared {profile}: {seedMs:F1} ms, {seedBytes:N0} managed bytes, {seedTokenizer.Calls:N0} tokenized lines; checkpoints {firstCheckpoint:N0}..{lastCheckpoint:N0} plus zero.");
        }
        var initialStates = Variants.ToDictionary(type => type, _ => seed);
        if (checkpointStates != null)
        {
            var productionSeed = new Cache(typeof(TokenizationCache), tokenizer());
            productionSeed.SeedFromScannedStates(seed, checkpointStates, model.LineCount, lastSeedLine);
            initialStates[typeof(TokenizationCache)] = productionSeed.Snapshot();
        }
        foreach (var scenario in scenarios)
        {
            var measurements = Variants.ToDictionary(type => type, _ => new List<Sample>());
            var workloads = new Dictionary<Type, (long Calls, long Characters)>();
            if (seeds != null) ValidateLargeScenario(model, tokenizer, initialStates, scenario);
            for (var round = -warmupRounds; round < rounds; round++)
            {
                // Rotate order to spread thermal/background effects across variants.
                for (var i = 0; i < Variants.Length; i++)
                {
                    var type = Variants[(i + round + warmupRounds) % Variants.Length];
                    var counter = new CountingTokenizer(tokenizer());
                    var cache = new Cache(type, counter);
                    if (!scenario.Cold) cache.Restore(initialStates[type]);
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
                    var allocated = GC.GetAllocatedBytesForCurrentThread();
                    var start = Stopwatch.GetTimestamp();
                    if (scenario.Invalidate >= 0) cache.Invalidate(scenario.Invalidate);
                    long checksum = 0;
                    foreach (var line in scenario.Lines)
                    {
                        var tokens = cache.Get(model, line);
                        checksum += tokens.Count;
                        if (tokens.Count != 0) checksum += tokens[0].Length;
                    }
                    var elapsed = Stopwatch.GetTimestamp() - start;
                    var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    _sink ^= checksum;
                    var work = (counter.Calls, counter.Characters);
                    if (workloads.TryGetValue(type, out var priorWork) && work != priorWork)
                        throw new InvalidOperationException($"Unstable tokenizer work: {profile}/{scenario.Name}/{type.Name}.");
                    workloads[type] = work;
                    AssertComparableWork(type, scenario, work, workloads[ReferenceType]);
                    if (round >= 0) measurements[type].Add(new(elapsed * 1_000_000_000d / Stopwatch.Frequency, bytes));
                }
            }
            foreach (var type in Variants)
            {
                var samples = measurements[type];
                var times = samples.Select(sample => sample.Nanoseconds / scenario.Lines.Length).Order().ToArray();
                var allocations = samples.Select(sample => sample.Bytes / (double)scenario.Lines.Length).Order().ToArray();
                var result = new Result(profile, count, scenario.Name, type.Name, scenario.Unit, times[times.Length / 2],
                    times[0], times[^1], allocations[allocations.Length / 2], workloads[type].Calls,
                    workloads[type].Characters, scenario.Lines.Length,
                    samples.Select(sample => sample.Nanoseconds / scenario.Lines.Length).ToArray(),
                    model.LineCount, model.Length, scenario.Cold ? -1 : lastSeedLine, scenario.Invalidate,
                    scenario.Lines[0], scenario.Lines[^1], scenario.Lines.Min(), scenario.Lines.Max(),
                    scenario.Cold, warmupRounds, rounds, initialStates[type].GlobalInterval,
                    initialStates[type].Checkpoints[1].Key,
                    initialStates[type].Checkpoints.Zip(initialStates[type].Checkpoints.Skip(1), (a, b) => b.Key - a.Key).Max());
                results.Add(result);
                Console.WriteLine(FormattableString.Invariant($"{profile,-9} K={count,4} {scenario.Name,-18} {type.Name,-24} {result.MedianNs,12:F1} ns/{scenario.Unit} {result.BytesPerUnit,10:F1} B/{scenario.Unit} calls={result.TokenizerCalls}"));
            }
        }
    }

    private static void AssertComparableWork(Type type, Scenario scenario, (long Calls, long Characters) work,
        (long Calls, long Characters) baseline)
    {
        var expectedCalls = scenario.ExpectedCalls;
        if (ReferenceType == typeof(CheckpointBaselineTokenizationCache) && scenario.Name == "invalidate-tail") expectedCalls = 113;
        if (expectedCalls is long expected && (type != typeof(TokenizationCache) || scenario.Cold) && work.Calls != expected)
            throw new InvalidOperationException($"Unexpected replay work: {scenario.Name}/{type.Name}, expected {expected}, got {work.Calls}.");
        if (type == typeof(TokenizationCache))
        {
            if (work.Calls > baseline.Calls || work.Characters > baseline.Characters)
                throw new InvalidOperationException($"Production replay increased: {scenario.Name}.");
        }
        else if (work != baseline)
        {
            throw new InvalidOperationException($"Legacy tokenizer work changed: {scenario.Name}/{type.Name}.");
        }
    }

    private static void ValidateLargeScenario(ICodeEditorTextModel model, Func<ITokenizer> tokenizer,
        IReadOnlyDictionary<Type, Snapshot> initialStates, Scenario scenario)
    {
        IReadOnlyList<TokenSpan>[]? expectedTokens = null;
        Snapshot? expectedSnapshot = null;
        (long Calls, long Characters)? baselineWork = null;
        foreach (var type in Variants)
        {
            var counter = new CountingTokenizer(tokenizer());
            var cache = new Cache(type, counter);
            if (!scenario.Cold)
            {
                cache.Restore(initialStates[type]);
                AssertSameSnapshot(initialStates[type], cache.Snapshot());
            }
            if (scenario.Invalidate >= 0) cache.Invalidate(scenario.Invalidate);
            var tokens = scenario.Lines.Select(line => cache.Get(model, line)).ToArray();
            baselineWork ??= (counter.Calls, counter.Characters);
            AssertComparableWork(type, scenario, (counter.Calls, counter.Characters), baselineWork.Value);
            if (expectedTokens != null)
                for (var i = 0; i < tokens.Length; i++)
                    if (!tokens[i].SequenceEqual(expectedTokens[i]))
                        throw new InvalidOperationException($"Large trace token mismatch: {scenario.Name}/{type.Name}/{i}.");
            expectedTokens ??= tokens;
            var snapshot = cache.Snapshot();
            if (expectedSnapshot != null) AssertSameSnapshot(expectedSnapshot, snapshot, compareCheckpoints: type != typeof(TokenizationCache));
            expectedSnapshot ??= snapshot;
            if (scenario.Cold && scenario.Lines[^1] == model.LineCount - 1 && model.LineCount % 64 == 0)
            {
                if (snapshot.Checkpoints[^1].Key != model.LineCount)
                    throw new InvalidOperationException("Missing temporary EOF checkpoint.");
                var previousCalls = counter.Calls;
                var second = cache.Get(model, model.LineCount - 1);
                if (counter.Calls != previousCalls || !second.SequenceEqual(tokens[^1]) || cache.Snapshot().Checkpoints[^1].Key >= model.LineCount)
                    throw new InvalidOperationException("EOF checkpoint was not pruned on the next cached request.");
            }
        }
        Console.WriteLine($"Validated large trace: {scenario.Name}.");
    }

    private static void AssertSameSnapshot(Snapshot expected, Snapshot actual, bool compareCheckpoints = true)
    {
        if (actual.Checkpoints.Length > 1025 || actual.Lines.Length > 257 || actual.Checkpoints[0].Key != 0 ||
            (compareCheckpoints && !expected.Checkpoints.Select(pair => pair.Key).SequenceEqual(actual.Checkpoints.Select(pair => pair.Key))) ||
            (compareCheckpoints && !expected.Lines.Select(pair => pair.Key).Order().SequenceEqual(actual.Lines.Select(pair => pair.Key).Order())) ||
            !actual.Lines.Select(pair => pair.Key).Order().SequenceEqual(actual.States.Select(pair => pair.Key).Order()))
            throw new InvalidOperationException("Large trace cache retention mismatch.");
        var actualLines = actual.Lines.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var pair in expected.Lines)
            if (actualLines.TryGetValue(pair.Key, out var actualTokens) && !pair.Value.SequenceEqual(actualTokens))
                throw new InvalidOperationException("Large trace cached token mismatch.");
    }

    private static void Validate()
    {
        foreach (var stateful in new[] { false, true })
        {
            var lines = MakeLines(100_003);
            var model = new ArrayModel(lines);
            var caches = Variants.Select(type => new Cache(type, stateful ? new SyntheticTokenizer() : new StringTokenizer())).ToArray();
            var expected = SequentialTokens(model, stateful ? new SyntheticTokenizer() : new StringTokenizer());
            var random = new Random(513);
            var trace = new[] { -1, 0, 1, 63, 64, 65, 127, 128, 129, 65535, 65599, 99999, 0, 64, 100002, 100003 }
                .Concat(Enumerable.Range(0, 24).Select(_ => random.Next(model.LineCount)));
            foreach (var line in trace) CheckRequest(caches, model, expected, line);
            foreach (var editLine in new[] { 64, 50001, 99990, 0 })
            {
                lines[editLine] = (editLine == 0 ? "public class Changed {}" : "/* changed state").PadRight(80);
                foreach (var cache in caches) cache.Invalidate(editLine);
                expected = SequentialTokens(model, stateful ? new SyntheticTokenizer() : new StringTokenizer());
                foreach (var line in new[] { editLine, Math.Min(editLine + 130, model.LineCount - 1), model.LineCount - 1 })
                    CheckRequest(caches, model, expected, line);
            }
            // No notification: production pruning must also cope with LineCount shrinking on the next request.
            model.Lines = lines[..65536];
            expected = SequentialTokens(model, stateful ? new SyntheticTokenizer() : new StringTokenizer());
            CheckRequest(caches, model, expected, 65535);
            CheckRequest(caches, model, expected, 65535); // Removes checkpoint == LineCount on second request.
            model.Lines = lines;
            expected = SequentialTokens(model, stateful ? new SyntheticTokenizer() : new StringTokenizer());
            foreach (var cache in caches) cache.Invalidate(65536);
            CheckRequest(caches, model, expected, 100002);
            foreach (var cache in caches) { cache.Invalidate(-1); cache.Reset(); }
            CheckRequest(caches, model, expected, 65);
        }
    }

    private static void ValidateTextMate(IGrammar grammar)
    {
        var model = new CodeEditorTextModel();
        model.SetValue(string.Join('\n', MakeCodeLines(1027)));
        var caches = Variants.Select(type => new Cache(type, new TextMateTokenizer(grammar))).ToArray();
        var expected = SequentialTokens(model, new TextMateTokenizer(grammar));
        foreach (var line in new[] { 0, 63, 64, 65, 129, 1026, 3, 900 }) CheckRequest(caches, model, expected, line);
        model.Replace(model.GetLineStart(64), 0, "*/ ");
        foreach (var cache in caches) cache.Invalidate(64);
        expected = SequentialTokens(model, new TextMateTokenizer(grammar));
        foreach (var line in new[] { 64, 65, 129, 1026 }) CheckRequest(caches, model, expected, line);
        Console.WriteLine("Real TextMate cross-line and edit checks passed.");
    }

    private static IReadOnlyList<TokenSpan>[] SequentialTokens(ICodeEditorTextModel model, ITokenizer tokenizer)
    {
        var result = new IReadOnlyList<TokenSpan>[model.LineCount];
        object? state = null;
        var stringState = "root";
        for (var line = 0; line < result.Length; line++)
            result[line] = tokenizer is IStatefulTokenizer stateful
                ? stateful.TokenizeLine(model.GetLineContent(line), ref state)
                : tokenizer.TokenizeLine(model.GetLineContent(line), ref stringState);
        return result;
    }

    private static void CheckRequest(Cache[] caches, ICodeEditorTextModel model, IReadOnlyList<TokenSpan>[] expected, int line)
    {
        Snapshot? reference = null;
        foreach (var cache in caches)
        {
            var tokens = cache.Get(model, line);
            if (!tokens.SequenceEqual(line < 0 || line >= model.LineCount ? [] : expected[line]))
                throw new InvalidOperationException($"Token mismatch: {cache.Type.Name}, line={line}.");
            var snapshot = cache.Snapshot();
            if (snapshot.Checkpoints.Length > 1025 || snapshot.Checkpoints[0].Key != 0 || snapshot.Lines.Length > 257)
                throw new InvalidOperationException("Cache bound/sentinel mismatch.");
            if (reference != null && ((cache.Type != typeof(TokenizationCache) && !snapshot.Checkpoints.Select(item => item.Key).SequenceEqual(reference.Checkpoints.Select(item => item.Key)))
                || (cache.Type != typeof(TokenizationCache) && !snapshot.Lines.Select(item => item.Key).Order().SequenceEqual(reference.Lines.Select(item => item.Key).Order()))))
                throw new InvalidOperationException($"Cache state mismatch: {cache.Type.Name}, line={line}.");
            reference = snapshot;
        }
    }

    private static string[] MakeLines(int count) => Enumerable.Range(0, count).Select(line => ((line % 130) switch
    {
        0 => "/* comment spanning checkpoint boundaries 中文",
        129 => "*/ public class Sample { int value = 42; }",
        _ => "public string text = \"sample 中文\"; // repeated code"
    }).PadRight(80)).ToArray();

    private static string[] MakeCodeLines(int count)
    {
        string[] ordinary =
        [
            "public sealed class Example { private int _value = 42; }",
            "public int Add(int left, int right) => left + right;",
            "var message = $\"value: {value}\"; // inline comment 中文",
            "if (items.Count > 0) { foreach (var item in items) Use(item); }",
            "using System.Collections.Generic;",
            "[Obsolete(\"sample\")] public string Name { get; init; } = \"demo\";",
            "// standalone comment with Unicode 中文",
            "var values = new List<int> { 1, 2, 3, 5, 8 };"
        ];
        return Enumerable.Range(0, count).Select(line => ((line % 256) switch
        {
            61 => "/* block comment begins before checkpoint 64",
            >= 62 and <= 66 => "comment body across checkpoint 中文",
            67 => "*/ public int AfterComment => 7;",
            125 => "var multiline = @\"verbatim string begins before checkpoint 128",
            >= 126 and <= 132 => "verbatim string content across checkpoint 中文",
            133 => "end of string\"; public int AfterString => 9;",
            _ => ordinary[line % ordinary.Length]
        }).PadRight(80)).ToArray();
    }

    private sealed record Scenario(string Name, string Unit, int[] Lines, int Invalidate, bool Cold, long? ExpectedCalls = null);
    private sealed record SeedMeasurement(string Profile, int DocumentLines, int TextCodeUnits, int SeedLine,
        int FirstCheckpoint, int LastCheckpoint, int CheckpointCount, long TokenizerCalls,
        long TokenizedCharacters, double ElapsedMs, long AllocatedBytes);
    private sealed record Sample(double Nanoseconds, long Bytes);
    private sealed record Result(string Profile, int Checkpoints, string Scenario, string Variant, string Unit,
        double MedianNs, double MinNs, double MaxNs, double BytesPerUnit, long TokenizerCalls,
        long TokenizedCharacters, int Operations, double[] SamplesNs, int DocumentLines, int TextCodeUnits,
        int SeedLine, int InvalidateLine, int FirstRequestedLine, int LastRequestedLine, int MinRequestedLine,
        int MaxRequestedLine, bool Cold, int WarmupRounds, int MeasuredRounds,
        int GlobalCheckpointInterval, int FirstSeedCheckpoint, int MaxSeedCheckpointGap);
}

internal sealed class Cache
{
    internal readonly Type Type;
    private readonly object _value;
    internal readonly Func<ICodeEditorTextModel, int, IReadOnlyList<TokenSpan>> Get;
    internal readonly Action<int> Invalidate;
    internal readonly Action Reset;
    private bool Negative => Type == typeof(BBTreeOptimizedCache);
    private object Field(string name) => Type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_value)!;

    internal Cache(Type type, ITokenizer tokenizer)
    {
        Type = type;
        _value = Activator.CreateInstance(type, tokenizer)!;
        Get = type.GetMethod("GetLineTokens")!.CreateDelegate<Func<ICodeEditorTextModel, int, IReadOnlyList<TokenSpan>>>(_value);
        Invalidate = type.GetMethod("InvalidateFromLine")!.CreateDelegate<Action<int>>(_value);
        Reset = type.GetMethod("Reset")!.CreateDelegate<Action>(_value);
    }

    internal Snapshot Snapshot()
    {
        var checkpoints = ((IEnumerable<KeyValuePair<int, object?>>)Field("_checkpoints"))
            .Select(pair => new KeyValuePair<int, object?>(Negative ? -pair.Key : pair.Key, pair.Value));
        if (Negative) checkpoints = checkpoints.Append(new(0, null));
        var snapshot = new Snapshot(checkpoints.OrderBy(pair => pair.Key).ToArray(),
            ((Dictionary<int, IReadOnlyList<TokenSpan>>)Field("_lines")).ToArray(),
            ((Dictionary<int, object?>)Field("_lineEndStates")).ToArray(),
            (int?)Type.GetField("_globalCheckpointInterval", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_value) ?? 0);
        if (snapshot.GlobalInterval != 0)
        {
            var keys = snapshot.Checkpoints.Select(pair => pair.Key).ToHashSet();
            for (var key = snapshot.GlobalInterval; key <= snapshot.Checkpoints[^1].Key; key += snapshot.GlobalInterval)
                if (!keys.Contains(key)) throw new InvalidOperationException("Global checkpoint coverage has a hole.");
        }
        return snapshot;
    }

    internal void SeedFromScannedStates(Snapshot canonical, IReadOnlyList<KeyValuePair<int, object?>> checkpoints,
        int lineCount, int centerLine)
    {
        Type.GetMethod("EnsureCheckpointInterval", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_value, [lineCount]);
        var store = Type.GetMethod("StoreCheckpoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var pair in checkpoints) store.Invoke(_value, [pair.Key, pair.Value, centerLine]);
        CopyLines(canonical);
    }

    private void CopyLines(Snapshot snapshot)
    {
        foreach (var pair in snapshot.Lines) ((Dictionary<int, IReadOnlyList<TokenSpan>>)Field("_lines"))[pair.Key] = pair.Value;
        foreach (var pair in snapshot.States) ((Dictionary<int, object?>)Field("_lineEndStates"))[pair.Key] = pair.Value;
    }

    internal void Restore(Snapshot snapshot)
    {
        var checkpoints = (IDictionary<int, object?>)Field("_checkpoints");
        checkpoints.Clear();
        Type.GetField("_globalCheckpointInterval", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(_value, snapshot.GlobalInterval);
        foreach (var pair in snapshot.Checkpoints)
            if (!Negative || pair.Key != 0) checkpoints[Negative ? -pair.Key : pair.Key] = pair.Value;
        // Keep lazy collection-view setup in restoration, outside warmed-request timing.
        _ = checkpoints.Keys;
        _ = checkpoints.Values;
        CopyLines(snapshot);
        Type.GetField("_checkpointUpperBound", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(_value, snapshot.Checkpoints[^1].Key);
    }
}

internal sealed record Snapshot(KeyValuePair<int, object?>[] Checkpoints,
    KeyValuePair<int, IReadOnlyList<TokenSpan>>[] Lines, KeyValuePair<int, object?>[] States, int GlobalInterval = 0);

internal sealed class CountingTokenizer(ITokenizer inner, bool reportProgress = false,
    List<KeyValuePair<int, object?>>? checkpointStates = null) : ITokenizer, IStatefulTokenizer
{
    internal long Calls;
    internal long Characters;
    private void Capture(object? state)
    {
        if (checkpointStates != null && Calls % 64 == 0) checkpointStates.Add(new((int)Calls, state));
    }
    public IReadOnlyList<TokenSpan> TokenizeLine(string line, ref string state)
    {
        Calls++; Characters += line.Length;
        if (reportProgress && Calls % 131072 == 0) Console.WriteLine($"Initial scan: {Calls:N0} lines tokenized.");
        var result = inner.TokenizeLine(line, ref state);
        Capture(state);
        return result;
    }
    public IReadOnlyList<TokenSpan> TokenizeLine(string line, ref object? state)
    {
        Calls++; Characters += line.Length;
        if (reportProgress && Calls % 131072 == 0) Console.WriteLine($"Initial scan: {Calls:N0} lines tokenized.");
        if (inner is IStatefulTokenizer stateful)
        {
            var result = stateful.TokenizeLine(line, ref state);
            Capture(state);
            return result;
        }
        var text = state as string ?? "root";
        var tokens = inner.TokenizeLine(line, ref text);
        state = text;
        Capture(state);
        return tokens;
    }
}

internal sealed class SyntheticTokenizer : ITokenizer, IStatefulTokenizer
{
    private static readonly object Open = new();
    private static readonly object Closed = new();
    internal static readonly TokenSpan[] Comment = [new(0, 80, "comment")];
    internal static readonly TokenSpan[] Source = [new(0, 80, "source")];
    public IReadOnlyList<TokenSpan> TokenizeLine(string line, ref string state) => throw new NotSupportedException();
    public IReadOnlyList<TokenSpan> TokenizeLine(string line, ref object? state)
    {
        var comment = ReferenceEquals(state, Open) || line.StartsWith("/*", StringComparison.Ordinal);
        state = line.StartsWith("*/", StringComparison.Ordinal) ? Closed : comment ? Open : Closed;
        return comment ? Comment : Source;
    }
}

internal sealed class StringTokenizer : ITokenizer
{
    public IReadOnlyList<TokenSpan> TokenizeLine(string line, ref string state)
    {
        var comment = state == "comment" || line.StartsWith("/*", StringComparison.Ordinal);
        state = line.StartsWith("*/", StringComparison.Ordinal) ? "root" : comment ? "comment" : "root";
        return comment ? SyntheticTokenizer.Comment : SyntheticTokenizer.Source;
    }
}

internal sealed class ArrayModel(string[] lines) : ICodeEditorTextModel
{
    internal string[] Lines = lines;
    public int LineCount => Lines.Length;
    public int Length => Lines.Sum(line => line.Length + 1) - 1;
    public long Version => 0;
    public long HistoryStateId => 0;
    public string GetLineContent(int lineNumber) => Lines[lineNumber];
    public string GetValue() => string.Join('\n', Lines);
    public string GetText(int offset, int length) => throw new NotSupportedException();
    public void SetValue(string text) => throw new NotSupportedException();
    public int GetLineStart(int lineNumber) => throw new NotSupportedException();
    public int GetLineNumberAt(int offset) => throw new NotSupportedException();
    public (int Line, int Column) GetPositionAt(int offset) => throw new NotSupportedException();
    public int GetOffsetAt(int line, int column) => throw new NotSupportedException();
    public void ApplyEdits(IReadOnlyList<TextEdit> edits) => throw new NotSupportedException();
    public void Replace(int offset, int length, string text) => throw new NotSupportedException();
    public event EventHandler<ContentChangedEventArgs>? Changed { add { } remove { } }
}
