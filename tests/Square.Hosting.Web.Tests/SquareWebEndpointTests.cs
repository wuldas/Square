using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Square.Controls;
using Square.Hosting.Web;
using Square.Platform;
using Square.UI;
using Xunit;
#if PLATFORM_WIN32
using Square.Platform.Win32;
#endif
using SquareText = Square.Controls.Text;

namespace Square.Hosting.Web.Tests;

public sealed class SquareWebEndpointTests
{
    [Fact]
    public async Task EndpointReturnsHtmlAndCreatesIndependentPagePerRequest()
    {
        StatefulPage.Reset();
        await using var app = await StartApp(builder => builder.MapSquarePage<StatefulPage>("/"));
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var first = await client.GetStringAsync("/");
        var second = await client.GetStringAsync("/");

        Assert.Contains("Request 1", first);
        Assert.Contains("Request 2", second);
        Assert.Equal(2, StatefulPage.Created);
    }

    [Fact]
    public async Task EndpointCanReadRouteValuesAndReturnsHtmlContentType()
    {
        await using var app = await StartApp(builder => builder.MapSquarePage(
            "/users/{id}",
            context => new SquareText("User " + context.Request.RouteValues["id"]),
            options => options.Html.Title = "User page"));
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var response = await client.GetAsync("/users/42");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Contains("<title>User page</title>", html);
        Assert.Contains("User 42", html);
    }

    [Fact]
    public async Task StylesheetEndpointReturnsGeneratedCssForExternalHtmlLink()
    {
        await using var app = await StartApp(builder =>
        {
            builder.MapSquarePage(
                "/",
                _ => CreateStyledPage(),
                options => options.Html.StylesheetHref = "/square.css");
            builder.MapSquareStylesheet("/square.css", _ => CreateStyledPage());
        });
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var htmlResponse = await client.GetAsync("/");
        using var cssResponse = await client.GetAsync("/square.css");
        var html = await htmlResponse.Content.ReadAsStringAsync();
        var css = await cssResponse.Content.ReadAsStringAsync();

        Assert.Contains("<link rel=\"stylesheet\" href=\"/square.css\">", html);
        Assert.DoesNotContain("<style data-square-css=\"true\">", html);
        Assert.Equal("text/css", cssResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("display:flex;", css);
    }

    [Fact]
    public async Task WebHostingDoesNotReplaceDesktopPlatformRegistration()
    {
#if PLATFORM_WIN32
        var factory = new Win32PlatformFactory();
        PlatformRegistry.Register(factory);
        await using var app = await StartApp(builder => builder.MapSquarePage("/", _ => new View()));
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        _ = await client.GetStringAsync("/");

        Assert.Same(factory, PlatformRegistry.Get());
#endif
    }

    private static async Task<WebApplication> StartApp(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        map(app);
        await app.StartAsync();
        return app;
    }

    private static Element CreateStyledPage()
    {
        var page = new View();
        page.Style.Set("display", "flex");
        page.Children.Add(new SquareText("External CSS"));
        return page;
    }

    private sealed class StatefulPage : View
    {
        private readonly int _id = Interlocked.Increment(ref Created);
        internal static int Created;

        internal static void Reset() => Created = 0;

        public override void BuildElementTree()
        {
            if (Children.Count > 0) return;
            Children.Add(new SquareText("Request " + _id));
        }
    }
}
