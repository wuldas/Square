using System.Diagnostics;
#if SQUARE_SAMPLE_VULKAN
using Square.Backends.Vulkan;
#endif
#if SQUARE_SAMPLE_SKIA
using Square.Backends.Skia;
#endif
#if SQUARE_SAMPLE_DIRECT2D
using Square.Backends.Direct2D;
#endif
using Square.Graphics;
using Square.Graphics.Codecs;
using Square.Hosting;
using Square.Extensions.Routing;
using Square.Sample.Components;
using Square.Platform;
#if SQUARE_SAMPLE_DEVTOOLS
using Square.DevTools;
#endif
using Square.UI;

namespace Square.Sample;

public static class Program
{
    public static void Main(string[] args)
    {
        System.Console.WriteLine("Square Framework Sample");

        var circleDiffDirectory = GetOption(args, "--circle-regression-diff");
        if (!string.IsNullOrWhiteSpace(circleDiffDirectory))
        {
            RunCircleRegressionDiff(circleDiffDirectory);
            return;
        }

        var mediaDiffDirectory = GetOption(args, "--media-regression-diff");
        if (!string.IsNullOrWhiteSpace(mediaDiffDirectory))
        {
            RunMediaRegressionDiff(mediaDiffDirectory);
            return;
        }

        var window = new AppWindow("Square Framework", 900, 980);
        var router = window.UseRouter(routes =>
        {
            routes.Map("/", static () => new RouteShell(), route =>
            {
                route.KeepAlive = true;
                route.Map("", static () => new RouteHomePage());
                route.Map("users/:id", static () => new RouteUserPage(), child => child.KeepAlive = true);
                route.Map("admin", static () => new RouteAdminPage());
                route.Map("login", static () => new RouteLoginPage());
            });
            routes.Map("*", static () => new RouteHomePage());
        });
        router.BeforeEach((to, _) => to.Path == "/admin"
            ? RouteGuardResult.Redirect("/login?returnUrl=/admin")
            : RouteGuardResult.Allow);
        window.Load(CreatePage(args));
        window.LoadCustomTitleBar(new MyTitleBar());
        window.BorderStyle = BorderStyle.Resizable;
        var app = new DesktopApplication(window);
        var backend = GetOption(args, "--backend") ?? Environment.GetEnvironmentVariable("SQUARE_RENDER_BACKEND");
        if (string.Equals(backend, "Vulkan", StringComparison.OrdinalIgnoreCase))
#if SQUARE_SAMPLE_VULKAN
            window.UseVulkanBackend();
#else
            throw new NotSupportedException("This build does not include Vulkan. Build with -p:SquareSampleUseVulkan=true to enable it.");
#endif
        else if (string.Equals(backend, "Skia", StringComparison.OrdinalIgnoreCase))
#if SQUARE_SAMPLE_SKIA
            window.UseSkiaBackend();
#else
            throw new NotSupportedException("This build does not include Skia. Build with -p:SquareSampleUseSkia=true to enable it.");
#endif
        else if (string.Equals(backend, "Direct2D", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(backend, "D2D", StringComparison.OrdinalIgnoreCase))
#if SQUARE_SAMPLE_DIRECT2D
            window.UseDirect2DBackend();
#else
            throw new NotSupportedException("This build does not include Direct2D. Build for Win32 with -p:SquareSampleUseDirect2D=true to enable it.");
#endif
        ConfigureRendering(window, args);
        ConfigureDebugOverlayToggle(window);
        SampleSignals.Initialize(app.Dispatcher);
        var screenshot = GetOption(args, "--screenshot");
        if (!string.IsNullOrWhiteSpace(screenshot))
            ScheduleScreenshot(window, screenshot, GetScreenshotValidator(args), GetOption(args, "--circle-regression-bgra"));

        if (HasOption(args, "--devtools"))
        {
#if SQUARE_SAMPLE_DEVTOOLS
            var devTools = window.UseDevToolsServer(new DevToolsOptions
            {
                Port = int.TryParse(GetOption(args, "--devtools-port"), out var port) ? port : 0,
                AccessToken = GetOption(args, "--devtools-token"),
                AllowInputInjection = true,
                AllowInspector = true,
                AllowMemoryDiagnostics = HasOption(args, "--devtools-memory"),
                AllowChromeInspect = HasOption(args, "--devtools-chrome-inspect"),
                IncludeTextContent = HasOption(args, "--devtools-chrome-inspect")
            });
            System.Console.WriteLine($"Square DevTools: {devTools.BaseAddress}/api/v1/health");
            System.Console.WriteLine($"Token header: {DevToolsServer.TokenHeader}: {devTools.AccessToken}");
            if (HasOption(args, "--devtools-memory"))
                System.Console.WriteLine($"Memory diagnostics: {devTools.BaseAddress}/api/v1/memory");
            if (HasOption(args, "--devtools-chrome-inspect"))
            {
                System.Console.WriteLine($"Chrome Inspector: {devTools.BaseAddress}/json/list");
                System.Console.WriteLine("Warning: Chrome Inspector allows local unauthenticated CDP connections while enabled.");
            }
#else
            throw new NotSupportedException("This build does not include DevTools. Build with -p:SquareSampleUseDevTools=true to enable it.");
#endif
        }

        app.Run();

        System.Console.WriteLine("Window closed. Demo complete.");
    }

    private static void RunCircleRegressionDiff(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var softwarePng = Path.Combine(outputDirectory, "software.png");
        var vulkanPng = Path.Combine(outputDirectory, "vulkan.png");
        var softwareBgra = Path.Combine(outputDirectory, "software.bgra");
        var vulkanBgra = Path.Combine(outputDirectory, "vulkan.bgra");

        RunCircleRegressionCapture("Software", softwarePng, softwareBgra);
        RunCircleRegressionCapture("Vulkan", vulkanPng, vulkanBgra);

        using var software = LoadBitmapDump(softwareBgra);
        using var vulkan = LoadBitmapDump(vulkanBgra);
        var result = CircleRegressionDiff.Save(software, vulkan, outputDirectory);

        System.Console.WriteLine($"Circle regression diff written to {outputDirectory}");
        System.Console.WriteLine($"Software: {result.SoftwarePath}");
        System.Console.WriteLine($"Vulkan:   {result.VulkanPath}");
        System.Console.WriteLine($"Diff:     {result.DiffPath}");
        System.Console.WriteLine($"Report:   {result.ReportPath}");
        foreach (var (name, stats) in result.Regions)
        {
            System.Console.WriteLine(
                $"{name}: differingPixels={stats.DifferingPixels}, totalDelta={stats.TotalDelta}, maxDelta={stats.MaxDelta}, " +
                $"softwareHeavier={stats.SoftwareHeavier}, vulkanHeavier={stats.VulkanHeavier}, shapeOnly={stats.ShapeOnly}");
        }
    }

    private static void RunMediaRegressionDiff(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var softwarePng = Path.Combine(outputDirectory, "software.png");
        var vulkanPng = Path.Combine(outputDirectory, "vulkan.png");
        var softwareBgra = Path.Combine(outputDirectory, "software.bgra");
        var vulkanBgra = Path.Combine(outputDirectory, "vulkan.bgra");

        RunMediaRegressionCapture("Software", softwarePng, softwareBgra);
        RunMediaRegressionCapture("Vulkan", vulkanPng, vulkanBgra);

        using var software = LoadBitmapDump(softwareBgra);
        using var vulkan = LoadBitmapDump(vulkanBgra);
        var result = CircleRegressionDiff.SaveMediaSvg(software, vulkan, outputDirectory);

        System.Console.WriteLine($"Media regression diff written to {outputDirectory}");
        System.Console.WriteLine($"Software: {result.SoftwarePath}");
        System.Console.WriteLine($"Vulkan:   {result.VulkanPath}");
        System.Console.WriteLine($"Diff:     {result.DiffPath}");
        System.Console.WriteLine($"Report:   {result.ReportPath}");
        foreach (var (name, stats) in result.Regions)
        {
            System.Console.WriteLine(
                $"{name}: differingPixels={stats.DifferingPixels}, totalDelta={stats.TotalDelta}, maxDelta={stats.MaxDelta}, " +
                $"softwareHeavier={stats.SoftwareHeavier}, vulkanHeavier={stats.VulkanHeavier}, shapeOnly={stats.ShapeOnly}");
        }
    }

    private static void RunCircleRegressionCapture(string backend, string pngPath, string bgraPath)
    {
        var processPath = Environment.ProcessPath ?? "dotnet";
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "Square.Sample.dll");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false
        };
        if (Path.GetFileNameWithoutExtension(startInfo.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--backend");
        startInfo.ArgumentList.Add(backend);
        startInfo.ArgumentList.Add("--circle-regression");
        startInfo.ArgumentList.Add("--verify-circle-regression");
        startInfo.ArgumentList.Add("--screenshot");
        startInfo.ArgumentList.Add(pngPath);
        startInfo.ArgumentList.Add("--circle-regression-bgra");
        startInfo.ArgumentList.Add(bgraPath);
        if (string.Equals(backend, "Vulkan", StringComparison.OrdinalIgnoreCase))
            startInfo.Environment["SQUARE_VULKAN_READBACK"] = "1";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {backend} capture process.");
        if (!process.WaitForExit(30000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{backend} circle regression capture timed out.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{backend} circle regression capture exited with code {process.ExitCode}.");
    }

    private static void RunMediaRegressionCapture(string backend, string pngPath, string bgraPath)
    {
        var processPath = Environment.ProcessPath ?? "dotnet";
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "Square.Sample.dll");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false
        };
        if (Path.GetFileNameWithoutExtension(startInfo.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--backend");
        startInfo.ArgumentList.Add(backend);
        startInfo.ArgumentList.Add("--media-regression");
        startInfo.ArgumentList.Add("--screenshot");
        startInfo.ArgumentList.Add(pngPath);
        startInfo.ArgumentList.Add("--circle-regression-bgra");
        startInfo.ArgumentList.Add(bgraPath);
        if (string.Equals(backend, "Vulkan", StringComparison.OrdinalIgnoreCase))
            startInfo.Environment["SQUARE_VULKAN_READBACK"] = "1";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {backend} capture process.");
        if (!process.WaitForExit(30000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{backend} media regression capture timed out.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{backend} media regression capture exited with code {process.ExitCode}.");
    }

    private static void SaveBitmapDump(Bitmap bitmap, string path)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("SQBGRA1");
        writer.Write(bitmap.Width);
        writer.Write(bitmap.Height);
        writer.Write(bitmap.Pixels);
    }

    private static Bitmap LoadBitmapDump(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadString() != "SQBGRA1")
            throw new InvalidOperationException($"Invalid bitmap dump: {path}");
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        var bitmap = new Bitmap(width, height);
        var bytesRead = reader.Read(bitmap.Pixels, 0, bitmap.Pixels.Length);
        if (bytesRead != bitmap.Pixels.Length)
        {
            bitmap.Dispose();
            throw new InvalidOperationException($"Bitmap dump is truncated: {path}");
        }
        return bitmap;
    }
    private static UIElement CreatePage(string[] args)
    {
        if (HasOption(args, "--circle-regression")) return new CircleRegressionPage();
        if (HasOption(args, "--media-regression")) return new MediaSvgRegressionPage();
        if (HasOption(args, "--stroke-regression")) return new VulkanStrokeRegressionPage();
        return new Main();
    }

    private static Action<Bitmap>? GetScreenshotValidator(string[] args)
    {
        if (HasOption(args, "--verify-circle-regression")) return CircleRegressionPage.ValidateScreenshot;
        if (HasOption(args, "--verify-stroke-regression")) return VulkanStrokeRegressionPage.ValidateScreenshot;
        return null;
    }

    private static void ScheduleScreenshot(AppWindow window, string path, Action<Bitmap>? validateScreenshot, string? bitmapDumpPath = null)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(1800);
            try
            {
                using var bitmap = await window.CaptureRendererBitmapAsync();
                validateScreenshot?.Invoke(bitmap);
                BitmapPngEncoder.Save(bitmap, path);
                if (!string.IsNullOrWhiteSpace(bitmapDumpPath))
                    SaveBitmapDump(bitmap, bitmapDumpPath);
                System.Console.WriteLine($"Screenshot saved to {path}");
            }
            catch (Exception exception)
            {
                System.Console.Error.WriteLine($"Screenshot failed: {exception}");
                Environment.ExitCode = 1;
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void ConfigureRendering(AppWindow window, string[] args)
    {
        var mode = GetOption(args, "--render-mode") ?? Environment.GetEnvironmentVariable("SQUARE_RENDER_MODE");
        if (Enum.TryParse<RenderMode>(mode, ignoreCase: true, out var renderMode))
            window.RenderingMode = renderMode;

        var overlay = GetOption(args, "--render-overlay") ?? Environment.GetEnvironmentVariable("SQUARE_RENDER_OVERLAY");
        if (TryParseBool(overlay, out var showOverlay))
            window.ShowRenderDiagnosticsOverlay = showOverlay;

        var dirtyOverlay = GetOption(args, "--dirty-overlay") ?? Environment.GetEnvironmentVariable("SQUARE_DIRTY_OVERLAY");
        if (TryParseBool(dirtyOverlay, out var showDirtyOverlay))
            window.ShowDirtyUnionOverlay = showDirtyOverlay;

        var maxDirtyArea = GetOption(args, "--max-dirty-area") ?? Environment.GetEnvironmentVariable("SQUARE_MAX_DIRTY_AREA");
        if (float.TryParse(maxDirtyArea, out var areaRatio))
            window.MaxDirtyAreaRatio = Math.Clamp(areaRatio, 0f, 1f);

        var maxDirtyRects = GetOption(args, "--max-dirty-rects") ?? Environment.GetEnvironmentVariable("SQUARE_MAX_DIRTY_RECTS");
        if (int.TryParse(maxDirtyRects, out var rectCount))
            window.MaxDirtyRectCount = Math.Max(1, rectCount);

        System.Console.WriteLine($"Render: mode={window.RenderingMode}, overlay={window.ShowRenderDiagnosticsOverlay}, dirtyOverlay={window.ShowDirtyUnionOverlay}, maxDirtyArea={window.MaxDirtyAreaRatio:0.##}, maxDirtyRects={window.MaxDirtyRectCount}");
    }

    private static void ConfigureDebugOverlayToggle(AppWindow window)
    {
#if DEBUG
        const int f12 = 0x7B;
        const string baseTitle = "Square Framework";

        UpdateDebugTitle(window, window.ShowRenderDiagnosticsOverlay);
        window.GlobalKeyEvent += (keyCode, action) =>
        {
            if (action != KeyAction.Down || keyCode != f12) return;

            window.ShowRenderDiagnosticsOverlay = !window.ShowRenderDiagnosticsOverlay;
            UpdateDebugTitle(window, window.ShowRenderDiagnosticsOverlay);
            window.RequestRender();
        };

        static void UpdateDebugTitle(AppWindow window, bool overlayVisible)
        {
            window.Title = $"{baseTitle} - Overlay: {(overlayVisible ? "On" : "Off")}";
        }
#endif
    }

    private static bool HasOption(string[] args, string name)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return args[i][(name.Length + 1)..];
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];
        }
        return null;
    }

    private static bool TryParseBool(string? value, out bool result)
    {
        result = false;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (bool.TryParse(value, out result)) return true;
        if (value == "1" || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }
        if (value == "0" || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }
        return false;
    }

}
