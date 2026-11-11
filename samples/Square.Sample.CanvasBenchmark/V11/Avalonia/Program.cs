using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Threading;

namespace CanvasBenchmark.V11;

internal static class Program
{
    internal static Options Options { get; private set; } = null!;
    internal static readonly Stopwatch Clock = Stopwatch.StartNew();
    internal static readonly PlatformLog PlatformLog = new();
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Options = Options.Parse(args);
            Logger.Sink = PlatformLog;
            return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            if (Options is not null) WriteFailure(ex);
            return 1;
        }
    }

    internal static void WriteOutput(object result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Options.Output)!);
        File.WriteAllText(Options.Output, JsonSerializer.Serialize(result, JsonOptions));
    }

    internal static void WriteFailure(Exception error) => WriteOutput(new
    {
        protocolVersion = 11, framework = "Avalonia", backend = "Skia/platform-default",
        status = error is NotSupportedException ? "unsupported" : "failed", timingEligible = false,
        traceSha256 = File.Exists(Options.Trace) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Options.Trace))) : null,
        tag = Options.Tag, verify = Options.Verify, failureReason = error.ToString(),
        platformLog = PlatformLog.Messages.ToArray()
    });
}

internal sealed record Options(string Trace, string Output, int Tag, bool Verify)
{
    internal static Options Parse(string[] args)
    {
        const string usage = "Expected --trace <absolute json> --output <absolute json> --tag <1..255> [--verify 0|1]. Avalonia uses its default platform-selected Skia backend.";
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--trace" or "--output" or "--tag" or "--verify") || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException(usage);
        }
        if (!values.TryGetValue("--trace", out string? trace) || !Path.IsPathFullyQualified(trace)
            || !values.TryGetValue("--output", out string? output) || !Path.IsPathFullyQualified(output)
            || !values.TryGetValue("--tag", out string? tagText) || !int.TryParse(tagText, out int tag) || tag is < 1 or > 255)
            throw new ArgumentException(usage);
        string verify = values.GetValueOrDefault("--verify", "0");
        if (verify is not ("0" or "1")) throw new ArgumentException(usage);
        return new Options(Path.GetFullPath(trace), Path.GetFullPath(output), tag, verify == "1");
    }
}

internal sealed class PlatformLog : ILogSink
{
    internal ConcurrentQueue<string> Messages { get; } = new();
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Debug
        && area is "Platform" or "OpenGL" or "Vulkan" or "Skia" or "FontManager";
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        => Log(level, area, source, messageTemplate, []);
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (Messages.Count < 256)
            Messages.Enqueue($"{level}/{area}: {messageTemplate} | {string.Join(", ", propertyValues.Select(value => value?.ToString()))}");
    }
}

