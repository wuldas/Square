using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Square.FontComparison;

internal static class ControlReportIO
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static async Task<ControlComparisonManifest> LoadManifestAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ControlComparisonManifest>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Control manifest '{path}' is empty.");
    }

    public static async Task<ControlGeometryReport> ReadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ControlGeometryReport>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Control geometry report '{path}' is empty.");
    }

    public static async Task WriteAsync(string path, ControlGeometryReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, JsonOptions));
    }
}

internal static class ControlComparisonRunner
{
    public static async Task<int> RunAsync(string phase, string manifestPath, string output, string[] backends)
    {
        if (phase.Equals("geometry", StringComparison.OrdinalIgnoreCase))
            return await RunGeometryAsync(manifestPath, output, backends);
        if (phase.Equals("visual", StringComparison.OrdinalIgnoreCase))
            return await RunVisualAsync(manifestPath, output, backends);
        throw new ArgumentException($"Unknown control comparison phase '{phase}'. Expected geometry or visual.");
    }

    private static async Task<int> RunGeometryAsync(string manifestPath, string output, string[] backends)
    {
        var manifest = await ControlReportIO.LoadManifestAsync(manifestPath);
        var cases = manifest.ExpandCases();
        if (cases.Count == 0) throw new InvalidOperationException("Control manifest contains no cases.");
        var chromium = await ControlBrowserCapture.CaptureAsync(manifest, Path.Combine(output, "chrome"));
        var reports = new List<ControlGeometryReport> { chromium };
        var failed = 0;
        foreach (var backend in backends)
        {
            await RunSquareChildAsync(backend, manifestPath, Path.Combine(output, backend.ToLowerInvariant()));
            var path = Path.Combine(output, backend.ToLowerInvariant(), "geometry.json");
            var captured = await ControlReportIO.ReadAsync(path);
            var compared = ControlGeometryComparer.Compare(chromium, captured);
            await ControlReportIO.WriteAsync(path, compared);
            reports.Add(compared);
            failed += compared.Cases.Count(item => !item.Passed);
        }
        var matrixPath = Path.Combine(output, "geometry-matrix.md");
        await File.WriteAllTextAsync(matrixPath, ControlGeometryMatrix.CreateMarkdown(cases, reports));
        ControlGeometryGate.EnsureVisualAllowed(
            reports,
            cases.Select(item => item.Id),
            manifest.ComputeFingerprint(),
            output,
            ["Chromium", .. backends]);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            phase = "geometry",
            cases = cases.Count,
            chromium = chromium.Cases.Count,
            renderers = backends.Select(backend => new
            {
                renderer = backend,
                metrics = Path.Combine(output, backend.ToLowerInvariant(), "geometry.json")
            }),
            matrix = matrixPath,
            failed
        }));
        return failed == 0 ? 0 : 1;
    }

    private static async Task<int> RunVisualAsync(string manifestPath, string output, string[] backends)
    {
        var manifest = await ControlReportIO.LoadManifestAsync(manifestPath);
        var requiredCaseIds = manifest.ExpandCases().Select(item => item.Id).ToArray();
        var manifestFingerprint = manifest.ComputeFingerprint();
        var reports = new List<ControlGeometryReport>
        {
            await ControlReportIO.ReadAsync(Path.Combine(output, "chrome", "geometry.json"))
        };
        foreach (var backend in backends)
            reports.Add(await ControlReportIO.ReadAsync(Path.Combine(output, backend.ToLowerInvariant(), "geometry.json")));
        ControlGeometryGate.EnsureVisualAllowed(
            reports,
            requiredCaseIds,
            manifestFingerprint,
            output,
            ["Chromium", .. backends]);

        var visualCases = reports[0].Cases
            .Where(item => item.Kind is ControlKind.Button or ControlKind.Input or ControlKind.TextArea or ControlKind.Select)
            .ToArray();
        var comparisons = new List<ControlVisualCaseResult>();
        foreach (var backend in backends)
        {
            var squareReport = reports.Single(report => report.Renderer.Equals(backend, StringComparison.OrdinalIgnoreCase));
            foreach (var chromiumCase in visualCases)
            {
                var squareCase = squareReport.Cases.Single(item => item.Id == chromiumCase.Id);
                var diffRelative = Path.Combine("diff", backend.ToLowerInvariant(), chromiumCase.Id + ".png");
                var chromiumPath = Path.Combine(output, "chrome", chromiumCase.Screenshot.Replace('/', Path.DirectorySeparatorChar));
                var squarePath = Path.Combine(output, backend.ToLowerInvariant(), squareCase.Screenshot.Replace('/', Path.DirectorySeparatorChar));
                var diffPath = Path.Combine(output, diffRelative);
                comparisons.Add(chromiumCase.Kind switch
                {
                    ControlKind.Button => ControlVisualComparer.CompareButton(
                        chromiumPath, squarePath, diffPath, chromiumCase.BorderBox,
                        ControlVisualThresholds.Button, chromiumCase.Id, backend),
                    ControlKind.Input => ControlVisualComparer.CompareInput(
                        chromiumPath, squarePath, diffPath, chromiumCase.BorderBox, squareCase.BorderBox, chromiumCase.State,
                        ControlVisualThresholds.Input, chromiumCase.Id, backend),
                    ControlKind.TextArea => ControlVisualComparer.CompareTextArea(
                        chromiumPath, squarePath, diffPath, chromiumCase.BorderBox, squareCase.BorderBox, chromiumCase.State,
                        ControlVisualThresholds.TextArea, chromiumCase.Id, backend),
                    ControlKind.Select => ControlVisualComparer.CompareSelect(
                        chromiumPath, squarePath, diffPath, chromiumCase.BorderBox, squareCase.BorderBox,
                        ControlVisualThresholds.Select, chromiumCase.Id, backend),
                    _ => throw new InvalidOperationException($"Unsupported visual control '{chromiumCase.Kind}'.")
                });
            }
        }

        var html = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>Square form-control visual comparison</title>");
        html.Append("<style>body{font-family:sans-serif}table{border-collapse:collapse}td,th{padding:6px;border:1px solid #ccc}img{width:320px}.fail{background:#fee}.pass{background:#efe}</style>");
        html.Append("<h1>Button, Input, TextArea and Select visual comparison</h1><p>Chromium is the before/baseline capture; Square is the after capture.</p>");
        html.Append("<table><tr><th>Case</th><th>Renderer</th><th>Status</th><th>Chromium before</th><th>Square after</th><th>Diff</th><th>Regions</th></tr>");
        foreach (var comparison in comparisons)
        {
            var rendererDirectory = comparison.Renderer.ToLowerInvariant();
            html.Append("<tr class=\"").Append(comparison.Passed ? "pass" : "fail").Append("\"><td>")
                .Append(WebUtility.HtmlEncode(comparison.Id)).Append("</td><td>")
                .Append(WebUtility.HtmlEncode(comparison.Renderer)).Append("</td><td>")
                .Append(comparison.Passed ? "pass" : "fail").Append("</td>");
            html.Append("<td><img src=\"chrome/cases/").Append(WebUtility.HtmlEncode(comparison.Id)).Append(".png\"></td>");
            html.Append("<td><img src=\"").Append(rendererDirectory).Append("/cases/")
                .Append(WebUtility.HtmlEncode(comparison.Id)).Append(".png\"></td>");
            html.Append("<td><img src=\"diff/").Append(rendererDirectory).Append('/')
                .Append(WebUtility.HtmlEncode(comparison.Id)).Append(".png\"></td><td><ul>");
            foreach (var region in comparison.Regions)
                html.Append("<li>").Append(WebUtility.HtmlEncode(region.Name)).Append(": ")
                    .Append(region.Passed ? "pass" : WebUtility.HtmlEncode(string.Join("; ", region.Failures)))
                    .Append("</li>");
            html.Append("</ul></td>");
            html.Append("</tr>");
        }
        html.Append("</table>");
        await File.WriteAllTextAsync(Path.Combine(output, "report.html"), html.ToString());
        await File.WriteAllTextAsync(Path.Combine(output, "visual.json"), JsonSerializer.Serialize(new
        {
            phase = "visual",
            geometryGate = "pass",
            controls = new[] { "Button", "Input", "TextArea", "Select" },
            thresholds = new { button = ControlVisualThresholds.Button, input = ControlVisualThresholds.Input, textArea = ControlVisualThresholds.TextArea, select = ControlVisualThresholds.Select },
            cases = comparisons,
            supported = new
            {
                button = new[] { "normal", "hover", "active", "focus", "disabled" },
                input = new[] { "normal", "hover", "focus", "disabled", "value", "placeholder" },
                textArea = new[] { "normal", "hover", "focus", "disabled", "value", "placeholder" },
                select = new[] { "normal", "hover", "focus", "disabled", "arrow" }
            },
            unsupported = new[] { "Select native popup/open capture is unsupported in headless Chromium." }
        }, ControlReportIO.JsonOptions));
        var failed = comparisons.Count(item => !item.Passed);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            phase = "visual",
            geometryGate = "pass",
            controls = new[] { "Button", "Input", "TextArea", "Select" },
            passed = comparisons.Count - failed,
            failed,
            report = Path.Combine(output, "report.html")
        }));
        return failed == 0 ? 0 : 1;
    }

    private static async Task RunSquareChildAsync(string backend, string manifestPath, string output)
    {
        var processPath = Environment.ProcessPath ?? "dotnet";
        var startInfo = new ProcessStartInfo { FileName = processPath, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        startInfo.ArgumentList.Add("capture-controls-square");
        startInfo.ArgumentList.Add("--backend");
        startInfo.ArgumentList.Add(backend);
        startInfo.ArgumentList.Add("--manifest");
        startInfo.ArgumentList.Add(manifestPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(output);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start Square {backend} control capture process.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Square {backend} control capture exited with code {process.ExitCode}.");
    }
}
