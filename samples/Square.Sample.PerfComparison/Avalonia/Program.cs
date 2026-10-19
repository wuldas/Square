using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace Square.Sample.PerfComparison.Avalonia;

// Standalone Avalonia desktop counterpart of the 720-box animation workload in
// samples/Square.Sample.PerfComparison (Square, Avalonia, Electron and Flutter).
// This is NOT the Square HtmlExporter/WebServer path: it is a plain Avalonia window
// driven by TopLevel.RequestAnimationFrame (render-loop hook; no DispatcherTimer or
// other manually render-forcing timer).
internal static class Program
{
    private static BenchmarkOptions? _options;

    internal static BenchmarkOptions Options => _options
        ?? throw new InvalidOperationException("Benchmark options were not parsed before startup.");

    [STAThread]
    public static int Main(string[] args)
    {
        // Shared cross-target benchmark CLI: --seconds 30 --warmup 2 --output C:\abs\result.json
        // (--flag value or --flag=value). Invalid usage exits 2 without opening a window.
        var options = BenchmarkOptions.Parse(args);
        if (options is null)
        {
            Console.Error.WriteLine("Usage: Avalonia --seconds 30 --warmup 2 --output C:\\abs\\result.json");
            return 2;
        }

        _options = options;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}

// --seconds / --warmup / --output following the shared benchmark contract:
// 2 s warmup, 30 s measured window by default.
internal sealed record BenchmarkOptions(double Seconds, double WarmupSeconds, string? Output)
{
    public static BenchmarkOptions? Parse(IReadOnlyList<string>? args)
    {
        double seconds = 30.0;
        double warmupSeconds = 2.0;
        string? output = null;

        for (var i = 0; args is not null && i < args.Count; i++)
        {
            string name;
            string value;
            var argument = args[i];
            var separator = argument.IndexOf('=');
            if (argument.StartsWith("--", StringComparison.Ordinal) && separator > 2)
            {
                name = argument[..separator];
                value = argument[(separator + 1)..];
            }
            else
            {
                if (i + 1 >= args.Count) return null;
                name = argument;
                value = args[++i];
            }

            switch (name)
            {
                case "--seconds":
                    if (!TryFinite(value, out seconds) || seconds <= 0) return null;
                    break;
                case "--warmup":
                    if (!TryFinite(value, out warmupSeconds) || warmupSeconds < 0) return null;
                    break;
                case "--output":
                    if (value.Length == 0) return null;
                    output = value;
                    break;
                default:
                    return null;
            }
        }

        return new BenchmarkOptions(seconds, warmupSeconds, output);
    }

    private static bool TryFinite(string value, out double parsed) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
        && double.IsFinite(parsed);
}

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new BenchmarkWindow(Program.Options);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

// One 900x1240-client window holding the shared workload, matching CssAnimationPerfPage.sqv:
//   group 1 (red #ef4444):   transform-only; 240 boxes, each three nested 30x30 layers
//                            animating translate 0->36->0, rotate 0->180->360deg and
//                            scale 0.6->1.4->0.6 around each layer's center
//                            (layout box of the box itself is untouched); the colored
//                            core is an 18x18 square centered in the 30x30 scale layer
//                            (6px margin all around) so scaled diamonds do not merge;
//   group 2 (blue #3b82f6):  opacity-only; 240 boxes, opacity 0->1->0;
//   group 3 (amber #f59e0b): width-only; 240 boxes, width 8->30->8, re-packing the
//                            wrap rows every frame (layout heavy).
// All loops are 6s linear (triangle wave X->peak->X; rotation wraps 360->0 seamlessly),
// boxes are 30x30 with margin 0 5px 4px 0 in flex-wrap style wrap rows.
// The window opens at outer origin (100,100); after --warmup seconds (default 2) of
// animated warmup the measured window runs for --seconds (default 30). CPU uses
// Process.TotalProcessorTime deltas over the measured window only, so cpuOneCorePercent
// is the fraction of ONE logical core. Memory is PrivateMemorySize64 (private commit)
// plus WorkingSet64 (working set) of this process sampled at 1 Hz; GPU VRAM is not
// included. frames/fps count TopLevel.RequestAnimationFrame callbacks per wall time:
// the callback updates the scene, but Avalonia 11 exposes no public physical-present
// counter, so fpsDefinition labels this callback FPS rather than scanout telemetry.
// The compact result JSON (shared cross-target contract, unrounded numbers) always
// goes to stdout; --output additionally writes it to a file.
internal sealed class BenchmarkWindow : Window
{
    private const double LoopSeconds = 6.0;
    private const int BoxesPerGroup = 240;

    private const double ClientWidth = 900.0;
    private const double ClientHeight = 1240.0;

    private const double BoxWidth = 30.0;
    private const double BoxHeight = 30.0;
    private const double BoxMarginRight = 5.0;
    private const double BoxMarginBottom = 4.0;
    private const double TransformCoreSize = 18.0; // colored core inside the scale layer
    private const double TransformCoreMargin = 6.0; // 6 + 18 + 6 = 30, centered

