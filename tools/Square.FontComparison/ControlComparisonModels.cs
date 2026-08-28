using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Square.FontComparison;

[JsonConverter(typeof(JsonStringEnumConverter<ControlKind>))]
public enum ControlKind { Button, Input, TextArea, Select, CheckBox, Radio }

[JsonConverter(typeof(JsonStringEnumConverter<ControlAppearance>))]
public enum ControlAppearance { Auto, None }

[JsonConverter(typeof(JsonStringEnumConverter<ControlState>))]
public enum ControlState { Normal, Hover, Active, Focus, Disabled, Value, Placeholder, Unchecked, Checked, Open }

public sealed class ControlComparisonManifest
{
    public const string ButtonAppearanceAutoCss = "font: 13.3333px Arial;";
    public const string AppearanceNoneCss =
        "appearance: none; box-sizing: border-box; margin: 0; width: 180px; height: 36px; " +
        "padding: 6px 10px; border: 2px solid #345678; border-radius: 4px; " +
        "background: #e8eef4; color: #102030; font: 14px Arial;";
    public const string AppearanceNoneFocusCss =
        " outline: 1px solid Highlight; outline-offset: 0;";

    public string AppearanceNoneAuthorCss { get; init; } = AppearanceNoneCss;
    public required List<ControlDefinition> Controls { get; init; }

    public static ControlComparisonManifest CreateDefault() => new()
    {
        Controls =
        [
            Define(ControlKind.Button, ControlState.Normal, ControlState.Hover, ControlState.Active, ControlState.Focus, ControlState.Disabled),
            Define(ControlKind.Input, ControlState.Normal, ControlState.Hover, ControlState.Focus, ControlState.Disabled, ControlState.Value, ControlState.Placeholder),
            Define(ControlKind.TextArea, ControlState.Normal, ControlState.Hover, ControlState.Focus, ControlState.Disabled, ControlState.Value, ControlState.Placeholder),
            Define(ControlKind.Select, ControlState.Normal, ControlState.Hover, ControlState.Focus, ControlState.Disabled),
            Define(ControlKind.CheckBox, ControlState.Unchecked, ControlState.Checked, ControlState.Hover, ControlState.Active, ControlState.Focus, ControlState.Disabled),
            Define(ControlKind.Radio, ControlState.Unchecked, ControlState.Checked, ControlState.Hover, ControlState.Active, ControlState.Focus, ControlState.Disabled)
        ]
    };

    public static ControlComparisonManifest CreateSmoke() => new()
    {
        Controls = Enum.GetValues<ControlKind>()
            .Select(kind => Define(kind, ControlState.Normal))
            .ToList()
    };

    public IReadOnlyList<ControlComparisonCase> ExpandCases() => Controls
        .SelectMany(control => control.Appearances.SelectMany(appearance => control.States.Select(state =>
            new ControlComparisonCase
            {
                Id = $"{ToId(control.Kind)}-{appearance.ToString().ToLowerInvariant()}-{state.ToString().ToLowerInvariant()}",
                Kind = control.Kind,
                Element = control.Element,
                Appearance = appearance,
                State = state,
                Text = control.Text,
                Value = control.Value,
                Placeholder = control.Placeholder,
                AuthorCss = appearance == ControlAppearance.None
                    ? AppearanceNoneAuthorCss + (state == ControlState.Focus ? AppearanceNoneFocusCss : "")
                    : control.AutoAuthorCss
            })))
        .ToArray();

    public string ComputeFingerprint()
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static ControlDefinition Define(ControlKind kind, params ControlState[] states) => new()
    {
        Kind = kind,
        Element = kind switch
        {
            ControlKind.TextArea => "textarea",
            ControlKind.CheckBox or ControlKind.Radio or ControlKind.Input => "input",
            _ => kind.ToString().ToLowerInvariant()
        },
        Appearances = [ControlAppearance.Auto, ControlAppearance.None],
        States = states.ToList(),
        AutoAuthorCss = kind switch
        {
            ControlKind.Button => ButtonAppearanceAutoCss,
            ControlKind.TextArea => "box-sizing: border-box; width: 168px;",
            _ => ""
        },
        Text = kind is ControlKind.Button or ControlKind.CheckBox or ControlKind.Radio ? "Control" : "",
        Value = kind == ControlKind.TextArea ? "Line one\nLine two" :
            kind is ControlKind.Input or ControlKind.Select ? "Value" : "",
        Placeholder = kind is ControlKind.Input or ControlKind.TextArea ? "Placeholder" : ""
    };

