namespace Square.Native.Html;

/// <summary>控制静态语义 HTML 输出。</summary>
public sealed class HtmlExportOptions
{
    /// <summary>HTML 文档标题。为空时使用根元素标签名。</summary>
    public string? Title { get; set; }

    /// <summary>HTML 文档语言。</summary>
    public string Language { get; set; } = "en";

    /// <summary>是否输出完整 doctype/html/head/body 文档；false 时只输出元素片段。</summary>
    public bool IncludeDocument { get; set; } = true;

    /// <summary>是否输出 Square 的基础浏览器样式。</summary>
    public bool IncludeBaselineCss { get; set; } = true;

    /// <summary>附加到 head 的可信 CSS 文本。调用方负责内容来源安全。</summary>
    public string? AdditionalCss { get; set; }
}