internal sealed class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new BenchmarkWindow(Scene.Load(Program.Options.Trace));
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class BenchmarkWindow : Window
{
    private readonly Scene _scene;
    private readonly BenchmarkCanvas _canvas;
    private bool _finished;

    internal BenchmarkWindow(Scene scene)
    {
        _scene = scene;
        _canvas = new BenchmarkCanvas(scene, EndRendered, Fail);
        Title = $"Canvas v11 | Avalonia | tag {Program.Options.Tag}";
        ClientSize = new Size(1280, 800);
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(100, 100);
        Background = Scene.Background;
        Content = _canvas;
        Opened += (_, _) =>
        {
            if (RenderScaling != 1 || ClientSize != new Size(1280, 800))
                Fail(new NotSupportedException($"Requires 1280x800 client pixels at DPR=1; observed {ClientSize}, DPR={RenderScaling}."));
            else RequestAnimationFrame(Animate);
        };
        Closed += (_, _) => scene.Dispose();
    }

    private void Animate(TimeSpan _)
    {
        if (_finished || _canvas.Ended) return;
        _canvas.InvalidateVisual();
        RequestAnimationFrame(Animate);
    }

    private void EndRendered()
    {
        if (_finished) return;
        // This timer starts only after an actual Render recorded the END marker.
        DispatcherTimer.RunOnce(Finish, TimeSpan.FromMilliseconds(750));
    }

    private void Fail(Exception error)
    {
        if (_finished) return;
        _finished = true;
        Console.Error.WriteLine(error);
        Program.WriteFailure(error);
        Dispatcher.UIThread.Post(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(1));
    }

    private void Finish()
    {
        if (_finished) return;
        try
        {
            _scene.CheckMeasuredTotals(_canvas.ActualMeasuredOpcodeTotals);
            var screenshots = new List<object>();
            if (Program.Options.Verify)
            {
                // An independent control avoids replaying BenchmarkCanvas.Render and advancing production.
                foreach (int seq in new[] { 0, 17, 37 })
                {
                    if (seq >= _scene.MeasureFrames) continue;
                    var snapshot = new SnapshotCanvas(_scene, _scene.WarmupFrames + seq, seq);
                    snapshot.Measure(new Size(1280, 800));
                    snapshot.Arrange(new Rect(0, 0, 1280, 800));
                    using var bitmap = new RenderTargetBitmap(new PixelSize(1280, 800), new Vector(96, 96));
                    bitmap.Render(snapshot);
                    string path = Path.ChangeExtension(Program.Options.Output, $".frame{seq}.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    bitmap.Save(path);
                    screenshots.Add(new { sequence = seq, logicalFrame = _scene.WarmupFrames + seq, path });
                }
            }
            Program.WriteOutput(new
            {
                protocolVersion = 11, framework = "Avalonia", backend = "Skia/platform-default", status = "ok",
                timingEligible = !Program.Options.Verify, verify = Program.Options.Verify,
                traceSha256 = _scene.TraceSha256, @case = _scene.Case, level = _scene.Level,
                samplingProfile = _scene.SamplingProfile, nativeScaledProfile = _scene.SamplingProfile == "native-scaled",
                clientWidth = ClientSize.Width, clientHeight = ClientSize.Height, devicePixelRatio = RenderScaling,
                tag = Program.Options.Tag, warmupFrames = _scene.WarmupFrames, measureFrames = _scene.MeasureFrames,
                assetLoadMs = _scene.AssetLoadMs, staticPrepareMs = _scene.StaticPrepareMs,
                assetsReadyMs = _scene.AssetsReadyMs, firstDrawMs = _canvas.FirstDrawMs,
                memory = new { privateBytesBeforeStaticPrepare = _scene.PrivateBytesBeforeStaticPrepare, privateBytesAtAssetsReady = _scene.PrivateBytesAtAssetsReady,
                    privateBytesAtFinish = Process.GetCurrentProcess().PrivateMemorySize64, boundary = "Current process snapshots outside frame timing; external observer supplies uniform process-tree samples" },
                frameCpu = _canvas.FrameCpu, producerFrameTimes = _canvas.ProducerFrameTimes,
                producerFrameIntervalsMs = _canvas.ProducerFrameIntervals,
                actualMeasuredOpcodeTotals = _canvas.ActualMeasuredOpcodeTotals,
                expectedMeasuredOpcodeTotals = _scene.ExpectedMeasuredOpcodeTotals,
                assets = new { fontSha256 = _scene.FontSha256, imageSha256 = _scene.ImageSha256, fontFamily = _scene.Font.GlyphTypeface.FamilyName,
                    fontCollectionUri = _scene.Font.Typeface.FontFamily.Key!.Source.AbsoluteUri,
                    fontRegistration = "IFontManagerImpl.TryCreateGlyphTypeface over trace font.ttf bytes; fonts:CanvasV11 collection; same glyph-typeface identity verified; all trace codepoints checked before timing",
                    staticPathVariants = _scene.StaticPathVariants, staticTextLayouts = _scene.StaticTextLayouts },
                versions = new { avalonia = typeof(Application).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    dotnet = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
                    rendererAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Avalonia.Skia")?.FullName },
                hardwareStatus = new { processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    nativeWindow = TryGetPlatformHandle()?.HandleDescriptor,
                    renderingMode = "UsePlatformDetect defaults; hardware acceleration not asserted; see platformLog", platformLog = Program.PlatformLog.Messages.ToArray() },
                diagnostics = new { screenshots, isolatedLayer = "DrawingContext.PushClip(bounds) + PushOpacityMask(constant-alpha white brush, bounds); Avalonia.Skia 11.3.12 uses SaveLayer then DstIn mask on pop",
                    bitmapInterpolation = _scene.BitmapInterpolation.ToString(),
                    samplingLimitation = _scene.SamplingProfile == "native-scaled" ? "2x LowQuality/linear diagnostic profile; excluded from six-framework fixed-quality image conclusions" : "1:1 image profile; interpolation disabled",
                    strokeProfile = "native default Pen line cap/join/miter; separate native-stroke quality profile",
                    gradientProfile = "native pad interpolation; subject to image qualification",
                    cpuBoundary = "Control.Render entry through native preparation, all DrawingContext operations and final marker; no compositor raster/fence/present completion is observable; presentMs=null",
                    producerBoundary = "Successive actual Control.Render entries, one fixed logical frame per entry; excludes independent verification screenshots",
                    screenshotBoundary = "verify only, independent redraw after END hold; excluded from frameCpu and opcode totals" }
            });
            _finished = true;
            Close();
        }
        catch (Exception ex) { Fail(ex); }
    }
}

internal readonly record struct CpuFrame(int LogicalFrame, int Sequence, double NativePrepareMs, double RecordSubmitMs, double? PresentMs, double TotalMs);
internal readonly record struct ProducerFrame(int LogicalFrame, int Phase, int Sequence, double RenderStartMs);

internal sealed class BenchmarkCanvas : Control
{
    private readonly Scene _scene;
    private readonly Action _endRendered;
    private readonly Action<Exception> _fail;
    private int _nextFrame;
    private double? _previousMeasuredEntry;
    internal bool Ended { get; private set; }
    internal double? FirstDrawMs { get; private set; }
    internal CpuFrame[] FrameCpu { get; }
    internal List<ProducerFrame> ProducerFrameTimes { get; } = new();
    internal List<double> ProducerFrameIntervals { get; } = new();
    internal Dictionary<string, long> ActualMeasuredOpcodeTotals { get; } = new(StringComparer.Ordinal);

    internal BenchmarkCanvas(Scene scene, Action endRendered, Action<Exception> fail)
    {
        _scene = scene;
        _endRendered = endRendered;
        _fail = fail;
        FrameCpu = new CpuFrame[scene.MeasureFrames];
        ProducerFrameTimes.Capacity = scene.Frames.Length + 1;
        ProducerFrameIntervals.Capacity = Math.Max(0, scene.MeasureFrames - 1);
        Configure(this, scene.BitmapInterpolation);
    }

    internal static void Configure(Control control, BitmapInterpolationMode interpolation)
    {
        control.ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(control, interpolation);
        RenderOptions.SetEdgeMode(control, EdgeMode.Unspecified);
    }

    public override void Render(DrawingContext context)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            int index = Math.Min(_nextFrame, _scene.Frames.Length - 1);
            bool end = _nextFrame == _scene.Frames.Length;
            bool measured = !Ended && !end && index >= _scene.WarmupFrames;
            int phase = end ? 3 : measured ? 2 : 1;
            int seq = end ? _scene.MeasureFrames : measured ? index - _scene.WarmupFrames : index;
            // Even an exposure Render after END redraws the last picture without advancing production.
            if (Ended) { phase = 3; seq = _scene.MeasureFrames; }
            double entryMs = Program.Clock.Elapsed.TotalMilliseconds;
            PreparedOp[] prepared = _scene.PrepareFrame(index);
            double nativePrepareMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long recordStart = Stopwatch.GetTimestamp();
            _scene.Draw(context, prepared, measured ? ActualMeasuredOpcodeTotals : null);
            Marker.Draw(context, Program.Options.Tag, phase, seq);
            Scene.DisposeFrame(prepared);
            double recordMs = Stopwatch.GetElapsedTime(recordStart).TotalMilliseconds;
            double totalMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            FirstDrawMs ??= totalMs;
            if (!Ended)
            {
                ProducerFrameTimes.Add(new ProducerFrame(index, phase, seq, entryMs));
                if (measured)
                {
                    FrameCpu[seq] = new CpuFrame(index, seq, nativePrepareMs, recordMs, null, totalMs);
                    if (_previousMeasuredEntry is double previous) ProducerFrameIntervals.Add(entryMs - previous);
                    _previousMeasuredEntry = entryMs;
                }
                if (end)
                {
                    Ended = true;
                    // Schedule only after this Render has returned to the UI dispatcher.
                    Dispatcher.UIThread.Post(_endRendered);
                }
                else _nextFrame++;
            }
        }
        catch (Exception ex) { Ended = true; _fail(ex); }
    }
}

