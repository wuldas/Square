using Microsoft.AspNetCore.Http;
using Square.Native.Html;

namespace Square.Hosting.Web;

/// <summary>Square 交互页面输出和服务端会话选项。</summary>
public sealed class SquareWebInteractivePageOptions
{
    /// <summary>HTML 输出选项。</summary>
    public HtmlExportOptions Html { get; } = new();

    /// <summary>页面成功时返回的 HTTP 状态码。</summary>
    public int StatusCode { get; set; } = StatusCodes.Status200OK;

    /// <summary>将 HTML 生成诊断计数写入响应头。</summary>
    public bool IncludeDiagnosticHeaders { get; set; }

    /// <summary>无事件后保留页面状态的时间。</summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>此 endpoint 同时保留的最大页面会话数。</summary>
    public int MaxSessions { get; set; } = 1024;
}
