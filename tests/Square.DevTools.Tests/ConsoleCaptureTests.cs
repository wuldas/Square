using System.Text;
using Square.Hosting;
using Xunit;

namespace Square.DevTools.Tests;

[CollectionDefinition("ConsoleCapture", DisableParallelization = true)]
public sealed class ConsoleCaptureCollection;

[Collection("ConsoleCapture")]
public sealed class ConsoleCaptureTests
{
    [Fact]
    public async Task CapturedManagedLineReplaysWithoutChangingOriginalOutput()
    {
        var savedOut = System.Console.Out;
        var original = new StringWriter();
        System.Console.SetOut(original);
        try
        {
            await using var server = DevToolsServer.Start(new AppWindow("Console capture"), new()
            {
                AllowChromeInspect = true,
                AllowChromeConsole = true
            });
            var started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            System.Console.Write("a");
            System.Console.WriteLine("b");
            using var connection = new CdpConnection();
            await connection.ConnectAsync(server);
            using var enabled = await connection.RequestAsync("Log.enable");
            using var added = await connection.ReceiveEventAsync("Log.entryAdded");
            var entry = added.RootElement.GetProperty("params").GetProperty("entry");
            Assert.Equal("other", entry.GetProperty("source").GetString());
            Assert.Equal("info", entry.GetProperty("level").GetString());
            Assert.Equal("ab", entry.GetProperty("text").GetString());
            Assert.InRange(entry.GetProperty("timestamp").GetInt64(), started, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Assert.Equal("ab" + original.NewLine, original.ToString());
        }
        finally
        {
            System.Console.SetOut(savedOut);
        }
    }

    [Fact]
    public void TeeMergesPartialWritesAndTagsDirections()
    {
        var savedOut = System.Console.Out;
        var savedError = System.Console.Error;
        var outWriter = new StringWriter();
        var errorWriter = new StringWriter();
        System.Console.SetOut(outWriter);
        System.Console.SetError(errorWriter);
        try
        {
            var lines = new List<(bool Stderr, string Text)>();
            using var subscription = DevToolsConsoleCapture.Subscribe((stderr, text) => lines.Add((stderr, text)));

            System.Console.Write("a");
            System.Console.WriteLine("b");
            System.Console.Error.WriteLine("oops");
            System.Console.Error.Write("er");
            System.Console.Write("out");
            System.Console.Error.WriteLine("ror");
            System.Console.WriteLine("put");

            Assert.Equal<(bool, string)>([
                (false, "ab"),
                (true, "oops"),
                (true, "error"),
                (false, "output")
            ], lines);
            Assert.Equal("ab" + outWriter.NewLine + "output" + outWriter.NewLine, outWriter.ToString());
            Assert.Equal("oops" + errorWriter.NewLine + "error" + errorWriter.NewLine, errorWriter.ToString());
        }
        finally
        {
            System.Console.SetOut(savedOut);
            System.Console.SetError(savedError);
        }
    }

    [Fact]
    public void TeeTreatsCrAndLfAndCrlfAsSingleEndingsAndFlushesPartialLines()
    {
        var savedOut = System.Console.Out;
        var outWriter = new StringWriter();
        System.Console.SetOut(outWriter);
        try
        {
            var lines = new List<string>();
            using var subscription = DevToolsConsoleCapture.Subscribe((_, text) => lines.Add(text));

            System.Console.Write("a\r\nb\nc\r"); // c ends on a trailing CR, which may continue as CRLF
            Assert.Equal(["a", "b", "c"], lines);
            System.Console.Write("\nd"); // the LF completes the split CRLF, so no empty line appears
            Assert.Equal(["a", "b", "c"], lines);
            System.Console.Out.Flush(); // flush emits the pending partial line
            Assert.Equal(["a", "b", "c", "d"], lines);
            System.Console.Write("\r\ne"); // Finishes the flushed partial line, not an extra empty line.
            System.Console.Write("\n\n");
            Assert.Equal(["a", "b", "c", "d", "e", ""], lines);
            System.Console.Write("x\ry"); // CR followed by content is a plain line ending
            Assert.Equal(["a", "b", "c", "d", "e", "", "x"], lines);
            System.Console.Out.Flush();
            Assert.Equal(["a", "b", "c", "d", "e", "", "x", "y"], lines);

            Assert.Equal("a\r\nb\nc\r" + "\nd" + "\r\ne" + "\n\n" + "x\ry", outWriter.ToString());
        }
        finally
        {
            System.Console.SetOut(savedOut);
        }
    }

    [Fact]
    public void TeeSplitsLongLinesWithoutBreakingSurrogatePairs()
    {
        var savedOut = System.Console.Out;
        var outWriter = new StringWriter();
        System.Console.SetOut(outWriter);
        try
        {
            var segments = new List<string>();
            using var subscription = DevToolsConsoleCapture.Subscribe((_, text) => segments.Add(text));

            // 8191 ASCII units place the first high surrogate exactly on the 8192
            // boundary; the second emoji exercises a mid-buffer chunk boundary.
            var payload = new string('a', 8191) + "\uD83D\uDE00" + new string('b', 8192) + "\uD83D\uDE00" + "z";
            System.Console.Write(payload);
            System.Console.Out.Flush();

            Assert.Equal(3, segments.Count);
            Assert.All(segments, segment => Assert.True(segment.Length <= 8192, "segment exceeded the 8192 unit limit"));
            Assert.All(segments, AssertNoBrokenSurrogates);
            Assert.Equal(payload, string.Concat(segments));
            Assert.Equal(payload, outWriter.ToString());
        }
        finally
        {
            System.Console.SetOut(savedOut);
        }
    }

    [Fact]
    public void TeeRestoresOriginalWriterOnlyAfterLastSubscriber()
    {
        var savedOut = System.Console.Out;
        var original = new StringWriter();
        System.Console.SetOut(original);
        var originalIdentity = System.Console.Out;
        try
        {
            var firstLines = new List<string>();
            var secondLines = new List<string>();
            using var first = DevToolsConsoleCapture.Subscribe((_, text) => firstLines.Add(text));
            using var second = DevToolsConsoleCapture.Subscribe((_, text) => secondLines.Add(text));

            System.Console.WriteLine("mid");
            Assert.Equal(["mid"], firstLines);
            Assert.Equal(["mid"], secondLines);
            Assert.False(ReferenceEquals(System.Console.Out, original));

            first.Dispose();
            System.Console.WriteLine("tail");
            Assert.Equal(["mid", "tail"], secondLines);
            Assert.False(ReferenceEquals(System.Console.Out, original));

            second.Dispose();
            Assert.Same(originalIdentity, System.Console.Out);
            System.Console.Write("plain");
            Assert.Equal(["mid", "tail"], secondLines);
            Assert.Equal("mid" + original.NewLine + "tail" + original.NewLine + "plain", original.ToString());
        }
        finally
        {
            System.Console.SetOut(savedOut);
        }
    }

    [Fact]
    public void TeeKeepsApplicationWriterInstalledWhenAppReplacesItDuringCapture()
    {
        var savedOut = System.Console.Out;
        var wrapped = new StringWriter();
        var appWriter = new StringWriter();
        System.Console.SetOut(wrapped);
        try
        {
            var received = new List<string>();
            using var subscription = DevToolsConsoleCapture.Subscribe((_, text) => received.Add(text));
            System.Console.WriteLine("pre");
            Assert.Equal(["pre"], received);

            System.Console.SetOut(appWriter); // application replaces the captured writer
            var appIdentity = System.Console.Out;
            System.Console.WriteLine("app-only");
            Assert.Equal(["pre"], received); // replacement output bypasses the capture
            Assert.Equal("app-only" + appWriter.NewLine, appWriter.ToString());

            subscription.Dispose();
            Assert.Same(appIdentity, System.Console.Out); // Unsubscribe never overwrites the app writer.
        }
        finally
        {
            System.Console.SetOut(savedOut);
        }
    }

    [Fact]
    public async Task TeePropagatesOriginalWriterExceptionBeforeCapturing()
    {
        var savedOut = System.Console.Out;
        System.Console.SetOut(new ThrowingWriter());
        try
        {
            var received = new List<string>();
            using var subscription = DevToolsConsoleCapture.Subscribe((_, text) => received.Add(text));

            var exception = Assert.Throws<InvalidOperationException>(() => System.Console.Write("x"));
            Assert.Equal("original boom", exception.Message);
            Assert.Empty(received); // the original write runs first, so nothing is captured from it

            Assert.Throws<InvalidOperationException>(() => System.Console.WriteLine());
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await System.Console.Out.WriteAsync("y"));
        }
        finally
        {
            System.Console.SetOut(savedOut);
        }
    }

