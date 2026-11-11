using System.Text.Json;

namespace CanvasBenchmark.Observer;

internal static class SelfTest
{
    internal static int Run()
    {
        var checks = 0;
        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new InvalidOperationException($"Self-test failed: {name}");
        }
        void Reject(Action action, string name)
        {
            try { action(); } catch (ArgumentException) { checks++; return; }
            throw new InvalidOperationException($"Self-test accepted invalid input: {name}");
        }
        try
        {
            // Independent CRC golden vectors: little-endian four-byte payload, CRC-8/poly07/init00.
            foreach (var (payload, expected) in new (uint, byte)[]
                { (0, 0), (1, 0x16), (0x100, 0x6B), (0x10000, 0x15), (0x1000000, 0x07), (uint.MaxValue, 0xDE) })
                Check(MarkerCodec.Crc(payload) == expected, $"CRC {payload:X8}");
            var random = new Random(0x1234ABCD);
            for (var i = 0; i < 10000; i++)
            {
                var payload = (uint)random.NextInt64(0, 1L << 32);
                Check(MarkerCodec.Crc(payload) == ReferenceCrc(payload), "CRC polynomial reference");
            }
            for (var tag = 1; tag <= 255; tag++)
                for (var phase = 1; phase <= 3; phase++)
                    foreach (var seq in new[] { 0, 1, 17, 32767, 65535 })
                    {
                        var expected = new Marker(tag, phase, seq);
                        var bits = MarkerCodec.Encode(expected);
                        var pixels = MarkerCodec.Pixels(bits);
                        Check(MarkerCodec.Decode(pixels, tag, out var actual) == "valid" && actual == expected, "marker roundtrip");
                    }
            var sample = new Marker(17, 2, 37);
            var encoded = MarkerCodec.Encode(sample);
            Check((encoded & 255) == 0xA5, "sync LSB first");
            Check((uint)(encoded >> 8) == ((17u << 18) | (2u << 16) | 37u), "payload layout");
            Check(MarkerCodec.Decode(MarkerCodec.Pixels(encoded), 18, out _) == "staleTag", "old tag rejected");
            for (var bit = 0; bit < 48; bit++)
            {
                var status = MarkerCodec.Decode(MarkerCodec.Pixels(encoded ^ (1UL << bit)), 17, out _);
                Check(status == (bit < 8 ? "sync" : "crc"), "single-bit corruption rejected");
            }
            uint reservedPayload = (17u << 18) | (2u << 16) | (1u << 26);
            var reserved = 0xA5UL | ((ulong)reservedPayload << 8) | ((ulong)MarkerCodec.Crc(reservedPayload) << 40);
            Check(MarkerCodec.Decode(MarkerCodec.Pixels(reserved), 17, out _) == "reservedBits", "reserved bits rejected");
            Check(MarkerCodec.Decode(MarkerCodec.Pixels(MarkerCodec.Encode(new(17, 0, 0))), 17, out _) == "phase", "phase zero rejected");
            var uncertain = MarkerCodec.Pixels(encoded);
            uncertain[(4 * 96 + 1) * 4] = 100;
            Check(MarkerCodec.Decode(uncertain, 17, out _) == "uncertainPixel", "uncertain pixel rejected");
            var threshold = MarkerCodec.Pixels(encoded);
            for (var i = 0; i < threshold.Length; i += 4)
                for (var channel = 0; channel < 3; channel++) threshold[i + channel] = threshold[i + channel] == 255 ? (byte)200 : (byte)40;
            Check(MarkerCodec.Decode(threshold, 17, out var thresholdMarker) == "valid" && thresholdMarker == sample, "inclusive RGB thresholds");
            Check(MarkerCodec.Decode([], 17, out _) == "bufferSize", "buffer size rejected");
            var distribution = Distribution.Of(Enumerable.Range(1, 100).Select(v => (double)v));
            Check(distribution.P50 == 50 && distribution.P95 == 95 && distribution.P99 == 99 && distribution.Mean == 50.5, "distribution nearest rank");
            Check(Distribution.Of([]).Count == 0, "empty distribution");
            var log = new MarkerLog(3, 3);
            log.Add(2, .1, "sync", default);
            log.Add(6, .1, "valid", new(17, 1, 0));
            log.Add(10, .1, "valid", new(17, 2, 0));
            log.Add(14, .1, "valid", new(17, 2, 0));
            log.Add(18, .1, "valid", new(17, 2, 2));
            log.Add(22, .1, "valid", new(17, 3, 3));
            Check(log.Transitions.Count == 4 && log.Unique.Count == 2 && log.Samples.Count == 6, "transition uniqueness");
            using (var result = JsonDocument.Parse(JsonSerializer.Serialize(log.MarkerResult(), Program.JsonOptions)))
            {
                var root = result.RootElement;
                Check(root.GetProperty("measurementDurationMs").GetDouble() == 12, "phase boundary duration");
                Check(root.GetProperty("missingMeasuredSequences").EnumerateArray().Single().GetInt32() == 1, "missing IDs");
                Check(Math.Abs(root.GetProperty("desktopObservedChangesPerSecond").GetDouble() - 2000d / 12) < .0001, "desktop observation rate");
                Check(root.GetProperty("measurementStartBracketMs")[0].GetDouble() == 6, "boundary predecessor");
            }
            using (var result = JsonDocument.Parse(JsonSerializer.Serialize(log.SamplingResult(), Program.JsonOptions)))
                Check(!result.RootElement.GetProperty("observerLimited").GetBoolean(), "good coverage");
            var limited = new MarkerLog(3, 3);
            limited.Add(0, .1, "sync", default);
            limited.Add(1, .1, "valid", new(17, 2, 0));
            limited.Add(31, .1, "crc", default);
            limited.Add(61, .1, "valid", new(17, 3, 3));
            using (var result = JsonDocument.Parse(JsonSerializer.Serialize(limited.SamplingResult(), Program.JsonOptions)))
                Check(result.RootElement.GetProperty("badCoverage").GetBoolean() &&
                    result.RootElement.GetProperty("reasons").GetArrayLength() == 3, "coverage gaps and invalid flag");
            var unbracketed = new MarkerLog(3, 3);
            unbracketed.Add(1, .1, "valid", new(17, 2, 0));
            unbracketed.Add(5, .1, "valid", new(17, 3, 3));
            using (var result = JsonDocument.Parse(JsonSerializer.Serialize(unbracketed.SamplingResult(), Program.JsonOptions)))
                Check(result.RootElement.GetProperty("reasons").EnumerateArray().Any(r => r.GetString() == "measurementStartUnbracketed"), "unbracketed boundary flag");
            var regression = new MarkerLog(3, 3);
            regression.Add(1, 0, "valid", new(17, 2, 2));
            regression.Add(2, 0, "valid", new(17, 2, 1));
            regression.Add(3, 0, "valid", new(17, 3, 2));
            Check(regression.Failures.Contains("markerSequenceRegression") && regression.Failures.Contains("markerSequenceOutOfRange"), "protocol sequencing failures");
            var observedLaunch = JsonSerializer.Deserialize<LaunchSpec>(
                """{"fileName":"app","args":[],"workingDirectory":"work","environment":{}}""", Program.JsonOptions)!;
            var controlLaunch = JsonSerializer.Deserialize<LaunchSpec>(
                """{"fileName":"app","args":[],"workingDirectory":"work","environment":{},"observeFrames":false}""", Program.JsonOptions)!;
            var explicitObservedLaunch = JsonSerializer.Deserialize<LaunchSpec>(
                """{"fileName":"app","args":[],"workingDirectory":"work","environment":{},"observeFrames":true}""", Program.JsonOptions)!;
            Check(observedLaunch.ObserveFrames && explicitObservedLaunch.ObserveFrames, "frame observation defaults true");
            Check(!controlLaunch.ObserveFrames, "capture control flag accepted");
            Check(Program.ResultStatus(observedLaunch.ObserveFrames, false) == "ok", "observed success status unchanged");
            Check(Program.ResultStatus(controlLaunch.ObserveFrames, false) == "control-complete", "control success is diagnostic-only");
            Check(Program.ResultStatus(true, true) == "failed" && Program.ResultStatus(false, true) == "failed", "control failures remain failures");
            using (var result = JsonDocument.Parse(JsonSerializer.Serialize(Program.SamplingResult(controlLaunch, null), Program.JsonOptions)))
                Check(!result.RootElement.GetProperty("enabled").GetBoolean() && result.RootElement.EnumerateObject().Count() == 1,
                    "control sampling disabled without frame metrics");
            using (var result = JsonDocument.Parse(JsonSerializer.Serialize(Program.SamplingResult(observedLaunch, log), Program.JsonOptions)))
                Check(result.RootElement.GetProperty("enabled").GetBoolean() && result.RootElement.GetProperty("sampleCount").GetInt32() == 6,
                    "default observation sampling unchanged");
            Check(System.Runtime.InteropServices.Marshal.SizeOf<DuplicationInfo>() == 48 &&
                System.Runtime.InteropServices.Marshal.SizeOf<DuplicationFrame>() == 80, "native ABI sizes");
            var converted = DuplicationCapture.ConvertFrame(new DuplicationFrame
            { LastPresentQpc = 1010000, AcquiredQpc = 1011000, CompletedQpc = 1011200, AccumulatedFrames = 1 },
                1000000, 1000000, 11.5);
            Check(Math.Abs(converted.ReadbackLatencyMs - 1.5) < .00001 &&
                Math.Abs(converted.NativeProcessingMs - .2) < .00001, "QPC origin frequency and processing latency");
            var pointer = DuplicationCapture.ConvertFrame(new DuplicationFrame
            { LastPresentQpc = 0, LastMouseQpc = 1010000, AcquiredQpc = 1011000, CompletedQpc = 1011200, PointerOnly = 1 },
                1000000, 1000000, 11.5);
            Check(pointer.PointerOnly && pointer.ReadbackLatencyMs == 0, "pointer-only is not desktop content");
            var desktopLog = new MarkerLog(1, 2, "desktop-duplication") { DesktopFrameIntervalMs = 1000d / 60 };
            desktopLog.Add(0, .2, "valid", new(17, 1, 0), converted);
            desktopLog.Add(17, .2, "valid", new(17, 2, 0), converted);
            desktopLog.Add(34, .2, "valid", new(17, 2, 1), converted);
            desktopLog.Add(51, .2, "valid", new(17, 3, 2), converted);
            using (var result = JsonDocument.Parse(JsonSerializer.Serialize(desktopLog.SamplingResult(), Program.JsonOptions)))
                Check(!result.RootElement.GetProperty("observerLimited").GetBoolean() &&
                    result.RootElement.GetProperty("eventDriven").GetBoolean(), "desktop events use native coverage instead of polling thresholds");
            var accumulated = new MarkerLog(1, 2, "desktop-duplication") { DesktopFrameIntervalMs = 1000d / 60 };
            accumulated.Add(0, .2, "valid", new(17, 1, 0), converted);
            accumulated.Add(17, .2, "valid", new(17, 2, 0), converted with
                { AccumulatedFrames = 3, ReadbackLatencyMs = 100, NativeProcessingMs = 30, ProtectedContentMaskedOut = true });
            accumulated.Add(34, .2, "valid", new(17, 3, 2), converted);
            accumulated.CaptureFailures.Add("mock ACCESS_LOST");
            using (var result = JsonDocument.Parse(JsonSerializer.Serialize(accumulated.SamplingResult(), Program.JsonOptions)))
            {
                var reasons = result.RootElement.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ToArray();
                Check(result.RootElement.GetProperty("observerLimited").GetBoolean() &&
                    reasons.Contains("desktopUpdatesAccumulated") && reasons.Contains("captureFailure") &&
                    reasons.Contains("nativeProcessingExceedsDesktopInterval") && reasons.Contains("desktopEventReadbackLatencyAboveTwoIntervals"),
                    "native accumulation failures and processing latency limit coverage");
            }
            Check(observedLaunch.CaptureBackend == "desktop-duplication" && controlLaunch.CaptureBackend == "desktop-duplication", "default native backend and control parsing");
            var cli = new[] { "--launch", @"C:\a\launch.json", "--output", @"C:\a\observer.json", "--tag", "255" };
            Check(Options.Parse(cli).TimeoutSeconds == 180, "CLI defaults");
            Reject(() => Options.Parse([.. cli, "--tag", "17"]), "duplicate options");
            Reject(() => Options.Parse([.. cli, "--timeout-seconds", "0"]), "invalid timeout");
            Reject(() => Options.Parse(["--launch", "relative", "--output", @"C:\out.json", "--tag", "1"]), "relative launch");
            Reject(() => Options.Parse(["--launch", @"C:\launch.json", "--output", @"C:\out.json", "--tag", "0"]), "zero tag");
            Reject(() => Options.Parse(["--launch"]), "unpaired option");
            Console.WriteLine($"PASS: {checks} CPU-only checks; no Win32 calls, child processes, GUI, benchmark or GPU work.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    internal static int RunNativeCpu()
    {
        try
        {
            var expected = new Marker(41, 2, 37);
            var pixels = MarkerCodec.Pixels(MarkerCodec.Encode(expected));
            const int pitch = 512;
            var padded = new byte[pitch * 8];
            for (var row = 0; row < 8; row++) pixels.AsSpan(row * 384, 384).CopyTo(padded.AsSpan(row * pitch, 384));
            var actual = new byte[3072];
            var hr = DuplicationCapture.CpuCopyRows(padded, (uint)padded.Length, pitch, actual, (uint)actual.Length, out var nativeSize);
            if (hr != 0 || nativeSize != System.Runtime.InteropServices.Marshal.SizeOf<DuplicationFrame>() ||
                !actual.AsSpan().SequenceEqual(pixels) || MarkerCodec.Decode(actual, 41, out var marker) != "valid" || marker != expected)
                throw new InvalidOperationException("Native row-copy / ABI / BGRA contract mismatch.");
            var probes = RoiDiagnostics.Probe(actual);
            if (probes[0].Classification != "white" || probes[1].Classification != "black")
                throw new InvalidOperationException("Native BGRA cell centers differ.");
            Console.WriteLine("PASS: native CPU row-copy, ABI80, marshaled BGRA and marker decode; no device/capture/window/child/GPU creation.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static byte ReferenceCrc(uint payload)
    {
        // Feed each little-endian byte MSB-first, independent of the production byte-XOR loop.
        var remainder = 0;
        for (var b = 0; b < 4; b++)
            for (var bit = 7; bit >= 0; bit--)
            {
                var feedback = ((remainder >> 7) ^ (int)(payload >> (8 * b + bit))) & 1;
                remainder = ((remainder << 1) & 255) ^ (feedback != 0 ? 7 : 0);
            }
        return (byte)remainder;
    }
}
