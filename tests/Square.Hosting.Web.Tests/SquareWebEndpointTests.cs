using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Square.Controls;
using Square.Hosting.Web;
using Square.Html;
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
    private const string WidgetNamespace = "urn:acme:web-tests:widgets";

    static SquareWebEndpointTests()
    {
        // 与生成代码相同的 URI+local 工厂注册；重复注册同一工厂是幂等的。
        ElementRegistry.Register(WidgetNamespace, "widget", static () => new PackageWidget());
    }

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
    public async Task InteractiveHtmlFormReflectsValueAndCheckedState()
    {
        await using var app = await StartApp(builder => builder.MapSquareInteractivePage<HtmlFormPage>("/"));
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var html = await client.GetStringAsync("/");
        // 框架自有桥接脚本不注册浏览器自定义元素。
        Assert.DoesNotContain("customElements", html, StringComparison.OrdinalIgnoreCase);
        var token = Match(html, "data-square-token=\"([^\"]+)\"");
        var inputId = int.Parse(Match(html, "data-square-id=\"(\\d+)\"[^>]* id=\"html-name\""));
        var addId = int.Parse(Match(html, "data-square-id=\"(\\d+)\"[^>]* id=\"html-add\""));
        var checkboxId = int.Parse(Match(html, "data-square-id=\"(\\d+)\"[^>]* id=\"html-check\""));
        var selectId = int.Parse(Match(html, "data-square-id=\"(\\d+)\"[^>]* id=\"html-choice\""));

        using var inputResponse = await PostEvent(client, token, 0, inputId, "input", "Ada");
        var inputUpdate = await JsonDocument.ParseAsync(await inputResponse.Content.ReadAsStreamAsync());
        Assert.Contains("value=\"Ada\"", inputUpdate.RootElement.GetProperty("bodyHtml").GetString());
        Assert.Contains("name:Ada", inputUpdate.RootElement.GetProperty("bodyHtml").GetString());

        using var clickResponse = await PostEvent(client, token, 1, addId, "click");
        var clickUpdate = await JsonDocument.ParseAsync(await clickResponse.Content.ReadAsStreamAsync());
        Assert.Contains("clicks:1", clickUpdate.RootElement.GetProperty("bodyHtml").GetString());

        using var checkedResponse = await PostEvent(client, token, 2, checkboxId, "change", checkedValue: true);
        var checkedUpdate = await JsonDocument.ParseAsync(await checkedResponse.Content.ReadAsStreamAsync());
        Assert.Contains(" checked", checkedUpdate.RootElement.GetProperty("bodyHtml").GetString());
        Assert.Contains("checked:yes", checkedUpdate.RootElement.GetProperty("bodyHtml").GetString());

        using var selectResponse = await PostEvent(client, token, 3, selectId, "change", "B");
        var selectUpdate = await JsonDocument.ParseAsync(await selectResponse.Content.ReadAsStreamAsync());
        var selectedBody = selectUpdate.RootElement.GetProperty("bodyHtml").GetString();
        Assert.Contains("choice:B", selectedBody);
        Assert.Contains("value=\"B\" selected", selectedBody);
    }

    [Fact]
    public async Task DisabledHtmlHostsRejectEventsWithoutConsumingRevision()
    {
        await using var app = await StartApp(builder => builder.MapSquareInteractivePage<HtmlFormPage>("/"));
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var html = await client.GetStringAsync("/");
        var token = Match(html, "data-square-token=\"([^\"]+)\"");
        var inputId = int.Parse(Match(html, "data-square-id=\"(\\d+)\"[^>]* id=\"html-name\""));
        var blockedId = int.Parse(Match(html, "data-square-id=\"(\\d+)\"[^>]* id=\"html-blocked\""));
        var innerId = int.Parse(Match(html, "data-square-id=\"(\\d+)\"[^>]* id=\"html-inner\""));

        // 禁用的 button 与 disabled fieldset 内的后代一律 409：即使监听器存在也不派发。
        using var blockedClick = await PostEvent(client, token, 0, blockedId, "click", ensureSuccess: false);
        Assert.Equal(HttpStatusCode.Conflict, blockedClick.StatusCode);
        using var gatedInput = await PostEvent(client, token, 0, innerId, "input", "nope", ensureSuccess: false);
        Assert.Equal(HttpStatusCode.Conflict, gatedInput.StatusCode);

        // 拒绝不消耗修订号：同一修订在启用的宿主上照常工作。
        using var live = await PostEvent(client, token, 0, inputId, "input", "Ada");
        var update = await JsonDocument.ParseAsync(await live.Content.ReadAsStreamAsync());
        Assert.Equal(1, update.RootElement.GetProperty("revision").GetInt64());
        Assert.Contains("value=\"Ada\"", update.RootElement.GetProperty("bodyHtml").GetString());
    }

    [Fact]
    public async Task InteractiveRepresentationHostForwardsClicksToSourceElement()
    {
        await using var app = await StartApp(builder => builder.MapSquareInteractivePage<WidgetPage>("/"));
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var html = await client.GetStringAsync("/");
        // 包解析 URI 与自定义元素名都不进入浏览器可见输出，也没有客户端 customElements 注册。
        Assert.DoesNotContain("urn:acme", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customElements", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Acme widget", html);
        Assert.Contains("data-square-events=\"click\"", html);
        var token = Match(html, "data-square-token=\"([^\"]+)\"");
        var widgetId = int.Parse(Match(html, "<span[^>]*data-square-id=\"(\\d+)\"[^>]*data-square-events=\"click\""));

        // 表示根携带源元素的事件 ID：按它 POST 会派发到原始宿主而不是表示节点。
        using var clickResponse = await PostEvent(client, token, 0, widgetId, "click");
        var clickUpdate = await JsonDocument.ParseAsync(await clickResponse.Content.ReadAsStreamAsync());
        Assert.Equal(1, clickUpdate.RootElement.GetProperty("revision").GetInt64());
        Assert.Contains("Acme widget", clickUpdate.RootElement.GetProperty("bodyHtml").GetString());
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
        bool ensureSuccess = true,
        bool? checkedValue = null)
    {
        var json = JsonSerializer.Serialize(new { token, revision, elementId, type, value, @checked = checkedValue });
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

    /// <summary>
    /// HTML 表单宿主使用具体 CLR 类型与真实 DOM Text 子节点：事件回写走
    /// <c>Value</c>/<c>Checked</c>/<c>Selected</c> 类型化属性，文本输出直接变更
    /// <c>Square.UI.Text.Data</c> 或经 <see cref="HtmlTextBinding"/> 响应式绑定。
    /// </summary>
    private sealed class HtmlFormPage : View
    {
        private bool _built;

        public override void BuildElementTree()
        {
            if (_built) return;
            _built = true;
            var article = new HTMLArticleElement();

            var input = new HTMLInputElement { Id = "html-name" };
            var nameOutput = new Square.UI.Text("name:");
            input.AddEventListener("input", e =>
                nameOutput.Data = "name:" + ((HTMLInputElement)e.Target!).Value);
            article.AppendChild(input);
            article.ChildNodes.Add(nameOutput);

            var check = new HTMLInputElement { Id = "html-check", Type = "checkbox" };
            var checkedState = new ObservableValue<bool>(false);
            check.AddEventListener("change", e =>
                checkedState.Value = ((HTMLInputElement)e.Target!).Checked);
            var checkedOutput = new Square.UI.Text("checked:no");
            HtmlTextBinding.Bind(
                article,
                checkedOutput,
                () => "checked:" + (checkedState.Value ? "yes" : "no"),
                checkedState);
            article.AppendChild(check);
            article.ChildNodes.Add(checkedOutput);

            var select = new HTMLSelectElement { Id = "html-choice", Value = "A" };
            var first = new HTMLOptionElement { Value = "A", Selected = true };
            first.ChildNodes.Add(new Square.UI.Text("A"));
            var second = new HTMLOptionElement { Value = "B" };
            second.ChildNodes.Add(new Square.UI.Text("B"));
            select.AppendChild(first);
            select.AppendChild(second);
            var choiceOutput = new Square.UI.Text("choice:A");
            select.AddEventListener("change", e =>
                choiceOutput.Data = "choice:" + ((HTMLSelectElement)e.Target!).Value);
            article.AppendChild(select);
            article.ChildNodes.Add(choiceOutput);

            var add = new HTMLButtonElement { Id = "html-add" };
            add.ChildNodes.Add(new Square.UI.Text("Add"));
            var clickOutput = new Square.UI.Text("clicks:0");
            add.AddEventListener("click", () => clickOutput.Data = "clicks:1");
            article.AppendChild(add);
            article.ChildNodes.Add(clickOutput);

            // 禁用宿主与 disabled fieldset 后代都必须被会话网关拦截。
            var blocked = new HTMLButtonElement { Id = "html-blocked", Disabled = true };
            blocked.ChildNodes.Add(new Square.UI.Text("Blocked"));
            blocked.AddEventListener("click", () => clickOutput.Data = "clicks:blocked");
            article.AppendChild(blocked);

            var fieldset = new HTMLFieldSetElement { Id = "html-fieldset" };
            fieldset.SetAttribute("disabled", "");
            var inner = new HTMLInputElement { Id = "html-inner" };
            inner.AddEventListener("input", e => clickOutput.Data = "clicks:inner");
            fieldset.AppendChild(inner);
            article.AppendChild(fieldset);

            Children.Add(article);
        }
    }

    /// <summary>
    /// 包导出组件挂到交互页：模板工厂创建即打标记，导出走安全静态表示，
    /// 点击事件按表示根上的源事件 ID 派发回原始宿主。
    /// </summary>
    private sealed class WidgetPage : View
    {
        private bool _built;
        internal int Clicks { get; private set; }

        public override void BuildElementTree()
        {
            if (_built) return;
            _built = true;
            Element widget = new UIDocument().CreateComponentElement(WidgetNamespace, "widget");
            widget.AddEventListener("click", () => Clicks++);
            Children.Add(widget);
        }
    }

    private sealed class PackageWidget : View, IHtmlStaticRepresentation
    {
        public HTMLElement CreateHtmlRepresentation()
        {
            var span = new HTMLSpanElement();
            span.ChildNodes.Add(new Square.UI.Text("Acme widget"));
            return span;
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
