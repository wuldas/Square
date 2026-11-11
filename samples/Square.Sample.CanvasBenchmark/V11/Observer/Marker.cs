namespace CanvasBenchmark.Observer;

internal readonly record struct Marker(int Tag, int Phase, int Seq);

internal static class MarkerCodec
{
    internal const int Width = 96, Height = 8;

    internal static byte Crc(uint payload)
    {
        byte crc = 0;
        for (var b = 0; b < 4; b++)
        {
            crc ^= (byte)(payload >> (b * 8));
            for (var bit = 0; bit < 8; bit++)
                crc = (byte)((crc << 1) ^ ((crc & 0x80) != 0 ? 0x07 : 0));
        }
        return crc;
    }

    internal static ulong Encode(Marker marker)
    {
        var payload = (uint)((marker.Tag & 255) << 18 | (marker.Phase & 3) << 16 | (marker.Seq & 65535));
        return 0xA5UL | ((ulong)payload << 8) | ((ulong)Crc(payload) << 40);
    }

    internal static string Decode(ReadOnlySpan<byte> bgra, int expectedTag, out Marker marker)
    {
        marker = default;
        if (bgra.Length != Width * Height * 4) return "bufferSize";
        ulong bits = 0;
        for (var cell = 0; cell < 48; cell++)
        {
            var offset = (4 * Width + 2 * cell + 1) * 4;
            var b = bgra[offset]; var g = bgra[offset + 1]; var r = bgra[offset + 2];
            if (r >= 200 && g >= 200 && b >= 200) bits |= 1UL << cell;
            else if (!(r <= 40 && g <= 40 && b <= 40)) return "uncertainPixel";
        }
        if ((byte)bits != 0xA5) return "sync";
        var payload = (uint)(bits >> 8);
        if (Crc(payload) != (byte)(bits >> 40)) return "crc";
        if ((payload >> 26) != 0) return "reservedBits";
        marker = new((int)(payload >> 18) & 255, (int)(payload >> 16) & 3, (int)payload & 65535);
        if (marker.Tag != expectedTag) return "staleTag";
        if (marker.Phase == 0) return "phase";
        return "valid";
    }

    internal static byte[] Pixels(ulong bits)
    {
        var pixels = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var offset = (y * Width + x) * 4;
                var value = (byte)(((bits >> (x / 2)) & 1) != 0 ? 255 : 0);
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        return pixels;
    }
}

internal sealed record Distribution(int Count, double? Mean, double? P50, double? P95, double? P99, double? Max)
{
    internal static Distribution Of(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return new(0, null, null, null, null, null);
        double Percentile(double p) => sorted[Math.Max(0, (int)Math.Ceiling(p * sorted.Length) - 1)];
        return new(sorted.Length, sorted.Average(), Percentile(.5), Percentile(.95), Percentile(.99), sorted[^1]);
    }
}

internal sealed record CaptureSample(double AtMs, double? GapMs, double CaptureMs, string DecodeStatus,
    DesktopFrameTelemetry? DesktopFrame = null);
internal sealed record MarkerTransition(double AtMs, int Tag, int Phase, int Seq, double? PrecedingSampleGapMs);
internal sealed record ObservedFrame(int Seq, double FirstObservedMs);

internal sealed class MarkerLog(int warmupFrames, int measuredFrames, string captureBackend = "gdi")
{
    internal bool EventDriven => captureBackend == "desktop-duplication";
    internal double? DesktopFrameIntervalMs { get; set; }
    internal List<string> CaptureFailures { get; } = [];
    internal List<CaptureSample> Samples { get; } = [];
    internal List<MarkerTransition> Transitions { get; } = [];
    internal List<ObservedFrame> Unique { get; } = [];
    internal List<string> Failures { get; } = [];
    internal double? FirstMarkerMs { get; private set; }
    internal double? FirstMeasurementMs { get; private set; }
    internal double? FirstEndMs { get; private set; }
    private Marker? _last;
    private readonly HashSet<int> _seen = [];

