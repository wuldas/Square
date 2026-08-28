using System.Text.Json;
using Microsoft.Playwright;

namespace Square.FontComparison;

internal static class ControlBrowserCapture
{
    private const int CanvasWidth = 320;
    private const int CanvasHeight = 160;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<ControlGeometryReport> CaptureAsync(
        ControlComparisonManifest manifest,
        string outputDirectory,
        string? captureSession = null)
    {
        Directory.CreateDirectory(Path.Combine(outputDirectory, "cases"));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = CanvasWidth, Height = CanvasHeight },
            DeviceScaleFactor = 1,
            ColorScheme = ColorScheme.Light
        });
        var page = await context.NewPageAsync();
        await page.SetContentAsync("""
            <!doctype html><html><head><meta charset="utf-8"><style>
            html, body { margin: 0; width: 320px; height: 160px; background: white; }
            #container { box-sizing: border-box; width: 320px; height: 160px; display: flex; align-items: center; justify-content: center; overflow: hidden; }
            </style></head><body><div id="container"></div></body></html>
            """);

        var captures = new List<ControlGeometryCaseResult>();
        foreach (var item in manifest.ExpandCases())
        {
            await ResetStateAsync(page);
            await ConfigureCaseAsync(page, item);
            var locator = page.Locator("#case");
            var heldActive = await ApplyStateAsync(page, locator, item.State);
            var paths = ControlArtifactPaths.For(Path.GetDirectoryName(outputDirectory)!, "chrome", item.Id);
            await page.Locator("#container").ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = paths.Screenshot,
                Animations = ScreenshotAnimations.Disabled,
                Caret = ScreenshotCaret.Initial,
                Scale = ScreenshotScale.Css
            });
            var json = await locator.EvaluateAsync<string>("""
                node => {
                  const container = document.querySelector('#container').getBoundingClientRect();
                  const box = node.getBoundingClientRect();
                  const style = getComputedStyle(node);
                  const number = value => Number.parseFloat(value) || 0;
                  const border = {
                    top: number(style.borderTopWidth), right: number(style.borderRightWidth),
                    bottom: number(style.borderBottomWidth), left: number(style.borderLeftWidth)
                  };
                  const padding = {
                    top: number(style.paddingTop), right: number(style.paddingRight),
                    bottom: number(style.paddingBottom), left: number(style.paddingLeft)
                  };
                  const borderBox = { x: box.x - container.x, y: box.y - container.y, width: box.width, height: box.height };
                  const contentBox = {
                    x: borderBox.x + border.left + padding.left,
                    y: borderBox.y + border.top + padding.top,
                    width: Math.max(0, box.width - border.left - border.right - padding.left - padding.right),
                    height: Math.max(0, box.height - border.top - border.bottom - padding.top - padding.bottom)
                  };
                  return JSON.stringify({ borderBox, contentBox, padding, border, computedStyles: {
                    appearance: style.appearance, boxSizing: style.boxSizing, width: style.width, height: style.height,
                    padding: style.padding, border: style.border, borderRadius: style.borderRadius,
                    backgroundColor: style.backgroundColor, color: style.color, font: style.font
                  }});
                }
                """);
            if (heldActive) await page.Mouse.UpAsync();
            var geometry = JsonSerializer.Deserialize<BrowserGeometry>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Chromium returned no geometry for '{item.Id}'.");
            captures.Add(new ControlGeometryCaseResult
            {
                Id = item.Id,
                Kind = item.Kind,
                Appearance = item.Appearance,
                State = item.State,
                Passed = true,
                BorderBox = geometry.BorderBox,
                ContentBox = geometry.ContentBox,
                Padding = geometry.Padding,
                Border = geometry.Border,
                ComputedStyles = geometry.ComputedStyles,
                Screenshot = Path.Combine("cases", item.Id + ".png").Replace('\\', '/'),
                ScreenshotSha256 = ControlArtifactIdentity.ComputeFileSha256(paths.Screenshot)
            });
        }

        var report = new ControlGeometryReport
        {
            Renderer = "Chromium",
            ManifestFingerprint = manifest.ComputeFingerprint(),
            BuildFingerprint = ControlArtifactIdentity.ComputeBuildFingerprint(),
            CaptureSession = captureSession ?? Guid.NewGuid().ToString("N"),
            Version = browser.Version,
            CapturedAt = DateTimeOffset.UtcNow,
            Cases = captures
        };
        await ControlReportIO.WriteAsync(Path.Combine(outputDirectory, "geometry.json"), report);
        return report;
    }

    private static async Task ResetStateAsync(IPage page)
    {
        await page.Mouse.UpAsync();
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("document.activeElement instanceof HTMLElement && document.activeElement.blur()");
    }

    private static async Task ConfigureCaseAsync(IPage page, ControlComparisonCase item)
    {
        await page.EvaluateAsync("""
            config => {
              const container = document.querySelector('#container');
              container.replaceChildren();
              const node = document.createElement(config.element);
              node.id = 'case';
              if (config.kind === 'Input') node.type = 'text';
              if (config.kind === 'CheckBox') node.type = 'checkbox';
              if (config.kind === 'Radio') node.type = 'radio';
              if (config.kind === 'Button') node.textContent = config.text;
              if (config.kind === 'Select') {
                const option = document.createElement('option');
                option.value = config.value;
                option.textContent = config.value;
                node.append(option);
                node.value = config.value;
              }
              if (config.kind === 'Input' || config.kind === 'TextArea') {
                node.placeholder = config.placeholder;
                node.value = config.state === 'Placeholder' ? '' : config.value;
              }
              if (config.state === 'Checked') node.checked = true;
              if (config.state === 'Disabled') node.disabled = true;
              node.style.cssText = config.authorCss;
              container.append(node);
            }
            """, ControlBrowserCaseConfig.Create(item));
    }

    private static async Task<bool> ApplyStateAsync(IPage page, ILocator locator, ControlState state)
    {
        switch (state)
        {
            case ControlState.Hover:
                await locator.HoverAsync();
                break;
            case ControlState.Focus:
                await locator.FocusAsync();
                break;
            case ControlState.Active:
                await locator.HoverAsync();
                var box = await locator.BoundingBoxAsync()
                    ?? throw new InvalidOperationException("Unable to locate active control geometry.");
                await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
                await page.Mouse.DownAsync();
                return true;
        }
        return false;
    }

    private sealed class BrowserGeometry
    {
        public ControlRect BorderBox { get; init; }
        public ControlRect ContentBox { get; init; }
        public ControlEdges Padding { get; init; }
        public ControlEdges Border { get; init; }
        public Dictionary<string, string> ComputedStyles { get; init; } = [];
    }
}
