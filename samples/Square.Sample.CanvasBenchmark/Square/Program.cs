using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Square.Backends;
using Square.Backends.Direct2D;
using Square.Backends.Vulkan;
using Square.Graphics;
using Square.Hosting;
using Square.Images;
using Square.Platform;
using Square.Platform.Win32;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows benchmark");
var config = new Dictionary<string, string>();
for (var i = 0; i < args.Length; i += 2) config.Add(args[i], args[i + 1]);
string Get(string key, string fallback) => config.GetValueOrDefault("--" + key, fallback);
var scenePath = Path.GetFullPath(Get("scene", "../scene.json"));
var output = Path.GetFullPath(Get("output", "square.json"));
var backend = Get("backend", "Software");
var load = int.Parse(Get("load", "1"), CultureInfo.InvariantCulture);
var seconds = double.Parse(Get("seconds", "5"), CultureInfo.InvariantCulture);
var warmup = double.Parse(Get("warmup", "2"), CultureInfo.InvariantCulture);
if (load < 1 || seconds <= 0 || warmup < 0) throw new ArgumentException("Invalid measurement duration or load");
if (backend is not ("Software" or "Direct2D" or "Vulkan")) throw new ArgumentException("Unknown backend");
if (backend == "Vulkan")
{
    // Keep readback/validation out of performance measurements; opt in for preflight only.
    Environment.SetEnvironmentVariable("SQUARE_VULKAN_READBACK", Get("readback", "0"));
    Environment.SetEnvironmentVariable("SQUARE_VULKAN_VALIDATION", Get("validation", "0"));
}
using var scene = JsonDocument.Parse(File.ReadAllText(scenePath));
using var imageDocument = ImageDecoder.Decode(Path.Combine(Path.GetDirectoryName(scenePath)!, "icon.png"));
var icon = imageDocument.PrimaryBitmap;
var root = scene.RootElement;
var background = ParseColor(root.GetProperty("background").GetString()!);
var prepared = root.GetProperty("frames").EnumerateArray()
    .Select(f => f.GetProperty("ops").EnumerateArray().Select(Prepare).ToArray()).ToArray();
RenderBackendRegistry.Register(new RenderBackendFactory());
RenderBackendRegistry.Register(new Direct2DBackendFactory());
RenderBackendRegistry.Register(new VulkanBackendFactory());
Environment.SetEnvironmentVariable("SQUARE_DIRECT2D_REQUIRE_HARDWARE", "1");
var label = Get("label", "");
var windowTitle = $"Square {backend} | Canvas | {load}x" + (label.Length > 0 ? " | " + label : "");
using var host = new Win32PlatformFactory().CreateHost(new PlatformHostCreateInfo
{
    Title = windowTitle,
    Width = 800, Height = 600, RenderBackend = backend,
    TitleStyle = TitleStyle.System, BorderStyle = BorderStyle.Fixed
});
host.Show();
var windowHandle = BenchmarkWindowFrame.SizeForClient(Environment.ProcessId, 800, 600);
var ctx = host.CreateRenderContext(); // owned and disposed by host
var vulkan = ctx as IVulkanRenderContextInfo;
var frameDiagnostics = ctx as IVulkanFrameDiagnostics;
if (backend == "Vulkan" && frameDiagnostics is null)
    throw new InvalidOperationException("Vulkan v5 benchmark requires frame submission/fence diagnostics.");
if (backend == "Vulkan" && (vulkan is null || vulkan.DeviceInfo.DeviceType is not ("DiscreteGpu" or "IntegratedGpu")))
    throw new InvalidOperationException("Vulkan benchmark requires an actual discrete or integrated GPU.");
if (host.ClientSize.Width != 800 || host.ClientSize.Height != 600)
    throw new InvalidOperationException($"Unexpected client size: {host.ClientSize}");
