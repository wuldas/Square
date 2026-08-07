using Square.Graphics;
using Xunit;

namespace Square.Graphics.Tests;

public class TextLayoutBidiTests
{
    [Theory]
    [InlineData(BidiDirection.Auto)]
    [InlineData(BidiDirection.Ltr)]
    public void WrappedContinuationUsesTheParagraphBaseDirection(BidiDirection direction)
    {
        var layout = new TextLayout("abc אבג xyz", new Font("Segoe UI", 20))
        {
            Direction = direction,
            MaxSize = new Size(70, float.MaxValue)
        };

        var lines = layout.GetVisualLines();

        Assert.True(lines.Count > 1);
        Assert.Equal(new[] { 0, 1, 2, 3 }, lines[0].Runes.Select(rune => rune.StartOffset));
        Assert.Equal(
            new[] { 6, 5, 4, 7, 8, 9, 10 },
            lines.Skip(1).SelectMany(line => line.Runes).Select(rune => rune.StartOffset));
    }

    [Fact]
    public void WrappedPureLtrLinesKeepLogicalVisualOrder()
    {
        var layout = new TextLayout("abc def ghi", new Font("Segoe UI", 20))
        {
            MaxSize = new Size(70, float.MaxValue)
        };

        Assert.Equal(
            new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            layout.GetVisualLines().SelectMany(line => line.Runes).Select(rune => rune.StartOffset));
    }

    [Fact]
    public void WrappedVisualLinesKeepSupplementaryRuneUtf16Offsets()
    {
        var text = "abc 😀 אבג xyz";
        var layout = new TextLayout(text, new Font("Segoe UI", 20))
        {
            MaxSize = new Size(70, float.MaxValue)
        };

        var lines = layout.GetVisualLines();

        Assert.True(lines.Count > 1);
        Assert.Equal(text.Length, lines.SelectMany(line => line.Runes).Sum(rune => rune.EndOffset - rune.StartOffset));
        Assert.Equal(
            new[] { 0, 1, 2, 3, 4, 6, 9, 8, 7, 10, 11, 12, 13 },
            lines.SelectMany(line => line.Runes).Select(rune => rune.StartOffset));
    }

    [Fact]
    public void RtlHitTestEdgesReturnTheLogicalParagraphEdges()
    {
        var layout = new TextLayout("אבג", new Font("Segoe UI", 20))
        {
            Direction = BidiDirection.Rtl
        };

        Assert.Equal(3, layout.HitTestOffset(0));
        Assert.Equal(0, layout.HitTestOffset(float.MaxValue));
    }

    [Fact]
    public void AutoDirectionIsResolvedSeparatelyForNewlineSeparatedParagraphs()
    {
        var layout = new TextLayout("abc אבג\nאבג xyz", new Font("Segoe UI", 20))
        {
            MaxSize = new Size(100, float.MaxValue)
        };

        var lines = layout.GetVisualLines();

        Assert.Collection(
            lines,
            line => Assert.Equal(new[] { 0, 1, 2, 3, 6, 5, 4 }, line.Runes.Select(rune => rune.StartOffset)),
            line => Assert.Equal(new[] { 12, 13, 14, 11, 10, 9, 8 }, line.Runes.Select(rune => rune.StartOffset)));
    }
}
