using System.Text.Json;
using Square.Backends;
using Square.Backends.Skia;
using Square.Backends.Vulkan;
using Square.Controls;
using Square.Graphics;
using Square.Graphics.Codecs;
using Square.Rendering;
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
            if (item.IsLayoutCase)
            {
                captures.Add(CaptureLayoutCase(factory, item, screenshotDirectory));
                continue;
            }

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
                Alignment = ParseAlignment(item.TextAlign),
                WhiteSpace = ParseWhiteSpace(item.WhiteSpace),
                LetterSpacing = ParseLength(item.LetterSpacing, item.FontSize),
                WordSpacing = ParseLength(item.WordSpacing, item.FontSize),
                TextIndent = ParseLength(item.TextIndent, item.FontSize),
                TextTransform = ParseTextTransform(item.TextTransform),
                TextDecorationLines = ParseTextDecoration(item.TextDecoration)
            };
            var measured = layout.Measure();
            var width = item.Width ?? Math.Max(1, measured.Width);
            var height = Math.Max(1, measured.Height);
            var canvasWidth = item.ContainerWidth ?? width;
            var canvasHeight = item.ContainerHeight ?? height;
            var originX = item.ContainerWidth.HasValue ? Math.Max(0, (canvasWidth - width) / 2f) : 0;
            var originY = item.ContainerHeight.HasValue ? Math.Max(0, (canvasHeight - height) / 2f) : 0;
            using var context = factory.CreateContext(new RenderContextCreateInfo
            {
                CanvasSize = new Size(canvasWidth, canvasHeight),
                DpiScale = 1
            });
            context.Clear(Color.White);
            context.DrawText(layout, new Point(originX, originY), Brush.FromColor(Color.Black));
            using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
            var screenshotName = item.Id + ".png";
            BitmapPngEncoder.Save(bitmap, Path.Combine(screenshotDirectory, screenshotName));

            var characters = new List<CharacterCapture>();
            var lines = layout.GetVisualLines();
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                var x = layout.GetLineOriginX(0, lineIndex, line.Width);
                var y = lineIndex * lineHeight;
                foreach (var visualRune in line.Runes)
                {
                    characters.Add(new CharacterCapture
                    {
                        StartOffset = visualRune.StartOffset,
                        EndOffset = visualRune.EndOffset,
                        X = x,
                        Y = y,
                        Width = visualRune.Advance,
                        Height = lineHeight
                    });
                    x += visualRune.Advance;
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
                X = originX,
                Y = originY,
                ContainerLayout = item.ContainerWidth.HasValue || item.ContainerHeight.HasValue,
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

    private static CaseCapture CaptureLayoutCase(
        IRenderBackendFactory factory,
        FontComparisonCase item,
        string screenshotDirectory)
    {
        var canvasSize = GetLayoutCanvasSize(item);
        var (root, target) = CreateLayoutTree(item);
        Layout(root, canvasSize);
        using var context = factory.CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = canvasSize,
            DpiScale = 1
        });
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        tree.Render(context);
        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var screenshotName = item.Id + ".png";
        BitmapPngEncoder.Save(bitmap, Path.Combine(screenshotDirectory, screenshotName));
        return CreateLayoutMetrics(item, root, target, screenshotName);
    }

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
            if (item.IsLayoutCase)
            {
                captures.Add(CaptureVulkanLayoutCase(item, screenshotDirectory));
                continue;
            }

            var font = new Font(item.FontFamily, item.FontSize, (FontWeight)item.FontWeight, ParseStyle(item.FontStyle));
            var lineHeight = ParseLineHeight(item.LineHeight, item.FontSize);
            var maxWidth = item.Width ?? float.MaxValue;
            var layout = new TextLayout(item.Text, font)
            {
                MaxSize = new Size(maxWidth, float.MaxValue),
                LineHeight = lineHeight / item.FontSize,
                Alignment = ParseAlignment(item.TextAlign),
                WhiteSpace = ParseWhiteSpace(item.WhiteSpace),
                LetterSpacing = ParseLength(item.LetterSpacing, item.FontSize),
                WordSpacing = ParseLength(item.WordSpacing, item.FontSize),
                TextIndent = ParseLength(item.TextIndent, item.FontSize),
                TextTransform = ParseTextTransform(item.TextTransform),
                TextDecorationLines = ParseTextDecoration(item.TextDecoration)
            };
            var measured = layout.Measure();
            var width = Math.Max(1, (int)MathF.Ceiling(item.Width ?? measured.Width));
            var height = Math.Max(1, (int)MathF.Ceiling(measured.Height));
            var canvasWidth = Math.Max(1, (int)MathF.Ceiling(item.ContainerWidth ?? width));
            var canvasHeight = Math.Max(1, (int)MathF.Ceiling(item.ContainerHeight ?? height));
            var originX = item.ContainerWidth.HasValue ? Math.Max(0, (canvasWidth - width) / 2f) : 0;
            var originY = item.ContainerHeight.HasValue ? Math.Max(0, (canvasHeight - height) / 2f) : 0;
            using var host = new Win32PlatformFactory().CreateHost(new PlatformHostCreateInfo
            {
                Title = "Square font Vulkan conformance",
                Width = canvasWidth,
                Height = canvasHeight,
                RenderBackend = "Vulkan"
            });
            host.Show();
            using var context = host.CreateRenderContext();
            context.Clear(Color.White);
            context.DrawText(layout, new Point(originX, originY), Brush.FromColor(Color.Black));
            context.Present();
            host.ShowAfterFirstFrame();
            using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
            var screenshotName = item.Id + ".png";
            BitmapPngEncoder.Save(bitmap, Path.Combine(screenshotDirectory, screenshotName));
            captures.Add(CreateMetrics(item, layout, font, lineHeight, width, height, originX, originY, screenshotName));
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