    private const double ShiftMax = 36.0;
    private const double ScaleMin = 0.6;
    private const double ScaleMax = 1.4;
    private const double OpacityMin = 0.0;
    private const double WidthMin = 8.0;

    private static readonly IBrush TransformBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly IBrush OpacityBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly IBrush WidthBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly IBrush PageBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFB));
    private static readonly IBrush HeaderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xF1, 0xFF));
    private static readonly IBrush HeaderBorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
    private static readonly IBrush CardBorderBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));

    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Stopwatch _clock = new();
    private readonly BenchmarkOptions _options;
    private readonly double _measureSeconds;
    private readonly double _warmupSeconds;

    private readonly TranslateTransform[] _shifts = new TranslateTransform[BoxesPerGroup];
    private readonly RotateTransform[] _spins = new RotateTransform[BoxesPerGroup];
    private readonly ScaleTransform[] _scales = new ScaleTransform[BoxesPerGroup];
    private readonly Border[] _fades = new Border[BoxesPerGroup];
    private readonly Border[] _wobbles = new Border[BoxesPerGroup];

    private TimeSpan _measureStart;
    private TimeSpan _cpuStart;
    private DateTime _startedUtc;
    private long _frames;
    private bool _measuring;
    private bool _finished;
    private long _privateSum, _privatePeak, _workingSum, _workingPeak;
    private int _memorySamples;
    private TimeSpan _lastMemorySample;

    public BenchmarkWindow(BenchmarkOptions options)
    {
        _options = options;
        _measureSeconds = options.Seconds;
        _warmupSeconds = options.WarmupSeconds;
        Title = "Avalonia Perf Comparison";
        // Pin the outer window origin so all three targets start at (100,100) on screen.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(100, 100);
        ClientSize = new Size(ClientWidth, ClientHeight);
        Background = PageBrush;
        CanResize = false;
        Content = BuildContent();
        Opened += (_, _) =>
        {
            _clock.Start();
            RequestAnimationFrame(OnFrame);
        };
    }

    private Control BuildContent()
    {
        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };
        var transformGrid = new WrapPanel { Orientation = Orientation.Horizontal };
        var fadeGrid = new WrapPanel { Orientation = Orientation.Horizontal };
        var wobbleGrid = new WrapPanel { Orientation = Orientation.Horizontal };
        var center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        var boxMargin = new Thickness(0.0, 0.0, BoxMarginRight, BoxMarginBottom);

        for (int i = 0; i < BoxesPerGroup; i++)
        {
            // Group 1: transform-only, three nested layers like .perf-shift/.perf-spin/.perf-scale.
            var shift = new TranslateTransform(0.0, 0.0);
            var spin = new RotateTransform(0.0);
            var scale = new ScaleTransform(ScaleMin, ScaleMin);
            var scaled = new Border
            {
                Width = BoxWidth,
                Height = BoxHeight,
                RenderTransformOrigin = center,
                RenderTransform = scale,
                Child = new Border
                {
                    Width = TransformCoreSize,
                    Height = TransformCoreSize,
                    Margin = new Thickness(TransformCoreMargin),
                    Background = TransformBrush,
                },
            };
            var spun = new Border
            {
                Width = BoxWidth,
                Height = BoxHeight,
                RenderTransformOrigin = center,
                RenderTransform = spin,
                Child = scaled,
            };
            var shifted = new Border
            {
                Width = BoxWidth,
                Height = BoxHeight,
                Margin = boxMargin,
                RenderTransformOrigin = center,
                RenderTransform = shift,
                Child = spun,
            };
            transformGrid.Children.Add(shifted);
            _shifts[i] = shift;
            _spins[i] = spin;
            _scales[i] = scale;

            // Group 2: opacity-only, static 30x30 layout box.
            var fade = new Border
            {
                Width = BoxWidth,
                Height = BoxHeight,
                Margin = boxMargin,
                Background = OpacityBrush,
                Opacity = OpacityMin,
            };
            fadeGrid.Children.Add(fade);
            _fades[i] = fade;

            // Group 3: width-only, re-packs the wrap rows every frame.
            var wobble = new Border
            {
                Width = WidthMin,
                Height = BoxHeight,
                Margin = boxMargin,
                Background = WidthBrush,
            };
            wobbleGrid.Children.Add(wobble);
            _wobbles[i] = wobble;
        }

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "CSS 动画性能对比",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = TextBrush,
            Margin = new Thickness(0, 0, 0, 4)
        });
        heading.Children.Add(new TextBlock
        {
            Text = "240 方块 × 3 组 · 6 秒循环 · 目标 60 FPS",
            Foreground = TextBrush,
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new Border
        {
            Background = HeaderBrush,
            BorderBrush = HeaderBorderBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4),
            Child = heading
        });
        root.Children.Add(CreateCard("transform：不触发布局（推荐）", transformGrid));
        root.Children.Add(CreateCard("opacity：只触发合成与重绘", fadeGrid));
        root.Children.Add(CreateCard("width：每帧触发布局（最贵）", wobbleGrid));
        return root;
    }

    private static Border CreateCard(string title, WrapPanel grid)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = TextBrush,
            Margin = new Thickness(0, 0, 0, 3)
        });
        content.Children.Add(grid);
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 5, 0, 0),
            Padding = new Thickness(5),
            Child = content
        };
    }

    private void OnFrame(TimeSpan _)
    {
        if (_finished)
        {
            return;
        }

        TimeSpan now = _clock.Elapsed;
        if (!_measuring)
        {
            Animate(now);
            if (now.TotalSeconds >= _warmupSeconds)
            {
                _measuring = true;
                _measureStart = now;
                _cpuStart = _process.TotalProcessorTime;
                _startedUtc = DateTime.UtcNow;
                _frames = 0;
                SampleMemory(now);
            }

            RequestAnimationFrame(OnFrame);
            return;
        }

        double measured = (now - _measureStart).TotalSeconds;
        SampleMemory(now);
        if (measured >= _measureSeconds)
        {
            Finish(measured);
            return;
        }

        _frames++;
        Animate(now);
        RequestAnimationFrame(OnFrame);
    }

    private void Animate(TimeSpan time)
    {
        double phase = time.TotalSeconds % LoopSeconds / LoopSeconds; // 0..1 over the 6s loop
        double triangle = phase <= 0.5 ? phase * 2.0 : 2.0 - phase * 2.0; // 0 -> 1 -> 0
        double shift = ShiftMax * triangle; // 0 -> 36 -> 0 px
        double spin = 360.0 * phase; // 0 -> 180 -> 360 deg over a full loop
        double scale = ScaleMin + (ScaleMax - ScaleMin) * triangle; // 0.6 -> 1.4 -> 0.6
        double opacity = OpacityMin + (1.0 - OpacityMin) * triangle; // 0 -> 1 -> 0
        double width = WidthMin + (BoxWidth - WidthMin) * triangle; // 8 -> 30 -> 8 px

        for (int i = 0; i < BoxesPerGroup; i++)
        {
            _shifts[i].X = shift;
            _spins[i].Angle = spin;
            _scales[i].ScaleX = scale;
            _scales[i].ScaleY = scale;
            _fades[i].Opacity = opacity;
            _wobbles[i].Width = width;
        }
    }

    private void SampleMemory(TimeSpan now)
    {
        if (_memorySamples > 0 && (now - _lastMemorySample).TotalSeconds < 1) return;
        _process.Refresh();
        var privateBytes = _process.PrivateMemorySize64;
        var workingBytes = _process.WorkingSet64;
        _privateSum += privateBytes;
        _workingSum += workingBytes;
        _privatePeak = Math.Max(_privatePeak, privateBytes);
        _workingPeak = Math.Max(_workingPeak, workingBytes);
        _memorySamples++;
        _lastMemorySample = now;
    }

    private void Finish(double measured)
    {
        _finished = true;
        _process.Refresh();
        double cpuSeconds = (_process.TotalProcessorTime - _cpuStart).TotalSeconds;
        double cpuOneCorePercent = measured > 0 ? cpuSeconds / measured * 100.0 : 0.0;
        double fps = measured > 0 ? _frames / measured : 0.0;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("target", $"Avalonia {(RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT")}");
            writer.WriteNumber("seconds", measured);
            writer.WriteNumber("frames", _frames);
            writer.WriteNumber("fps", fps);
            writer.WriteString("fpsDefinition", "TopLevel.RequestAnimationFrame callback count over the measured wall window; Avalonia 11.3 exposes no public presented-frame/scanout counter, so this is callback FPS, not present telemetry");
            writer.WriteNumber("cpuSeconds", cpuSeconds);
            writer.WriteNumber("cpuOneCorePercent", cpuOneCorePercent);
            writer.WriteNumber("privateAvgMiB", _privateSum / (double)_memorySamples / 1048576.0);
            writer.WriteNumber("privatePeakMiB", _privatePeak / 1048576.0);
            writer.WriteNumber("workingAvgMiB", _workingSum / (double)_memorySamples / 1048576.0);
            writer.WriteNumber("workingPeakMiB", _workingPeak / 1048576.0);
            writer.WriteNumber("samples", _memorySamples);
            writer.WriteString("memoryDefinition", "PrivateMemorySize64 (private commit) + WorkingSet64 (working set) of the Avalonia process sampled at 1 Hz; GPU VRAM is not included");
            writer.WriteNumber("clientWidth", ClientSize.Width);
            writer.WriteNumber("clientHeight", ClientSize.Height);
            writer.WriteNumber("warmupSeconds", _warmupSeconds);
            writer.WriteNumber("targetFps", 60.0);
            writer.WriteString("startedUtc", _startedUtc.ToString("o", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }
        var json = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        Console.WriteLine(json);
        Console.Out.Flush();

        if (_options.Output is { Length: > 0 } outputPath)
        {
            var fullPath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, json);
        }

        Close();
    }
}
