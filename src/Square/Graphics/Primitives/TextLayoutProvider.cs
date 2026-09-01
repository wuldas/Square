using System.Text;
using System.Threading;

namespace Square.Graphics;

/// <summary>为一个 <see cref="TextLayout"/> 创建权威布局快照。</summary>
public interface ITextLayoutProvider : ITextMetricsProvider
{
    /// <summary>尝试创建布局快照；不支持当前选项时返回 false 并由 Square 默认实现回退。</summary>
    bool TryCreateLayout(TextLayout layout, out ITextLayoutSnapshot? snapshot);
}

/// <summary>由渲染后端提供权威文本布局服务。</summary>
public interface ITextLayoutProviderSource
{
    /// <summary>该后端使用的文本布局提供器。</summary>
    ITextLayoutProvider TextLayoutProvider { get; }
}

/// <summary>不可变文本布局结果；坐标均为相对布局原点的逻辑像素。</summary>
public interface ITextLayoutSnapshot
{
    /// <summary>测量尺寸。</summary>
    Size Size { get; }
    /// <summary>实际墨迹边界。</summary>
    Rect InkBounds { get; }
    /// <summary>按视觉顺序排列的行与 cluster。</summary>
    IReadOnlyList<TextLayoutLine> Lines { get; }
    /// <summary>测量第一行行首到 UTF-16 偏移的距离。</summary>
    float MeasureOffset(int utf16Offset);
    /// <summary>返回 UTF-16 偏移对应的 caret 位置。</summary>
    Point GetCaretPoint(int utf16Offset, bool trailing = false);
    /// <summary>按相对坐标命中 UTF-16 偏移。</summary>
    int HitTestPoint(Point point);
    /// <summary>返回 UTF-16 范围的选择矩形。</summary>
    IReadOnlyList<Rect> GetSelectionRects(int start, int length);
}

/// <summary>权威布局中的一行。</summary>
public sealed record TextLayoutLine(
    int StartOffset,
    int EndOffset,
    float Width,
    float Height,
    float Baseline,
    IReadOnlyList<TextLayoutCluster> Clusters)
{
    /// <summary>相对布局原点的行顶坐标。</summary>
    public float Top { get; init; }
}

/// <summary>不可拆分的文本 cluster 及其视觉边界。</summary>
public readonly record struct TextLayoutCluster(
    int StartOffset,
    int EndOffset,
    Rune Rune,
    Rect Bounds,
    BidiDirection Direction);

/// <summary>将权威文本布局提供器限定到当前异步/线程执行上下文。</summary>
public static class TextLayoutProviderContext
{
    private static readonly AsyncLocal<ITextLayoutProvider?> CurrentProvider = new();

    /// <summary>当前布局提供器；未选择后端提供器时为 null。</summary>
    public static ITextLayoutProvider? Current => CurrentProvider.Value;

    /// <summary>在当前执行上下文中临时使用指定提供器。</summary>
    public static IDisposable Push(ITextLayoutProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var previous = CurrentProvider.Value;
        CurrentProvider.Value = provider;
        return new Scope(previous);
    }

    /// <summary>在当前执行上下文中临时禁用后端布局提供器。</summary>
    public static IDisposable Suppress()
    {
        var previous = CurrentProvider.Value;
        CurrentProvider.Value = null;
        return new Scope(previous);
    }

    private sealed class Scope(ITextLayoutProvider? previous) : IDisposable
    {
        private ITextLayoutProvider? _previous = previous;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CurrentProvider.Value = _previous;
            _previous = null;
        }
    }
}
