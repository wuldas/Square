namespace Square.Native.Html;

/// <summary>HTML 生成诊断。</summary>
public sealed record HtmlExportDiagnostic(string ElementKind, string Message);

/// <summary>HTML 生成结果。</summary>
public sealed class HtmlExportResult
{
    /// <summary>生成的 HTML 文本。</summary>
    public required string Html { get; init; }

    /// <summary>未支持控件或被拒绝 URL 等非致命诊断。</summary>
    public IReadOnlyList<HtmlExportDiagnostic> Diagnostics { get; init; } = [];
}
