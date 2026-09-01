using Square.Graphics;
using Xunit;

namespace Square.Graphics.Tests;

public sealed class AuthoritativeTextLayoutTests
{
    [Fact]
    public void MeasureVisualLinesHitTestingSelectionAndInkUseOneSnapshot()
    {
        var clusters = new[]
        {
            new TextLayoutCluster(0, 2, new System.Text.Rune('e'), new Rect(0, 0, 13, 20), BidiDirection.Ltr),
            new TextLayoutCluster(2, 3, new System.Text.Rune('x'), new Rect(13, 0, 7, 20), BidiDirection.Ltr)
        };
        var snapshot = new TestSnapshot(
            new Size(20, 20),
            new Rect(-1, -2, 22, 24),
            [new TextLayoutLine(0, 3, 20, 20, 15, clusters)]);
        using var scope = TextLayoutProviderContext.Push(new TestProvider(snapshot));
        var layout = new TextLayout("e\u0301x", new Font("Test", 16));

        Assert.Equal(new Size(20, 20), layout.Measure());
        var line = Assert.Single(layout.GetVisualLines());
        Assert.Equal(2, line.Runes.Count);
        Assert.Equal((0, 2), (line.Runes[0].StartOffset, line.Runes[0].EndOffset));
        Assert.Equal(9, layout.MeasureOffset(2));
        Assert.Equal(2, layout.HitTestOffset(11));
        Assert.Equal(2, layout.HitTestPoint(new Point(11, 4)));
        Assert.Equal(new Rect(13, 0, 7, 20), Assert.Single(layout.GetSelectionRects(2, 1)));
        Assert.Equal(new Rect(4, 3, 22, 24), TextMetrics.MeasureInkBounds(layout, new Point(5, 5)));
        Assert.Equal(7, snapshot.CallCount);
    }

    [Fact]
    public void ProviderScopeRestoresPreviousProvider()
    {
        var first = new TestProvider(TestSnapshot.Empty);
        var second = new TestProvider(TestSnapshot.Empty);

        using (TextLayoutProviderContext.Push(first))
        {
            Assert.Same(first, TextLayoutProviderContext.Current);
            using (TextLayoutProviderContext.Push(second))
                Assert.Same(second, TextLayoutProviderContext.Current);
            Assert.Same(first, TextLayoutProviderContext.Current);
            using (TextLayoutProviderContext.Suppress())
                Assert.Null(TextLayoutProviderContext.Current);
            Assert.Same(first, TextLayoutProviderContext.Current);
        }

        Assert.Null(TextLayoutProviderContext.Current);
    }

    private sealed class TestProvider(TestSnapshot snapshot) : ITextLayoutProvider
    {
        public bool TryCreateLayout(TextLayout layout, out ITextLayoutSnapshot? result)
        {
            snapshot.CallCount++;
            result = snapshot;
            return true;
        }

        public bool TryGetFontMetrics(Font font, out FontMetrics metrics)
        {
            metrics = new FontMetrics(-10, -10, 3, 3, 0);
            return true;
        }

        public bool TryGetGlyphMetrics(Font font, System.Text.Rune rune, out GlyphMetrics metrics)
        {
            metrics = new GlyphMetrics(7, new Rect(0, -10, 7, 13));
            return true;
        }
    }

    private sealed class TestSnapshot(Size size, Rect inkBounds, IReadOnlyList<TextLayoutLine> lines)
        : ITextLayoutSnapshot
    {
        public static TestSnapshot Empty { get; } = new(Size.Zero, Rect.Empty, []);
        public int CallCount { get; set; }
        public Size Size { get; } = size;
        public Rect InkBounds { get; } = inkBounds;
        public IReadOnlyList<TextLayoutLine> Lines { get; } = lines;
        public float MeasureOffset(int utf16Offset) => utf16Offset == 2 ? 9 : 0;
        public Point GetCaretPoint(int utf16Offset, bool trailing = false) =>
            new(MeasureOffset(utf16Offset), 0);
        public int HitTestPoint(Point point) => point.X >= 9 ? 2 : 0;
        public IReadOnlyList<Rect> GetSelectionRects(int start, int length) =>
            start == 2 && length == 1 ? [new Rect(13, 0, 7, 20)] : [];
    }
}