internal sealed class SnapshotCanvas : Control
{
    private readonly Scene _scene;
    private readonly int _index;
    private readonly int _seq;
    internal SnapshotCanvas(Scene scene, int index, int seq)
    {
        _scene = scene; _index = index; _seq = seq;
        BenchmarkCanvas.Configure(this, scene.BitmapInterpolation);
    }
    public override void Render(DrawingContext context)
    {
        PreparedOp[] prepared = _scene.PrepareFrame(_index);
        _scene.Draw(context, prepared, null);
        Marker.Draw(context, Program.Options.Tag, 2, _seq);
        Scene.DisposeFrame(prepared);
    }
}

internal static class Marker
{
    internal static void Draw(DrawingContext context, int tag, int phase, int sequence)
    {
        uint payload = (uint)(((tag & 255) << 18) | ((phase & 3) << 16) | (sequence & 65535));
        byte crc = 0;
        for (int b = 0; b < 4; b++)
        {
            crc ^= (byte)(payload >> (b * 8));
            for (int bit = 0; bit < 8; bit++) crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
        }
        ulong bits = 0xA5UL | ((ulong)payload << 8) | ((ulong)crc << 40);
        for (int bit = 0; bit < 48; bit++)
            context.DrawRectangle(((bits >> bit) & 1) != 0 ? Brushes.White : Brushes.Black, null, new Rect(bit * 2, 0, 2, 8));
    }
}

