using Square.Hosting;
using Square.Images;
using Square.Platform;
using Square.Sample.Vue.Components;
using Square.UI;
using Square.Backends.Vulkan;
using Square.Extensions.Routing;
using Square.DevTools;
namespace Square.Sample.Vue;

public static class Program
{
    public static void Main(string[] args)
    {
        System.Console.WriteLine("Square Vue Template Sample");
        ImageSourceRegistration.RegisterDefaults();
        var window = new AppWindow("Square Vue Template Sample", 900, 980);
        var router = window.UseRouter(routes =>
        {
            routes.Map("/", static () => new RouteShell(), route =>
            {
                route.KeepAlive = true;
                route.Map("", static () => new RouteHomePage());
                route.Map("users/:id", static () => new RouteUserPage(), child => child.KeepAlive = true);
                route.Map("admin", static () => new RouteAdminPage());
                route.Map("login", static () => new RouteLoginPage());
            });
            routes.Map("*", static () => new RouteHomePage());
        });
        router.BeforeEach((to, _) => to.Path == "/admin"
            ? RouteGuardResult.Redirect("/login?returnUrl=/admin")
            : RouteGuardResult.Allow);
        window.RenderingMode = RenderMode.Auto;
        window.Load(new Main());
        var app = new DesktopApplication(window);
        var backend = GetOption(args, "--backend") ?? Environment.GetEnvironmentVariable("SQUARE_RENDER_BACKEND");
        if (string.Equals(backend, "Vulkan", StringComparison.OrdinalIgnoreCase))
            window.UseVulkanBackend();
        ConfigureRendering(window, args);
        SampleSignals.Initialize(app.Dispatcher);
        if (HasOption(args, "--devtools"))
        {
            var devTools = window.UseDevToolsServer(new DevToolsOptions
            {
                Port = int.TryParse(GetOption(args, "--devtools-port"), out var port) ? port : 0,
                AccessToken = GetOption(args, "--devtools-token"),
                AllowInputInjection = true,
                AllowInspector = true,
                IncludeTextContent = true
            });
            System.Console.WriteLine($"Square DevTools: {devTools.BaseAddress}/api/v1/health");
            System.Console.WriteLine($"Token header: {DevToolsServer.TokenHeader}: {devTools.AccessToken}");
        }
        app.Run();
    }

    private static void ConfigureRendering(AppWindow window, string[] args)
    {
        var mode = GetOption(args, "--render-mode") ?? Environment.GetEnvironmentVariable("SQUARE_RENDER_MODE");
        if (Enum.TryParse<RenderMode>(mode, ignoreCase: true, out var renderMode))
            window.RenderingMode = renderMode;
    }

    private static string? GetOption(string[] args, string name)
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

    private static bool HasOption(string[] args, string name) =>
        args.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

}
