using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Logging;

namespace CanvasBenchmark;

internal static class Program
{
    internal static Options Options { get; private set; } = null!;
    internal static PlatformLog PlatformLog { get; } = new();

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Options = Options.Parse(args);
            Logger.Sink = PlatformLog;
            return AppBuilder.Configure<App>().UsePlatformDetect()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}

internal sealed class PlatformLog : ILogSink
{
    internal ConcurrentQueue<string> Messages { get; } = new();

    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Debug
        && area is "Platform" or "OpenGL" or "Vulkan" or "Skia";

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        => Log(level, area, source, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (Messages.Count < 128)
            Messages.Enqueue($"{level}/{area}: {messageTemplate} | {string.Join(", ", propertyValues.Select(value => value?.ToString()))}");
    }
}

internal sealed record Options(string Scene, string Output, int Load, double Seconds, double Warmup, string? Label = null)
{
    internal static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--scene" or "--output" or "--load" or "--seconds" or "--warmup" or "--label"))
                throw new ArgumentException("Expected --scene <json> --output <json> --load <integer> --seconds <number> --warmup <number> [--label <text>].");
            values.Add(args[i], args[i + 1]);
        }
        string scene = Path.GetFullPath(values["--scene"]);
        string output = Path.GetFullPath(values["--output"]);
        int load = int.Parse(values.GetValueOrDefault("--load", "1"), CultureInfo.InvariantCulture);
        double seconds = double.Parse(values.GetValueOrDefault("--seconds", "10"), CultureInfo.InvariantCulture);
        double warmup = double.Parse(values.GetValueOrDefault("--warmup", "2"), CultureInfo.InvariantCulture);
        if (load < 1 || !double.IsFinite(seconds) || seconds <= 0 || !double.IsFinite(warmup) || warmup < 0)
            throw new ArgumentOutOfRangeException(nameof(args), "load >= 1, seconds > 0 and warmup >= 0 are required.");
        return new Options(scene, output, load, seconds, warmup, values.GetValueOrDefault("--label"));
    }
}

internal sealed class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new BenchmarkWindow(Program.Options);
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class BenchmarkWindow : Window
{
    private readonly Options _options;
    private readonly BenchmarkCanvas _canvas;
    private readonly Stopwatch _clock = new();
    private bool _finished;

    internal BenchmarkWindow(Options options)
    {
        _options = options;
        _canvas = new BenchmarkCanvas(Scene.Load(options.Scene), options, _clock);
        Title = $"Avalonia Canvas | {options.Load}x" + (string.IsNullOrEmpty(options.Label) ? "" : $" | {options.Label}");
        ClientSize = new Size(_canvas.Scene.Width, _canvas.Scene.Height);
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(100, 100);
        Background = _canvas.Scene.Background;
        Content = _canvas;
        Opened += (_, _) =>
        {
            _clock.Start();
            RequestAnimationFrame(OnAnimationFrame);
        };
        Closed += (_, _) => _canvas.Scene.Dispose();
    }

    private void OnAnimationFrame(TimeSpan _)
    {
        if (_finished) return;
        double elapsed = _clock.Elapsed.TotalSeconds;
        if (elapsed >= _options.Warmup + _options.Seconds)
        {
            Finish(Math.Max(0, elapsed - _options.Warmup - _options.Seconds));
            return;
        }
        _canvas.InvalidateVisual();
        RequestAnimationFrame(OnAnimationFrame);
    }

    private void Finish(double stopDelaySeconds)
    {
        double measuredSeconds = _options.Seconds;
        _finished = true;
        _canvas.MeasuringComplete = true;
        Directory.CreateDirectory(Path.GetDirectoryName(_options.Output)!);
        var result = new
        {
            framework = "Avalonia",
            load = _options.Load,
            frames = _canvas.DrawTimes.Count,
            seconds = measuredSeconds,
            stopDelaySeconds,
            fps = _canvas.DrawTimes.Count / measuredSeconds,
            frameIntervalsMs = _canvas.FrameIntervals,
            drawTimesMs = _canvas.DrawTimes,
            clientWidth = ClientSize.Width,
            clientHeight = ClientSize.Height,
            devicePixelRatio = RenderScaling,
            versions = new
            {
                avalonia = typeof(Application).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                dotnet = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                rendererAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly => assembly.GetName().Name == "Avalonia.Skia")?.FullName,
                nativeWindow = TryGetPlatformHandle()?.HandleDescriptor,
                backend = "Default platform-selected backend; platformLog records available initialization details.",
                platformLog = Program.PlatformLog.Messages.ToArray(),
                pipeline = "UsePlatformDetect defaults; no rendering/vsync overrides"
            },
            metric = "Control.Render invocations during measurement; frameIntervalsMs is successive measured Render entry spacing; drawTimesMs is CPU command submission inside Render, excludes compositor/GPU rasterization. Physical presentation/scanout is not observable."
        };
        File.WriteAllText(_options.Output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Avalonia: {result.frames} paints / {measuredSeconds:F3}s = {result.fps:F2} fps; {_options.Output}");
        // A preflight image is rendered after timing and never contributes to the counters.
        using var screenshot = new RenderTargetBitmap(
            new PixelSize((int)Math.Round(ClientSize.Width * RenderScaling), (int)Math.Round(ClientSize.Height * RenderScaling)),
            new Vector(96 * RenderScaling, 96 * RenderScaling));
        screenshot.Render(_canvas);
        screenshot.Save(Path.ChangeExtension(_options.Output, ".png"));
        Close();
    }
}