// A dedicated one-face collection prevents substituting a system font with the same family name.
// API contract reference: Avalonia 11.3.12 FontManager.AddFontCollection / IFontCollection.Initialize.
internal sealed class TraceFontCollection(byte[] bytes) : IFontCollection
{
    public Uri Key { get; } = new("fonts:CanvasV11");
    internal IGlyphTypeface GlyphTypeface { get; private set; } = null!;
    internal Typeface Typeface { get; private set; }
    public int Count => 1;
    public FontFamily this[int index] => index == 0 ? Typeface.FontFamily : throw new ArgumentOutOfRangeException(nameof(index));
    public void Initialize(IFontManagerImpl manager)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        if (!manager.TryCreateGlyphTypeface(stream, FontSimulations.None, out var glyph))
            throw new NotSupportedException("Avalonia could not load trace font.ttf; font fallback is forbidden.");
        GlyphTypeface = glyph;
        // FontFamily's baseUri overload does not select a collection for a bare family name.
        // 11.3.12 FontFamily.GetFontSourceIdentifier requires the explicit source#family syntax.
        Typeface = new Typeface(new FontFamily($"{Key.AbsoluteUri}#{glyph.FamilyName}"), glyph.Style, glyph.Weight, glyph.Stretch);
        if (Typeface.FontFamily.Key?.Source != Key)
            throw new NotSupportedException("Trace font family did not retain its registered collection URI; system font lookup is forbidden.");
    }
    public bool TryGetGlyphTypeface(string familyName, FontStyle style, FontWeight weight, FontStretch stretch, [NotNullWhen(true)] out IGlyphTypeface? glyphTypeface)
    {
        glyphTypeface = familyName == GlyphTypeface.FamilyName && style == GlyphTypeface.Style && weight == GlyphTypeface.Weight && stretch == GlyphTypeface.Stretch ? GlyphTypeface : null;
        return glyphTypeface is not null;
    }
    public bool TryMatchCharacter(int codepoint, FontStyle style, FontWeight weight, FontStretch stretch, string? familyName, CultureInfo? culture, out Typeface typeface)
    {
        typeface = Typeface;
        return style == GlyphTypeface.Style && weight == GlyphTypeface.Weight && stretch == GlyphTypeface.Stretch && GlyphTypeface.TryGetGlyph((uint)codepoint, out _);
    }
    public IEnumerator<FontFamily> GetEnumerator() { yield return Typeface.FontFamily; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Dispose() => GlyphTypeface?.Dispose();
}

internal sealed class Op
{
    internal required string Kind { get; init; }
    internal int Id { get; init; }
    internal Point[] Points { get; init; } = [];
    internal Rect Rect { get; init; }
    internal Point Origin { get; init; }
    internal Point End { get; init; }
    internal Matrix Matrix { get; init; }
    internal Color Color { get; init; }
    internal Color Color2 { get; init; }
    internal double Value { get; init; }
    internal string Text { get; init; } = "";
}
internal sealed record PreparedOp(Op Input, StreamGeometry? Geometry = null, TextLayout? Text = null, IBrush? Brush = null, IPen? Pen = null);
internal sealed record TextAsset(string Text, double Size);

