using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Square.CSS.Engine;
using Square.Native.Html;
using Square.UI;
using System.Security.Cryptography;
using System.Text.Json;

namespace Square.Hosting.Web;

/// <summary>ASP.NET Core endpoint 映射扩展。</summary>
public static class SquareWebEndpointRouteBuilderExtensions
{
    private static readonly string[] InteractiveMethods = [HttpMethods.Get, HttpMethods.Post];

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

    /// <summary>映射一个保留每个浏览器页面状态并桥接 C# 事件的交互页面。</summary>
    public static IEndpointConventionBuilder MapSquareInteractivePage<TPage>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Action<SquareWebInteractivePageOptions>? configure = null)
        where TPage : Element, new() =>
        endpoints.MapSquareInteractivePage(pattern, static _ => new TPage(), configure);

    /// <summary>映射一个保留每个浏览器页面状态并桥接 C# 事件的交互页面。</summary>
    public static IEndpointConventionBuilder MapSquareInteractivePage(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<HttpContext, Element> pageFactory,
        Action<SquareWebInteractivePageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(pageFactory);

        var options = new SquareWebInteractivePageOptions();
        configure?.Invoke(options);
        if (options.SessionIdleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.SessionIdleTimeout));
        if (options.MaxSessions <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxSessions));
        options.Html.EnableInteractions = true;
        options.Html.IncludeDocument = true;
        options.Html.StylesheetHref = null;

        var sessions = new SquareWebInteractiveSessionStore(options.SessionIdleTimeout, options.MaxSessions);
        if (endpoints.ServiceProvider.GetService(typeof(IHostApplicationLifetime)) is IHostApplicationLifetime lifetime)
            lifetime.ApplicationStopping.Register(sessions.Dispose);

        return endpoints.MapMethods(pattern, InteractiveMethods, async context =>
        {
            if (HttpMethods.IsGet(context.Request.Method))
            {
                await WriteInteractivePageAsync(context, pageFactory, options, sessions);
                return;
            }

            await DispatchInteractiveEventAsync(context, options, sessions);
        });
    }

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

    private static async Task WriteInteractivePageAsync(
        HttpContext context,
        Func<HttpContext, Element> pageFactory,
        SquareWebInteractivePageOptions options,
        SquareWebInteractiveSessionStore sessions)
    {
        SquareWebInteractiveSession? session = null;
        var stored = false;
        try
        {
            var page = pageFactory(context) ?? throw new InvalidOperationException("The Square page factory returned null.");
            session = new SquareWebInteractiveSession(RandomNumberGenerator.GetHexString(32), page);
            if (!sessions.TryAdd(session))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            stored = true;

            var result = HtmlExporter.Export(page, options.Html);
            if (options.IncludeDiagnosticHeaders && result.Diagnostics.Count > 0)
                context.Response.Headers["X-Square-Html-Diagnostics"] = result.Diagnostics.Count.ToString();
            context.Response.Headers.CacheControl = "no-store";
            context.Response.StatusCode = options.StatusCode;
            context.Response.ContentType = "text/html; charset=utf-8";
            var bootstrap = $"<script data-square-token=\"{session.Token}\" data-square-revision=\"0\">{SquareWebInteractiveRuntime.Script}</script>";
            var html = result.Html.Replace("</body>", bootstrap + "</body>", StringComparison.Ordinal);
            await context.Response.WriteAsync(html, context.RequestAborted);
            session = null;
        }
        finally
        {
            if (session != null)
            {
                if (stored) sessions.Remove(session.Token);
                else session.Dispose();
            }
        }
    }

    private static async Task DispatchInteractiveEventAsync(
        HttpContext context,
        SquareWebInteractivePageOptions options,
        SquareWebInteractiveSessionStore sessions)
    {
        if (!context.Request.HasJsonContentType())
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }
        const long maxRequestBodySize = 1_048_576;
        if (context.Request.ContentLength is > maxRequestBodySize)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySizeFeature)
            bodySizeFeature.MaxRequestBodySize = maxRequestBodySize;

        SquareWebEventRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync(
                SquareWebJsonContext.Default.SquareWebEventRequest,
                context.RequestAborted);
        }
        catch (JsonException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (request == null ||
            string.IsNullOrWhiteSpace(request.Token) ||
            string.IsNullOrWhiteSpace(request.Type) ||
            request.ElementId <= 0 ||
            request.Value?.Length > 1_000_000)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        var type = request.Type.Trim().ToLowerInvariant();
        if (type is not ("click" or "input" or "change"))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        request = request with { Type = type };

        if (!sessions.TryGet(request.Token, out var session))
        {
            context.Response.StatusCode = StatusCodes.Status410Gone;
            return;
        }
        if (request.Revision != session.Revision)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        var dispatch = await session.DispatchAsync(request, options.Html, context.RequestAborted);
        if (dispatch == null)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new SquareWebEventResponse(
                dispatch.Export.BodyHtml,
                dispatch.Export.Css,
                dispatch.Revision,
                dispatch.DefaultPrevented),
            SquareWebJsonContext.Default.SquareWebEventResponse,
            context.RequestAborted);
    }
}
