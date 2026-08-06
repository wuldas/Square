using System.Runtime.CompilerServices;
using Square.Graphics;

namespace Square.UI;

internal sealed class ElementLayoutData
{
    public Size CssDesiredSize { get; set; }
    public IReadOnlyList<TextLayoutFragment>? CssTextFragments { get; set; }
    public bool IsFixedRoot { get; set; }
}

internal readonly record struct TextLayoutFragment(
    string Text,
    Rect Bounds,
    BidiDirection Direction,
    BidiTextMode UnicodeBidi);

internal static class ElementLayoutStore
{
    private static readonly ConditionalWeakTable<Element, ElementLayoutData> Data = new();

    public static ElementLayoutData Get(Element element) => Data.GetOrCreateValue(element);

    public static bool TryGet(Element element, out ElementLayoutData data) => Data.TryGetValue(element, out data!);
}
