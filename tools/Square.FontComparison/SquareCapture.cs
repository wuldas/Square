using System.Text.Json;
using Square.Backends;
using Square.Backends.Skia;
using Square.Backends.Vulkan;
using Square.Graphics;
using Square.Graphics.Codecs;
#if PLATFORM_WIN32
using Square.Platform;
using Square.Platform.Win32;
#endif

namespace Square.FontComparison;

internal static class SquareCapture
{
    public static async Task<CaptureReport> CaptureAsync(
        string backend,
        FontComparisonManifest manifest,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var screenshotDirectory = Path.Combine(outputDirectory, "cases");
        Directory.CreateDirectory(screenshotDirectory);
        if (backend.Equals("Vulkan", StringComparison.OrdinalIgnoreCase))
            return await CaptureVulkanAsync(manifest, outputDirectory, screenshotDirectory);
        var factory = CreateFactory(backend);
        if (backend.Equals("Skia", StringComparison.OrdinalIgnoreCase))
        {
            using var registrationContext = factory.CreateContext(new RenderContextCreateInfo
            {
                CanvasSize = new Size(1, 1),
                DpiScale = 1
            });
        }
        var captures = new List<CaseCapture>();

        foreach (var item in manifest.Cases)
        {
            var font = new Font(
                item.FontFamily,
                item.FontSize,
                (FontWeight)item.FontWeight,
                ParseStyle(item.FontStyle));
            var lineHeight = ParseLineHeight(item.LineHeight, item.FontSize);
            var maxWidth = item.Width ?? float.MaxValue;
            var layout = new TextLayout(item.Text, font)
            {
                MaxSize = new Size(maxWidth, float.MaxValue),
                LineHeight = lineHeight / item.FontSize,
                Alignment = ParseAlignment(item.TextAlign)
            };
            var measured = layout.Measure();
            var width = item.Width ?? Math.Max(1, measured.Width);
            var height = Math.Max(1, measured.Height);
            using var context = factory.CreateContext(new RenderContextCreateInfo
            {
                CanvasSize = new Size(width, height),
                DpiScale = 1
            });
            context.Clear(Color.White);
            context.DrawText(layout, Point.Zero, Brush.FromColor(Color.Black));
            using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
            var screenshotName = item.Id + ".png";
            BitmapPngEncoder.Save(bitmap, Path.Combine(screenshotDirectory, screenshotName));

            var characters = new List<CharacterCapture>();
            var advances = new Dictionary<int, float>();
            var lines = TextWrapping.Wrap(item.Text, maxWidth, (offset, rune) =>
            {
                var advance = TextMetrics.GetGlyphMetrics(font, rune).AdvanceX;
                advances[offset] = advance;
                return advance;
            });
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                var x = GetTextAlignmentOffset(layout, line.Width);
                var y = lineIndex * lineHeight;
                for (var offset = line.StartOffset; offset < line.EndOffset;)
                {
                    var status = System.Text.Rune.DecodeFromUtf16(item.Text.AsSpan(offset), out var rune, out var consumed);
                    if (status != System.Buffers.OperationStatus.Done) break;
                    var advance = advances[offset];
                    characters.Add(new CharacterCapture
                    {
                        StartOffset = offset,
                        EndOffset = offset + consumed,
                        X = x,
                        Y = y,
                        Width = advance,
                        Height = lineHeight
                    });
                    x += advance;
                    offset += consumed;
                }
            }
            var metrics = TextMetrics.GetFontMetrics(font);
            captures.Add(new CaseCapture
            {
                Id = item.Id,
                Category = item.Category,
                FontFamily = font.Family,
                FontSize = item.FontSize,
                FontWeight = item.FontWeight,
                FontStyle = item.FontStyle,
                LineHeight = item.LineHeight,
                TextAlign = item.TextAlign,
                Width = width,
                Height = height,
                Baseline = TextMetrics.GetBaselineOffset(font, lineHeight),
                Ascent = -metrics.Ascent,
                Descent = metrics.Descent,
                Characters = characters,
                Screenshot = Path.Combine("cases", screenshotName).Replace('\\', '/')
            });
        }