    private static string ToId(ControlKind kind) => kind switch
    {
        ControlKind.TextArea => "textarea",
        ControlKind.CheckBox => "checkbox",
        _ => kind.ToString().ToLowerInvariant()
    };
}

public sealed class ControlDefinition
{
    public required ControlKind Kind { get; init; }
    public required string Element { get; init; }
    public required List<ControlAppearance> Appearances { get; init; }
    public required List<ControlState> States { get; init; }
    public string AutoAuthorCss { get; init; } = "";
    public string Text { get; init; } = "";
    public string Value { get; init; } = "";
    public string Placeholder { get; init; } = "";
}

public sealed class ControlComparisonCase
{
    public required string Id { get; init; }
    public required ControlKind Kind { get; init; }
    public required string Element { get; init; }
    public required ControlAppearance Appearance { get; init; }
    public required ControlState State { get; init; }
    public required string Text { get; init; }
    public required string Value { get; init; }
    public required string Placeholder { get; init; }
    public required string AuthorCss { get; init; }
    [JsonIgnore] public string ChromiumAuthorCss => AuthorCss;
    [JsonIgnore] public string SquareAuthorCss => AuthorCss;
}

public sealed record ControlBrowserCaseConfig(
    string element,
    string kind,
    string state,
    string text,
    string value,
    string placeholder,
    string authorCss)
{
    public static ControlBrowserCaseConfig Create(ControlComparisonCase item) => new(
        item.Element,
        item.Kind.ToString(),
        item.State.ToString(),
        item.Text,
        item.Value,
        item.Placeholder,
        item.AuthorCss);
}

public sealed record ControlArtifactPaths(string Metrics, string Screenshot)
{
    public static ControlArtifactPaths For(string root, string renderer, string caseId)
    {
        var rendererDirectory = Path.Combine(root, renderer.ToLowerInvariant());
        return new(
            Path.Combine(rendererDirectory, "geometry.json"),
            Path.Combine(rendererDirectory, "cases", caseId + ".png"));
    }
}

public static class ControlArtifactIdentity
{
    public static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string ComputeBuildFingerprint()
    {
        var components = new[]
        {
            typeof(ControlArtifactIdentity).Assembly,
            typeof(Square.UI.Element).Assembly,
            typeof(Square.Backends.RenderBackendFactory).Assembly,
            typeof(Square.Backends.Skia.SkiaBackendFactory).Assembly,
            typeof(Square.Backends.Vulkan.VulkanBackendFactory).Assembly
        };
        var identity = string.Join('\n', components
            .DistinctBy(assembly => assembly.FullName)
            .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .Select(assembly => $"{assembly.GetName().Name}:{ComputeFileSha256(assembly.Location)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    public static string ResolveScreenshotPath(
        string artifactRoot,
        string renderer,
        string caseId,
        string screenshot)
    {
        if (string.IsNullOrWhiteSpace(screenshot) || Path.IsPathRooted(screenshot) ||
            caseId.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            throw new InvalidOperationException($"{renderer} geometry screenshot path is invalid for '{caseId}'.");
        var normalized = screenshot.Replace('\\', '/');
        var expected = $"cases/{caseId}.png";
        if (!string.Equals(normalized, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"{renderer} geometry screenshot path is invalid for '{caseId}'.");

        var rendererRoot = Path.GetFullPath(Path.Combine(artifactRoot, ArtifactDirectory(renderer)));
        var casesRoot = Path.GetFullPath(Path.Combine(rendererRoot, "cases"));
        var fullPath = Path.GetFullPath(Path.Combine(rendererRoot, screenshot));
        RejectReparsePoint(Path.GetFullPath(artifactRoot), renderer, caseId);
        RejectReparsePoint(rendererRoot, renderer, caseId);
        RejectReparsePoint(casesRoot, renderer, caseId);
        RejectReparsePoint(fullPath, renderer, caseId);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(fullPath), casesRoot, comparison) ||
            !string.Equals(Path.GetFileName(fullPath), caseId + ".png", comparison))
            throw new InvalidOperationException($"{renderer} geometry screenshot path escapes its cases directory for '{caseId}'.");
        return fullPath;
    }

    public static string ResolveCaptureScreenshotPath(
        string outputDirectory,
        string renderer,
        string caseId,
        string screenshot)
    {
        if (string.IsNullOrWhiteSpace(screenshot) || Path.IsPathRooted(screenshot) ||
            !string.Equals(screenshot.Replace('\\', '/'), $"cases/{caseId}.png", StringComparison.Ordinal))
            throw new InvalidOperationException($"{renderer} geometry screenshot path is invalid for '{caseId}'.");
        var rendererRoot = Path.GetFullPath(outputDirectory);
        var root = Path.GetDirectoryName(rendererRoot)
            ?? throw new InvalidOperationException($"{renderer} output directory has no artifact root.");
        var casesRoot = Path.GetFullPath(Path.Combine(rendererRoot, "cases"));
        var fullPath = Path.GetFullPath(Path.Combine(rendererRoot, screenshot));
        RejectReparsePoint(root, renderer, "capture root");
        RejectReparsePoint(rendererRoot, renderer, caseId);
        RejectReparsePoint(casesRoot, renderer, caseId);
        RejectReparsePoint(fullPath, renderer, caseId);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(fullPath), casesRoot, comparison))
            throw new InvalidOperationException($"{renderer} geometry screenshot path escapes its cases directory for '{caseId}'.");
        return fullPath;
    }

