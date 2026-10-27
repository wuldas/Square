using System.Runtime.CompilerServices;
using Square.Graphics;
using Square.UI;

namespace Square.Rendering;

/// <summary>
/// One visible piece of a DOM <see cref="Square.UI.Text"/> node after whitespace processing and
/// line wrapping. <see cref="SourceOffset"/>/<see cref="SourceCharOffsets"/>/<see cref="SourceLength"/>
/// map every character back to its UTF-16 position in the node's <c>data</c>, so document
/// selection ranges survive whitespace collapse, text transforms and wrap splits.
/// </summary>
internal readonly record struct HtmlTextFragment(
    string Text,
    Rect Bounds,
    int SourceOffset,
    int[]? SourceCharOffsets,
    int SourceLength,
    BidiDirection Direction,
    BidiTextMode UnicodeBidi);

internal sealed class HtmlTextLayoutData
{
    public List<HtmlTextFragment> Fragments { get; } = [];
}

/// <summary>
/// Layout geometry of DOM Text nodes, keyed by the source node — the Element-keyed
/// <see cref="Square.UI.ElementLayoutStore"/> cannot hold non-element nodes. Written by the CSS
/// normal-flow arrange pass and read by <c>HTMLElement.Paint</c> and descendant decorations.
/// </summary>
internal static class HtmlTextLayoutStore
{
    private static readonly ConditionalWeakTable<Square.UI.Text, HtmlTextLayoutData> Data = new();

    public static HtmlTextLayoutData Get(Square.UI.Text node) => Data.GetOrCreateValue(node);

    public static bool TryGet(Square.UI.Text node, out HtmlTextLayoutData data) =>
        Data.TryGetValue(node, out data!);
}