        var report = new CaptureReport
        {
            Renderer = backend,
            Version = typeof(TextLayout).Assembly.GetName().Version?.ToString() ?? "unknown",
            CapturedAt = DateTimeOffset.UtcNow,
            Cases = captures
        };
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "metrics.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return report;
    }

    private static IRenderBackendFactory CreateFactory(string backend) => backend.ToLowerInvariant() switch
    {
        "software" => new RenderBackendFactory(),
        "skia" => new SkiaBackendFactory(),
        _ => throw new ArgumentException($"Unsupported headless backend '{backend}'.")
    };

    private static async Task<CaptureReport> CaptureVulkanAsync(
        FontComparisonManifest manifest,
        string outputDirectory,
        string screenshotDirectory)
    {
#if PLATFORM_WIN32
        if (!string.Equals(Environment.GetEnvironmentVariable("SQUARE_VULKAN_READBACK"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException("Vulkan font capture requires SQUARE_VULKAN_READBACK=1.");
        RenderBackendRegistry.Register(new VulkanBackendFactory());
        var captures = new List<CaseCapture>();
        foreach (var item in manifest.Cases)
        {
            var font = new Font(item.FontFamily, item.FontSize, (FontWeight)item.FontWeight, ParseStyle(item.FontStyle));
            var lineHeight = ParseLineHeight(item.LineHeight, item.FontSize);
            var maxWidth = item.Width ?? float.MaxValue;
            var layout = new TextLayout(item.Text, font)
            {
                MaxSize = new Size(maxWidth, float.MaxValue),
                LineHeight = lineHeight / item.FontSize,
                Alignment = ParseAlignment(item.TextAlign)
            };
            var measured = layout.Measure();
            var width = Math.Max(1, (int)MathF.Ceiling(item.Width ?? measured.Width));
            var height = Math.Max(1, (int)MathF.Ceiling(measured.Height));
            using var host = new Win32PlatformFactory().CreateHost(new PlatformHostCreateInfo
            {
                Title = "Square font Vulkan conformance",
                Width = width,
                Height = height,
                RenderBackend = "Vulkan"
            });
            host.Show();
            using var context = host.CreateRenderContext();
            context.Clear(Color.White);
            context.DrawText(layout, Point.Zero, Brush.FromColor(Color.Black));
            context.Present();
            host.ShowAfterFirstFrame();
            using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
            var screenshotName = item.Id + ".png";
            BitmapPngEncoder.Save(bitmap, Path.Combine(screenshotDirectory, screenshotName));
            captures.Add(CreateMetrics(item, layout, font, lineHeight, width, height, screenshotName));
            host.Close();
        }

        var report = new CaptureReport
        {
            Renderer = "Vulkan",
            Version = typeof(VulkanBackendFactory).Assembly.GetName().Version?.ToString() ?? "unknown",
            CapturedAt = DateTimeOffset.UtcNow,
            Cases = captures
        };
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "metrics.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return report;
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("Vulkan font capture is currently configured for Win32.");
#endif
    }

    private static CaseCapture CreateMetrics(
        FontComparisonCase item,
        TextLayout layout,
        Font font,
        float lineHeight,
        float width,
        float height,
        string screenshotName)
    {
        var characters = new List<CharacterCapture>();
        var advances = new Dictionary<int, float>();
        var lines = TextWrapping.Wrap(item.Text, layout.MaxSize.Width, (offset, rune) =>
        {
            var advance = TextMetrics.GetGlyphMetrics(font, rune).AdvanceX;
            advances[offset] = advance;
            return advance;
        });
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var x = GetTextAlignmentOffset(layout, line.Width);
            for (var offset = line.StartOffset; offset < line.EndOffset;)
            {
                var status = System.Text.Rune.DecodeFromUtf16(item.Text.AsSpan(offset), out _, out var consumed);
                if (status != System.Buffers.OperationStatus.Done) break;
                var advance = advances[offset];
                characters.Add(new CharacterCapture
                {
                    StartOffset = offset,
                    EndOffset = offset + consumed,
                    X = x,
                    Y = lineIndex * lineHeight,
                    Width = advance,
                    Height = lineHeight
                });
                x += advance;
                offset += consumed;
            }
        }
        var metrics = TextMetrics.GetFontMetrics(font);
        return new CaseCapture
        {
            Id = item.Id,
            Category = item.Category,
            FontFamily = font.Family,
            FontSize = item.FontSize,
            FontWeight = item.FontWeight,
            FontStyle = item.FontStyle,
            LineHeight = item.LineHeight,
            TextAlign = item.TextAlign,
            Width = width,
            Height = height,
            Baseline = TextMetrics.GetBaselineOffset(font, lineHeight),
            Ascent = -metrics.Ascent,
            Descent = metrics.Descent,
            Characters = characters,
            Screenshot = Path.Combine("cases", screenshotName).Replace('\\', '/')
        };
    }

    private static FontStyle ParseStyle(string value) => value.ToLowerInvariant() switch
    {
        "italic" => FontStyle.Italic,
        "oblique" => FontStyle.Oblique,
        _ => FontStyle.Normal
    };

    private static TextAlignment ParseAlignment(string value) => value.ToLowerInvariant() switch
    {
        "center" => TextAlignment.Center,
        "right" => TextAlignment.Right,
        "justify" => TextAlignment.Justify,
        _ => TextAlignment.Left
    };

    private static float GetTextAlignmentOffset(TextLayout text, float lineWidth)
    {
        if (!float.IsFinite(text.MaxSize.Width) || text.MaxSize.Width <= lineWidth) return 0;
        return text.Alignment switch
        {
            TextAlignment.Center => (text.MaxSize.Width - lineWidth) / 2f,
            TextAlignment.Right => text.MaxSize.Width - lineWidth,
            _ => 0
        };
    }

    private static float ParseLineHeight(string value, float fontSize)
    {
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)
            && float.TryParse(value[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pixels))
            return pixels;
        return float.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var multiplier)
            ? multiplier * fontSize
            : TextMetrics.GetLineHeight(new Font("sans-serif", fontSize), TextLayout.DefaultLineHeight);
    }
}