    public static void EnsureCaptureDirectory(string outputDirectory, string renderer)
    {
        var rendererRoot = Path.GetFullPath(outputDirectory);
        var root = Path.GetDirectoryName(rendererRoot)
            ?? throw new InvalidOperationException($"{renderer} output directory has no artifact root.");
        var casesRoot = Path.GetFullPath(Path.Combine(rendererRoot, "cases"));
        RejectReparsePoint(root, renderer, "capture root");
        Directory.CreateDirectory(root);
        RejectReparsePoint(root, renderer, "capture root");
        RejectReparsePoint(rendererRoot, renderer, "capture root");
        Directory.CreateDirectory(rendererRoot);
        RejectReparsePoint(rendererRoot, renderer, "capture root");
        RejectReparsePoint(casesRoot, renderer, "capture root");
        Directory.CreateDirectory(casesRoot);
        RejectReparsePoint(casesRoot, renderer, "capture root");
    }

    private static void RejectReparsePoint(string path, string renderer, string caseId)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException(
                $"{renderer} geometry screenshot path contains a reparse point for '{caseId}'.");
    }

    public static string ArtifactDirectory(string renderer) => renderer.ToLowerInvariant() switch
    {
        "chromium" => "chrome",
        "software" => "software",
        "skia" => "skia",
        "vulkan" => "vulkan",
        _ => throw new InvalidOperationException($"Unsupported control comparison renderer '{renderer}'.")
    };

    public static string CanonicalBackend(string backend) => backend.ToLowerInvariant() switch
    {
        "software" => "Software",
        "skia" => "Skia",
        "vulkan" => "Vulkan",
        _ => throw new InvalidOperationException($"Unsupported control comparison renderer '{backend}'.")
    };
}

public sealed class ControlGeometryReport
{
    public required string Renderer { get; init; }
    public string ManifestFingerprint { get; init; } = "";
    public string BuildFingerprint { get; init; } = "";
    public string CaptureSession { get; init; } = "";
    public string Version { get; init; } = "unknown";
    public DateTimeOffset CapturedAt { get; init; }
    public required List<ControlGeometryCaseResult> Cases { get; init; }
}

public sealed class ControlGeometryCaseResult
{
    public required string Id { get; init; }
    public required bool Passed { get; init; }
    public ControlKind Kind { get; init; }
    public ControlAppearance Appearance { get; init; }
    public ControlState State { get; init; }
    public ControlRect BorderBox { get; init; }
    public ControlRect ContentBox { get; init; }
    public ControlEdges Padding { get; init; }
    public ControlEdges Border { get; init; }
    public Dictionary<string, string> ComputedStyles { get; init; } = [];
    public string Screenshot { get; init; } = "";
    public string ScreenshotSha256 { get; init; } = "";
    public string[] Failures { get; init; } = [];
}