    [Fact]
    public void TypedAndFormattedOriginalWriterExceptionsArePreserved()
    {
        var saved = System.Console.Out;
        var writer = new OverloadFailureWriter();
        System.Console.SetOut(writer);
        try
        {
            using var subscription = DevToolsConsoleCapture.Subscribe((_, _) => { });
            Assert.Same(writer.Failure, Assert.Throws<InvalidOperationException>(() => System.Console.Write(42)));
            Assert.Same(writer.Failure, Assert.Throws<InvalidOperationException>(() => System.Console.Write("{0}", 42)));
            Assert.Same(writer.Failure, Assert.Throws<InvalidOperationException>(() => System.Console.WriteLine(1.5)));
        }
        finally { System.Console.SetOut(saved); }
    }

    [Fact]
    public void ExactChunkEndAndSplitCrlfDoNotCreatePhantomEmptyLines()
    {
        var saved = System.Console.Out;
        var writer = new StringWriter();
        System.Console.SetOut(writer);
        try
        {
            var lines = new List<string>();
            using var subscription = DevToolsConsoleCapture.Subscribe((_, text) => lines.Add(text));
            var chunk = new string('x', 8192);
            System.Console.Write(chunk);
            Assert.Equal(chunk, Assert.Single(lines));
            System.Console.WriteLine();
            Assert.Single(lines);
            System.Console.Write("a\r");
            System.Console.Write("");
            System.Console.Out.Flush();
            System.Console.Write("\n");
            Assert.Equal([chunk, "a"], lines);
            System.Console.WriteLine();
            Assert.Equal([chunk, "a", ""], lines);
            System.Console.Out.NewLine = "|";
            System.Console.WriteLine("tail");
            System.Console.Out.Flush();
            Assert.Equal("tail|", lines[^1]);
        }
        finally { System.Console.SetOut(saved); }
    }

