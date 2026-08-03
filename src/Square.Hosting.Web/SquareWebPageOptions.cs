using Square.Native.Html;
using Microsoft.AspNetCore.Http;

namespace Square.Hosting.Web;

/// <summary>Square Web Server 页面输出选项。</summary>
public sealed class SquareWebPageOptions
{
    /// <summary>HTML 输出选项。</summary>
    public HtmlExportOptions Html { get; } = new();

    /// <summary>页面成功时返回的 HTTP 状态码。</summary>
    public int StatusCode { get; set; } = StatusCodes.Status200OK;

    /// <summary>将 HTML 生成诊断写入响应头。默认关闭，避免生产环境泄露实现信息。</summary>
    public bool IncludeDiagnosticHeaders { get; set; }
}