var intervals = new List<double>();
var drawTimes = new List<double>();
var presents = new List<double>();
var clock = new Stopwatch();
var previous = double.NaN;
var finished = false;
var frames = 0;
var process = Process.GetCurrentProcess();
TimeSpan cpuStart = default;
var measurementStarted = false;
var actualSeconds = 0d;
var cpuSeconds = 0d;
var cpuObservationSeconds = 0d;
long measurementStart = 0, measurementDeadline = 0;
var observedApiReturns = 0;
VulkanFrameStatistics? gpuBefore = null, gpuAtDeadline = null, gpuAfterDrain = null;
var gpuObservationSeconds = 0d;
var warmupDrainMs = 0d;
var endDrainMs = 0d;
void Draw(int index)
{
    ctx.Clear(background);
    for (var n = 0; n < load; n++)
        foreach (var operation in prepared[index]) operation(ctx);
    ctx.Flush();
    ctx.Present();
}
Draw(0);
host.ShowAfterFirstFrame();
// Save evidence before measurement; encoding/capture does not enter timed interval.
if (ctx is IRenderBitmapSource capture && (capture.IsCaptureAvailable || vulkan?.ReadbackEnabled == true))
{
    using var bitmap = capture.CaptureBitmap();
    SaveBmp(Path.ChangeExtension(output, ".bmp"), bitmap);
    if (backend == "Vulkan") SaveBmp(Path.ChangeExtension(output, ".gpu.bmp"), bitmap);
}
clock.Start(); // Animation phase remains independent from the measurement/drain boundaries.
host.Tick += () =>
{
    if (!measurementStarted)
    {
        if (clock.Elapsed.TotalSeconds < warmup)
        {
            Draw((int)(clock.Elapsed.TotalSeconds * 60) % prepared.Length);
            return;
        }
        if (frameDiagnostics is not null)
        {
            var drainStart = Stopwatch.GetTimestamp();
            frameDiagnostics.WaitForSubmittedFrames();
            warmupDrainMs = Stopwatch.GetElapsedTime(drainStart).TotalMilliseconds;
            gpuBefore = frameDiagnostics.GetFrameStatistics();
            if (gpuBefore.PendingFrames != 0 || gpuBefore.CompletedFrames != gpuBefore.SubmittedFrames)
                throw new InvalidOperationException("Warmup GPU work was not drained before measurement.");
        }
        process.Refresh();
        cpuStart = process.TotalProcessorTime;
        measurementStart = Stopwatch.GetTimestamp();
        measurementDeadline = measurementStart + (long)(seconds * Stopwatch.Frequency);
        measurementStarted = true;
    }
    if (Stopwatch.GetTimestamp() >= measurementDeadline)
    {
        // This poll is nonblocking and precedes end drain. Long draws can make this
        // observation later than configured seconds; keep its real duration.
        gpuAtDeadline = frameDiagnostics?.GetFrameStatistics();
        var deadlinePollEnd = Stopwatch.GetTimestamp();
        gpuObservationSeconds = Stopwatch.GetElapsedTime(measurementStart, deadlinePollEnd).TotalSeconds;
        actualSeconds = seconds;
        process.Refresh();
        cpuSeconds = (process.TotalProcessorTime - cpuStart).TotalSeconds;
        cpuObservationSeconds = Stopwatch.GetElapsedTime(measurementStart).TotalSeconds;
        if (frameDiagnostics is not null)
        {
            var drainStart = Stopwatch.GetTimestamp();
            frameDiagnostics.WaitForSubmittedFrames();
            endDrainMs = Stopwatch.GetElapsedTime(drainStart).TotalMilliseconds;
            gpuAfterDrain = frameDiagnostics.GetFrameStatistics();
        }
        if (new Win32PlatformFactory().TryCaptureByProcessId(Environment.ProcessId, out var screen) && screen is not null)
        {
            using (screen)
            {
                SaveBmp(Path.ChangeExtension(output, ".window.bmp"), screen);
                if (backend is "Direct2D" or "Vulkan" && ctx is not IRenderBitmapSource { IsCaptureAvailable: true })
                {
                    using var client = BenchmarkWindowFrame.CropClient(windowHandle, screen);
                    SaveBmp(Path.ChangeExtension(output, ".bmp"), client);
                }
            }
        }
        finished = true;
        host.Close(); // Submitted GPU work is drained before destroying the window.
        return;
    }
    var start = Stopwatch.GetTimestamp();
    Draw((int)(clock.Elapsed.TotalSeconds * 60) % prepared.Length);
    var end = Stopwatch.GetTimestamp();
    observedApiReturns++;
    if (end < measurementDeadline)
    {
        var timestamp = Stopwatch.GetElapsedTime(measurementStart, end).TotalSeconds;
        frames++;
        if (!double.IsNaN(previous)) intervals.Add((timestamp - previous) * 1000);
        previous = timestamp;
        presents.Add(timestamp);
        drawTimes.Add(Stopwatch.GetElapsedTime(start, end).TotalMilliseconds);
    }
};
host.PumpEvents();
if (!finished || frames == 0) throw new InvalidOperationException("Benchmark closed before completion");
ulong gpuSubmitted = 0, gpuCompleted = 0;
if (frameDiagnostics is not null)
{
    if (gpuBefore is null || gpuAtDeadline is null || gpuAfterDrain is null || gpuObservationSeconds <= 0 ||
        gpuAtDeadline.SubmittedFrames < gpuBefore.SubmittedFrames || gpuAtDeadline.CompletedFrames < gpuBefore.CompletedFrames ||
        gpuAtDeadline.CompletedFrames > gpuAtDeadline.SubmittedFrames || gpuAtDeadline.PendingFrames < 0 ||
        gpuAtDeadline.PendingFrames > gpuAtDeadline.FramesInFlight ||
        (ulong)gpuAtDeadline.PendingFrames != gpuAtDeadline.SubmittedFrames - gpuAtDeadline.CompletedFrames ||
        gpuAfterDrain.SubmittedFrames != gpuAtDeadline.SubmittedFrames || gpuAfterDrain.PendingFrames != 0 ||
        gpuAfterDrain.CompletedFrames != gpuAfterDrain.SubmittedFrames)
        throw new InvalidOperationException("Inconsistent pre-drain/drained Vulkan frame statistics.");
    gpuSubmitted = gpuAtDeadline.SubmittedFrames - gpuBefore.SubmittedFrames;
    gpuCompleted = gpuAtDeadline.CompletedFrames - gpuBefore.CompletedFrames;
    if (gpuSubmitted == 0 || gpuSubmitted > (ulong)observedApiReturns || gpuCompleted > gpuSubmitted)
        throw new InvalidOperationException("Vulkan submitted/completed counts do not match observed API calls.");
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(new
{
    framework = "Square " + backend, windowTitle, load, frames, seconds = actualSeconds,
    fps = frames / actualSeconds, frameIntervalsMs = intervals, drawTimesMs = drawTimes,
    frameTimestampsSeconds = presents, cpuSeconds, cpuObservationSeconds, measurementVersion = 5,
    cpuMetric = "process CPU from post-warmup measurement start until deadline snapshot; excludes warmup and end drain",
    vulkanFrameDiagnostics = frameDiagnostics is null ? null : new
    {
        before = gpuBefore, atDeadline = gpuAtDeadline, afterDrain = gpuAfterDrain,
        configuredSeconds = seconds, observationSeconds = gpuObservationSeconds,
        apiReturnsObserved = observedApiReturns, submittedFrames = gpuSubmitted, completedFrames = gpuCompleted,
        completedFps = gpuCompleted / gpuObservationSeconds,
        pendingAtDeadline = gpuAtDeadline!.PendingFrames, framesInFlight = gpuAtDeadline!.FramesInFlight,
        warmupDrainMilliseconds = warmupDrainMs, endDrainMilliseconds = endDrainMs,
        metric = "Rendering-fence completions observed at the first nonblocking deadline poll; real observation duration, not physical scanout. End drain excluded. API returns in root frames/fps use the fixed configured window."
    },
    clientWidth = host.ClientSize.Width, clientHeight = host.ClientSize.Height,
    devicePixelRatio = host.DpiScale,
    versions = new
    {
        square = typeof(IRenderContext).Assembly.GetName().Version?.ToString(),
        dotnet = Environment.Version.ToString(), context = ctx.GetType().FullName,
        vulkan = vulkan is null ? null : new
        {
            device = vulkan.DeviceInfo, colorSampleCount = vulkan.ColorSampleCount,
            readbackEnabled = vulkan.ReadbackEnabled,
            validationEnabled = Environment.GetEnvironmentVariable("SQUARE_VULKAN_VALIDATION") is "1" or "true"
        }
    },
    metric = backend == "Vulkan"
        ? "CPU/API Present returns inside the fixed window; Vulkan queues rendering, not GPU completion. Separate fence-observed completion statistics provided. Native host paced at 60 Hz; not scanout."
        : "completed IRenderContext.Present API calls inside the fixed window; native host paced at 60 Hz; not physical scanout",
    drawMetric = backend == "Vulkan"
        ? "CPU wall time Clear + draw + Flush + Present, including any reused-slot fence/acquire wait; no regular present queue-idle wait, not GPU time"
        : "CPU wall time: Clear + all draw calls + Flush + Present; not a uniform GPU completion metric",
    hardwareRequired = backend != "Software",
    renderingEquivalent = true,
    renderingLimitations = Array.Empty<string>()
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{backend} load={load} frames={frames} fps={frames / actualSeconds:F2}");

Action<IRenderContext> Prepare(JsonElement op)
{
    float N(int index) => op[index].GetSingle();
    string S(int index) => op[index].GetString()!;
    SolidColorBrush B(int index) => new(ParseColor(S(index)));
    switch (S(0))
    {
        case "rect":
        {
            var rect = new Rect(N(1), N(2), N(3), N(4)); var brush = B(5);
            return c => c.FillRect(rect, brush);
        }
        case "line":
        {
            var path = new PathGeometry().MoveTo(new Point(N(1), N(2))).LineTo(new Point(N(3), N(4)));
            var pen = new Pen(B(5), N(6)); return c => c.DrawPath(path, pen);
        }
        case "circle":
        {
            var circle = new EllipseGeometry(new Point(N(1), N(2)), N(3), N(3));
            var pen = new Pen(B(4), N(5)); return c => c.DrawGeometry(circle, pen);
        }
        case "arc":
        {
            var cx = N(1); var cy = N(2); var r = N(3); var start = N(4); var sweep = N(5);
            var path = new PathGeometry().MoveTo(new Point(cx + r * MathF.Cos(start), cy + r * MathF.Sin(start)))
                .ArcTo(new Rect(cx - r, cy - r, 2*r, 2*r), start * 180 / MathF.PI, sweep * 180 / MathF.PI);
            var pen = new Pen(B(6), N(7));
            var capBrush = B(6); var capRadius = N(7) / 2;
            var cap1 = new EllipseGeometry(new Point(cx + r * MathF.Cos(start), cy + r * MathF.Sin(start)), capRadius, capRadius);
            var cap2 = new EllipseGeometry(new Point(cx + r * MathF.Cos(start + sweep), cy + r * MathF.Sin(start + sweep)), capRadius, capRadius);
            return c => { c.DrawPath(path, pen); c.FillGeometry(cap1, capBrush); c.FillGeometry(cap2, capBrush); };
        }
        case "curve":
        {
            var line = new PathGeometry().MoveTo(new Point(N(1), N(2)));
            var area = new PathGeometry().MoveTo(new Point(N(1), N(2)));
            for (var i = 1; i <= 32; i++)
            {
                var t = i / 32f; var u = 1 - t;
                var p = new Point(u*u*u*N(1) + 3*u*u*t*N(3) + 3*u*t*t*N(5) + t*t*t*N(7),
                    u*u*u*N(2) + 3*u*u*t*N(4) + 3*u*t*t*N(6) + t*t*t*N(8));
                line.LineTo(p); area.LineTo(p);
            }
            area.LineTo(new Point(N(7), N(9))).LineTo(new Point(N(1), N(9))).Close();
            var brush = new LinearGradientBrush(new Point(0, 210), new Point(0, 390),
                new GradientStop(0, ParseColor(S(10))), new GradientStop(1, ParseColor(S(11))));
            var pen = new Pen(B(10), N(12));
            var bounds = new Rect(N(1), 200, N(7) - N(1), 190);
            return c => { c.PushClip(area); c.FillRect(bounds, brush); c.PopClip(); c.DrawPath(line, pen); };
        }
        case "text":
        {
            var text = new TextLayout(S(1), new Font("Segoe UI", N(4)));
            text.Measure();
            var origin = new Point(N(2), N(3)); var brush = B(5);
            return c => c.DrawText(text, origin, brush);
        }
        case "image":
        {
            var rect = new Rect(N(1), N(2), N(3), N(4)); return c => c.DrawImage(icon, rect);
        }
        default: throw new ArgumentException("Unknown operation " + S(0));
    }
}

static Color ParseColor(string value)
{
    var hex = value.AsSpan(1);
    return new Color(byte.Parse(hex[..2], NumberStyles.HexNumber), byte.Parse(hex.Slice(2, 2), NumberStyles.HexNumber),
        byte.Parse(hex.Slice(4, 2), NumberStyles.HexNumber), hex.Length == 8 ? byte.Parse(hex.Slice(6, 2), NumberStyles.HexNumber) : (byte)255);
}

static void SaveBmp(string path, Bitmap bitmap)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var writer = new BinaryWriter(File.Create(path));
    writer.Write((ushort)0x4D42); writer.Write(54 + bitmap.Pixels.Length); writer.Write(0); writer.Write(54);
    writer.Write(40); writer.Write(bitmap.Width); writer.Write(-bitmap.Height);
    writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(bitmap.Pixels.Length);
    writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0); writer.Write(bitmap.Pixels);
}

internal static class BenchmarkWindowFrame
{
    internal static IntPtr SizeForClient(int processId, int width, int height)
    {
        var handle = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)processId) return true;
            handle = window;
            return false;
        }, IntPtr.Zero);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Benchmark window not found.");
        var dpi = GetDpiForWindow(handle);
        var rect = new NativeRect
        {
            Right = (int)Math.Round(width * dpi / 96d),
            Bottom = (int)Math.Round(height * dpi / 96d)
        };
        var style = unchecked((uint)GetWindowLong(handle, -16));
        if (!AdjustWindowRectExForDpi(ref rect, style, false, 0, dpi) ||
            !SetWindowPos(handle, IntPtr.Zero, 0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top,
                0x0002 | 0x0004 | 0x0010 | 0x0020))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        return handle;
    }

    internal static Bitmap CropClient(IntPtr handle, Bitmap window)
    {
        var origin = new NativePoint();
        if (!GetClientRect(handle, out var client) || !GetWindowRect(handle, out var outer) ||
            !ClientToScreen(handle, ref origin))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        var x = origin.X - outer.Left;
        var y = origin.Y - outer.Top;
        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > window.Width || y + height > window.Height)
            throw new InvalidOperationException("Captured window does not contain its client area.");
        var result = new Bitmap(width, height);
        for (var row = 0; row < height; row++)
            window.Pixels.AsSpan((y + row) * window.Stride + x * 4, width * 4).CopyTo(result.GetRow(row));
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    private delegate bool EnumCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AdjustWindowRectExForDpi(ref NativeRect rect, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
}

