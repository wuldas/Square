using System.Text.Json;
using SkiaSharp;

namespace Square.FontComparison;

internal static class ComparisonEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<ComparisonReport> CompareAsync(string outputDirectory, IEnumerable<string> renderers)
    {
        var chromium = await ReadCaptureAsync(Path.Combine(outputDirectory, "chrome", "metrics.json"));
        var comparisons = new List<RendererComparison>();
        foreach (var renderer in renderers)
        {
            var square = await ReadCaptureAsync(Path.Combine(outputDirectory, renderer.ToLowerInvariant(), "metrics.json"));
            comparisons.Add(CompareRenderer(outputDirectory, chromium, square, renderer));
        }

        var report = new ComparisonReport
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            ChromiumVersion = chromium.Version,
            Renderers = comparisons
        };
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "report.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        ReportWriter.Write(outputDirectory, report);
        return report;
    }

    private static RendererComparison CompareRenderer(
        string outputDirectory,
        CaptureReport chromium,
        CaptureReport square,
        string renderer)
    {
        var chromiumById = chromium.Cases.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var cases = new List<CaseComparison>();
        foreach (var actual in square.Cases)
        {
            if (!chromiumById.TryGetValue(actual.Id, out var expected))
                throw new InvalidOperationException($"Chromium capture is missing case '{actual.Id}'.");

            var widthDelta = Math.Abs(actual.Width - expected.Width);
            var heightDelta = Math.Abs(actual.Height - expected.Height);
            var baselineDelta = Math.Abs(actual.Baseline - expected.Baseline);
            var expectedCharacters = expected.Characters.Where(character => character.Width > 0.001f).ToArray();
            var characterCount = Math.Min(actual.Characters.Count, expectedCharacters.Length);
            var maxCharacterXDelta = 0f;
            for (var index = 0; index < characterCount; index++)
                maxCharacterXDelta = Math.Max(
                    maxCharacterXDelta,
                    Math.Abs(actual.Characters[index].X - expectedCharacters[index].X));

            var failures = new List<string>();
            if (widthDelta > 0.5f) failures.Add($"width {widthDelta:0.###}px");
            if (heightDelta > 0.5f) failures.Add($"height {heightDelta:0.###}px");
            if (baselineDelta > 0.5f) failures.Add($"baseline {baselineDelta:0.###}px");
            if (actual.Characters.Count != expectedCharacters.Length)
                failures.Add($"character count {actual.Characters.Count}/{expectedCharacters.Length}");
            if (maxCharacterXDelta > 0.5f) failures.Add($"character x {maxCharacterXDelta:0.###}px");

            var diffDirectory = Path.Combine(outputDirectory, "diff", renderer.ToLowerInvariant());
            Directory.CreateDirectory(diffDirectory);
            var diffName = actual.Id + ".png";
            var pixels = ComparePixels(
                Path.Combine(outputDirectory, "chrome", expected.Screenshot.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(outputDirectory, renderer.ToLowerInvariant(), actual.Screenshot.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(diffDirectory, diffName));
            var isSkia = renderer.Equals("Skia", StringComparison.OrdinalIgnoreCase);
            var minimumIoU = isSkia ? 0.995f : 0.78f;
            var maximumMeanDelta = isSkia ? 35f : 90f;
            var maximumHighDeltaRatio = isSkia ? 0.18f : 0.42f;
            if (pixels.MaskIoU < minimumIoU) failures.Add($"mask IoU {pixels.MaskIoU:0.####}");
            if (pixels.MeanInkDelta > maximumMeanDelta) failures.Add($"mean ink delta {pixels.MeanInkDelta:0.###}");
            if (pixels.HighDeltaRatio > maximumHighDeltaRatio)
                failures.Add($"high delta ratio {pixels.HighDeltaRatio:P2}");

            var isProbe = actual.Category.Equals("probe", StringComparison.OrdinalIgnoreCase);
            cases.Add(new CaseComparison
            {
                Id = actual.Id,
                Category = actual.Category,
                Status = isProbe ? "probe" : failures.Count == 0 ? "pass" : "fail",
                WidthDelta = widthDelta,
                HeightDelta = heightDelta,
                BaselineDelta = baselineDelta,
                MaxCharacterXDelta = maxCharacterXDelta,
                ChromiumCharacterCount = expectedCharacters.Length,
                SquareCharacterCount = actual.Characters.Count,
                Failures = failures.ToArray(),
                ChromiumScreenshot = $"chrome/{expected.Screenshot}",
                SquareScreenshot = $"{renderer.ToLowerInvariant()}/{actual.Screenshot}",
                DiffScreenshot = $"diff/{renderer.ToLowerInvariant()}/{diffName}",
                MaskIoU = pixels.MaskIoU,
                MeanInkDelta = pixels.MeanInkDelta,
                HighDeltaRatio = pixels.HighDeltaRatio
            });
        }

        return new RendererComparison
        {
            Renderer = renderer,
            Passed = cases.Count(item => item.Status == "pass"),
            Failed = cases.Count(item => item.Status == "fail"),
            Probes = cases.Count(item => item.Status == "probe"),
            Cases = cases
        };
    }

    private static PixelComparison ComparePixels(string expectedPath, string actualPath, string diffPath)
    {
        using var expected = SKBitmap.Decode(expectedPath)
            ?? throw new InvalidOperationException($"Unable to decode '{expectedPath}'.");
        using var actual = SKBitmap.Decode(actualPath)
            ?? throw new InvalidOperationException($"Unable to decode '{actualPath}'.");
        var width = Math.Max(expected.Width, actual.Width);
        var height = Math.Max(expected.Height, actual.Height);
        using var diff = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var expectedMask = new bool[width * height];
        var actualMask = new bool[width * height];
        var expectedInkValues = new int[width * height];
        var actualInkValues = new int[width * height];
        long expectedCount = 0;
        long actualCount = 0;
        long totalDelta = 0;
        long highDelta = 0;
        long coverageSamples = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var expectedColor = x < expected.Width && y < expected.Height ? expected.GetPixel(x, y) : SKColors.White;
                var actualColor = x < actual.Width && y < actual.Height ? actual.GetPixel(x, y) : SKColors.White;
                var expectedInk = Ink(expectedColor);
                var actualInk = Ink(actualColor);
                var index = y * width + x;
                expectedInkValues[index] = expectedInk;
                actualInkValues[index] = actualInk;
                expectedMask[index] = expectedInk >= 12;
                actualMask[index] = actualInk >= 12;
                if (expectedMask[index]) expectedCount++;
                if (actualMask[index]) actualCount++;
            }
        }

        long matchedExpected = 0;
        long matchedActual = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (expectedMask[index] && HasNeighbor(actualMask, width, height, x, y)) matchedExpected++;
                if (actualMask[index] && HasNeighbor(expectedMask, width, height, x, y)) matchedActual++;

                if (expectedMask[index])
                {
                    var delta = NearestInkDelta(actualInkValues, actualMask, width, height, x, y, expectedInkValues[index]);
                    totalDelta += delta;
                    if (delta > 64) highDelta++;
                    coverageSamples++;
                }
                if (actualMask[index])
                {
                    var delta = NearestInkDelta(expectedInkValues, expectedMask, width, height, x, y, actualInkValues[index]);
                    totalDelta += delta;
                    if (delta > 64) highDelta++;
                    coverageSamples++;
                }

                var expectedInk = expectedInkValues[index];
                var actualInk = actualInkValues[index];
                if (expectedMask[index] || actualMask[index])
                {
                    var delta = Math.Abs(expectedInk - actualInk);
                    diff.SetPixel(x, y, HeatColor(delta, expectedMask[index], actualMask[index]));
                }
                else
                {
                    diff.SetPixel(x, y, new SKColor(12, 15, 20));
                }
            }
        }

        using var image = SKImage.FromBitmap(diff);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(diffPath);
        data.SaveTo(stream);
        var shapeScore = expectedCount + actualCount == 0
            ? 1
            : (float)(matchedExpected + matchedActual) / (expectedCount + actualCount);
        return new PixelComparison(
            shapeScore,
            coverageSamples == 0 ? 0 : (float)totalDelta / coverageSamples,
            coverageSamples == 0 ? 0 : (float)highDelta / coverageSamples);
    }

    private static int NearestInkDelta(
        int[] ink,
        bool[] mask,
        int width,
        int height,
        int x,
        int y,
        int targetInk)
    {
        var best = 255;
        for (var dy = -1; dy <= 1; dy++)
        {
            var candidateY = y + dy;
            if ((uint)candidateY >= (uint)height) continue;
            for (var dx = -1; dx <= 1; dx++)
            {
                var candidateX = x + dx;
                if ((uint)candidateX >= (uint)width) continue;
                var index = candidateY * width + candidateX;
                if (!mask[index]) continue;
                best = Math.Min(best, Math.Abs(targetInk - ink[index]));
            }
        }
        return best;
    }

    private static bool HasNeighbor(bool[] mask, int width, int height, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            var candidateY = y + dy;
            if ((uint)candidateY >= (uint)height) continue;
            for (var dx = -1; dx <= 1; dx++)
            {
                var candidateX = x + dx;
                if ((uint)candidateX >= (uint)width) continue;
                if (mask[candidateY * width + candidateX]) return true;
            }
        }
        return false;
    }

    private static int Ink(SKColor color)
        => 255 - (color.Red * 299 + color.Green * 587 + color.Blue * 114) / 1000;

    private static SKColor HeatColor(int delta, bool expectedMask, bool actualMask)
    {
        var intensity = (byte)Math.Clamp(48 + delta * 4, 0, 255);
        if (expectedMask && !actualMask) return new SKColor(intensity, 45, 55);
        if (!expectedMask && actualMask) return new SKColor(35, 125, intensity);
        return new SKColor(intensity, intensity, 35);
    }

    private readonly record struct PixelComparison(float MaskIoU, float MeanInkDelta, float HighDeltaRatio);

    private static async Task<CaptureReport> ReadCaptureAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CaptureReport>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Capture report '{path}' is empty.");
    }
}
