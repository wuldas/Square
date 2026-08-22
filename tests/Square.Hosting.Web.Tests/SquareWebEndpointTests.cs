using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Square.Controls;
using Square.Hosting.Web;
using Square.Platform;
using Square.Runtime.Binding;
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

    [Fact]
    public async Task InteractiveEndpointDispatchesEventsAndUpdatesReactiveTree()
    {
        await using var app = await StartApp(builder => builder.MapSquareInteractivePage<InteractivePage>("/"));
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var html = await client.GetStringAsync("/");
        Assert.Contains("data-square-token=", html);
        Assert.Contains("document.addEventListener", html);
        var token = Match(html, "data-square-token=\"([^\"]+)\"");
        var inputId = int.Parse(Match(html, "data-square-id=\"(\\d+)\"[^>]* id=\"name\""));
        var buttonId = int.Parse(Match(html, "data-square-id=\"(\\d+)\"[^>]* id=\"add\""));
        var checkBoxId = int.Parse(Match(html, "data-square-id=\"(\\d+)\"[^>]* id=\"remember\""));

        using var inputResponse = await PostEvent(client, token, 0, inputId, "input", "Ada");
        var inputUpdate = await JsonDocument.ParseAsync(await inputResponse.Content.ReadAsStreamAsync());
        Assert.Equal(1, inputUpdate.RootElement.GetProperty("revision").GetInt64());
        Assert.Contains("value=\"Ada\"", inputUpdate.RootElement.GetProperty("bodyHtml").GetString());
        Assert.Contains(">Ada</span>", inputUpdate.RootElement.GetProperty("bodyHtml").GetString());

        using var clickResponse = await PostEvent(client, token, 1, buttonId, "click");
        var clickUpdate = await JsonDocument.ParseAsync(await clickResponse.Content.ReadAsStreamAsync());
        var body = clickUpdate.RootElement.GetProperty("bodyHtml").GetString();
        Assert.Equal(2, clickUpdate.RootElement.GetProperty("revision").GetInt64());
        Assert.Contains("Added Ada", body);
        Assert.Contains("Item Ada", body);

        using var checkResponse = await PostEvent(client, token, 2, checkBoxId, "click");
        var checkUpdate = await JsonDocument.ParseAsync(await checkResponse.Content.ReadAsStreamAsync());
        Assert.Contains("type=\"checkbox\" checked", checkUpdate.RootElement.GetProperty("bodyHtml").GetString());
    }

    [Fact]
    public async Task InteractiveEndpointIsolatesSessionsAndRejectsUnknownToken()
    {
        await using var app = await StartApp(builder => builder.MapSquareInteractivePage<InteractivePage>("/"));
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var first = await client.GetStringAsync("/");
        var second = await client.GetStringAsync("/");
        var firstToken = Match(first, "data-square-token=\"([^\"]+)\"");
        var secondToken = Match(second, "data-square-token=\"([^\"]+)\"");
        Assert.NotEqual(firstToken, secondToken);

        var inputId = int.Parse(Match(first, "data-square-id=\"(\\d+)\"[^>]* id=\"name\""));
        using var response = await PostEvent(client, "missing", 0, inputId, "input", "Ada", ensureSuccess: false);
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task InteractiveEndpointRejectsStaleRevisionAndExpiredSession()
    {
        await using var app = await StartApp(builder => builder.MapSquareInteractivePage<InteractivePage>(
            "/",
            options => options.SessionIdleTimeout = TimeSpan.FromMilliseconds(500)));
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var html = await client.GetStringAsync("/");
        var token = Match(html, "data-square-token=\"([^\"]+)\"");
        var inputId = int.Parse(Match(html, "data-square-id=\"(\\d+)\"[^>]* id=\"name\""));

        using var first = await PostEvent(client, token, 0, inputId, "input", "Ada");
        using var stale = await PostEvent(client, token, 0, inputId, "input", "Grace", ensureSuccess: false);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        await Task.Delay(650);
        using var expired = await PostEvent(client, token, 1, inputId, "input", "Grace", ensureSuccess: false);
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
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

    private static async Task<HttpResponseMessage> PostEvent(
        HttpClient client,
        string token,
        long revision,
        int elementId,
        string type,
        string? value = null,
        bool ensureSuccess = true)
    {
        var json = JsonSerializer.Serialize(new { token, revision, elementId, type, value });
        var response = await client.PostAsync("/", new StringContent(json, Encoding.UTF8, "application/json"));
        if (ensureSuccess) response.EnsureSuccessStatusCode();
        return response;
    }

    private static string Match(string value, string pattern)
    {
        var match = Regex.Match(value, pattern, RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Pattern '{pattern}' was not found.");
        return match.Groups[1].Value;
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

    private sealed class InteractivePage : View
    {
        private readonly ObservableValue<string> _name = new("");
        private readonly ObservableValue<bool> _showResult = new(false);
        private readonly ObservableCollection<string> _items = [];
        private bool _built;

        public override void BuildElementTree()
        {
            if (_built) return;
            _built = true;

            var input = new Input { Id = "name" };
            input.BindProperty("Value", _name);
            input.AddEventListener("input", e => _name.Value = ((Input)e.Target!).Value);
            Children.Add(input);

            var value = new SquareText();
            value.BindProperty("TextContent", _name);
            Children.Add(value);

            var add = new Button("Add") { Id = "add" };
            add.AddEventListener("click", () =>
            {
                _showResult.Value = true;
                _items.Add("Item " + _name.Value);
            });
            Children.Add(add);

            var remembered = new ObservableValue<bool>(false);
            var checkBox = new CheckBox { Id = "remember", TextContent = "Remember" };
            checkBox.BindProperty("IsChecked", remembered);
            checkBox.AddEventListener("change", e => remembered.Value = ((CheckBox)e.Target!).IsChecked);
            Children.Add(checkBox);

            var show = new Square.Controls.Primitives.ShowNode(
                _showResult,
                () => new SquareText("Added " + _name.Value));
            RegisterGeneratedResource(show);
            show.AttachTo(this);

            var loop = Square.Controls.Primitives.ForNode.Create(
                _items,
                item => new SquareText(item));
            RegisterGeneratedResource(loop);
            loop.AttachTo(this);
        }
    }
}