internal sealed class Scene : IDisposable
{
    internal static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x10, 0x18, 0x27)).ToImmutable();
    internal required string Case { get; init; }
    internal required string Level { get; init; }
    internal required string SamplingProfile { get; init; }
    internal BitmapInterpolationMode BitmapInterpolation => SamplingProfile == "native-scaled" ? BitmapInterpolationMode.LowQuality : BitmapInterpolationMode.None;
    internal required string TraceSha256 { get; init; }
    internal required string FontSha256 { get; init; }
    internal required string ImageSha256 { get; init; }
    internal required TraceFontCollection Font { get; init; }
    internal required Bitmap Image { get; init; }
    internal required int WarmupFrames { get; init; }
    internal required int MeasureFrames { get; init; }
    internal required Op[][] Frames { get; init; }
    internal required Dictionary<string, long> ExpectedMeasuredOpcodeTotals { get; init; }
    internal required StreamGeometry?[] ClosedPaths { get; init; }
    internal required StreamGeometry?[] OpenPaths { get; init; }
    private readonly Dictionary<(int, Color), TextLayout> _staticTexts = new();
    internal int StaticPathVariants => ClosedPaths.Count(p => p is not null) + OpenPaths.Count(p => p is not null);
    internal int StaticTextLayouts => _staticTexts.Count;
    internal double AssetLoadMs { get; private set; }
    internal double StaticPrepareMs { get; private set; }
    internal double AssetsReadyMs { get; private set; }
    internal long PrivateBytesBeforeStaticPrepare { get; private set; }
    internal long PrivateBytesAtAssetsReady { get; private set; }

    internal static Scene Load(string path)
    {
        long start = Stopwatch.GetTimestamp();
        byte[] jsonBytes = File.ReadAllBytes(path);
        using var json = JsonDocument.Parse(jsonBytes);
        JsonElement root = json.RootElement;
        if (root.GetProperty("protocolVersion").GetInt32() != 11 || root.GetProperty("width").GetInt32() != 1280 || root.GetProperty("height").GetInt32() != 800)
            throw new InvalidDataException("Requires protocolVersion=11 and a 1280x800 trace.");
        int warmup = root.GetProperty("warmupFrames").GetInt32(), measure = root.GetProperty("measureFrames").GetInt32();
        if (warmup is < 0 or > 65535 || measure is < 1 or > 65535) throw new InvalidDataException("Frame counts must fit marker sequence numbers; measurement must be nonempty.");
        Op[][] frames = root.GetProperty("frames").EnumerateArray().Select(frame => frame.GetProperty("ops").EnumerateArray().Select(ParseOp).ToArray()).ToArray();
        if (frames.Length != warmup + measure) throw new InvalidDataException("Trace frames must equal warmupFrames + measureFrames; modulo replay is forbidden.");
        Point[][] paths = root.GetProperty("paths").EnumerateArray().Select(ParsePoints).ToArray();
        TextAsset[] texts = root.GetProperty("texts").EnumerateArray().Select(text => new TextAsset(text.GetProperty("text").GetString()!, text.GetProperty("size").GetDouble())).ToArray();
        Validate(frames, paths.Length, texts.Length);
        var expected = root.GetProperty("expectedMeasuredOpcodeTotals").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt64(), StringComparer.Ordinal);
        var counted = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (Op op in frames.Skip(warmup).SelectMany(frame => frame)) counted[op.Kind] = counted.GetValueOrDefault(op.Kind) + 1;
        CheckTotals(counted, expected);
        string directory = Path.GetDirectoryName(path)!;
        byte[] imageBytes = File.ReadAllBytes(Path.Combine(directory, "image.png")), fontBytes = File.ReadAllBytes(Path.Combine(directory, "font.ttf"));
        string imageHash = Convert.ToHexStringLower(SHA256.HashData(imageBytes)), fontHash = Convert.ToHexStringLower(SHA256.HashData(fontBytes));
        CheckAssetHash(root, "image", imageHash);
        CheckAssetHash(root, "font", fontHash);
        using var imageStream = new MemoryStream(imageBytes, writable: false);
        var image = new Bitmap(imageStream);
        var font = new TraceFontCollection(fontBytes);
        FontManager.Current.AddFontCollection(font);
        if (!FontManager.Current.TryGetGlyphTypeface(font.Typeface, out var resolved) || !ReferenceEquals(resolved, font.GlyphTypeface))
            throw new NotSupportedException("Could not prove that text resolves to the registered font.ttf glyph typeface.");
        foreach (string text in texts.Select(t => t.Text).Concat(frames.SelectMany(f => f).Where(op => op.Kind == "text-new").Select(op => op.Text)))
            foreach (Rune rune in text.EnumerateRunes())
                if (rune.Value is not (9 or 10 or 13) && !font.GlyphTypeface.TryGetGlyph((uint)rune.Value, out _))
                    throw new NotSupportedException($"font.ttf lacks U+{rune.Value:X4}; system font fallback is forbidden.");
        string samplingProfile = root.TryGetProperty("samplingProfile", out var sampling) ? sampling.GetString()! : "identity-1to1";
        foreach (Op op in frames.SelectMany(frame => frame).Where(op => op.Kind == "image"))
        {
            double sx = op.Rect.Width / image.PixelSize.Width, sy = op.Rect.Height / image.PixelSize.Height;
            if (sx != sy || sx is not (1 or 2) || (sx == 2 && samplingProfile != "native-scaled")
                || op.Rect.X != Math.Truncate(op.Rect.X) || op.Rect.Y != Math.Truncate(op.Rect.Y))
                throw new NotSupportedException("Image profile requires integer-position 1x scaling; 2x is supported only under native-scaled.");
        }
        double assetLoadMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        using var process = Process.GetCurrentProcess();
        long privateBytesBeforeStaticPrepare = process.PrivateMemorySize64;
        long prepareStart = Stopwatch.GetTimestamp();
        var scene = new Scene
        {
            Case = root.GetProperty("case").GetString()!, Level = root.GetProperty("level").GetString()!,
            SamplingProfile = samplingProfile,
            TraceSha256 = Convert.ToHexStringLower(SHA256.HashData(jsonBytes)), FontSha256 = fontHash, ImageSha256 = imageHash,
            Font = font, Image = image, WarmupFrames = warmup, MeasureFrames = measure, Frames = frames,
            ExpectedMeasuredOpcodeTotals = expected,
            ClosedPaths = PreparePaths(paths, frames.SelectMany(f => f).Where(op => op.Kind is "poly" or "clip").Select(op => op.Id), true),
            OpenPaths = PreparePaths(paths, frames.SelectMany(f => f).Where(op => op.Kind == "stroke").Select(op => op.Id), false), AssetLoadMs = assetLoadMs
        };
        foreach (Op op in frames.SelectMany(f => f).Where(op => op.Kind == "text"))
        {
            var key = (op.Id, op.Color);
            if (!scene._staticTexts.ContainsKey(key))
                scene._staticTexts.Add(key, scene.Layout(texts[op.Id].Text, texts[op.Id].Size, new SolidColorBrush(op.Color).ToImmutable()));
        }
        scene.StaticPrepareMs = Stopwatch.GetElapsedTime(prepareStart).TotalMilliseconds;
        scene.PrivateBytesBeforeStaticPrepare = privateBytesBeforeStaticPrepare;
        process.Refresh();
        scene.PrivateBytesAtAssetsReady = process.PrivateMemorySize64;
        scene.AssetsReadyMs = Program.Clock.Elapsed.TotalMilliseconds;
        return scene;
    }

    private static void CheckAssetHash(JsonElement root, string name, string hash)
    {
        if (root.TryGetProperty("assets", out var assets) && assets.TryGetProperty(name, out var asset)
            && asset.TryGetProperty("sha256", out var expected) && !string.Equals(expected.GetString(), hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Trace asset {name} SHA256 does not match its declared bytes.");
    }

    private static Point[] ParsePoints(JsonElement points) => points.EnumerateArray().Select(p => new Point(p[0].GetDouble(), p[1].GetDouble())).ToArray();
    private static Color ParseColor(string value)
    {
        if (value.Length != 9 || value[0] != '#') throw new InvalidDataException("Colors must use #RRGGBBAA.");
        uint rgba = uint.Parse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromArgb((byte)rgba, (byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8));
    }
    private static Op ParseOp(JsonElement op)
    {
        string kind = op[0].GetString()!;
        double N(int i) => op[i].GetDouble();
        Color C(int i) => ParseColor(op[i].GetString()!);
        Rect R() => new(N(1), N(2), N(3), N(4));
        return kind switch
        {
            "poly" => new Op { Kind = kind, Id = op[1].GetInt32(), Color = C(2) },
            "poly-new" => new Op { Kind = kind, Points = ParsePoints(op[1]), Color = C(2) },
            "stroke" => new Op { Kind = kind, Id = op[1].GetInt32(), Color = C(2), Value = N(3) },
            "stroke-new" => new Op { Kind = kind, Points = ParsePoints(op[1]), Color = C(2), Value = N(3) },
            "rect" => new Op { Kind = kind, Rect = R(), Color = C(5) },
            "text" => new Op { Kind = kind, Id = op[1].GetInt32(), Origin = new Point(N(2), N(3)), Color = C(4) },
            "text-new" => new Op { Kind = kind, Text = op[1].GetString()!, Value = N(2), Origin = new Point(N(3), N(4)), Color = C(5) },
            "image" => new Op { Kind = kind, Rect = R() },
            "transform" => new Op { Kind = kind, Matrix = new Matrix(N(1), N(2), N(3), N(4), N(5), N(6)) },
            "clip" => new Op { Kind = kind, Id = op[1].GetInt32() },
            "clip-new" => new Op { Kind = kind, Points = ParsePoints(op[1]) },
            "layer" => new Op { Kind = kind, Rect = R(), Value = N(5) },
            "gradient" => new Op { Kind = kind, Rect = R(), Origin = new Point(N(5), N(6)), End = new Point(N(7), N(8)), Color = C(9), Color2 = C(10) },
            "end-transform" or "end-clip" or "end-layer" => new Op { Kind = kind },
            _ => throw new InvalidDataException($"Unknown opcode '{kind}'.")
        };
    }

    private static void Validate(Op[][] frames, int pathCount, int textCount)
    {
        foreach (Op[] frame in frames)
        {
            var stack = new Stack<string>();
            foreach (Op op in frame)
            {
                if ((op.Kind is "poly" or "stroke" or "clip") && (uint)op.Id >= pathCount) throw new InvalidDataException("Invalid pathId.");
                if (op.Kind == "text" && (uint)op.Id >= textCount) throw new InvalidDataException("Invalid textId.");
                if (op.Kind == "transform") stack.Push("transform");
                if (op.Kind is "clip" or "clip-new") stack.Push("clip");
                if (op.Kind == "layer")
                {
                    if (stack.Count != 0 || op.Rect.Width <= 0 || op.Rect.Height <= 0 || op.Value is < 0 or > 1)
                        throw new NotSupportedException("Protocol v11 layers must be nonnested, top-level groups with positive bounds and opacity in [0,1].");
                    stack.Push("layer");
                }
                if (op.Kind.StartsWith("end-", StringComparison.Ordinal)
                    && (stack.Count == 0 || stack.Pop() != op.Kind[4..])) throw new InvalidDataException("Trace state operations must be balanced LIFO.");
            }
            if (stack.Count != 0) throw new InvalidDataException("Unclosed trace state operation.");
        }
    }

    private static StreamGeometry?[] PreparePaths(Point[][] paths, IEnumerable<int> ids, bool closed)
    {
        var result = new StreamGeometry?[paths.Length];
        foreach (int id in ids.Distinct()) result[id] = Geometry(paths[id], closed);
        return result;
    }

    private static StreamGeometry Geometry(Point[] points, bool closed)
    {
        if (points.Length < (closed ? 3 : 2)) throw new InvalidDataException("Path contains too few points.");
        var geometry = new StreamGeometry();
        using (StreamGeometryContext writer = geometry.Open())
        {
            writer.BeginFigure(points[0], closed);
            foreach (Point point in points.AsSpan(1)) writer.LineTo(point);
            writer.EndFigure(closed);
        }
        _ = geometry.Bounds; // Force native geometry construction inside the preparation boundary.
        return geometry;
    }

    private TextLayout Layout(string text, double size, IBrush brush)
    {
        // TextLayout eagerly creates and retains TextLines; static layouts reuse those lines.
        // Reference: Avalonia 11.3.12 TextLayout constructor / Draw (FormattedText only caches metrics).
        return new TextLayout(text, Font.Typeface, size, brush);
    }

    internal PreparedOp[] PrepareFrame(int index)
    {
        var result = new PreparedOp[Frames[index].Length];
        for (int i = 0; i < result.Length; i++)
        {
            Op op = Frames[index][i];
            IBrush? brush = op.Kind is "poly" or "poly-new" or "stroke" or "stroke-new" or "rect" or "text-new"
                ? new SolidColorBrush(op.Color).ToImmutable() : null;
            result[i] = op.Kind switch
            {
                "poly" => new(op, ClosedPaths[op.Id], Brush: brush),
                "poly-new" => new(op, Geometry(op.Points, true), Brush: brush),
                "stroke" => new(op, OpenPaths[op.Id], Pen: new Pen(brush, op.Value).ToImmutable()),
                "stroke-new" => new(op, Geometry(op.Points, false), Pen: new Pen(brush, op.Value).ToImmutable()),
                "text" => new(op, Text: _staticTexts[(op.Id, op.Color)]),
                "text-new" => new(op, Text: Layout(op.Text, op.Value, brush!)),
                "clip" => new(op, ClosedPaths[op.Id]),
                "clip-new" => new(op, Geometry(op.Points, true)),
                "layer" => new(op, Brush: new SolidColorBrush(Colors.White, op.Value).ToImmutable()),
                "gradient" => new(op, Brush: new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(op.Origin, RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(op.End, RelativeUnit.Absolute),
                    SpreadMethod = GradientSpreadMethod.Pad,
                    GradientStops = new GradientStops { new GradientStop(op.Color, 0), new GradientStop(op.Color2, 1) }
                }.ToImmutable()),
                _ => new(op, Brush: brush)
            };
        }
        return result;
    }

    internal void Draw(DrawingContext context, PreparedOp[] prepared, Dictionary<string, long>? totals)
    {
        context.DrawRectangle(Background, null, new Rect(0, 0, 1280, 800));
        var stack = new Stack<(IDisposable State, IDisposable? Clip)>();
        foreach (PreparedOp item in prepared)
        {
            Op op = item.Input;
            switch (op.Kind)
            {
                case "poly": case "poly-new": context.DrawGeometry(item.Brush, null, item.Geometry!); break;
                case "stroke": case "stroke-new": context.DrawGeometry(null, item.Pen, item.Geometry!); break;
                case "rect": case "gradient": context.DrawRectangle(item.Brush, null, op.Rect); break;
                case "text": case "text-new": item.Text!.Draw(context, op.Origin); break;
                case "image": context.DrawImage(Image, new Rect(0, 0, Image.Size.Width, Image.Size.Height), op.Rect); break;
                case "transform": stack.Push((context.PushTransform(op.Matrix), null)); break;
                case "clip": case "clip-new": stack.Push((context.PushGeometryClip(item.Geometry!), null)); break;
                case "layer":
                    // PushOpacity alone can distribute alpha per child. This is an isolated SaveLayer,
                    // masked on pop after all source-over children, then composited once to the parent.
                    // Reference: Avalonia.Skia 11.3.12 DrawingContextImpl.Push/PopOpacityMask.
                    var clip = context.PushClip(op.Rect);
                    stack.Push((context.PushOpacityMask(item.Brush!, op.Rect), clip));
                    break;
                case "end-transform": case "end-clip": case "end-layer":
                    var state = stack.Pop();
                    state.State.Dispose();
                    state.Clip?.Dispose();
                    break;
            }
            if (totals is not null) totals[op.Kind] = totals.GetValueOrDefault(op.Kind) + 1;
        }
    }

    internal void CheckMeasuredTotals(Dictionary<string, long> actual) => CheckTotals(actual, ExpectedMeasuredOpcodeTotals);
    private static void CheckTotals(Dictionary<string, long> actual, Dictionary<string, long> expected)
    {
        foreach (string kind in actual.Keys.Concat(expected.Keys).Distinct(StringComparer.Ordinal))
            if (actual.GetValueOrDefault(kind) != expected.GetValueOrDefault(kind))
                throw new InvalidDataException($"Opcode total mismatch for {kind}: actual={actual.GetValueOrDefault(kind)}, expected={expected.GetValueOrDefault(kind)}.");
    }
    internal static void DisposeFrame(PreparedOp[] prepared)
    {
        foreach (PreparedOp op in prepared)
            if (op.Input.Kind == "text-new") op.Text!.Dispose();
    }

    public void Dispose()
    {
        foreach (TextLayout text in _staticTexts.Values) text.Dispose();
        Image.Dispose();
        FontManager.Current.RemoveFontCollection(Font.Key);
    }
}
