using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Square.CSS.Engine;
using Square.Native.Html;
using Square.UI;

namespace Square.Hosting.Web;

/// <summary>ASP.NET Core endpoint 映射扩展。</summary>
public static class SquareWebEndpointRouteBuilderExtensions
{
    /// <summary>映射一个每请求创建独立 Square 组件实例的 GET 页面。</summary>
    public static IEndpointConventionBuilder MapSquarePage<TPage>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Action<SquareWebPageOptions>? configure = null)
        where TPage : Element, new() =>
        endpoints.MapSquarePage(pattern, static _ => new TPage(), configure);

    /// <summary>映射一个返回指定页面类型生成 CSS 的 stylesheet。</summary>
    public static IEndpointConventionBuilder MapSquareStylesheet<TPage>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Action<HtmlExportOptions>? configure = null)
        where TPage : Element, new() =>
        endpoints.MapSquareStylesheet(pattern, static _ => new TPage(), configure);

    /// <summary>映射一个每请求创建独立 Square 组件实例的 GET 页面。</summary>
    public static IEndpointConventionBuilder MapSquarePage(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<HttpContext, Element> pageFactory,
        Action<SquareWebPageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(pageFactory);

        var options = new SquareWebPageOptions();
        configure?.Invoke(options);
        return endpoints.MapGet(pattern, async context =>
        {
            Element? page = null;
            try
            {
                page = pageFactory(context) ?? throw new InvalidOperationException("The Square page factory returned null.");
                var result = HtmlExporter.Export(page, options.Html);
                if (options.IncludeDiagnosticHeaders && result.Diagnostics.Count > 0)
                    context.Response.Headers["X-Square-Html-Diagnostics"] = result.Diagnostics.Count.ToString();
                context.Response.StatusCode = options.StatusCode;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(result.Html, context.RequestAborted);
            }
            finally
            {
                if (page != null)
                {
                    CssStyleReconciler.UnregisterScopesForTree(page);
                    page.DiscardGeneratedSubtree();
                }
            }
        });
    }

    /// <summary>映射一个返回静态 Square CSS 的 GET stylesheet。</summary>
    public static IEndpointConventionBuilder MapSquareStylesheet(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<HttpContext, Element> pageFactory,
        Action<HtmlExportOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(pageFactory);

        var options = new HtmlExportOptions
        {
            IncludeDocument = false,
            UseInlineStyles = false
        };
        configure?.Invoke(options);
        options.IncludeDocument = false;
        options.UseInlineStyles = false;
        options.StylesheetHref = null;

        return endpoints.MapGet(pattern, async context =>
        {
            Element? page = null;
            try
            {
                page = pageFactory(context) ?? throw new InvalidOperationException("The Square stylesheet page factory returned null.");
                var result = HtmlExporter.Export(page, options);
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/css; charset=utf-8";
                await context.Response.WriteAsync(result.Css, context.RequestAborted);
            }
            finally
            {
                if (page != null)
                {
                    CssStyleReconciler.UnregisterScopesForTree(page);
                    page.DiscardGeneratedSubtree();
                }
            }
        });
    }
}
