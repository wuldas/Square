namespace Square.Native.Html;

/// <summary>HTML 生成诊断。</summary>
public sealed record HtmlExportDiagnostic(string ElementKind, string Message);

/// <summary>HTML 生成结果。</summary>
public sealed class HtmlExportResult
{
    /// <summary>生成的 HTML 文本。</summary>
    public required string Html { get; init; }

    /// <summary>不含 document/head/body 外壳的根元素 HTML。</summary>
    public string BodyHtml { get; init; } = "";

    /// <summary>
    /// 生成的完整 CSS 文本。
    /// 内嵌样式模式下完整文档会将它写入 head；外部 stylesheet 模式下调用方应将它写入 <c>StylesheetHref</c> 对应的资源。
    /// </summary>
    public string Css { get; init; } = "";

    /// <summary>未支持控件或被拒绝 URL 等非致命诊断。</summary>
    public IReadOnlyList<HtmlExportDiagnostic> Diagnostics { get; init; } = [];
}