#if PLATFORM_WIN32
    private static CaseCapture CaptureVulkanLayoutCase(
        FontComparisonCase item,
        string screenshotDirectory)
    {
        var canvasSize = GetLayoutCanvasSize(item);
        var canvasWidth = Math.Max(1, (int)MathF.Ceiling(canvasSize.Width));
        var canvasHeight = Math.Max(1, (int)MathF.Ceiling(canvasSize.Height));
        using var host = new Win32PlatformFactory().CreateHost(new PlatformHostCreateInfo
        {
            Title = "Square layout Vulkan conformance",
            Width = canvasWidth,
            Height = canvasHeight,
            RenderBackend = "Vulkan"
        });
        host.Show();
        using var context = host.CreateRenderContext();
        var (root, target) = CreateLayoutTree(item);
        Layout(root, canvasSize);
        context.Clear(Color.White);
        var tree = new DisplayTree();
        tree.BuildFrom(root);
        tree.Render(context);
        context.Present();
        host.ShowAfterFirstFrame();
        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var screenshotName = item.Id + ".png";
        BitmapPngEncoder.Save(bitmap, Path.Combine(screenshotDirectory, screenshotName));
        var result = CreateLayoutMetrics(item, root, target, screenshotName);
        host.Close();
        return result;
    }
