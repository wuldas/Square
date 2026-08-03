using System.Text.Json.Serialization;

namespace Square.FontComparison;

internal sealed class FontManifest
{
    [JsonPropertyName("fonts")]
    public required List<FontFaceDefinition> Fonts { get; init; }
}

internal sealed class FontFaceDefinition
{
    [JsonPropertyName("family")]
    public required string Family { get; init; }

    [JsonPropertyName("file")]
    public required string File { get; init; }

    [JsonPropertyName("weight")]
    public int Weight { get; init; }

    [JsonPropertyName("style")]
    public required string Style { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }
}

internal sealed class FontComparisonManifest
{
    [JsonPropertyName("cases")]
    public required List<FontComparisonCase> Cases { get; init; }
}

internal sealed class FontComparisonCase
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("fontFamily")]
    public required string FontFamily { get; init; }

    [JsonPropertyName("fontSize")]
    public float FontSize { get; init; }

    [JsonPropertyName("fontWeight")]
    public int FontWeight { get; init; }

    [JsonPropertyName("fontStyle")]
    public required string FontStyle { get; init; }

    [JsonPropertyName("lineHeight")]
    public required string LineHeight { get; init; }

    [JsonPropertyName("textAlign")]
    public required string TextAlign { get; init; }

    [JsonPropertyName("width")]
    public float? Width { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

internal sealed class CaptureReport
{
    public required string Renderer { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required List<CaseCapture> Cases { get; init; }
}

internal sealed class CaseCapture
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string FontFamily { get; init; }
    public required float FontSize { get; init; }
    public required int FontWeight { get; init; }
    public required string FontStyle { get; init; }
    public required string LineHeight { get; init; }
    public required string TextAlign { get; init; }
    public required float Width { get; init; }
    public required float Height { get; init; }
    public required float Baseline { get; init; }
    public required float Ascent { get; init; }
    public required float Descent { get; init; }
    public required List<CharacterCapture> Characters { get; init; }
    public required string Screenshot { get; init; }
}

internal sealed class CharacterCapture
{
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
}

internal sealed class ComparisonReport
{
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string ChromiumVersion { get; init; }
    public required List<RendererComparison> Renderers { get; init; }
}

internal sealed class RendererComparison
{
    public required string Renderer { get; init; }
    public required int Passed { get; init; }
    public required int Failed { get; init; }
    public required int Probes { get; init; }
    public required List<CaseComparison> Cases { get; init; }
}

internal sealed class CaseComparison
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Status { get; init; }
    public required float WidthDelta { get; init; }
    public required float HeightDelta { get; init; }
    public required float BaselineDelta { get; init; }
    public required float MaxCharacterXDelta { get; init; }
    public required int ChromiumCharacterCount { get; init; }
    public required int SquareCharacterCount { get; init; }
    public required string[] Failures { get; init; }
    public required string ChromiumScreenshot { get; init; }
    public required string SquareScreenshot { get; init; }
    public required string DiffScreenshot { get; init; }
    public required float MaskIoU { get; init; }
    public required float MeanInkDelta { get; init; }
    public required float HighDeltaRatio { get; init; }
}
