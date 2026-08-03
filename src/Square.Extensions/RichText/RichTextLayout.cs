using System.Text;
using Square.Graphics;

namespace Square.Extensions.RichText;

public sealed record RichTextLayoutFragment(
    RichTextRun Run,
    int StartOffset,
    int EndOffset,
    Font Font,
    Rect Bounds);

public sealed record RichTextLayoutLine(
    int StartOffset,
    int EndOffset,
    Rect Bounds,
    IReadOnlyList<RichTextLayoutFragment> Fragments);

public sealed class RichTextBlockLayout
{
    public RichTextBlockLayout(RichTextBlock block, Rect bounds, IReadOnlyList<RichTextLayoutLine> lines)
    {
        Block = block;
        Bounds = bounds;
        Lines = lines;
    }

    public RichTextBlock Block { get; }
    public Rect Bounds { get; }
    public IReadOnlyList<RichTextLayoutLine> Lines { get; }

    public int HitTestOffset(Point point)
    {
        var line = FindNearestLine(point.Y);
        if (line.Fragments.Count == 0) return line.StartOffset;
        foreach (var fragment in line.Fragments)
        {
            if (point.X > fragment.Bounds.Right) continue;
            var localX = Math.Max(0, point.X - fragment.Bounds.X);
            var localOffset = HitTestOffset(fragment.Run.Text, fragment.Font, localX);
            return Math.Clamp(fragment.StartOffset + localOffset, fragment.StartOffset, fragment.EndOffset);
        }
        return line.EndOffset;
    }

    public Rect GetCaretRect(int offset)
    {
        if (offset < 0 || offset > Block.PlainText.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var line = Lines[GetLineIndex(offset)];
        if (line.Fragments.Count == 0) return new Rect(line.Bounds.X, line.Bounds.Y, 1, line.Bounds.Height);

        foreach (var fragment in line.Fragments)
        {
            if (offset > fragment.EndOffset) continue;
            var localOffset = Math.Clamp(offset - fragment.StartOffset, 0, fragment.Run.Text.Length);
            var x = fragment.Bounds.X + MeasureOffset(fragment.Run.Text, fragment.Font, localOffset);
            return new Rect(x, line.Bounds.Y, 1, line.Bounds.Height);
        }
        return new Rect(line.Bounds.Right, line.Bounds.Y, 1, line.Bounds.Height);
    }

    public int GetLineIndex(int offset)
    {
        if (offset < 0 || offset > Block.PlainText.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        for (var i = 0; i < Lines.Count; i++)
        {
            if (offset < Lines[i].EndOffset || i == Lines.Count - 1)
                return i;
        }
        return Lines.Count - 1;
    }

    public IReadOnlyList<Rect> GetSelectionRects(int startOffset, int endOffset)
    {
        if (startOffset < 0 || endOffset < startOffset || endOffset > Block.PlainText.Length)
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        if (startOffset == endOffset) return [];

        var rects = new List<Rect>();
        foreach (var line in Lines)
        {
            var start = Math.Max(startOffset, line.StartOffset);
            var end = Math.Min(endOffset, line.EndOffset);
            if (end <= start) continue;
            var startX = MeasureOffsetOnLine(line, start);
            var endX = MeasureOffsetOnLine(line, end);
            var left = startX;
            var right = endX;
            if (TryGetGlyphAt(line, start, out var firstGlyph))
                left += Math.Min(0, firstGlyph.InkBounds.Left);
            if (TryGetGlyphBefore(line, end, out var lastGlyph))
                right += Math.Max(0, lastGlyph.InkBounds.Right - lastGlyph.AdvanceX);
            rects.Add(new Rect(left, line.Bounds.Y, Math.Max(1, right - left), line.Bounds.Height));
        }
        return rects;
    }

    private static bool TryGetGlyphAt(RichTextLayoutLine line, int offset, out GlyphMetrics glyph)
    {
        foreach (var fragment in line.Fragments)
        {
            if (offset < fragment.StartOffset || offset >= fragment.EndOffset) continue;
            var localOffset = offset - fragment.StartOffset;
            var status = Rune.DecodeFromUtf16(fragment.Run.Text.AsSpan(localOffset), out var rune, out _);
            if (status != System.Buffers.OperationStatus.Done) break;
            glyph = TextMetrics.GetGlyphMetrics(fragment.Font, rune);
            return true;
        }
        glyph = default;
        return false;
    }

    private static bool TryGetGlyphBefore(RichTextLayoutLine line, int offset, out GlyphMetrics glyph)
    {
        foreach (var fragment in line.Fragments)
        {
            if (offset <= fragment.StartOffset || offset > fragment.EndOffset) continue;
            var localEnd = offset - fragment.StartOffset;
            var status = Rune.DecodeLastFromUtf16(fragment.Run.Text.AsSpan(0, localEnd), out var rune, out _);
            if (status != System.Buffers.OperationStatus.Done) break;
            glyph = TextMetrics.GetGlyphMetrics(fragment.Font, rune);
            return true;
        }
        glyph = default;
        return false;
    }

    private static float MeasureOffsetOnLine(RichTextLayoutLine line, int offset)
    {
        if (line.Fragments.Count == 0) return line.Bounds.X;
        foreach (var fragment in line.Fragments)
        {
            if (offset > fragment.EndOffset) continue;
            var localOffset = Math.Clamp(offset - fragment.StartOffset, 0, fragment.Run.Text.Length);
            return fragment.Bounds.X + MeasureOffset(fragment.Run.Text, fragment.Font, localOffset);
        }
        return line.Bounds.Right;
    }

    internal static float MeasureAdvance(Font font, Rune rune)
        => TextMetrics.GetGlyphMetrics(font, rune).AdvanceX;

    internal static float MeasureText(string text, Font font)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes()) width += MeasureAdvance(font, rune);
        return width;
    }

