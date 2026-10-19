using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
#if PLATFORM_WIN32
using System.Runtime.InteropServices;
#endif
#if PLATFORM_WIN32
using Square.Backends.Direct2D;
#endif
using Square.Hosting;
using Square.Sample.PerfComparison;

var seconds = 30;
var warmupSeconds = 2d;
var backend = "Software";
string? output = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--seconds" && i + 1 < args.Length &&
        int.TryParse(args[++i], out var requested) && requested > 0)
        seconds = requested;
    else if (args[i] == "--warmup" && i + 1 < args.Length &&
        double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var warmup) &&
        double.IsFinite(warmup) && warmup >= 0)
        warmupSeconds = warmup;
    else if (args[i] == "--backend" && i + 1 < args.Length)
        backend = args[++i];
    else if (args[i] == "--output" && i + 1 < args.Length)
        output = args[++i];
    else
        throw new ArgumentException("Usage: Square.PerfComparison [--backend Software|Direct2D] [--seconds positive-integer] [--warmup non-negative-seconds] [--output result.json]");
}

#if PLATFORM_WIN32
var (outerWidth, outerHeight) = Win32WindowMetrics.ForClient(900, 1240);
#else
var (outerWidth, outerHeight) = (900, 1240);
#endif
var window = new AppWindow("Square CSS animation benchmark", outerWidth, outerHeight);
window.RenderingMode = RenderMode.Auto;
if (backend.Equals("Direct2D", StringComparison.OrdinalIgnoreCase))
{
#if PLATFORM_WIN32
    Environment.SetEnvironmentVariable("SQUARE_DIRECT2D_REQUIRE_HARDWARE", "1");
    window.UseDirect2DBackend();
#else
    throw new PlatformNotSupportedException("The Direct2D window backend requires Windows.");
#endif
}
else if (!backend.Equals("Software", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException($"Unsupported Square backend: {backend}");
window.Load(new CssAnimationPerfPage());
var app = new DesktopApplication(window);
var process = Process.GetCurrentProcess();
var frames = 0;
var scheduled = false;
var timing = false;
var elapsed = 0d;
var cpuSeconds = 0d;
var startedUtc = DateTimeOffset.MinValue;
float clientWidth = 0, clientHeight = 0;
long privateSum = 0, privatePeak = 0, workingSum = 0, workingPeak = 0;
int memorySamples = 0;

void SampleMemory()
{
    process.Refresh();
    var privateBytes = process.PrivateMemorySize64;
    var workingBytes = process.WorkingSet64;
    privateSum += privateBytes;
    workingSum += workingBytes;
    privatePeak = Math.Max(privatePeak, privateBytes);
    workingPeak = Math.Max(workingPeak, workingBytes);
    memorySamples++;
}

void OnFramePresented()
{
    if (timing) frames++;
    if (scheduled) return;
    scheduled = true;
    _ = Task.Run(async () =>
    {
        // Let the first-frame tree/layout and JIT work settle before the measured interval.
        await Task.Delay(TimeSpan.FromSeconds(warmupSeconds));
        long startTicks = 0;
        TimeSpan startCpu = default;
        await window.Dispatcher.InvokeAsync(() =>
        {
            process.Refresh();
            var viewport = ((Square.UI.UIDocument)window.Document).DocumentElement.Geometry.Size;
            clientWidth = viewport.Width;
            clientHeight = viewport.Height;
            startCpu = process.TotalProcessorTime;
            SampleMemory();
            startTicks = Stopwatch.GetTimestamp();
            startedUtc = DateTimeOffset.UtcNow;
            timing = true;
        });
        for (var second = 0; second < seconds; second++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            SampleMemory();
        }
        await window.Dispatcher.InvokeAsync(() =>
        {
            timing = false;
            elapsed = Stopwatch.GetElapsedTime(startTicks).TotalSeconds;
            process.Refresh();
            cpuSeconds = (process.TotalProcessorTime - startCpu).TotalSeconds;
            window.Close();
        });
    });
}

app.FramePresented += OnFramePresented;
app.Run();
if (elapsed == 0) throw new InvalidOperationException("The window closed before measurement completed.");
using var buffer = new MemoryStream();
using (var writer = new Utf8JsonWriter(buffer))
{
    writer.WriteStartObject();
    writer.WriteString("target", $"Square {window.RenderBackend} {(RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT")}");
    writer.WriteNumber("seconds", elapsed);
    writer.WriteNumber("frames", frames);
    writer.WriteNumber("fps", frames / elapsed);
    writer.WriteString("fpsDefinition", "FramePresented render submissions per second; not physical scanout");
    writer.WriteNumber("cpuSeconds", cpuSeconds);
    writer.WriteNumber("cpuOneCorePercent", cpuSeconds / elapsed * 100);
    writer.WriteNumber("privateAvgMiB", privateSum / (double)memorySamples / 1048576);
    writer.WriteNumber("privatePeakMiB", privatePeak / 1048576d);
    writer.WriteNumber("workingAvgMiB", workingSum / (double)memorySamples / 1048576);
    writer.WriteNumber("workingPeakMiB", workingPeak / 1048576d);
    writer.WriteNumber("samples", memorySamples);
    writer.WriteNumber("clientWidth", clientWidth);
    writer.WriteNumber("clientHeight", clientHeight);
    writer.WriteNumber("warmupSeconds", warmupSeconds);
    writer.WriteNumber("targetFps", 60);
    writer.WriteString("startedUtc", startedUtc);
    writer.WriteString("renderMode", window.RenderingMode.ToString());
    writer.WriteEndObject();
}
var json = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
if (output != null)
{
    var path = Path.GetFullPath(output);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, json + Environment.NewLine);
}
Console.WriteLine(json);

#if PLATFORM_WIN32
internal static class Win32WindowMetrics
{
    // Win32Host uses a normal resizable system window and accepts outer dimensions.
    private const int ResizableWindowStyle = 0x00CF0000;

    internal static (int Width, int Height) ForClient(int width, int height)
    {
        var dpi = GetDpiForSystem();
        var scale = dpi / 96d;
        var rect = new WindowRect
        {
            Right = (int)Math.Round(width * scale),
            Bottom = (int)Math.Round(height * scale)
        };
        if (!AdjustWindowRectExForDpi(ref rect, ResizableWindowStyle, false, 0, dpi))
            throw new InvalidOperationException("Unable to size the benchmark's Win32 client area.");
        return ((int)Math.Round((rect.Right - rect.Left) / scale),
            (int)Math.Round((rect.Bottom - rect.Top) / scale));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectExForDpi(
        ref WindowRect rect, int style, [MarshalAs(UnmanagedType.Bool)] bool menu, int exStyle, uint dpi);
}
#endif