#endif

    private static CaseCapture CreateMetrics(
        FontComparisonCase item,
        TextLayout layout,
        Font font,
        float lineHeight,
        float width,
        float height,
        float originX,
        float originY,
        string screenshotName)
    {
        var characters = new List<CharacterCapture>();
        var lines = layout.GetVisualLines();
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var x = layout.GetLineOriginX(0, lineIndex, line.Width);
            foreach (var visualRune in line.Runes)
            {
                characters.Add(new CharacterCapture
                {
                    StartOffset = visualRune.StartOffset,
                    EndOffset = visualRune.EndOffset,
                    X = x,
                    Y = lineIndex * lineHeight,
                    Width = visualRune.Advance,
                    Height = lineHeight
                });
                x += visualRune.Advance;
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
            X = originX,
            Y = originY,
            ContainerLayout = item.ContainerWidth.HasValue || item.ContainerHeight.HasValue,
            Baseline = TextMetrics.GetBaselineOffset(font, lineHeight),
            Ascent = -metrics.Ascent,
            Descent = metrics.Descent,
            Characters = characters,
            Screenshot = Path.Combine("cases", screenshotName).Replace('\\', '/')
        };
    }

    private static Size GetLayoutCanvasSize(FontComparisonCase item) => new(
        item.ContainerWidth ?? throw new InvalidOperationException($"Layout case '{item.Id}' requires containerWidth."),
        item.ContainerHeight ?? throw new InvalidOperationException($"Layout case '{item.Id}' requires containerHeight."));

    private static (View Root, View Target) CreateLayoutTree(FontComparisonCase item)
    {
        var root = new View();
        root.Style.Set("display", item.ContainerDisplay);
        root.Style.Set("flex-direction", "row");
        root.Style.Set("justify-content", item.JustifyContent);
        root.Style.Set("align-items", item.AlignItems);
        var target = new View();
        if (item.Width.HasValue) target.Style.Set("width", $"{item.Width.Value}px");
        if (item.Height.HasValue) target.Style.Set("height", $"{item.Height.Value}px");
        target.Style.Set("margin-left", item.MarginLeft);
        target.Style.Set("margin-right", item.MarginRight);
        target.Style.Set("background", "#000000");
        root.Children.Add(target);
        return (root, target);
    }

    private static void Layout(View root, Size size)
    {
        var layout = new LayoutEngine();
        layout.Measure(root, size);
        layout.Arrange(root, new Rect(0, 0, size.Width, size.Height));
    }

    private static CaseCapture CreateLayoutMetrics(
        FontComparisonCase item,
        View root,
        View target,
        string screenshotName) => new()
        {
            Id = item.Id,
            Category = item.Category,
            FontFamily = item.FontFamily,
            FontSize = item.FontSize,
            FontWeight = item.FontWeight,
            FontStyle = item.FontStyle,
            LineHeight = item.LineHeight,
            TextAlign = item.TextAlign,
            Width = target.Geometry.Width,
            Height = target.Geometry.Height,
            X = target.Geometry.X - root.Geometry.X,
            Y = target.Geometry.Y - root.Geometry.Y,
            ContainerLayout = true,
            Baseline = 0,
            Ascent = 0,
            Descent = 0,
            Characters = [],
            Screenshot = Path.Combine("cases", screenshotName).Replace('\\', '/')
        };

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

    private static TextWhiteSpaceMode ParseWhiteSpace(string value) => value.ToLowerInvariant() switch
    {
        "pre" => TextWhiteSpaceMode.Pre,
        "nowrap" => TextWhiteSpaceMode.Nowrap,
        "pre-wrap" => TextWhiteSpaceMode.PreWrap,
        "pre-line" => TextWhiteSpaceMode.PreLine,
        _ => TextWhiteSpaceMode.Normal
    };

    private static TextTransformMode ParseTextTransform(string value) => value.ToLowerInvariant() switch
    {
        "capitalize" => TextTransformMode.Capitalize,
        "uppercase" => TextTransformMode.Uppercase,
        "lowercase" => TextTransformMode.Lowercase,
        _ => TextTransformMode.None
    };

    private static TextDecorationLine ParseTextDecoration(string value)
    {
        var result = TextDecorationLine.None;
        foreach (var token in value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            result |= token switch
            {
                "underline" => TextDecorationLine.Underline,
                "overline" => TextDecorationLine.Overline,
                "line-through" => TextDecorationLine.LineThrough,
                _ => TextDecorationLine.None
            };
        return result;
    }

    private static float ParseLength(string value, float fontSize)
    {
        value = value.Trim();
        if (value.Equals("normal", StringComparison.OrdinalIgnoreCase) || value.Length == 0) return 0;
        if (value.EndsWith("em", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(value[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var em))
            return em * fontSize;
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) value = value[..^2].Trim();
        return float.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var pixels) ? pixels : 0;
    }
}
