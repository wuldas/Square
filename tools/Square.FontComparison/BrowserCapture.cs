using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;

namespace Square.FontComparison;

internal static class BrowserCapture
{
    public static async Task<CaptureReport> CaptureAsync(
        FontManifest fonts,
        FontComparisonManifest manifest,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var screenshotDirectory = Path.Combine(outputDirectory, "cases");
        Directory.CreateDirectory(screenshotDirectory);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 640, Height = 480 },
            DeviceScaleFactor = 1,
            ColorScheme = ColorScheme.Light
        });
        var page = await context.NewPageAsync();
        var fontsByFile = fonts.Fonts.ToDictionary(font => font.File, StringComparer.OrdinalIgnoreCase);
        await page.RouteAsync("https://square-fonts.test/*", async route =>
        {
            var file = Path.GetFileName(new Uri(route.Request.Url).AbsolutePath);
            if (!fontsByFile.ContainsKey(file))
            {
                await route.FulfillAsync(new RouteFulfillOptions { Status = 404 });
                return;
            }
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                    ? "font/otf"
                    : "font/ttf",
                Headers = new Dictionary<string, string>
                {
                    ["Access-Control-Allow-Origin"] = "*",
                    ["Cache-Control"] = "no-store"
                },
                BodyBytes = await File.ReadAllBytesAsync(Path.Combine(ComparisonAssets.FontsDirectory, file))
            });
        });
        await page.SetContentAsync(BuildHtml(fonts));
        await page.EvaluateAsync("async () => await document.fonts.ready");

        foreach (var font in fonts.Fonts)
        {
            var loaded = await page.EvaluateAsync<bool>(
                """
                async ([family, weight, style]) => {
                  const shorthand = `${style} ${weight} 16px '${family}'`;
                  await document.fonts.load(shorthand, 'A中');
                  return document.fonts.check(shorthand, 'A中');
                }
                """,
                new object[] { font.Family, font.Weight, font.Style });
            if (!loaded)
                throw new InvalidOperationException(
                    $"Chromium did not load {font.Family} {font.Weight} {font.Style} from the fixed font asset.");
        }

        var captures = new List<CaseCapture>();
        foreach (var item in manifest.Cases)
        {
            var element = page.Locator("#case");
            await element.EvaluateAsync("""
                (node, config) => {
                  node.textContent = config.text;
                  node.style.fontFamily = `'${config.fontFamily}'`;
                  node.style.fontSize = config.fontSize;
                  node.style.fontWeight = config.fontWeight;
                  node.style.fontStyle = config.fontStyle;
                  node.style.lineHeight = config.lineHeight;
                  node.style.textAlign = config.textAlign;
                   node.style.width = config.width;
                   node.style.whiteSpace = config.whiteSpace;
                   node.style.letterSpacing = config.letterSpacing;
                   node.style.wordSpacing = config.wordSpacing;
                   node.style.textIndent = config.textIndent;
                   node.style.textTransform = config.textTransform;
                   node.style.textDecoration = config.textDecoration;
                   const container = document.querySelector('#container');
                   container.style.boxSizing = 'border-box';
                   container.style.overflow = 'hidden';
                   const containerWidth = config.containerWidthCss;
                   const containerHeight = config.containerHeightCss;
                   container.style.display = containerWidth == null && containerHeight == null ? 'block' : 'flex';
                   container.style.width = containerWidth || '';
                   container.style.height = containerHeight || '';
                   container.style.justifyContent = containerWidth == null ? '' : 'center';
                   container.style.alignItems = containerHeight == null ? '' : 'center';
                 }
                """, new
            {
                text = item.Text,
                fontFamily = item.FontFamily,
                fontSize = item.FontSize.ToString(CultureInfo.InvariantCulture) + "px",
                fontWeight = item.FontWeight.ToString(CultureInfo.InvariantCulture),
                fontStyle = item.FontStyle,
                lineHeight = item.LineHeight,
                textAlign = item.TextAlign,
                 width = item.Width.HasValue
                     ? item.Width.Value.ToString(CultureInfo.InvariantCulture) + "px"
                     : "max-content",
                 whiteSpace = item.WhiteSpace,
                 letterSpacing = item.LetterSpacing,
                 wordSpacing = item.WordSpacing,
                 textIndent = item.TextIndent,
                 textTransform = item.TextTransform,
                 textDecoration = item.TextDecoration
                   ,containerWidthCss = item.ContainerWidth.HasValue
                       ? item.ContainerWidth.Value.ToString(CultureInfo.InvariantCulture) + "px"
                       : null
                   ,containerHeightCss = item.ContainerHeight.HasValue
                       ? item.ContainerHeight.Value.ToString(CultureInfo.InvariantCulture) + "px"
                       : null
              });

            var screenshotName = item.Id + ".png";
            var screenshotTarget = item.ContainerWidth.HasValue || item.ContainerHeight.HasValue
                ? page.Locator("#container")
                : element;
            await screenshotTarget.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = Path.Combine(screenshotDirectory, screenshotName),
                Animations = ScreenshotAnimations.Disabled,
                Scale = ScreenshotScale.Css
            });

            var valueJson = await element.EvaluateAsync<string>("""
                node => {
                  const text = node.textContent || '';
                   const box = node.getBoundingClientRect();
                   const containerBox = document.querySelector('#container').getBoundingClientRect();
                   const containerStyle = getComputedStyle(document.querySelector('#container'));
                  const style = getComputedStyle(node);
                  const characters = [];
                  for (let offset = 0; offset < text.length;) {
                    const codePoint = text.codePointAt(offset);
                    const consumed = codePoint > 0xffff ? 2 : 1;
                    const range = document.createRange();
                    range.setStart(node.firstChild, offset);
                    range.setEnd(node.firstChild, offset + consumed);
                    const rect = range.getBoundingClientRect();
                    characters.push({
                      startOffset: offset,
                      endOffset: offset + consumed,
                     x: rect.x - box.x,
                      y: rect.y - box.y,
                      width: rect.width,
                      height: rect.height
                    });
                    offset += consumed;
                  }
                  const canvas = document.createElement('canvas');
                  const context = canvas.getContext('2d');
                  context.font = `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
                  const metrics = context.measureText(text.split('\n')[0]);
                  const lineHeight = Number.parseFloat(style.lineHeight);
                  const fontAscent = metrics.fontBoundingBoxAscent ?? metrics.actualBoundingBoxAscent;
                  const fontDescent = metrics.fontBoundingBoxDescent ?? metrics.actualBoundingBoxDescent;
                   return JSON.stringify({
                    fontFamily: style.fontFamily.replaceAll('"', '').replaceAll("'", ''),
                    fontSize: Number.parseFloat(style.fontSize),
                    fontWeight: Number.parseInt(style.fontWeight, 10),
                    fontStyle: style.fontStyle,
                     width: box.width,
                     height: box.height,
                     x: box.x - containerBox.x,
                     y: box.y - containerBox.y,
                     containerWidth: containerBox.width,
                     containerHeight: containerBox.height,
                     containerDisplay: containerStyle.display,
                     containerInlineWidth: container.style.width,
                     containerInlineHeight: container.style.height,
                    baseline: (lineHeight - fontAscent - fontDescent) / 2 + fontAscent,
                    ascent: fontAscent,
                    descent: fontDescent,
                    characters
                  });
                }
                """);
            var value = JsonSerializer.Deserialize<BrowserCaseResult>(valueJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
                ?? throw new InvalidOperationException($"Chromium returned no metrics for '{item.Id}'.");
            if (Math.Abs(value.FontSize - item.FontSize) > 0.01f
                || value.FontWeight != item.FontWeight
                || !string.Equals(value.FontStyle, item.FontStyle, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Chromium computed font does not match case '{item.Id}'.");
            if (item.ContainerWidth.HasValue &&
                (Math.Abs(value.ContainerWidth - item.ContainerWidth.Value) > 0.01f ||
                 Math.Abs(value.ContainerHeight - item.ContainerHeight.GetValueOrDefault()) > 0.01f ||
                 value.ContainerDisplay != "flex"))
                throw new InvalidOperationException(
                    $"Chromium container did not apply for '{item.Id}': " +
                    $"display={value.ContainerDisplay}, width={value.ContainerWidth}, height={value.ContainerHeight}, " +
                    $"inline={value.ContainerInlineWidth}/{value.ContainerInlineHeight}.");
            if (!item.ContainerWidth.HasValue && !item.ContainerHeight.HasValue &&
                (value.ContainerDisplay != "block" || Math.Abs(value.X) > 0.01f || Math.Abs(value.Y) > 0.01f))
                throw new InvalidOperationException(
                    $"Chromium case '{item.Id}' was not captured at the container origin: " +
                    $"display={value.ContainerDisplay}, position={value.X}/{value.Y}.");
            captures.Add(new CaseCapture
            {
                Id = item.Id,
                Category = item.Category,
                FontFamily = value.FontFamily,
                FontSize = item.FontSize,
                FontWeight = item.FontWeight,
                FontStyle = item.FontStyle,
                LineHeight = item.LineHeight,
                TextAlign = item.TextAlign,
                Width = value.Width,
                Height = value.Height,
                X = value.X,
                Y = value.Y,
                ContainerLayout = item.ContainerWidth.HasValue || item.ContainerHeight.HasValue,
                Baseline = value.Baseline,
                Ascent = value.Ascent,
                Descent = value.Descent,
                Characters = value.Characters,
                Screenshot = Path.Combine("cases", screenshotName).Replace('\\', '/')
            });
        }

        var report = new CaptureReport
        {
            Renderer = "Chromium",
            Version = browser.Version,
            CapturedAt = DateTimeOffset.UtcNow,
            Cases = captures
        };
        await WriteAsync(Path.Combine(outputDirectory, "metrics.json"), report);
        return report;
    }

    private static string BuildHtml(FontManifest fonts)
    {
        var fontFaces = string.Join(Environment.NewLine, fonts.Fonts.Select(font => $$"""
            @font-face {
              font-family: '{{font.Family}}';
              src: url('https://square-fonts.test/{{font.File}}');
              font-weight: {{font.Weight}};
              font-style: {{font.Style}};
            }
            """));
        return $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"><style>
            {{fontFaces}}
            html, body { margin: 0; padding: 0; background: white; }
            #case {
              display: block;
              margin: 0;
              padding: 0;
              border: 0;
              color: black;
              background: white;
              white-space: pre-wrap;
              overflow: visible;
              box-sizing: content-box;
              font-synthesis: none;
              font-kerning: none;
              font-variant-ligatures: none;
            }
            #container { display: block; margin: 0; padding: 0; background: white; }
            </style></head><body><div id="container"><div id="case"></div></div></body></html>
            """;
    }

    private static async Task WriteAsync(string path, CaptureReport report)
        => await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

    private sealed class BrowserCaseResult
    {
        public required string FontFamily { get; init; }
        public float FontSize { get; init; }
        public int FontWeight { get; init; }
        public required string FontStyle { get; init; }
        public float Width { get; init; }
        public float Height { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float ContainerWidth { get; init; }
        public float ContainerHeight { get; init; }
        public required string ContainerDisplay { get; init; }
        public required string ContainerInlineWidth { get; init; }
        public required string ContainerInlineHeight { get; init; }
        public float Baseline { get; init; }
        public float Ascent { get; init; }
        public float Descent { get; init; }
        public required List<CharacterCapture> Characters { get; init; }
    }
}