internal sealed class BenchmarkCanvas : Control
{
    private readonly Options _options;
    private readonly Stopwatch _clock;
    private double? _previousMeasuredPaint;
    internal Scene Scene { get; }
    internal bool MeasuringComplete { get; set; }
    internal List<double> FrameIntervals { get; } = new();
    internal List<double> DrawTimes { get; } = new();

    internal BenchmarkCanvas(Scene scene, Options options, Stopwatch clock)
    {
        Scene = scene;
        _options = options;
        _clock = clock;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.LowQuality);
        // Unspecified is Avalonia's antialiased default; do not select EdgeMode.Aliased.
        RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
    }

    public override void Render(DrawingContext context)
    {
        long start = Stopwatch.GetTimestamp();
        double elapsed = _clock.Elapsed.TotalSeconds;
        bool measured = _clock.IsRunning && !MeasuringComplete
            && elapsed >= _options.Warmup && elapsed < _options.Warmup + _options.Seconds;
        int frame = (int)(Math.Floor(elapsed * 60) % Scene.Frames.Length);
        context.DrawRectangle(Scene.Background, null, new Rect(0, 0, Scene.Width, Scene.Height));
        // Each invocation submits each prepared command again. No bitmap/picture/frame cache.
        for (int repeat = 0; repeat < _options.Load; repeat++)
            foreach (DrawOp op in Scene.Frames[frame])
                op.Draw(context, Scene.Icon);
        if (measured)
        {
            DrawTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            if (_previousMeasuredPaint is double previous)
                FrameIntervals.Add((elapsed - previous) * 1000);
            _previousMeasuredPaint = elapsed;
        }
    }
}

