using Square.Hosting.Web;
using Square.Sample.WebServer.Components;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapSquarePage<Main>("/", options =>
{
    options.Html.Title = "PiSquared";
    options.Html.Language = "zh-CN";
});

app.MapSquarePage("/hello/{name}", context =>
{
    var page = new HelloPage();
    page.Name.Value = "Hello, " + (context.Request.RouteValues["name"]?.ToString() ?? "Square") + ".";
    return page;
}, options => options.Html.Title = "Square route values");

app.Run();
