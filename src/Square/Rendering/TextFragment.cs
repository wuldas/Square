using Square.Graphics;
using Square.UI;
using System.Globalization;

namespace Square.Rendering;

/// <summary>文本片段，包含所属元素、文本、字体和字符级片段。</summary>
public sealed record TextFragment(Element Element, string Text, Font Font, Rect Bounds, IReadOnlyList<TextCharacterFragment> Characters)
{
    private readonly int[] _textElementStarts = StringInfo.ParseCombiningCharacters(Text);
    /// <summary>生成该片段的原始布局；存在时可在选择前景重绘中保持 shaping。</summary>
    public TextLayout? Layout { get; init; }
    /// <summary>原始布局在 DisplayTree 中的绘制原点。</summary>
    public Point LayoutOrigin { get; init; }

    /// <summary>按坐标命中测试，返回 UTF-16 偏移。</summary>
    public int HitTestOffset(Point point)
    {
        if (Characters.Count == 0) return 0;

        var lines = Characters
            .GroupBy(character => character.Bounds.Y)
            .Select(group => new
            {
                Characters = group.ToArray(),
                Top = group.Min(character => character.SelectionBounds.Top),
                Bottom = group.Max(character => character.SelectionBounds.Bottom)
            })
            .ToArray();
        var nearestLine = lines[0];
        var nearestLineDistance = float.MaxValue;
        var nearestLineCenterDistance = float.MaxValue;
        foreach (var line in lines)
        {
            var distance = point.Y < line.Top ? line.Top - point.Y :
                point.Y > line.Bottom ? point.Y - line.Bottom : 0;
            var centerDistance = Math.Abs(point.Y - (line.Top + line.Bottom) / 2f);
            if (distance > nearestLineDistance ||
                distance == nearestLineDistance && centerDistance >= nearestLineCenterDistance)
                continue;
            nearestLine = line;
            nearestLineDistance = distance;
            nearestLineCenterDistance = centerDistance;
        }

        var nearest = 0;
        var nearestForward = false;
        var nearestDistance = float.MaxValue;
        for (var i = 0; i < nearestLine.Characters.Length; i++)
        {
            var character = nearestLine.Characters[i];
            var bounds = character.Bounds;
            var distance = point.X < bounds.Left ? bounds.Left - point.X :
                point.X > bounds.Right ? point.X - bounds.Right : 0;
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearestForward = point.X > bounds.X + bounds.Width / 2f;
            nearest = character.Direction == BidiDirection.Rtl
                ? nearestForward ? character.StartOffset : character.EndOffset
                : nearestForward ? character.EndOffset : character.StartOffset;
        }
        return SnapToTextElementBoundary(nearest, nearestForward);
    }

    private int SnapToTextElementBoundary(int offset, bool forward)
    {
        if (offset <= 0 || offset >= Text.Length) return Math.Clamp(offset, 0, Text.Length);
        var index = Array.BinarySearch(_textElementStarts, offset);
        if (index >= 0) return offset;
        index = ~index;
        if (forward) return index < _textElementStarts.Length ? _textElementStarts[index] : Text.Length;
        return _textElementStarts[index - 1];
    }
}

/// <summary>字符级片段，包含偏移范围、布局边界和选择边界。</summary>
public readonly record struct TextCharacterFragment(int StartOffset, int EndOffset, Rect Bounds, Rect SelectionBounds)
{
    public BidiDirection Direction { get; init; }
}
