using Square.Events;

namespace Square.UI;

/// <summary>
/// Minimal document selection model. Square currently supports a single active range.
/// </summary>
public sealed class Selection
{
    private readonly Document _document;
    private readonly List<Range> _ranges = [];

    internal Selection(Document document) => _document = document;

    /// <summary>选区中 Range 数量。</summary>
    public int RangeCount => _ranges.Count;
    /// <summary>选区是否已折叠。</summary>
    public bool IsCollapsed => _ranges.Count == 0 || _ranges[0].Collapsed;
    /// <summary>选区锚点节点。</summary>
    public Node? AnchorNode => _ranges.Count == 0 ? null : _ranges[0].StartContainer;
    /// <summary>选区锚点偏移。</summary>
    public int AnchorOffset => _ranges.Count == 0 ? 0 : _ranges[0].StartOffset;
    /// <summary>选区焦点节点。</summary>
    public Node? FocusNode => _ranges.Count == 0 ? null : _ranges[0].EndContainer;
    /// <summary>选区焦点偏移。</summary>
    public int FocusOffset => _ranges.Count == 0 ? 0 : _ranges[0].EndOffset;

    /// <summary>获取指定索引处的 Range。</summary>
    public Range GetRangeAt(int index) => _ranges[index];

    /// <summary>添加 Range 到选区（对齐 <c>addRange</c>）。</summary>
    public void AddRange(Range range)
    {
        ArgumentNullException.ThrowIfNull(range);
        SetRange(range);
    }

    internal void SetRange(Range range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!ReferenceEquals(range.OwnerDocument, _document))
            throw new InvalidOperationException("Selection range belongs to a different document.");
        _ranges.Clear();
        _ranges.Add(range);
        _document.DispatchEvent(StandardEvents.CreateSelectionChange());
    }

    /// <summary>移除所有 Range（对齐 <c>removeAllRanges</c>）。</summary>
    public void RemoveAllRanges()
    {
        if (_ranges.Count == 0) return;
        _ranges.Clear();
        _document.DispatchEvent(StandardEvents.CreateSelectionChange());
    }

    /// <summary>返回选区文本。</summary>
    public override string ToString() => _ranges.Count == 0 ? string.Empty : _ranges[0].ToString();
}
