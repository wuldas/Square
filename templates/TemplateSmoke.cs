using System.Globalization;
using System.Text.RegularExpressions;
using Square.Controls;
using Square.Graphics;
using Square.Graphics.Codecs;
using Square.Hosting;
using Square.Platform;
using var resource = typeof(TemplateApp.SquareProgram).Assembly.GetManifestResourceStream("TemplateApp.Assets.template-probe.txt")
    ?? throw new InvalidOperationException("The shared asset was not embedded.");
using var reader = new StreamReader(resource);
if (reader.ReadToEnd().Trim() != "square-template-asset")
    throw new InvalidOperationException("The shared asset contents were not preserved.");


var window = TemplateApp.SquareProgram.CreateWindow();
var app = new DesktopApplication(window);
var verification = Task.Run(async () =>
{
    try
    {
        using var initial = await window.CaptureRendererBitmapAsync().WaitAsync(TimeSpan.FromSeconds(30));
        if (initial.Width <= 0 || initial.Height <= 0)
            throw new InvalidOperationException("The generated app did not render a frame.");
        var point = Point.Zero;
        var before = 0;
        await window.Dispatcher.InvokeAsync(() =>
        {
            var button = window.Document.Query<Button>("increment")
                ?? throw new InvalidOperationException("The counter button was not rendered.");
            var box = button.Geometry;
            if (box.IsEmpty) throw new InvalidOperationException("The counter button has no layout.");
            point = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
            before = ReadCount();
        });
        await window.InjectPointerAsync(new DevToolsPointerInput(point, MouseAction.Move));
        await window.InjectPointerAsync(new DevToolsPointerInput(point, MouseAction.Down));
        await window.InjectPointerAsync(new DevToolsPointerInput(point, MouseAction.Up));
        using var rendered = await window.CaptureRendererBitmapAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await window.Dispatcher.InvokeAsync(() =>
        {
            if (ReadCount() != before + 1)
                throw new InvalidOperationException("Clicking the generated app did not update its visible counter.");
        });
        if (initial.Pixels.AsSpan().SequenceEqual(rendered.Pixels))
            throw new InvalidOperationException("The generated app did not repaint after the counter changed.");
        BitmapPngEncoder.Save(rendered, args[0]);
        Console.WriteLine("Template counter interaction and renderer capture passed.");
    }
    finally
    {
        window.Close();
    }
});
app.Run();
verification.GetAwaiter().GetResult();

int ReadCount()
{
    var text = window.Document.Query<Square.Controls.Text>("counter-value")?.TextContent
        ?? throw new InvalidOperationException("The counter value was not rendered.");
    return int.Parse(Regex.Match(text, "[0-9]+").Value, CultureInfo.InvariantCulture);
}
