using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Square.Backends;
using Square.Backends.Direct2D;
using Square.Backends.Vulkan;
using Square.Graphics;
using Square.Hosting;
using Square.Images;
using Square.Platform;
using Square.Platform.Win32;
using Square.Text.Fonts;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
var config = new Dictionary<string, string>();
for (var a = 0; a < args.Length; a += 2) config.Add(args[a], args[a + 1]);
string Option(string key, string fallback = "") => config.GetValueOrDefault("--" + key, fallback);
var tracePath = Path.GetFullPath(Option("trace"));
var output = Path.GetFullPath(Option("output"));
var backend = Option("backend", "Software");
var tag = int.Parse(Option("tag"), CultureInfo.InvariantCulture);
var verify = Option("verify", "0") == "1";
if (tag is < 1 or > 255 || backend is not ("Software" or "Direct2D" or "Vulkan")) throw new ArgumentException("Invalid backend/tag");
Environment.SetEnvironmentVariable("SQUARE_DIRECT2D_REQUIRE_HARDWARE", "1");
Environment.SetEnvironmentVariable("SQUARE_VULKAN_READBACK", verify ? "1" : "0");
Environment.SetEnvironmentVariable("SQUARE_VULKAN_VALIDATION", "0");
var startup = Stopwatch.StartNew();
var traceBytes = File.ReadAllBytes(tracePath);
using var document = JsonDocument.Parse(traceBytes);
var root = document.RootElement;
if (root.GetProperty("protocolVersion").GetInt32() != 11 || root.GetProperty("width").GetInt32() != 1280 || root.GetProperty("height").GetInt32() != 800)
    throw new ArgumentException("Unexpected protocol/viewport");
var directory = Path.GetDirectoryName(tracePath)!;
var fontBytes = File.ReadAllBytes(Path.Combine(directory, "font.ttf"));
// Public FontFace.LoadAsync registers these exact bytes for both native and fallback glyph paths.
// Reference: src/Square/Text/Fonts/FontFace.cs:93 and :143.
var face = new FontFace("CanvasBench", fontBytes);
face.LoadAsync().GetAwaiter().GetResult();
if (face.Status != FontFaceLoadStatus.Loaded) throw new InvalidOperationException(face.ErrorMessage);
using var imageDocument = ImageDecoder.Decode(Path.Combine(directory, "image.png"));
var logicalFrames = root.GetProperty("frames").EnumerateArray().Select(frame => frame.GetProperty("ops").EnumerateArray().Select(Command.Parse).ToArray()).ToArray();
var points = root.GetProperty("paths").EnumerateArray().Select(Command.Points).ToArray();
var textDefinitions = root.GetProperty("texts").EnumerateArray().Select(t => (Text: t.GetProperty("text").GetString()!, Size: t.GetProperty("size").GetSingle())).ToArray();
var warmupFrames = root.GetProperty("warmupFrames").GetInt32();
var measureFrames = root.GetProperty("measureFrames").GetInt32();
if (logicalFrames.Length != warmupFrames + measureFrames) throw new InvalidOperationException("Trace sequence length mismatch");
var assetLoadMs = startup.Elapsed.TotalMilliseconds;
var staticWatch = Stopwatch.StartNew();
var closedPaths = points.Select(p => MakePath(p, true)).ToArray();
var openPaths = new Dictionary<int, PathGeometry>();
foreach (var command in logicalFrames.SelectMany(f => f).Where(c => c.Code == "stroke"))
    if (!openPaths.ContainsKey(command.Asset)) openPaths.Add(command.Asset, MakePath(points[command.Asset], false));
var textAssets = textDefinitions.Select(t => new TextLayout(t.Text, new Font("CanvasBench", t.Size))).ToArray();
var staticPrepareMs = staticWatch.Elapsed.TotalMilliseconds;
RenderBackendRegistry.Register(new RenderBackendFactory());
RenderBackendRegistry.Register(new Direct2DBackendFactory());
RenderBackendRegistry.Register(new VulkanBackendFactory());
var title = $"Canvas v11 | Square {backend} | {root.GetProperty("case").GetString()} | {root.GetProperty("level").GetString()}";
using var host = new Win32PlatformFactory().CreateHost(new PlatformHostCreateInfo
{
    Title = title, Width = 1280, Height = 800, RenderBackend = backend,
    TitleStyle = TitleStyle.System, BorderStyle = BorderStyle.Fixed
});
host.Show();
var windowHandle = WindowFrame.SizeForClient(Environment.ProcessId, 1280, 800);
var context = host.CreateRenderContext();
if (host.ClientSize != new Size(1280, 800) || host.DpiScale != 1f) throw new InvalidOperationException("1280x800/DPR1 required");
var vk = VulkanMetadata.Read(context);
var fences = vk;
if (backend == "Vulkan" && (vk is null || vk.DeviceInfo.DeviceType is not ("DiscreteGpu" or "IntegratedGpu")))
    throw new InvalidOperationException("Actual Vulkan GPU required");