    private sealed class OverloadFailureWriter : StringWriter
    {
        public InvalidOperationException Failure { get; } = new("original overload failed");
        public override void Write(int value) => throw Failure;
        public override void Write(string format, object? arg0) => throw Failure;
        public override void WriteLine(double value) => throw Failure;
    }

    private static void AssertNoBrokenSurrogates(string segment)
    {
        for (var i = 0; i < segment.Length; i++)
        {
            if (char.IsHighSurrogate(segment[i]))
                Assert.True(i + 1 < segment.Length && char.IsLowSurrogate(segment[i + 1]), "high surrogate was split across segments");
            else if (char.IsLowSurrogate(segment[i]))
                Assert.True(i > 0 && char.IsHighSurrogate(segment[i - 1]), "low surrogate was split across segments");
        }
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => throw CreateFailure();
        public override void Write(char[] buffer, int index, int count) => throw CreateFailure();
        public override void Write(string? value) => throw CreateFailure();
        public override void WriteLine() => throw CreateFailure();

        private static InvalidOperationException CreateFailure() => new("original boom");
    }
}

public sealed class DevToolsLogCollectorTests
{
    [Fact]
    public void CollectorAppendsMonotonicSequenceWithCurrentTimestamps()
    {
        using var collector = new DevToolsLogCollector();
        using (collector.Subscribe(_ => { }, out var empty))
            Assert.Empty(empty);

        var started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        collector.Append("info", "first");
        collector.Append("warning", "second");
        collector.Append("error", "third");
        var finished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var subscription = collector.Subscribe(_ => { }, out var snapshot);
        Assert.Equal(3, snapshot.Length);
        Assert.Equal([1, 2, 3], snapshot.Select(entry => entry.Sequence));
        Assert.Equal([("info", "first"), ("warning", "second"), ("error", "third")],
            snapshot.Select(entry => (entry.Level, entry.Text)));
        Assert.All(snapshot, entry => Assert.InRange(entry.Timestamp, started, finished));
        Assert.True(snapshot[0].Timestamp <= snapshot[1].Timestamp);
        Assert.True(snapshot[1].Timestamp <= snapshot[2].Timestamp);
    }

    [Fact]
    public void CollectorRingHoldsOnlyLatest256Entries()
    {
        using var collector = new DevToolsLogCollector();
        var received = new List<long>();
        using var subscription = collector.Subscribe(entry => received.Add(entry.Sequence), out _);

        for (var sequence = 1; sequence <= 257; sequence++)
            collector.Append("info", $"row {sequence}");

        Assert.Equal(257, received.Count);
        using var probe = collector.Subscribe(_ => { }, out var snapshot);
        Assert.Equal(256, snapshot.Length);
        Assert.Equal(2, snapshot[0].Sequence); // the first row was evicted
        Assert.Equal(257, snapshot[^1].Sequence);
        Assert.Equal("row 257", snapshot[^1].Text);
        Assert.Equal(Enumerable.Range(2, 256).Select(value => (long)value), snapshot.Select(entry => entry.Sequence));
    }