public readonly record struct ControlRect(float X, float Y, float Width, float Height);
public readonly record struct ControlEdges(float Top, float Right, float Bottom, float Left);

public static class ControlGeometryComparer
{
    public static ControlGeometryReport Compare(
        ControlGeometryReport chromium,
        ControlGeometryReport square,
        float tolerance = 0.5f)
    {
        var expected = chromium.Cases.ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (!string.Equals(chromium.ManifestFingerprint, square.ManifestFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{square.Renderer} geometry manifest fingerprint does not match Chromium.");
        var actualIds = square.Cases.Select(item => item.Id).ToArray();
        if (actualIds.Distinct(StringComparer.Ordinal).Count() != actualIds.Length)
            throw new InvalidOperationException($"{square.Renderer} geometry contains duplicate case IDs.");
        var missing = expected.Keys.Except(actualIds, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException(
                $"{square.Renderer} geometry is missing cases: {string.Join(", ", missing)}");
        var cases = new List<ControlGeometryCaseResult>();
        foreach (var actual in square.Cases)
        {
            if (!expected.TryGetValue(actual.Id, out var baseline))
                throw new InvalidOperationException($"Chromium geometry is missing case '{actual.Id}'.");
            var failures = new List<string>();
            CompareRect("border-box", baseline.BorderBox, actual.BorderBox, tolerance, failures);
            CompareRect("content-box", baseline.ContentBox, actual.ContentBox, tolerance, failures);
            cases.Add(new ControlGeometryCaseResult
            {
                Id = actual.Id,
                Kind = actual.Kind,
                Appearance = actual.Appearance,
                State = actual.State,
                Passed = failures.Count == 0,
                BorderBox = actual.BorderBox,
                ContentBox = actual.ContentBox,
                Padding = actual.Padding,
                Border = actual.Border,
                ComputedStyles = actual.ComputedStyles,
                Screenshot = actual.Screenshot,
                ScreenshotSha256 = actual.ScreenshotSha256,
                Failures = failures.ToArray()
            });
        }
        return new ControlGeometryReport
        {
            Renderer = square.Renderer,
            ManifestFingerprint = square.ManifestFingerprint,
            BuildFingerprint = square.BuildFingerprint,
            CaptureSession = square.CaptureSession,
            Version = square.Version,
            CapturedAt = square.CapturedAt,
            Cases = cases
        };
    }

    private static void CompareRect(
        string name, ControlRect expected, ControlRect actual, float tolerance, List<string> failures)
    {
        CompareValue(name + " x", expected.X, actual.X, tolerance, failures);
        CompareValue(name + " y", expected.Y, actual.Y, tolerance, failures);
        CompareValue(name + " width", expected.Width, actual.Width, tolerance, failures);
        CompareValue(name + " height", expected.Height, actual.Height, tolerance, failures);
    }

    private static void CompareValue(string name, float expected, float actual, float tolerance, List<string> failures)
    {
        var delta = Math.Abs(actual - expected);
        if (delta > tolerance)
            failures.Add($"{name} {delta:0.###}px ({actual:0.###}/{expected:0.###})");
    }
}

public static class ControlGeometryGate
{
    public static void EnsureVisualAllowed(
        IEnumerable<ControlGeometryReport> reports,
        IEnumerable<string> requiredCaseIds,
        string manifestFingerprint,
        string? artifactRoot = null,
        IEnumerable<string>? requiredRenderers = null)
    {
        var materialized = reports.ToArray();
        if (materialized.Length == 0)
            throw new InvalidOperationException("Visual comparison requires at least one geometry report.");
        var rendererNames = materialized.Select(report => report.Renderer).ToArray();
        if (rendererNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != rendererNames.Length)
            throw new InvalidOperationException("Geometry gate contains duplicate renderer reports.");
        var missingRenderers = (requiredRenderers ?? []).Except(rendererNames, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missingRenderers.Length != 0)
            throw new InvalidOperationException(
                $"Geometry gate is missing blocking renderers: {string.Join(", ", missingRenderers)}.");
        if (artifactRoot != null)
        {
            var sessions = materialized.Select(report => report.CaptureSession)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (sessions.Length != 1 || string.IsNullOrWhiteSpace(sessions[0]))
                throw new InvalidOperationException("Geometry reports do not share one valid capture session.");
            var earliest = materialized.Min(report => report.CapturedAt);
            var latest = materialized.Max(report => report.CapturedAt);
            var now = DateTimeOffset.UtcNow;
            if (earliest == default || latest - earliest > TimeSpan.FromMinutes(10) ||
                earliest < now.AddMinutes(-10) || latest > now.AddMinutes(1))
                throw new InvalidOperationException("Geometry reports have invalid or inconsistent capture timestamps.");
        }
        var required = requiredCaseIds.ToArray();
        if (required.Length == 0)
            throw new InvalidOperationException("Visual comparison requires at least one manifest case.");
        var duplicateRequired = required.GroupBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRequired != null)
            throw new InvalidOperationException($"Control manifest contains duplicate case ID '{duplicateRequired.Key}'.");
        foreach (var report in materialized)
        {
            if (!string.Equals(report.ManifestFingerprint, manifestFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{report.Renderer} geometry manifest fingerprint is stale or mismatched.");
            if (artifactRoot != null && !string.Equals(
                    report.BuildFingerprint,
                    ControlArtifactIdentity.ComputeBuildFingerprint(),
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{report.Renderer} geometry build fingerprint is stale or mismatched.");
            var actual = report.Cases.Select(item => item.Id).ToArray();
            if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length)
                throw new InvalidOperationException($"{report.Renderer} geometry contains duplicate case IDs.");
            var missing = required.Except(actual, StringComparer.Ordinal).ToArray();
            var extra = actual.Except(required, StringComparer.Ordinal).ToArray();
            if (missing.Length != 0 || extra.Length != 0)
                throw new InvalidOperationException(
                    $"{report.Renderer} geometry does not match the selected manifest. " +
                    $"Missing: {string.Join(", ", missing)}; Extra: {string.Join(", ", extra)}");
            if (artifactRoot != null)
            {
                foreach (var item in report.Cases)
                {
                    var screenshotPath = ControlArtifactIdentity.ResolveScreenshotPath(
                        artifactRoot, report.Renderer, item.Id, item.Screenshot);
                    if (string.IsNullOrWhiteSpace(item.Screenshot) || !File.Exists(screenshotPath))
                        throw new InvalidOperationException(
                            $"{report.Renderer} geometry screenshot is missing for '{item.Id}'.");
                    if (string.IsNullOrWhiteSpace(item.ScreenshotSha256))
                        throw new InvalidOperationException(
                            $"{report.Renderer} geometry screenshot hash is missing for '{item.Id}'.");
                    var actualHash = ControlArtifactIdentity.ComputeFileSha256(screenshotPath);
                    if (!string.Equals(item.ScreenshotSha256, actualHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"{report.Renderer} geometry screenshot hash is stale or mismatched for '{item.Id}'.");
                }
            }
        }
        var failed = materialized.SelectMany(report => report.Cases
            .Where(item => !item.Passed)
            .Select(item => $"{report.Renderer}/{item.Id}"))
            .ToArray();
        if (failed.Length != 0)
            throw new InvalidOperationException("Visual comparison requires a passing geometry gate. Failed: " + string.Join(", ", failed));
    }

}

public static class ControlGeometryMatrix
{
    public static string CreateMarkdown(
        IEnumerable<ControlComparisonCase> cases,
        IEnumerable<ControlGeometryReport> reports)
    {
        var materializedCases = cases.ToArray();
        var materializedReports = reports.ToArray();
        var builder = new StringBuilder("| Control | Appearance | Cases |");
        foreach (var report in materializedReports)
            builder.Append(' ').Append(report.Renderer).Append(" Renderer |");
        builder.AppendLine();
        builder.Append("|---|---:|---:|");
        foreach (var _ in materializedReports) builder.Append("---:|");
        builder.AppendLine();

        foreach (var group in materializedCases.GroupBy(item => (item.Kind, item.Appearance)))
        {
            var ids = group.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            builder.Append("| ").Append(group.Key.Kind)
                .Append(" | ").Append(group.Key.Appearance)
                .Append(" | ").Append(ids.Count).Append(" |");
            foreach (var report in materializedReports)
            {
                var matching = report.Cases.Where(item => ids.Contains(item.Id)).ToArray();
                builder.Append(' ').Append(matching.Count(item => item.Passed))
                    .Append('/').Append(ids.Count).Append(" |");
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }
}