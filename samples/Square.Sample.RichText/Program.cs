using Square.Extensions;
using Square.Hosting;
using Square.Sample.RichText.Components;
using Square.Platform;
using Square.DevTools;
using Square.UI;

namespace Square.Sample.RichText;

public static class Program
{
    public static void Main(string[] args)
    {
        ExtensionRegistration.RegisterDefaults();

        var window = new AppWindow("Square RichText Editor", 1100, 760)
        {
            RenderingMode = RenderMode.DirtyRegion
        };
        var page = new Main();
        window.Load(page);
        window.LoadCustomTitleBar(new RichTextTitleBar { Page = page });
        var app = new DesktopApplication(window);

        var devTools = window.UseDevToolsServer(new DevToolsOptions
        {
            Port = 0,
            AllowInputInjection = true,
            AllowInspector = true,
            IncludeTextContent = true
        });
        if (devTools == null)
        {
            System.Console.WriteLine("Square DevTools: unavailable; the application continues without it.");
        }
        else
        {
            System.Console.WriteLine($"Square DevTools: {devTools.BaseAddress}/api/v1/health");
            System.Console.WriteLine($"Token header: {DevToolsServer.TokenHeader}: {devTools.AccessToken}");
        }
        app.Run();
    }
}