var background = Command.Color(root.GetProperty("background").GetString()!);
context.Clear(background);
context.Flush();
context.Present();
host.ShowAfterFirstFrame();
var actualCounts = new Dictionary<string, int>();
var cpuFrames = new List<object>();
var producerTimes = new List<double>();
var verificationImages = new List<string>();
var runClock = Stopwatch.StartNew();
var index = 0;
var endSent = false;
var endTimestamp = 0L;
var finished = false;
object? fenceBefore = null, fenceAfter = null;
var black = new SolidColorBrush(Square.Graphics.Color.Black);
var white = new SolidColorBrush(Square.Graphics.Color.White);
host.Tick += () =>
{
    if (index < logicalFrames.Length)
    {
        if (index == warmupFrames) fenceBefore = fences?.GetFrameStatistics();
        var measured = index >= warmupFrames;
        var seq = measured ? index - warmupFrames : index;
        var start = Stopwatch.GetTimestamp();
        var native = Prepare(logicalFrames[index]);
        var prepared = Stopwatch.GetTimestamp();
        Draw(native, measured ? 2 : 1, seq);
        var recorded = Stopwatch.GetTimestamp();
        context.Present();
        var returned = Stopwatch.GetTimestamp();
        if (measured)
        {
            foreach (var op in logicalFrames[index]) actualCounts[op.Code] = actualCounts.GetValueOrDefault(op.Code) + 1;
            cpuFrames.Add(new
            {
                sequence = seq, worldFrame = index,
                nativePrepareMs = Stopwatch.GetElapsedTime(start, prepared).TotalMilliseconds,
                recordSubmitMs = Stopwatch.GetElapsedTime(prepared, recorded).TotalMilliseconds,
                presentMs = Stopwatch.GetElapsedTime(recorded, returned).TotalMilliseconds,
                totalMs = Stopwatch.GetElapsedTime(start, returned).TotalMilliseconds
            });
            producerTimes.Add(runClock.Elapsed.TotalMilliseconds);
            if (verify && seq is 0 or 17 or 37) Capture(seq);
        }
        index++;
        return;
    }
    if (!endSent)
    {
        fenceAfter = fences?.GetFrameStatistics();
        var native = Prepare(logicalFrames[^1]);
        Draw(native, 3, measureFrames);
        context.Present();
        endSent = true;
        endTimestamp = Stopwatch.GetTimestamp();
        return;
    }
    if (Stopwatch.GetElapsedTime(endTimestamp).TotalMilliseconds >= 750)
    {
        finished = true;
        host.Close();
    }
};
host.PumpEvents();
if (!finished || cpuFrames.Count != measureFrames) throw new InvalidOperationException("Incomplete fixed trace");
var expected = root.GetProperty("expectedMeasuredOpcodeTotals").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
if (expected.Count != actualCounts.Count || expected.Any(p => actualCounts.GetValueOrDefault(p.Key) != p.Value)) throw new InvalidOperationException("Submitted opcode mismatch");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(new
{
    protocolVersion = 11, framework = "Square " + backend, backend, windowTitle = title,
    traceSha256 = Convert.ToHexStringLower(SHA256.HashData(traceBytes)), tag,
    clientWidth = host.ClientSize.Width, clientHeight = host.ClientSize.Height, devicePixelRatio = host.DpiScale,
    warmupFrames, measureFrames, assetLoadMs, staticPrepareMs, frameCpu = cpuFrames, producerFrameTimestampsMs = producerTimes,
    actualMeasuredOpcodeTotals = actualCounts, timingEligible = !verify, verificationOnly = verify, verificationImages,
    font = new { family = "CanvasBench", sourceFamily = "Segoe UI", sha256 = Convert.ToHexStringLower(SHA256.HashData(fontBytes)), loaded = face.Status.ToString(), source = "Public FontFace exact bytes" },
    samplingProfile = root.TryGetProperty("samplingProfile", out var sampling) ? sampling.GetString() : "native",
    actualImageSampling = backend == "Software" ? "nearest (public API fixed)" : "linear (public API fixed)",
    diagnostics = new { fenceBefore, fenceAfter, boundary = "Nonblocking rendering-fence counters at CPU measurement transitions; not uniform GPU time or scanout" },
    versions = new { square = typeof(IRenderContext).Assembly.GetName().Version?.ToString(), dotnet = Environment.Version.ToString(), context = context.GetType().FullName, vulkan = vk is null ? null : new { device = vk.DeviceInfo, colorSampleCount = vk.ColorSampleCount, readbackEnabled = vk.ReadbackEnabled } },
    hardwareRequired = backend != "Software",
    metric = "Fixed trace CPU prepare/record/Present returns. Control metric uses producer cadence plus external tree lifetime CPU/memory; not desktop/scanout."
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"v11 Square {backend}: {measureFrames} measured logical frames complete");

NativeCommand[] Prepare(Command[] ops)
{
    var prepared = new NativeCommand[ops.Length];
    for (var i = 0; i < ops.Length; i++)
    {
        var op = ops[i];
        var path = op.Code switch
        {
            "poly" or "clip" => closedPaths[op.Asset],
            "stroke" => openPaths[op.Asset],
            "poly-new" or "clip-new" => MakePath(op.Vertices!, true),
            "stroke-new" => MakePath(op.Vertices!, false),
            _ => null
        };
        var text = op.Code switch { "text" => textAssets[op.Asset], "text-new" => new TextLayout(op.Text!, new Font("CanvasBench", op.Values[0])), _ => null };
        Brush? brush = op.Code is "poly" or "poly-new" or "stroke" or "stroke-new" or "rect" or "text" or "text-new" ? new SolidColorBrush(op.Paint) : null;
        if (op.Code == "gradient") brush = new LinearGradientBrush(new Point(op.Values[4], op.Values[5]), new Point(op.Values[6], op.Values[7]), new GradientStop(0, op.Paint), new GradientStop(1, op.Paint2));
        prepared[i] = new NativeCommand(op, path, text, brush, op.Code is "stroke" or "stroke-new" ? new Pen(brush!, op.Values[0]) : null);
    }
    return prepared;
}
void Draw(NativeCommand[] ops, int phase, int seq)
{
    context.Clear(background);
    foreach (var n in ops)
    {
        var op = n.Logical; var v = op.Values;
        switch (op.Code)
        {
            case "poly": case "poly-new": context.FillPath(n.Path!, n.Brush!); break;
            case "stroke": case "stroke-new": context.DrawPath(n.Path!, n.Pen!); break;
            case "rect": case "gradient": context.FillRect(new Rect(v[0], v[1], v[2], v[3]), n.Brush!); break;
            case "text": context.DrawText(n.Text!, new Point(v[0], v[1]), n.Brush!); break;
            case "text-new": context.DrawText(n.Text!, new Point(v[1], v[2]), n.Brush!); break;
            case "image": context.DrawImage(imageDocument.PrimaryBitmap, new Rect(v[0], v[1], v[2], v[3])); break;
            case "transform": context.PushTransform(new Matrix3x2(v[0], v[1], v[2], v[3], v[4], v[5])); break;
            case "end-transform": context.PopTransform(); break;
            case "clip": case "clip-new": context.PushClip(n.Path!); break;
            case "end-clip": context.PopClip(); break;
            case "layer": context.PushLayer(new Rect(v[0], v[1], v[2], v[3]), v[4]); break;
            case "end-layer": context.PopLayer(); break;
            default: throw new InvalidOperationException(op.Code);
        }
    }
    DrawMarker(phase, seq);
    context.Flush();
}
void DrawMarker(int phase, int seq)
{
    uint payload = ((uint)tag << 18) | ((uint)phase << 16) | (uint)seq;
    byte crc = 0;
    for (var b = 0; b < 4; b++)
    {
        crc ^= (byte)(payload >> (8 * b));
        for (var bit = 0; bit < 8; bit++) crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 7 : crc << 1);
    }
    for (var bit = 0; bit < 48; bit++)
    {
        var on = bit < 8 ? ((0xA5 >> bit) & 1) : bit < 40 ? (int)((payload >> (bit - 8)) & 1) : ((crc >> (bit - 40)) & 1);
        context.FillRect(new Rect(bit * 2, 0, 2, 8), on != 0 ? white : black);
    }
}
void Capture(int seq)
{
    var path = output + $".frame{seq}.bmp";
    if (context is IRenderBitmapSource { IsCaptureAvailable: true } source)
    {
        using var bitmap = source.CaptureBitmap(); WindowFrame.SaveBmp(path, bitmap);
    }
    else
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            WindowFrame.DwmFlush();
            using var client = WindowFrame.CaptureDesktopClient(windowHandle);
            uint expected = ((uint)tag << 18) | (2u << 16) | (uint)seq;
            var matches = true;
            for (var bit = 0; bit < 40; bit++)
            {
                var pixel = client.GetPixel(bit * 2 + 1, 4);
                var on = pixel[0] >= 200 && pixel[1] >= 200 && pixel[2] >= 200;
                var want = bit < 8 ? ((0xA5 >> bit) & 1) != 0 : ((expected >> (bit - 8)) & 1) != 0;
                if (on != want) { matches = false; break; }
            }
            if (matches) { WindowFrame.SaveBmp(path, client); verificationImages.Add(path); return; }
            if (attempt == 24) WindowFrame.SaveBmp(output + ".rejected-capture.bmp", client);
            Thread.Sleep(10);
        }
        throw new InvalidOperationException("Desktop verification image never contained this run's tag/phase/sequence; capture is occluded or stale.");
    }
    verificationImages.Add(path);
}
static PathGeometry MakePath(Point[] points, bool closed)
{
    var path = new PathGeometry();
    if (points.Length == 0) return path;
    path.MoveTo(points[0]);
    for (var p = 1; p < points.Length; p++) path.LineTo(points[p]);
    if (closed) path.Close();
    return path;
}

