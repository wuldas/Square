using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace CanvasBenchmark.Observer;

internal sealed record Options(string LaunchPath, string OutputPath, int Tag, int TimeoutSeconds)
{
    internal static Options Parse(string[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("All options must be key/value pairs.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (args[i] is not ("--launch" or "--output" or "--tag" or "--timeout-seconds") ||
                !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException($"Unknown or duplicate option {args[i]}.");
        }
        string Required(string key) => values.TryGetValue(key, out var value) ? value : throw new ArgumentException($"Missing {key}.");
        var launch = Required("--launch"); var output = Required("--output");
        if (!Path.IsPathFullyQualified(launch) || !Path.IsPathFullyQualified(output)) throw new ArgumentException("Launch/output paths must be absolute.");
        if (!int.TryParse(Required("--tag"), out var tag) || tag is < 1 or > 255) throw new ArgumentException("Tag must be 1..255.");
        var timeout = 180;
        if (values.TryGetValue("--timeout-seconds", out var raw) && (!int.TryParse(raw, out timeout) || timeout <= 0))
            throw new ArgumentException("Timeout must be a positive integer number of seconds.");
        return new(launch, output, tag, timeout);
    }
}

internal sealed record LaunchSpec(string FileName, string[] Args, string WorkingDirectory,
    Dictionary<string, string> Environment, int ExpectedClientWidth = 1280, int ExpectedClientHeight = 800,
    bool ObserveFrames = true, string CaptureBackend = "desktop-duplication")
{
    internal string Argument(string key)
    {
        var matches = Args.Select((arg, index) => (arg, index)).Where(p => p.arg == key).ToArray();
        if (matches.Length != 1 || matches[0].index + 1 >= Args.Length) throw new ArgumentException($"Launch args require one {key} value.");
        return Args[matches[0].index + 1];
    }

    internal void Validate(int tag)
    {
        if (!Path.IsPathFullyQualified(FileName) || !File.Exists(FileName)) throw new ArgumentException("fileName must name an existing absolute executable.");
        if (!Path.IsPathFullyQualified(WorkingDirectory) || !Directory.Exists(WorkingDirectory)) throw new ArgumentException("workingDirectory must be an existing absolute directory.");
        if (Args is null || Environment is null) throw new ArgumentException("args and environment are required.");
        if (ExpectedClientWidth != 1280 || ExpectedClientHeight != 800) throw new ArgumentException("V11 requires 1280x800.");
        if (CaptureBackend is not ("desktop-duplication" or "gdi")) throw new ArgumentException("captureBackend must be desktop-duplication or gdi.");
        if (!int.TryParse(Argument("--tag"), out var appTag) || appTag != tag) throw new ArgumentException("Application and observer tags must match.");
        if (!Path.IsPathFullyQualified(Argument("--trace"))) throw new ArgumentException("Application trace path must be absolute.");
    }
}

internal static class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, WriteIndented = true };

    internal static string ResultStatus(bool observeFrames, bool failed) =>
        failed ? "failed" : observeFrames ? "ok" : "control-complete";

    internal static object? SamplingResult(LaunchSpec? launch, MarkerLog? markers) =>
        launch?.ObserveFrames == false ? new { enabled = false } : markers?.SamplingResult();

    private static int Main(string[] args)
    {
        if (args.SequenceEqual(["--self-test"])) return SelfTest.Run();
        if (args.SequenceEqual(["--self-test-native-cpu"])) return SelfTest.RunNativeCpu();
        Options options;
        try { options = Options.Parse(args); }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 2; }
        return Run(options);
    }

    private static int Run(Options options)
    {
        var failures = new List<string>();
        var clock = new Stopwatch();
        var startedUtc = DateTime.UtcNow;
        using var observer = Process.GetCurrentProcess();
        var observerCpuBase = observer.TotalProcessorTime.TotalMilliseconds;
        var preparationCpuMs = 0d;
        LaunchSpec? launch = null;
        Process? child = null;
        ProcessTreeSampler? tree = null;
        MarkerLog? markers = null;
        DuplicationCapture? duplication = null;
        object? exitGrace = null;
        long clockOriginQpc = 0;
        double clockAlignmentUncertaintyMs = 0;
        WindowInfo? windowInfo = null;
        var geometry = new List<WindowInfo>();
        var roiDiagnostics = new RoiDiagnostics();
        object? desktopIdentityDiagnostic = null;
        var windowCloseObservations = new List<object>();
        double? windowDestroyedAtMs = null;
        var traceSha256 = "";
        var measureFrames = 0;
        int? childExitCode = null;
        int? rootPid = null;
        var cancelled = false;
        ConsoleCancelEventHandler onCancel = (_, e) => { cancelled = true; e.Cancel = true; };
        Console.CancelKeyPress += onCancel;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Desktop observer requires Windows.");
            launch = JsonSerializer.Deserialize<LaunchSpec>(File.ReadAllText(options.LaunchPath), JsonOptions)
                ?? throw new InvalidDataException("Empty launch JSON.");
            launch.Validate(options.Tag);
            var traceBytes = File.ReadAllBytes(launch.Argument("--trace"));
            traceSha256 = Convert.ToHexString(SHA256.HashData(traceBytes)).ToLowerInvariant();
            using var trace = JsonDocument.Parse(traceBytes);
            var root = trace.RootElement;
            if (root.GetProperty("protocolVersion").GetInt32() != 11 || root.GetProperty("width").GetInt32() != 1280 ||
                root.GetProperty("height").GetInt32() != 800) throw new InvalidDataException("Trace must be V11 and 1280x800.");
            var warmupFrames = root.GetProperty("warmupFrames").GetInt32();
            measureFrames = root.GetProperty("measureFrames").GetInt32();
            if (warmupFrames is < 0 or > 65536 || measureFrames is < 1 or > 65535 ||
                root.GetProperty("frames").GetArrayLength() != warmupFrames + measureFrames)
                throw new InvalidDataException("Trace frame counts are invalid for the native marker sequence.");
            markers = launch.ObserveFrames ? new(warmupFrames, measureFrames, launch.CaptureBackend) : null;
            Native.SetProcessDpiAwarenessContext(new nint(-4));
            if (!Native.AreDpiAwarenessContextsEqual(Native.GetThreadDpiAwarenessContext(), new nint(-4)))
                throw new InvalidOperationException("Observer must use per-monitor V2 DPI awareness.");
            var start = new ProcessStartInfo(launch.FileName)
            {
                WorkingDirectory = launch.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = false, RedirectStandardError = false, RedirectStandardInput = false
            };
            foreach (var arg in launch.Args) start.ArgumentList.Add(arg);
            foreach (var pair in launch.Environment) start.Environment[pair.Key] = pair.Value;
            observer.Refresh();
            var beforeLaunchCpuMs = observer.TotalProcessorTime.TotalMilliseconds;
            preparationCpuMs = beforeLaunchCpuMs - observerCpuBase;
            observerCpuBase = beforeLaunchCpuMs;
            startedUtc = DateTime.UtcNow;
            clock.Start();
            var alignmentStart = Stopwatch.GetTimestamp();
            var elapsedTicks = clock.ElapsedTicks;
            var alignmentEnd = Stopwatch.GetTimestamp();
            clockOriginQpc = alignmentStart + (alignmentEnd - alignmentStart) / 2 - elapsedTicks;
            clockAlignmentUncertaintyMs = (alignmentEnd - alignmentStart) * 1000d / Stopwatch.Frequency;
            child = Process.Start(start) ?? throw new InvalidOperationException("Process.Start returned no owned process.");
            rootPid = child.Id;
            tree = new(child.Id, clock, observer, observerCpuBase);
            tree.Sample();
            using var capture = launch.ObserveFrames && launch.CaptureBackend == "gdi" ? new DesktopCapture() : null;
            using var timer = new PollTimer();
            nint window = 0;
            double nextTreeSample = clock.Elapsed.TotalMilliseconds + 100;
            double nextWindowLookup = 0;
            while (!child.HasExited)
            {
                var iterationStart = clock.Elapsed.TotalMilliseconds;
                if (cancelled) throw new OperationCanceledException("Observer cancelled.");
                if (iterationStart >= options.TimeoutSeconds * 1000d) throw new TimeoutException($"Owned process exceeded {options.TimeoutSeconds}s.");
                if (windowDestroyedAtMs is { } destroyedAt && iterationStart - destroyedAt >= 2000)
                    throw new InvalidOperationException("Owned HWND closed or became hidden but root did not exit within 2000ms.");
                if (windowDestroyedAtMs is null && markers?.FirstEndMs is null && iterationStart >= nextWindowLookup)
                {
                    if (window == 0) window = WindowLookup.Find(child.Id);
                    if (window != 0)
                    {
                        try
                        {
                            var info = WindowLookup.Validate(window, child.Id, iterationStart, windowInfo is null);
                            windowInfo ??= info;
                            geometry.Add(info);
                        }
                        catch (Exception) when (child.HasExited || !Native.IsWindow(window) ||
                            (windowInfo is not null && !Native.IsWindowVisible(window)))
                        {
                            windowDestroyedAtMs ??= clock.Elapsed.TotalMilliseconds;
                            windowCloseObservations.Add(new { atMs = windowDestroyedAtMs, operation = "windowValidation", rootExited = child.HasExited,
                                hwndExists = Native.IsWindow(window), windowVisible = Native.IsWindowVisible(window) });
                            window = 0;
                        }
                    }
                    nextWindowLookup = iterationStart + 100;
                }
                if (markers is not null && window != 0 && markers.FirstEndMs is null)
                {
                    var origin = new Native.Point();
                    if (!Native.ClientToScreen(window, ref origin))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (child.HasExited || !Native.IsWindow(window) ||
                            (windowInfo is not null && !Native.IsWindowVisible(window)))
                        {
                            windowDestroyedAtMs ??= clock.Elapsed.TotalMilliseconds;
                            windowCloseObservations.Add(new { atMs = windowDestroyedAtMs, operation = "ClientToScreen", win32Error = error, rootExited = child.HasExited });
                            window = 0;
                            continue;
                        }
                        throw new System.ComponentModel.Win32Exception(error, "ClientToScreen for live desktop ROI");
                    }
                    var captureStart = clock.Elapsed.TotalMilliseconds;
                    try
                    {
                        if (launch.CaptureBackend == "desktop-duplication")
                        {
                            duplication ??= new(origin.X, origin.Y, clockOriginQpc, clock);
                            var info = duplication.Info;
                            if (info.RefreshNumerator > 0 && info.RefreshDenominator > 0)
                                markers.DesktopFrameIntervalMs = 1000d * info.RefreshDenominator / info.RefreshNumerator;
                            var frame = duplication.Next(origin.X, origin.Y);
                            if (frame is { PointerOnly: false })
                            {
                                var atMs = duplication.PresentTimeMs(frame);
                                var status = MarkerCodec.Decode(duplication.Pixels, options.Tag, out var marker);
                                markers.Add(atMs, clock.Elapsed.TotalMilliseconds - captureStart, status, marker, frame);
                                roiDiagnostics.Record(duplication.Pixels, atMs, origin.X, origin.Y, status, frame);
                                if (desktopIdentityDiagnostic is null && status != "valid" &&
                                    roiDiagnostics.Snapshots.Any(s => s.Kind == "stable-invalid"))
                                {
                                    var diagnosticStartMs = clock.Elapsed.TotalMilliseconds;
                                    failures.Add("diagnosticGdiComparisonPerformed"); // Diagnostic run, never a formal result.
                                    try
                                    {
                                        var identity = DesktopIdentity.Read(window);
                                        using var comparison = new DesktopCapture();
                                        var gdiStartMs = clock.Elapsed.TotalMilliseconds;
                                        var gdiPixels = comparison.Capture(origin.X, origin.Y).ToArray();
                                        var gdiEndMs = clock.Elapsed.TotalMilliseconds;
                                        var gdiStatus = MarkerCodec.Decode(gdiPixels, options.Tag, out var gdiMarker);
                                        roiDiagnostics.Snapshots.Add(new("gdi-comparison", gdiEndMs, origin.X, origin.Y, gdiStatus, gdiPixels, null));
                                        desktopIdentityDiagnostic = new { identity, ddEventMs = atMs, ddReceivedMs = frame.ReceivedAtMs,
                                            diagnosticStartMs, gdiStartMs, gdiEndMs, gdiStatus, gdiMarker,
                                            sequentialComparison = true, diagnosticOnly = true,
                                            note = "One GDI desktop ROI after stable invalid DD content, same coordinates, includes intentional diagnostic overhead; no desktop or affinity changes." };
                                    }
                                    catch (Exception diagnosticError)
                                    { desktopIdentityDiagnostic = new { diagnosticStartMs, diagnosticOnly = true, error = diagnosticError.Message }; }
                                }
                            }
                        }
                        else
                        {
                            var pixels = capture!.Capture(origin.X, origin.Y);
                            var capturedAt = clock.Elapsed.TotalMilliseconds;
                            var status = MarkerCodec.Decode(pixels, options.Tag, out var marker);
                            markers.Add(capturedAt, capturedAt - captureStart, status, marker);
                            roiDiagnostics.Record(pixels, capturedAt, origin.X, origin.Y, status);
                        }
                    }
                    catch (Exception captureError) { markers.CaptureFailures.Add(captureError.Message); throw; }
                }
                if (clock.Elapsed.TotalMilliseconds >= nextTreeSample)
                {
                    tree.Sample();
                    nextTreeSample = clock.Elapsed.TotalMilliseconds + 100;
                }
                if (window == 0 || duplication is null || markers?.FirstEndMs is not null)
                    timer.Wait(Math.Max(0, iterationStart + 4 - clock.Elapsed.TotalMilliseconds));
            }
            child.WaitForExit();
            childExitCode = child.ExitCode;
            tree.Sample();
            if (childExitCode != 0) failures.Add($"childNonzeroExit:{childExitCode}");
            if (windowInfo is null) failures.Add("rootOwnedCanvasV11WindowNotFound");
            if (!tree.Identities.Any(p => p.Pid == rootPid)) failures.Add("rootProcessSamplingUnavailable");
            var rootExitedAtMs = clock.Elapsed.TotalMilliseconds;
            var initiallyLivePids = tree.Identities.Where(p => p.ExitObservedMs is null).Select(p => p.Pid).ToArray();
            while (tree.LiveCount > 0 && clock.Elapsed.TotalMilliseconds - rootExitedAtMs < 2000)
            {
                timer.Wait(Math.Min(50, Math.Max(0, 2000 - (clock.Elapsed.TotalMilliseconds - rootExitedAtMs))));
                tree.Sample();
            }
            var remainingPids = tree.Identities.Where(p => p.ExitObservedMs is null).Select(p => p.Pid).ToArray();
            exitGrace = new { allowedMs = 2000, rootExitedAtMs,
                elapsedMs = clock.Elapsed.TotalMilliseconds - rootExitedAtMs,
                initiallyLivePids, remainingPids, timedOut = remainingPids.Length > 0,
                boundary = "Post-root-exit helper cleanup; excluded from marker measurement duration." };
            if (tree.LiveCount > 0)
            {
                failures.Add("ownedDescendantsStillRunningAfterRootExitGrace");
                tree.TerminateOwnedDescendants();
                tree.Sample();
            }
        }
        catch (Exception error)
        {
            failures.Add($"{error.GetType().Name}:{error.Message}");
            if (child is not null)
            {
                try
                {
                    if (!child.HasExited) { tree?.Sample(); child.Kill(entireProcessTree: true); child.WaitForExit(5000); }
                    if (child.HasExited) childExitCode = child.ExitCode;
                }
                catch (Exception cleanup) { failures.Add($"ownedRootCleanup:{cleanup.GetType().Name}:{cleanup.Message}"); }
                tree?.TerminateOwnedDescendants();
                try { tree?.Sample(); } catch (Exception sampling) { failures.Add($"finalSampling:{sampling.Message}"); }
            }
        }
        finally { Console.CancelKeyPress -= onCancel; duplication?.Dispose(); }
        var elapsedMs = clock.Elapsed.TotalMilliseconds;
        if (markers is not null)
        {
            failures.AddRange(markers.Failures);
            if (markers.FirstMeasurementMs is null) failures.Add("noVisibleMeasurementMarker");
            if (markers.FirstEndMs is null) failures.Add("noVisibleEndMarker");
            if (markers.FirstEndMs <= markers.FirstMeasurementMs) failures.Add("nonpositiveMeasurementObservationWindow");
        }
        observer.Refresh();
        var observerCpuMs = observer.TotalProcessorTime.TotalMilliseconds - observerCpuBase;
        var result = new
        {
            protocolVersion = 11, options.Tag, status = ResultStatus(launch?.ObserveFrames != false, failures.Count != 0), failures,
            startedUtc, elapsedMs, rootPid, childExitCode, launch, traceSha256, expectedMeasuredFrames = measureFrames,
            window = windowInfo, windowGeometrySamples = geometry,
            marker = markers?.MarkerResult(),
            sampling = SamplingResult(launch, markers),
            capture = duplication?.Report(),
            captureDiagnostics = roiDiagnostics.Snapshots, desktopIdentityDiagnostic, windowCloseObservations,
            decodeFailureReasons = markers?.Samples.Where(s => s.DecodeStatus != "valid").GroupBy(s => s.DecodeStatus).ToDictionary(g => g.Key, g => g.Count()),
            clock = new { originQpc = clockOriginQpc, stopwatchFrequency = Stopwatch.Frequency, alignmentUncertaintyMs = clockAlignmentUncertaintyMs },
            exitGrace,
            processTree = tree?.Result(markers?.FirstMeasurementMs, markers?.FirstEndMs),
            observer = new { preparationCpuMs, cpuMs = observerCpuMs, percentOfOneLogicalCore = elapsedMs > 0 ? observerCpuMs / elapsedMs * 100 : (double?)null,
                measurement = tree?.ObserverMeasurement(markers?.FirstMeasurementMs, markers?.FirstEndMs),
                cpuBoundary = "CPU from immediately before Process.Start through observation and cleanup, excludes final JSON serialization; prelaunch CPU is reported separately." },
            metricBoundary = launch?.ObserveFrames == false
                ? "Diagnostic control: owned-process launch, window configuration, and process-tree sampling only; desktop capture and marker decoding disabled. No desktop frame metric. Not eligible for formal collection."
                : launch?.CaptureBackend == "desktop-duplication"
                    ? "Composited-desktop marker changes from DXGI Desktop Duplication events, only 96x8 ROI readback. LastPresentTime QPC timestamps are desktop updates, not physical scanout or GPU kernel time. Accumulated desktop updates and readback latency are reported; missing marker IDs remain unobserved or merged."
                    : "Composited-desktop changes from GDI desktop 96x8 ROI. Capture-completion timestamps bracket coarse phase boundaries. Not physical scanout or GPU kernel time. Missing IDs are unobserved or merged, not proven GPU drops."
        };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
            roiDiagnostics.Save(options.OutputPath);
            File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonOptions));
        }
        catch (Exception error) { Console.Error.WriteLine($"Observer output failed: {error.Message}"); return 2; }
        finally { tree?.Dispose(); child?.Dispose(); }
        return failures.Count == 0 ? 0 : 1;
    }
}

internal sealed class PollTimer : IDisposable
{
    private readonly nint _handle;
    internal PollTimer()
    {
        _handle = CreateWaitableTimerEx(0, null, 2, 0x00100002);
        if (_handle == 0) _handle = CreateWaitableTimerEx(0, null, 0, 0x00100002);
        Native.Require(_handle != 0, "CreateWaitableTimerEx");
    }
    internal void Wait(double milliseconds)
    {
        if (milliseconds <= 0) return;
        var due = -(long)Math.Max(1, milliseconds * 10000);
        Native.Require(SetWaitableTimer(_handle, ref due, 0, 0, 0, false), "SetWaitableTimer");
        var wait = WaitForSingleObject(_handle, 1000);
        if (wait != 0) throw new InvalidOperationException($"Poll timer wait failed: {wait}.");
    }
    public void Dispose() => Native.CloseHandle(_handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    private static extern nint CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(nint timer, ref long due, int period, nint callback, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
}
