using System.Text.Json;
using Square.Graphics.Codecs;
using Square.Hosting;
using Square.Hosting.Web;
using Square.Sample.WebServer.Components;

if (HasOption(args, "--desktop"))
{
    RunDesktop(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapSquarePage<Main>("/", options =>
{
    options.Html.Title = "PiSquared";
    options.Html.Language = "zh-CN";
    options.Html.AdditionalCss = ".session-list-scroll::-webkit-scrollbar,.conversation-scroll::-webkit-scrollbar{display:none;}";
});

app.MapSquarePage("/hello/{name}", context =>
{
    var page = new HelloPage();
    page.Name.Value = "Hello, " + (context.Request.RouteValues["name"]?.ToString() ?? "Square") + ".";
    return page;
}, options => options.Html.Title = "Square route values");

app.Run();

static void RunDesktop(string[] args)
{
    var width = GetIntOption(args, "--width", 1600);
    var height = GetIntOption(args, "--height", 900);
    var window = new AppWindow("PiSquared", width, height)
    {
        TitleStyle = TitleStyle.Hidden,
        BorderStyle = BorderStyle.None
    };
    window.Load(new Main());

    var screenshotPath = GetOption(args, "--screenshot");
    var inspectionPath = GetOption(args, "--inspection");
    var metricsPath = GetOption(args, "--metrics");
    if (!string.IsNullOrWhiteSpace(screenshotPath) || !string.IsNullOrWhiteSpace(inspectionPath) ||
        !string.IsNullOrWhiteSpace(metricsPath))
        ScheduleCapture(window, screenshotPath, inspectionPath, metricsPath);

    new DesktopApplication(window).Run();
}

static void ScheduleCapture(AppWindow window, string? screenshotPath, string? inspectionPath, string? metricsPath)
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(1200);
        try
        {
            if (!string.IsNullOrWhiteSpace(screenshotPath))
            {
                using var bitmap = await window.CaptureRendererBitmapAsync();
                BitmapPngEncoder.Save(bitmap, screenshotPath);
                Console.WriteLine($"Desktop screenshot saved to {screenshotPath} ({bitmap.Width}x{bitmap.Height}).");
            }

            if (!string.IsNullOrWhiteSpace(inspectionPath))
            {
                var snapshot = await window.CaptureInspectionSnapshotAsync();
                await File.WriteAllTextAsync(inspectionPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
                Console.WriteLine($"Desktop inspection saved to {inspectionPath}.");
            }

            if (!string.IsNullOrWhiteSpace(metricsPath))
            {
                var metrics = GetConformanceSelectors().Select(selector =>
                {
                    var element = window.Document.QuerySelector(selector)
                        ?? throw new InvalidOperationException($"Desktop selector '{selector}' did not match an element.");
                    var bounds = element.Geometry;
                    return new
                    {
                        selector,
                        tagName = element.TagName,
                        x = bounds.X,
                        y = bounds.Y,
                        width = bounds.Width,
                        height = bounds.Height
                    };
                });
                await File.WriteAllTextAsync(metricsPath, JsonSerializer.Serialize(metrics, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
                Console.WriteLine($"Desktop metrics saved to {metricsPath}.");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Desktop capture failed: {exception}");
            Environment.ExitCode = 1;
        }
        finally
        {
            window.Close();
        }
    });
}

static int GetIntOption(string[] args, string name, int defaultValue) =>
    int.TryParse(GetOption(args, name), out var value) && value > 0 ? value : defaultValue;

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            return args[i][(name.Length + 1)..];
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            return args[i + 1];
    }
    return null;
}

static bool HasOption(string[] args, string name) =>
    args.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

static string[] GetConformanceSelectors() =>
[
    ".window-shell",
    ".window-title-bar",
    ".app-toolbar",
    ".workbench-row",
    ".workspace-rail",
    ".session-sidebar",
    ".workspace-heading",
    ".new-session-button",
    ".session-search-box",
    ".session-filter",
    ".session-list-scroll",
    ".conversation-panel",
    ".conversation-header",
    ".conversation-scroll",
    ".conversation-document",
    ".welcome-card",
    ".user-message",
    ".design-summary",
    ".tool-card",
    ".composer-shell",
    ".composer-box",
    ".composer-input",
    ".context-sidebar",
    ".context-tabs",
    ".context-content",
    ".change-summary-card",
    ".status-bar"
];