    internal void Add(double atMs, double captureMs, string decodeStatus, Marker marker, DesktopFrameTelemetry? desktopFrame = null)
    {
        var gap = Samples.Count == 0 ? (double?)null : atMs - Samples[^1].AtMs;
        Samples.Add(new(atMs, gap, captureMs, decodeStatus, desktopFrame));
        if (decodeStatus != "valid") return;
        FirstMarkerMs ??= atMs;
        if (_last == marker) return;
        Transitions.Add(new(atMs, marker.Tag, marker.Phase, marker.Seq, gap));
        if (_last is { } last && (marker.Phase < last.Phase ||
            (marker.Phase == last.Phase && marker.Seq < last.Seq)))
            FailOnce("markerSequenceRegression");
        _last = marker;
        if ((marker.Phase == 1 && marker.Seq >= warmupFrames) ||
            (marker.Phase == 2 && marker.Seq >= measuredFrames) ||
            (marker.Phase == 3 && marker.Seq != measuredFrames))
        { FailOnce("markerSequenceOutOfRange"); return; }
        if (marker.Phase == 2)
        {
            FirstMeasurementMs ??= atMs;
            if (_seen.Add(marker.Seq)) Unique.Add(new(marker.Seq, atMs));
        }
        if (marker.Phase == 3) FirstEndMs ??= atMs;
    }

    private void FailOnce(string failure) { if (!Failures.Contains(failure)) Failures.Add(failure); }

    internal object MarkerResult()
    {
        var duration = FirstEndMs - FirstMeasurementMs;
        return new
        {
            FirstMarkerMs, FirstMeasurementMs, FirstEndMs,
            measurementDurationMs = duration,
            desktopObservedChangesPerSecond = duration > 0 ? Unique.Count * 1000 / duration : null,
            uniqueMeasuredCount = Unique.Count, uniqueMeasuredFrames = Unique,
            missingMeasuredSequences = Enumerable.Range(0, measuredFrames).Where(seq => !_seen.Contains(seq)).ToArray(),
            transitionRecords = Transitions,
            visibleIntervalMs = Distribution.Of(Unique.Zip(Unique.Skip(1), (a, b) => b.FirstObservedMs - a.FirstObservedMs)),
            timestampSource = EventDriven ? "DXGI LastPresentTime QPC desktop-update event" : "GDI capture completion",
            measurementStartBracketMs = Bracket(FirstMeasurementMs), measurementEndBracketMs = Bracket(FirstEndMs)
        };
    }

    private double?[]? Bracket(double? at)
    {
        if (at is null) return null;
        if (EventDriven) return [at, at];
        var sample = Samples.First(s => s.AtMs == at);
        return [sample.GapMs is { } gap ? at - gap : null, at];
    }