    [Fact]
    public void CollectorSnapshotAndLiveDeliveryDoNotOverlap()
    {
        using var collector = new DevToolsLogCollector();
        collector.Append("info", "one");
        collector.Append("error", "two");
        collector.Append("info", "three");

        var live = new List<DevToolsLogEntry>();
        using var subscription = collector.Subscribe(live.Add, out var snapshot);
        Assert.Equal([1, 2, 3], snapshot.Select(entry => entry.Sequence));
        Assert.Equal(["one", "two", "three"], snapshot.Select(entry => entry.Text));

        collector.Append("warning", "four");
        collector.Append("verbose", "five");
        Assert.Equal([4, 5], live.Select(entry => entry.Sequence));
        Assert.Equal(["four", "five"], live.Select(entry => entry.Text));

        Assert.Equal([1L, 2, 3, 4, 5], snapshot.Concat(live).Select(entry => entry.Sequence).Distinct());
        subscription.Dispose();
        collector.Append("info", "six");
        Assert.Equal([4, 5], live.Select(entry => entry.Sequence));
    }

    [Fact]
    public void CollectorResubscribeReplaysRowsSeenDuringDetachment()
    {
        using var collector = new DevToolsLogCollector();
        collector.Append("info", "first");
        var first = collector.Subscribe(_ => { }, out var initialSnapshot);
        Assert.Single(initialSnapshot);
        first.Dispose();

        collector.Append("warning", "while detached");
        collector.Append("info", "also while detached");

        var live = new List<DevToolsLogEntry>();
        using var second = collector.Subscribe(live.Add, out var replayed);
        Assert.Equal([1, 2, 3], replayed.Select(entry => entry.Sequence));
        Assert.Equal(["first", "while detached", "also while detached"], replayed.Select(entry => entry.Text));

        collector.Append("info", "fresh");
        Assert.Single(live);
        Assert.Equal(4, live[0].Sequence);
        Assert.Equal("fresh", live[0].Text);
    }

    [Fact]
    public void CollectorClearAdvancesWatermarkWithoutRewindingSequence()
    {
        using var collector = new DevToolsLogCollector();
        collector.Append("info", "stale one");
        collector.Append("info", "stale two");
        Assert.Equal(0, collector.ClearedThrough);

        collector.Clear();
        Assert.Equal(2, collector.ClearedThrough);

        using var probe = collector.Subscribe(_ => { }, out var clearedSnapshot);
        Assert.Empty(clearedSnapshot); // cleared history is gone for new subscribers

        var live = new List<DevToolsLogEntry>();
        using var subscription = collector.Subscribe(live.Add, out _);
        collector.Append("info", "fresh");
        Assert.Single(live);
        Assert.Equal(3, live[0].Sequence); // sequences continue instead of rewinding
        Assert.Equal("fresh", live[0].Text);
    }

    [Fact]
    public void CollectorDisposeRejectsLaterArrivalsAndSubscriptions()
    {
        using var collector = new DevToolsLogCollector();
        collector.Append("info", "kept");
        var live = new List<DevToolsLogEntry>();
        using var subscription = collector.Subscribe(live.Add, out var snapshot);
        Assert.Single(snapshot);

        collector.Dispose();
        collector.Append("info", "after dispose");
        Assert.Empty(live);

        using var late = collector.Subscribe(live.Add, out var lateSnapshot);
        Assert.Empty(lateSnapshot);
        late.Dispose();

        collector.Clear(); // must be a harmless no-op after dispose
    }

    [Fact]
    public async Task CollectorDeliversConcurrentAppendsInSequenceOrder()
    {
        using var collector = new DevToolsLogCollector();
        var sequences = new List<long>();
        using var subscription = collector.Subscribe(entry =>
        {
            lock (sequences) sequences.Add(entry.Sequence);
        }, out _);

        var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++) collector.Append("info", "concurrent");
        })).ToArray();
        await Task.WhenAll(writers);

        lock (sequences)
        {
            Assert.Equal(400, sequences.Count);
            for (var i = 1; i < sequences.Count; i++)
                Assert.Equal(sequences[i - 1] + 1, sequences[i]);
        }

        using var probe = collector.Subscribe(_ => { }, out var snapshot);
        Assert.Equal(256, snapshot.Length);
        Assert.Equal(145, snapshot[0].Sequence);
        Assert.Equal(400, snapshot[^1].Sequence);
    }
}