internal sealed record NativeCommand(Command Logical, PathGeometry? Path, TextLayout? Text, Brush? Brush, Pen? Pen);
internal sealed record Command(string Code, float[] Values, int Asset = -1, Point[]? Vertices = null, string? Text = null, Color Paint = default, Color Paint2 = default)
{
    internal static Point[] Points(JsonElement p) => p.EnumerateArray().Select(x => new Point(x[0].GetSingle(), x[1].GetSingle())).ToArray();
    internal static Color Color(string value)
    {
        var hex = value.AsSpan(1);
        return new Color(byte.Parse(hex[..2], NumberStyles.HexNumber), byte.Parse(hex.Slice(2, 2), NumberStyles.HexNumber), byte.Parse(hex.Slice(4, 2), NumberStyles.HexNumber), byte.Parse(hex.Slice(6, 2), NumberStyles.HexNumber));
    }
    internal static Command Parse(JsonElement op)
    {
        var code = op[0].GetString()!;
        float F(int i) => op[i].GetSingle();
        Color C(int i) => Color(op[i].GetString()!);
        float[] Range(int from, int count) => Enumerable.Range(from, count).Select(F).ToArray();
        return code switch
        {
            "poly" => new(code, [], op[1].GetInt32(), Paint: C(2)),
            "poly-new" => new(code, [], Vertices: Points(op[1]), Paint: C(2)),
            "stroke" => new(code, [F(3)], op[1].GetInt32(), Paint: C(2)),
            "stroke-new" => new(code, [F(3)], Vertices: Points(op[1]), Paint: C(2)),
            "rect" => new(code, Range(1, 4), Paint: C(5)),
            "text" => new(code, [F(2), F(3)], op[1].GetInt32(), Paint: C(4)),
            "text-new" => new(code, [F(2), F(3), F(4)], Text: op[1].GetString(), Paint: C(5)),
            "image" => new(code, Range(1, 4)),
            "transform" => new(code, Range(1, 6)),
            "clip" => new(code, [], op[1].GetInt32()),
            "clip-new" => new(code, [], Vertices: Points(op[1])),
            "layer" => new(code, Range(1, 5)),
            "gradient" => new(code, Range(1, 8), Paint: C(9), Paint2: C(10)),
            "end-transform" or "end-clip" or "end-layer" => new(code, []),
            _ => throw new ArgumentException("Unsupported trace operation " + code)
        };
    }
}
