using Square.UI;

[assembly: ElementExport("urn:square:markdown", "markdown", "MarkdownViewer", typeof(Square.Extensions.Markdown.MarkdownViewer))]

namespace Square.Extensions.Markdown;

/// <summary>Markdown 扩展控件注册。</summary>
public static class MarkdownRegistration
{
    private static bool _registered;

    /// <summary>注册 MarkdownViewer 标签。重复调用安全。</summary>
    public static void RegisterDefaults()
    {
        if (_registered) return;
        _registered = true;
        ElementRegistry.Register("MarkdownViewer", static () => new MarkdownViewer());
    }
}