    internal object SamplingResult()
    {
        // The first measurement sample's predecessor gap brackets the unknown true phase boundary.
        var window = Samples.Where(s => FirstMeasurementMs is not null && s.AtMs >= FirstMeasurementMs &&
            (FirstEndMs is null || s.AtMs <= FirstEndMs)).ToArray();
        var gaps = Distribution.Of(window.Where(s => s.GapMs.HasValue).Select(s => s.GapMs!.Value));
        var reasons = new List<string>();
        var eventFrames = window.Where(s => s.DesktopFrame is not null).Select(s => s.DesktopFrame!).ToArray();
        var frameInterval = DesktopFrameIntervalMs ?? gaps.P50 ?? (1000d / 60);
        if (FirstMeasurementMs is null || FirstEndMs is null) reasons.Add("missingMeasurementBoundary");
        if (EventDriven)
        {
            if (eventFrames.Length != window.Length) reasons.Add("missingNativeDesktopMetadata");
            if (eventFrames.Any(f => f.AccumulatedFrames > 1)) reasons.Add("desktopUpdatesAccumulated");
            if (eventFrames.Any(f => f.ProtectedContentMaskedOut)) reasons.Add("protectedDesktopContentMasked");
            if (eventFrames.Any(f => f.NativeProcessingMs > frameInterval)) reasons.Add("nativeProcessingExceedsDesktopInterval");
            if (eventFrames.Any(f => f.ReadbackLatencyMs > 2 * frameInterval)) reasons.Add("desktopEventReadbackLatencyAboveTwoIntervals");
            if (window.Any(s => s.GapMs <= 0)) reasons.Add("nonmonotonicDesktopEventTimestamp");
        }
        else
        {
            if (FirstMeasurementMs is { } first && Samples.First(s => s.AtMs == first).GapMs is null)
                reasons.Add("measurementStartUnbracketed");
            if (gaps.P95 > 12) reasons.Add("measurementP95GapAbove12Ms");
            if (gaps.Max > 24) reasons.Add("measurementGapAbove24Ms");
        }
        if (CaptureFailures.Count > 0) reasons.Add("captureFailure");
        if (window.Any(s => s.DecodeStatus != "valid")) reasons.Add("invalidOrStaleSamplesInsideMeasurement");
        var valid = Samples.Count(s => s.DecodeStatus == "valid");
        var stale = Samples.Count(s => s.DecodeStatus == "staleTag");
        return new
        {
            enabled = true, backend = captureBackend, eventDriven = EventDriven,
            targetPollMs = EventDriven ? (double?)null : 4,
            acquireTimeoutMs = EventDriven ? (int?)8 : null,
            intervalResolution = EventDriven
                ? "DXGI desktop-update event (not scanout); event gap follows desktop changes, QPC tick is timestamp precision only"
                : "Desktop polling; actual capture-completion gaps bound visibility resolution",
            qpcTickMs = EventDriven ? 1000d / System.Diagnostics.Stopwatch.Frequency : (double?)null,
            desktopFrameIntervalMs = EventDriven ? frameInterval : (double?)null,
            desktopFrameIntervalSource = EventDriven ? DesktopFrameIntervalMs.HasValue ? "DXGI output refresh rate" : "measurement event-gap P50; 60Hz fallback if unavailable" : null,
            nativeProcessingBudgetMs = EventDriven ? frameInterval : (double?)null,
            readbackLatencyBudgetMs = EventDriven ? 2 * frameInterval : (double?)null,
            readbackLatencyMs = Distribution.Of(eventFrames.Select(f => f.ReadbackLatencyMs)),
            nativeProcessingMs = Distribution.Of(eventFrames.Select(f => f.NativeProcessingMs)),
            accumulatedExtraDesktopFrames = eventFrames.Sum(f => Math.Max(0, (long)f.AccumulatedFrames - 1)),
            captureFailures = CaptureFailures,
            sampleCount = Samples.Count, validSamples = valid,
            invalidSamples = Samples.Count - valid, staleTagSamples = stale,
            invalidRate = Samples.Count > 0 ? (double)(Samples.Count - valid) / Samples.Count : (double?)null,
            rejectionCounts = Samples.Where(s => s.DecodeStatus != "valid").GroupBy(s => s.DecodeStatus).ToDictionary(g => g.Key, g => g.Count()),
            captureMs = Distribution.Of(Samples.Select(s => s.CaptureMs)),
            gapMs = Distribution.Of(Samples.Where(s => s.GapMs.HasValue).Select(s => s.GapMs!.Value)),
            measurementGapMs = gaps,
            measurementCaptureMs = Distribution.Of(window.Select(s => s.CaptureMs)),
            measurementSampleCount = window.Length,
            measurementInvalidRate = window.Length > 0 ? (double)window.Count(s => s.DecodeStatus != "valid") / window.Length : (double?)null,
            observerLimited = reasons.Count > 0, badCoverage = reasons.Count > 0, reasons, samples = Samples
        };
    }
}
