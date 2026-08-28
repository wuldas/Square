using System.Text.Json;

namespace Square.FontComparison;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault() ?? "validate";
            return command.ToLowerInvariant() switch
            {
                "validate" => await ValidateAsync(),
                "capture-browser" => await CaptureBrowserAsync(GetOption(args, "--output") ?? "artifacts/font-comparison/chrome"),
                "capture-square" => await CaptureSquareAsync(
                    GetOption(args, "--backend") ?? "Software",
                    GetOption(args, "--output") ?? "artifacts/font-comparison/software"),
                "compare" => await CompareAsync(args),
                "compare-controls" => await CompareControlsAsync(args),
                "capture-controls-square" => await CaptureControlsSquareAsync(args),
                _ => throw new ArgumentException(
                    $"Unknown command '{command}'. Supported commands: validate, capture-browser, capture-square, compare, compare-controls, capture-controls-square.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<int> ValidateAsync()
    {
        var fonts = await ComparisonAssets.LoadAndRegisterFontsAsync();
        var cases = await ComparisonAssets.LoadCasesAsync();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            fonts = fonts.Fonts.Count,
            cases = cases.Cases.Count,
            supported = cases.Cases.Count(item => item.Category == "supported"),
            probes = cases.Cases.Count(item => item.Category == "probe")
        }));
        return 0;
    }

    private static async Task<int> CaptureBrowserAsync(string output)
    {
        var fonts = await ComparisonAssets.LoadAndRegisterFontsAsync();
        var cases = await ComparisonAssets.LoadCasesAsync();
        await BrowserCapture.CaptureAsync(fonts, cases, Path.GetFullPath(output));
        return 0;
    }

    private static async Task<int> CaptureSquareAsync(string backend, string output)
    {
        await ComparisonAssets.LoadAndRegisterFontsAsync();
        var cases = await ComparisonAssets.LoadCasesAsync();
        await SquareCapture.CaptureAsync(backend, cases, Path.GetFullPath(output));
        return 0;
    }

    private static async Task<int> CompareAsync(string[] args)
    {
        var output = Path.GetFullPath(GetOption(args, "--output") ?? "artifacts/font-comparison");
        var backends = (GetOption(args, "--backends") ?? "Software,Skia")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fonts = await ComparisonAssets.LoadAndRegisterFontsAsync();
        var cases = await ComparisonAssets.LoadCasesAsync();
        await BrowserCapture.CaptureAsync(fonts, cases, Path.Combine(output, "chrome"));
        foreach (var backend in backends)
            await RunSquareChildAsync(backend, Path.Combine(output, backend.ToLowerInvariant()));
        var report = await ComparisonEngine.CompareAsync(output, backends);
        Console.WriteLine(JsonSerializer.Serialize(report.Renderers.Select(renderer => new
        {
            renderer.Renderer,
            renderer.Passed,
            renderer.Failed,
            renderer.Probes
        })));
        return report.Renderers.Any(renderer => renderer.Failed > 0) ? 1 : 0;
    }

    private static async Task RunSquareChildAsync(string backend, string output)
    {
        var processPath = Environment.ProcessPath ?? "dotnet";
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false
        };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        startInfo.ArgumentList.Add("capture-square");
        startInfo.ArgumentList.Add("--backend");
        startInfo.ArgumentList.Add(backend);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(output);
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start Square {backend} capture process.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Square {backend} capture exited with code {process.ExitCode}.");
    }

    private static async Task<int> CompareControlsAsync(string[] args)
    {
        var phase = GetOption(args, "--phase") ?? "all";
        var manifest = Path.GetFullPath(GetOption(args, "--manifest")
            ?? Path.Combine(AppContext.BaseDirectory, "Cases", "ControlComparisonCases.json"));
        var output = Path.GetFullPath(GetOption(args, "--output") ?? "artifacts/control-comparison");
        var backends = (GetOption(args, "--backends") ?? "Software,Skia")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return await ControlComparisonRunner.RunAsync(phase, manifest, output, backends);
    }

    private static async Task<int> CaptureControlsSquareAsync(string[] args)
    {
        var backend = GetOption(args, "--backend") ?? "Software";
        var manifestPath = Path.GetFullPath(GetOption(args, "--manifest")
            ?? throw new ArgumentException("capture-controls-square requires --manifest."));
        var output = Path.GetFullPath(GetOption(args, "--output")
            ?? throw new ArgumentException("capture-controls-square requires --output."));
        var captureSession = GetOption(args, "--capture-session")
            ?? throw new ArgumentException("capture-controls-square requires --capture-session.");
        var manifest = await ControlReportIO.LoadManifestAsync(manifestPath);
        await ControlSquareCapture.CaptureAsync(backend, manifest, output, captureSession);
        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return argument[(name.Length + 1)..];
            if (argument.Equals(name, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                return args[index + 1];
        }
        return null;
    }
}