    private static float MeasureOffset(string text, Font font, int offset)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Utf16SequenceLength > offset) break;
            width += MeasureAdvance(font, rune);
            offset -= rune.Utf16SequenceLength;
        }
        return width;
    }

    private static int HitTestOffset(string text, Font font, float x)
    {
        if (string.IsNullOrEmpty(text) || x <= 0) return 0;
        var offset = 0;
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            var advance = MeasureAdvance(font, rune);
            if (x < width + advance / 2f) break;
            width += advance;
            offset += rune.Utf16SequenceLength;
        }
        return Math.Clamp(offset, 0, text.Length);
    }

    private RichTextLayoutLine FindNearestLine(float y)
    {
        foreach (var line in Lines)
            if (y <= line.Bounds.Bottom) return line;
        return Lines[^1];
    }
}

public static class RichTextLayoutEngine
{
    public static RichTextBlockLayout LayoutBlock(
        RichTextBlock block,
        Font baseFont,
        Point origin,
        float maxWidth,
        float lineHeight)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(baseFont);
        maxWidth = float.IsFinite(maxWidth) && maxWidth > 0 ? maxWidth : float.PositiveInfinity;
        lineHeight = lineHeight > 0 ? lineHeight : TextMetrics.GetLineHeight(baseFont, TextLayout.DefaultLineHeight);

        var tokens = new List<LayoutToken>();
        var offset = 0;
        foreach (var inline in block.Inlines)
        {
            if (inline is not RichTextRun run) continue;
            var font = ApplyMarks(baseFont, run.Marks);
            foreach (var rune in run.Text.EnumerateRunes())
            {
                var advance = RichTextBlockLayout.MeasureAdvance(font, rune);
                tokens.Add(new LayoutToken(offset, offset + rune.Utf16SequenceLength, rune, run, font, advance));
                offset += rune.Utf16SequenceLength;
            }
        }

        var tokenByOffset = tokens.ToDictionary(token => token.StartOffset);
        var ranges = TextWrapping.Wrap(block.PlainText, maxWidth,
            (runeOffset, rune) => tokenByOffset.TryGetValue(runeOffset, out var token)
                ? token.Advance
                : RichTextBlockLayout.MeasureAdvance(baseFont, rune));
        var lines = new List<RichTextLayoutLine>(ranges.Count);
        for (var lineIndex = 0; lineIndex < ranges.Count; lineIndex++)
        {
            var range = ranges[lineIndex];
            var lineY = origin.Y + lineIndex * lineHeight;
            var lineX = origin.X;
            var fragments = new List<RichTextLayoutFragment>();
            var lineTokens = tokens.Where(token => token.StartOffset >= range.StartOffset && token.EndOffset <= range.EndOffset).ToArray();
            for (var index = 0; index < lineTokens.Length;)
            {
                var first = lineTokens[index];
                var fragmentStart = first.StartOffset;
                var fragmentWidth = 0f;
                var text = new StringBuilder();
                while (index < lineTokens.Length && ReferenceEquals(lineTokens[index].Run, first.Run))
                {
                    text.Append(lineTokens[index].Rune.ToString());
                    fragmentWidth += lineTokens[index].Advance;
                    index++;
                }
                var fragmentEnd = lineTokens[index - 1].EndOffset;
                fragments.Add(new RichTextLayoutFragment(
                    new RichTextRun(text.ToString(), first.Run.Marks),
                    fragmentStart,
                    fragmentEnd,
                    first.Font,
                    new Rect(lineX, lineY, fragmentWidth, lineHeight)));
                lineX += fragmentWidth;
            }
            lines.Add(new RichTextLayoutLine(
                range.StartOffset,
                range.EndOffset,
                new Rect(origin.X, lineY, range.Width, lineHeight),
                fragments));
        }
        var width = lines.Count == 0 ? 0 : lines.Max(line => line.Bounds.Width);
        var bounds = new Rect(origin.X, origin.Y, width, lines.Count * lineHeight);
        return new RichTextBlockLayout(block, bounds, lines);
    }

    public static Font ApplyMarks(Font baseFont, RichTextMarks marks) => new(
        baseFont.Family,
        baseFont.Size,
        marks.Bold ? FontWeight.Bold : baseFont.Weight,
        marks.Italic ? FontStyle.Italic : baseFont.Style);

    private readonly record struct LayoutToken(
        int StartOffset,
        int EndOffset,
        Rune Rune,
        RichTextRun Run,
        Font Font,
        float Advance);
}
