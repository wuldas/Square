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

    /// <summary>是否将元素样式直接写入 HTML 的 <c>style</c> 属性；默认使用 head 中的去重 CSS 类。</summary>
    public bool UseInlineStyles { get; set; }

    /// <summary>将生成的 CSS 输出到外部 stylesheet 时使用的安全 URL；为空时内嵌到文档 head。</summary>
    public string? StylesheetHref { get; set; }

    /// <summary>附加到生成 stylesheet 的可信 CSS 文本。调用方负责内容来源安全。</summary>
    public string? AdditionalCss { get; set; }
}
