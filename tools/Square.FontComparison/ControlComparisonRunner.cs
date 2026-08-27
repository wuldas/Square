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

        var html = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>Square control comparison</title>");
        html.Append("<style>body{font-family:sans-serif}table{border-collapse:collapse}td,th{padding:6px;border:1px solid #ccc}img{width:320px}</style>");
        html.Append("<h1>Control visual capture</h1><table><tr><th>Case</th><th>Chromium</th>");
        foreach (var backend in backends) html.Append("<th>").Append(WebUtility.HtmlEncode(backend)).Append("</th>");
        html.Append("</tr>");
        foreach (var item in reports[0].Cases)
        {
            html.Append("<tr><td>").Append(WebUtility.HtmlEncode(item.Id)).Append("</td>");
            html.Append("<td><img src=\"chrome/cases/").Append(WebUtility.HtmlEncode(item.Id)).Append(".png\"></td>");
            foreach (var backend in backends)
                html.Append("<td><img src=\"").Append(backend.ToLowerInvariant()).Append("/cases/")
                    .Append(WebUtility.HtmlEncode(item.Id)).Append(".png\"></td>");
            html.Append("</tr>");
        }
        html.Append("</table>");
        await File.WriteAllTextAsync(Path.Combine(output, "report.html"), html.ToString());
        await File.WriteAllTextAsync(Path.Combine(output, "visual.json"), JsonSerializer.Serialize(new
        {
            phase = "visual",
            geometryGate = "pass",
            comparison = "capture-only",
            note = "Region-aware visual thresholds are intentionally not inferred from raw RGBA equality.",
            cases = reports[0].Cases.Select(item => item.Id),
            backends
        }, ControlReportIO.JsonOptions));
        Console.WriteLine(JsonSerializer.Serialize(new { phase = "visual", geometryGate = "pass", report = Path.Combine(output, "report.html") }));
        return 0;
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
