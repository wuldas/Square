using Square.Graphics;
using Square.UI;
using System.Globalization;

namespace Square.Rendering;

/// <summary>文本片段，包含所属元素、文本、字体和字符级片段。</summary>
public sealed record TextFragment(Element Element, string Text, Font Font, Rect Bounds, IReadOnlyList<TextCharacterFragment> Characters)
{
    private readonly int[] _textElementStarts = StringInfo.ParseCombiningCharacters(Text);

    /// <summary>按坐标命中测试，返回 UTF-16 偏移。</summary>
    public int HitTestOffset(Point point)
    {
        if (Characters.Count == 0) return 0;

        for (var i = 0; i < Characters.Count; i++)
        {
            var character = Characters[i];
            if (!character.Bounds.Contains(point)) continue;
            var midpoint = character.Bounds.X + character.Bounds.Width / 2f;
            var leadingOffset = character.Direction == BidiDirection.Rtl
                ? character.EndOffset
                : character.StartOffset;
            var trailingOffset = character.Direction == BidiDirection.Rtl
                ? character.StartOffset
                : character.EndOffset;
            return SnapToTextElementBoundary(
                point.X < midpoint ? leadingOffset : trailingOffset,
                forward: point.X >= midpoint);
        }

        var nearest = 0;
        var nearestForward = false;
        var nearestDistance = float.MaxValue;
        for (var i = 0; i < Characters.Count; i++)
        {
            var bounds = Characters[i].Bounds;
            var dx = point.X < bounds.Left ? bounds.Left - point.X : point.X > bounds.Right ? point.X - bounds.Right : 0;
            var dy = point.Y < bounds.Top ? bounds.Top - point.Y : point.Y > bounds.Bottom ? point.Y - bounds.Bottom : 0;
            var distance = dx * dx + dy * dy;
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearestForward = point.X > bounds.X + bounds.Width / 2f;
            nearest = Characters[i].Direction == BidiDirection.Rtl
                ? nearestForward ? Characters[i].StartOffset : Characters[i].EndOffset
                : nearestForward ? Characters[i].EndOffset : Characters[i].StartOffset;
        }
        return SnapToTextElementBoundary(nearest, nearestForward);
    }

    private int SnapToTextElementBoundary(int offset, bool forward)
    {
        if (offset <= 0 || offset >= Text.Length) return Math.Clamp(offset, 0, Text.Length);
        var index = Array.BinarySearch(_textElementStarts, offset);
        if (index >= 0) return offset;
        index = ~index;
        return forward && index < _textElementStarts.Length ? _textElementStarts[index] : _textElementStarts[index - 1];
    }
}

/// <summary>字符级片段，包含偏移范围、布局边界和选择边界。</summary>
public readonly record struct TextCharacterFragment(int StartOffset, int EndOffset, Rect Bounds, Rect SelectionBounds)
{
    public BidiDirection Direction { get; init; }
}
