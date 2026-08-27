using System.Globalization;
using System.Text.Json;
using Square.Backends;
using Square.Backends.Skia;
using Square.Controls;
using Square.CSS.Engine;
using Square.Graphics;
using Square.Graphics.Codecs;
using Square.Rendering;
using Square.UI;

namespace Square.FontComparison;

internal static class ControlSquareCapture
{
    private static readonly Size CanvasSize = new(320, 160);

    public static async Task<ControlGeometryReport> CaptureAsync(
        string backend,
        ControlComparisonManifest manifest,
        string outputDirectory)
    {
        Directory.CreateDirectory(Path.Combine(outputDirectory, "cases"));
        var factory = CreateFactory(backend);
        if (backend.Equals("Skia", StringComparison.OrdinalIgnoreCase))
        {
            using var registration = factory.CreateContext(new RenderContextCreateInfo
            {
                CanvasSize = new Size(1, 1),
                DpiScale = 1
            });
        }

        var captures = new List<ControlGeometryCaseResult>();
        foreach (var item in manifest.ExpandCases())
            captures.Add(CaptureCase(factory, backend, item, outputDirectory));

        var report = new ControlGeometryReport
        {
            Renderer = CanonicalRenderer(backend),
            ManifestFingerprint = manifest.ComputeFingerprint(),
            Version = typeof(LayoutEngine).Assembly.GetName().Version?.ToString() ?? "unknown",
            CapturedAt = DateTimeOffset.UtcNow,
            Cases = captures
        };
        await ControlReportIO.WriteAsync(Path.Combine(outputDirectory, "geometry.json"), report);
        return report;
    }

    private static ControlGeometryCaseResult CaptureCase(
        IRenderBackendFactory factory,
        string backend,
        ControlComparisonCase item,
        string outputDirectory)
    {
        var root = new View();
        root.Style.CssText =
            "display: flex; align-items: center; justify-content: center; " +
            "box-sizing: border-box; width: 320px; height: 160px; background: white; overflow: hidden;";
        var control = CreateControl(item);
        control.Style.CssText = item.AuthorCss;
        ApplyState(control, item.State);
        root.Children.Add(control);

        var css = new CssEngine();
        css.ApplyStylesToTree(root);
        var layout = new LayoutEngine();
        layout.MeasureAndArrange(root, CanvasSize);

        using var context = factory.CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = CanvasSize,
            DpiScale = 1
        });
        context.Clear(Color.White);
        var displayTree = new DisplayTree();
        displayTree.BuildFrom(root);
        displayTree.Render(context);
        using var bitmap = ((IRenderBitmapSource)context).CaptureBitmap();
        var screenshotPath = Path.Combine(outputDirectory, "cases", item.Id + ".png");
        BitmapPngEncoder.Save(bitmap, screenshotPath);

        var border = ReadEdges(control, "border", "width");
        var padding = ReadEdges(control, "padding", null);
        var borderBox = new ControlRect(
            control.Geometry.X - root.Geometry.X,
            control.Geometry.Y - root.Geometry.Y,
            control.Geometry.Width,
            control.Geometry.Height);
        var contentBox = new ControlRect(
            borderBox.X + border.Left + padding.Left,
            borderBox.Y + border.Top + padding.Top,
            Math.Max(0, borderBox.Width - border.Left - border.Right - padding.Left - padding.Right),
            Math.Max(0, borderBox.Height - border.Top - border.Bottom - padding.Top - padding.Bottom));
        return new ControlGeometryCaseResult
        {
            Id = item.Id,
            Kind = item.Kind,
            Appearance = item.Appearance,
            State = item.State,
            Passed = true,
            BorderBox = borderBox,
            ContentBox = contentBox,
            Padding = padding,
            Border = border,
            ComputedStyles = new Dictionary<string, string>
            {
                ["appearance"] = control.Style.Get("appearance") ?? "",
                ["boxSizing"] = control.Style.Get("box-sizing") ?? "",
                ["width"] = control.Style.Get("width") ?? "",
                ["height"] = control.Style.Get("height") ?? "",
                ["padding"] = control.Style.Get("padding") ?? "",
                ["border"] = control.Style.Get("border") ?? "",
                ["borderRadius"] = control.Style.Get("border-radius") ?? "",
                ["backgroundColor"] = control.Style.Get("background-color") ?? control.Style.Get("background") ?? "",
                ["color"] = control.Style.Get("color") ?? "",
                ["font"] = control.Style.Get("font") ?? ""
            },
            Screenshot = Path.Combine("cases", item.Id + ".png").Replace('\\', '/')
        };
    }

    private static UIElement CreateControl(ControlComparisonCase item) => item.Kind switch
    {
        ControlKind.Button => new Button(item.Text),
        ControlKind.Input => new Input { Value = item.State == ControlState.Placeholder ? "" : item.Value, Placeholder = item.Placeholder },
        ControlKind.TextArea => new TextArea { Value = item.State == ControlState.Placeholder ? "" : item.Value, Placeholder = item.Placeholder },
        ControlKind.Select => new Select { Options = [item.Value], Value = item.Value },
        ControlKind.CheckBox => new CheckBox { TextContent = "" },
        ControlKind.Radio => new Radio { TextContent = "" },
        _ => throw new ArgumentOutOfRangeException(nameof(item.Kind))
    };

    private static void ApplyState(UIElement control, ControlState state)
    {
        if (state == ControlState.Disabled) control.IsDisabled = true;
        if (state == ControlState.Hover) control.SetState(ElementState.Hover, true);
        if (state == ControlState.Focus) control.SetState(ElementState.Focus, true);
        if (state == ControlState.Active) control.SetState(ElementState.Active, true);
        if (state == ControlState.Checked)
        {
            if (control is CheckBox checkBox) checkBox.IsChecked = true;
            if (control is Radio radio) radio.IsChecked = true;
        }
    }

    private static ControlEdges ReadEdges(UIElement control, string property, string? suffix) => new(
        ReadPixels(control.Style.Get($"{property}-top{Suffix(suffix)}")),
        ReadPixels(control.Style.Get($"{property}-right{Suffix(suffix)}")),
        ReadPixels(control.Style.Get($"{property}-bottom{Suffix(suffix)}")),
        ReadPixels(control.Style.Get($"{property}-left{Suffix(suffix)}")));

    private static string Suffix(string? suffix) => suffix == null ? "" : "-" + suffix;

    private static float ReadPixels(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        value = value.Trim();
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static IRenderBackendFactory CreateFactory(string backend) => backend.ToLowerInvariant() switch
    {
        "software" => new RenderBackendFactory(),
        "skia" => new SkiaBackendFactory(),
        _ => throw new ArgumentException($"Unsupported headless control backend '{backend}'.")
    };

    private static string CanonicalRenderer(string backend) => backend.Equals("Skia", StringComparison.OrdinalIgnoreCase)
        ? "Skia"
        : "Software";
}