internal sealed class Scene : IDisposable
{
    internal double Width { get; private init; }
    internal double Height { get; private init; }
    internal IBrush Background { get; private set; } = null!;
    internal Bitmap Icon { get; private init; } = null!;
    internal DrawOp[][] Frames { get; private set; } = null!;
    private readonly Dictionary<string, IBrush> _brushes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, double, PenLineCap), IPen> _pens = new();
    private readonly Dictionary<(string, double, string), FormattedText> _texts = new();
    private readonly Dictionary<(string, string), IBrush> _gradients = new();

    internal static Scene Load(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = json.RootElement;
        var scene = new Scene
        {
            Width = root.GetProperty("width").GetDouble(),
            Height = root.GetProperty("height").GetDouble(),
            Icon = new Bitmap(Path.Combine(Path.GetDirectoryName(path)!, "icon.png"))
        };
        scene.Background = scene.Brush(root.GetProperty("background").GetString()!);
        scene.Frames = root.GetProperty("frames").EnumerateArray()
            .Select(frame => frame.GetProperty("ops").EnumerateArray().Select(scene.Prepare).ToArray()).ToArray();
        if (scene.Frames.Length != 60) throw new InvalidDataException("Expected 60 deterministic scene frames.");
        return scene;
    }

    private IBrush Brush(string color)
    {
        if (_brushes.TryGetValue(color, out IBrush? brush)) return brush;
        // The shared wire format is RRGGBBAA; Avalonia's eight-digit parser uses AARRGGBB.
        Color parsed = color.Length == 9
            ? Color.FromArgb(Convert.ToByte(color[7..9], 16), Convert.ToByte(color[1..3], 16),
                Convert.ToByte(color[3..5], 16), Convert.ToByte(color[5..7], 16))
            : Color.Parse(color);
        return _brushes[color] = new SolidColorBrush(parsed).ToImmutable();
    }

    private IPen Pen(string color, double width, PenLineCap cap = PenLineCap.Flat)
    {
        var key = (color, width, cap);
        if (!_pens.TryGetValue(key, out IPen? pen))
            _pens[key] = pen = new Pen(Brush(color), width, lineCap: cap, lineJoin: PenLineJoin.Miter, miterLimit: 10).ToImmutable();
        return pen;
    }

    private DrawOp Prepare(JsonElement op)
    {
        double N(int i) => op[i].GetDouble();
        string S(int i) => op[i].GetString()!;
        switch (S(0))
        {
            case "line":
                return new DrawOp { Kind = "line", P0 = new Point(N(1), N(2)), P1 = new Point(N(3), N(4)), Pen = Pen(S(5), N(6)) };
            case "rect":
                return new DrawOp { Kind = "rect", Rect = new Rect(N(1), N(2), N(3), N(4)), Brush = Brush(S(5)) };
            case "circle":
                return new DrawOp { Kind = "circle", P0 = new Point(N(1), N(2)), Radius = N(3), Pen = Pen(S(4), N(5)) };
            case "arc":
            {
                double cx = N(1), cy = N(2), radius = N(3), start = N(4), sweep = N(5);
                var geometry = new StreamGeometry();
                using (StreamGeometryContext path = geometry.Open())
                {
                    path.BeginFigure(new Point(cx + Math.Cos(start) * radius, cy + Math.Sin(start) * radius), false);
                    int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / Math.PI));
                    for (int i = 1; i <= segments; i++)
                    {
                        double angle = start + sweep * i / segments;
                        path.ArcTo(new Point(cx + Math.Cos(angle) * radius, cy + Math.Sin(angle) * radius),
                            new Size(radius, radius), 0, false, sweep >= 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                    }
                    path.EndFigure(false);
                }
                return new DrawOp
                {
                    Kind = "arc", Geometry = geometry, Pen = Pen(S(6), N(7)), Brush = Brush(S(6)), Radius = N(7) / 2,
                    P0 = new Point(cx + Math.Cos(start) * radius, cy + Math.Sin(start) * radius),
                    P1 = new Point(cx + Math.Cos(start + sweep) * radius, cy + Math.Sin(start + sweep) * radius)
                };
            }
            case "curve":
            {
                var points = new Point[33];
                for (int i = 0; i <= 32; i++)
                {
                    double t = i / 32.0, u = 1 - t;
                    points[i] = new Point(u * u * u * N(1) + 3 * u * u * t * N(3) + 3 * u * t * t * N(5) + t * t * t * N(7),
                        u * u * u * N(2) + 3 * u * u * t * N(4) + 3 * u * t * t * N(6) + t * t * t * N(8));
                }
                var stroke = new StreamGeometry();
                using (StreamGeometryContext path = stroke.Open())
                {
                    path.BeginFigure(points[0], false);
                    foreach (Point point in points.AsSpan(1)) path.LineTo(point);
                    path.EndFigure(false);
                }
                var area = new StreamGeometry();
                using (StreamGeometryContext path = area.Open())
                {
                    path.BeginFigure(points[0], true);
                    foreach (Point point in points.AsSpan(1)) path.LineTo(point);
                    path.LineTo(new Point(N(7), N(9)));
                    path.LineTo(new Point(N(1), N(9)));
                    path.EndFigure(true);
                }
                var key = (S(10), S(11));
                if (!_gradients.TryGetValue(key, out IBrush? gradient))
                {
                    gradient = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 210, RelativeUnit.Absolute),
                        EndPoint = new RelativePoint(0, 390, RelativeUnit.Absolute),
                        GradientStops = new GradientStops
                        {
                            new GradientStop(((ISolidColorBrush)Brush(S(10))).Color, 0),
                            new GradientStop(((ISolidColorBrush)Brush(S(11))).Color, 1)
                        }
                    }.ToImmutable();
                    _gradients[key] = gradient;
                }
                return new DrawOp
                {
                    Kind = "curve", Geometry = stroke, FillGeometry = area, Brush = gradient, Pen = Pen(S(10), N(12)),
                    Rect = new Rect(N(1), 200, N(7) - N(1), 190)
                };
            }
            case "text":
            {
                var key = (S(1), N(4), S(5));
                if (!_texts.TryGetValue(key, out FormattedText? text))
                {
                    text = new FormattedText(S(1), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI", FontStyle.Normal, FontWeight.Normal), N(4), Brush(S(5)));
                    _ = text.Width;
                    _ = text.Height;
                    _texts[key] = text;
                }
                return new DrawOp { Kind = "text", Text = text, P0 = new Point(N(2), N(3)) };
            }
            case "image":
                return new DrawOp { Kind = "image", Rect = new Rect(N(1), N(2), N(3), N(4)) };
            default:
                throw new InvalidDataException($"Unknown canvas operation: {S(0)}");
        }
    }

    public void Dispose() => Icon.Dispose();
}

internal sealed class DrawOp
{
    internal required string Kind { get; init; }
    internal Point P0 { get; init; }
    internal Point P1 { get; init; }
    internal Rect Rect { get; init; }
    internal double Radius { get; init; }
    internal IBrush? Brush { get; init; }
    internal IPen? Pen { get; init; }
    internal StreamGeometry? Geometry { get; init; }
    internal StreamGeometry? FillGeometry { get; init; }
    internal FormattedText? Text { get; init; }

    internal void Draw(DrawingContext context, Bitmap icon)
    {
        switch (Kind)
        {
            case "line": context.DrawLine(Pen!, P0, P1); break;
            case "rect": context.DrawRectangle(Brush, null, Rect); break;
            case "circle": context.DrawEllipse(null, Pen, P0, Radius, Radius); break;
            case "arc":
                context.DrawGeometry(null, Pen, Geometry!);
                context.DrawEllipse(Brush, null, P0, Radius, Radius);
                context.DrawEllipse(Brush, null, P1, Radius, Radius);
                break;
            case "curve":
                using (context.PushGeometryClip(FillGeometry!))
                    context.DrawRectangle(Brush, null, Rect);
                context.DrawGeometry(null, Pen, Geometry!);
                break;
            case "text": context.DrawText(Text!, P0); break;
            case "image": context.DrawImage(icon, Rect); break;
        }
    }
}
