using System.Runtime.Versioning;
using Square.Graphics;
using Square.Controls;
using Square.Text.Fonts;
using Xunit;

namespace Square.Backends.Direct2D.Tests;

[SupportedOSPlatform("windows6.1")]
public sealed class DirectWriteTextLayoutTests
{
    [Fact]
    public void SharedProviderInitializes()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        Assert.NotNull(DirectWriteTextLayoutProvider.Shared);
    }

    [Fact]
    public void SharedProviderCreatesSimpleLayout()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        provider.ClearCaches();
        Assert.True(provider.TryCreateLayout(
            new TextLayout("abc", new Font("Segoe UI", 20)), out var snapshot));
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void CombiningSequenceIsExposedAsOneUtf16Cluster()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        provider.ClearCaches();
        using var scope = TextLayoutProviderContext.Push(provider);
        var layout = new TextLayout("e\u0301 office", new Font("Segoe UI", 20));

        var snapshot = AssertSnapshot(layout);
        var first = Assert.Single(snapshot.Lines.SelectMany(line => line.Clusters),
            cluster => cluster.StartOffset == 0);

        Assert.Equal(2, first.EndOffset);
        Assert.Equal(layout.Measure(), snapshot.Size);
        Assert.Equal(layout.MeasureOffset(0), layout.MeasureOffset(1));
        Assert.Equal(first.EndOffset, layout.HitTestPoint(
            new Point(first.Bounds.Right - 0.01f, first.Bounds.Center.Y)));
    }

    [Fact]
    public void LayoutCacheIsBoundedAndReusesEquivalentRequests()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        provider.ClearCaches();
        using var scope = TextLayoutProviderContext.Push(provider);
        var first = new TextLayout("cache", new Font("Segoe UI", 16));

        AssertSnapshot(first);
        AssertSnapshot(new TextLayout("cache", new Font("Segoe UI", 16)));
        Assert.Equal(1, provider.LayoutCacheCount);

        for (var index = 0; index < DirectWriteTextLayoutProvider.MaxLayoutEntries + 64; index++)
            AssertSnapshot(new TextLayout($"entry-{index}", new Font("Segoe UI", 16)));

        Assert.InRange(provider.LayoutCacheCount, 1, DirectWriteTextLayoutProvider.MaxLayoutEntries);
        Assert.InRange(provider.LayoutCacheBytes, 1, DirectWriteTextLayoutProvider.MaxLayoutBytes);
        Assert.InRange(provider.FormatCacheCount, 1, DirectWriteTextLayoutProvider.MaxFormatEntries);
    }

    [Fact]
    public void UnsupportedTextIndentAndNarrowWordsUseSquareFallback()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        using var scope = TextLayoutProviderContext.Push(provider);

        Assert.False(new TextLayout("indent", new Font("Segoe UI", 16)) { TextIndent = 4 }
            .TryGetAuthoritativeSnapshot(out _));
        Assert.False(new TextLayout("Controls", new Font("Segoe UI", 16))
        {
            MaxSize = new Size(20, 40),
            WhiteSpace = TextWhiteSpaceMode.Normal
        }.TryGetAuthoritativeSnapshot(out _));
    }

    [Fact]
    public void Windows8CharacterSpacingUsesDirectWriteLayout1()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        using var scope = TextLayoutProviderContext.Push(provider);
        var plain = new TextLayout("a b", new Font("Segoe UI", 16));
        var spaced = new TextLayout("a b", new Font("Segoe UI", 16))
        {
            LetterSpacing = 1,
            WordSpacing = 2
        };

        Assert.True(spaced.TryGetAuthoritativeSnapshot(out _));
        Assert.True(spaced.Measure().Width > plain.Measure().Width);
    }

    [Fact]
    public void WrappingPreservesSourceUtf16Ranges()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        using var scope = TextLayoutProviderContext.Push(provider);
        var layout = new TextLayout("one   two three", new Font("Segoe UI", 18))
        {
            MaxSize = new Size(55, 200),
            WhiteSpace = TextWhiteSpaceMode.Normal
        };

        var snapshot = AssertSnapshot(layout);

        Assert.True(snapshot.Lines.Count > 1);
        Assert.All(snapshot.Lines.SelectMany(line => line.Clusters), cluster =>
        {
            Assert.InRange(cluster.StartOffset, 0, layout.Text.Length);
            Assert.InRange(cluster.EndOffset, cluster.StartOffset + 1, layout.Text.Length);
        });
        Assert.Equal(layout.Text.Length, snapshot.Lines[^1].EndOffset);
    }

    [Fact]
    public void RtlClustersKeepLogicalOffsetsAndVisualOrder()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        using var scope = TextLayoutProviderContext.Push(provider);
        var layout = new TextLayout("אבג", new Font("Segoe UI", 20))
        {
            Direction = BidiDirection.Rtl,
            WhiteSpace = TextWhiteSpaceMode.Pre
        };

        var line = Assert.Single(AssertSnapshot(layout).Lines);

        Assert.All(line.Clusters, cluster => Assert.Equal(BidiDirection.Rtl, cluster.Direction));
        Assert.Equal(new[] { 2, 1, 0 }, line.Clusters.Select(cluster => cluster.StartOffset));
        Assert.Equal(3, layout.HitTestPoint(new Point(0, 0)));
        Assert.Equal(0, layout.HitTestPoint(new Point(float.MaxValue, 0)));
    }

    [Fact]
    public async Task LoadedCustomFontUsesDirectWriteCollectionAndInvalidatesCache()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        provider.ClearCaches();
        using var scope = TextLayoutProviderContext.Push(provider);
        AssertSnapshot(new TextLayout("system-before-custom-font", new Font("Segoe UI", 16)));
        Assert.Equal(1, provider.LayoutCacheCount);
        var alias = "Square DirectWrite Test Inter";
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Inter-Regular.ttf");
        var face = new FontFace(alias, path);
        FontFaceSet.Default.Add(face);
        await face.LoadAsync();
        var layout = new TextLayout("AV office", new Font(alias, 20));

        var snapshot = AssertSnapshot(layout);

        Assert.True(snapshot.Size.Width > 0);
        Assert.NotEmpty(snapshot.Lines.SelectMany(line => line.Clusters));
        Assert.Equal(1, provider.LayoutCacheCount);
    }

    [Fact]
    public void InputCaretAndSelectionRespectCombiningClusterBoundaries()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        using var scope = TextLayoutProviderContext.Push(provider);
        var input = new Input
        {
            Geometry = new Rect(0, 0, 200, 40),
            Value = "e\u0301x"
        };
        input.Style.Set("font-family", "Segoe UI");
        input.Style.Set("font-size", "20px");
        input.Style.Set("white-space", "pre");
        var layout = new TextLayout(input.Value, new Font("Segoe UI", 20))
        {
            MaxSize = new Size(182, float.MaxValue),
            Direction = BidiDirection.Ltr,
            WhiteSpace = TextWhiteSpaceMode.Pre
        };
        var first = Assert.Single(AssertSnapshot(layout).Lines.SelectMany(line => line.Clusters),
            cluster => cluster.StartOffset == 0);

        input.HandlePointerDown(new Point(8 + first.Bounds.Right - 0.01f, 20));
        input.HandlePointerUp(new Point(8 + first.Bounds.Right - 0.01f, 20));

        Assert.Equal(2, input.CaretIndex);
        input.HandleKey(0x08);
        Assert.Equal("x", input.Value);

        input.Value = "e\u0301x";
        input.HandleKey(0x24);
        input.HandleKey(0x27);
        Assert.Equal(2, input.CaretIndex);
        input.SelectAll();
        Assert.Equal(input.Value.Length, input.SelectionLength);
        Assert.True(input.CaretRect.X > 8 + first.Bounds.Right);
    }

    [Fact]
    public void ConsecutiveSelectedClustersProduceOneBackgroundRectPerVisualRun()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        using var scope = TextLayoutProviderContext.Push(provider);
        var layout = new TextLayout("多行文本 30px 行高", new Font("Segoe UI", 20))
        {
            WhiteSpace = TextWhiteSpaceMode.Pre
        };

        var rect = Assert.Single(AssertSnapshot(layout).GetSelectionRects(0, layout.Text.Length));

        Assert.True(rect.Width > 0);
        Assert.True(rect.Height > 0);
    }

    [Theory]
    [InlineData("Controls")]
    [InlineData("Markdown")]
    [InlineData("Signals")]
    [InlineData("Overflow")]
    public void NaturalWidthRemainsSingleLineWhenReusedAsMaxWidth(string text)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        using var scope = TextLayoutProviderContext.Push(provider);
        var font = new Font("Arial", 13.3333f);
        var natural = new TextLayout(text, font).Measure();
        var constrained = new TextLayout(text, font) { MaxSize = natural };

        Assert.True(constrained.TryGetAuthoritativeSnapshot(out var snapshot));
        Assert.True(snapshot.Lines.Count == 1,
            $"'{text}' natural width {natural.Width:R} produced {snapshot.Lines.Count} lines.");
    }

    [Fact]
    public void RepeatedLayoutCacheHitsDoNotRebuildPreparedText()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        provider.ClearCaches();
        var layout = new TextLayout("cached directwrite text", new Font("Segoe UI", 16));
        Assert.True(provider.TryCreateLayout(layout, out _));

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
            if (!provider.TryCreateLayout(layout, out _)) throw new InvalidOperationException();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.InRange(allocated, 0, 256 * 1024);
        Assert.Equal(1, provider.LayoutCacheCount);
    }

    [Fact]
    public void SupplementaryAndTransformedClustersPreserveSourceUtf16Ranges()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        using var scope = TextLayoutProviderContext.Push(provider);
        var emoji = new TextLayout("A😀B", new Font("Segoe UI", 20))
        {
            WhiteSpace = TextWhiteSpaceMode.Pre
        };
        var transformed = new TextLayout("a   b", new Font("Segoe UI", 20))
        {
            TextTransform = TextTransformMode.Uppercase,
            WhiteSpace = TextWhiteSpaceMode.Normal
        };

        var emojiCluster = Assert.Single(AssertSnapshot(emoji).Lines.SelectMany(line => line.Clusters),
            cluster => cluster.StartOffset == 1);
        var transformedClusters = AssertSnapshot(transformed).Lines.SelectMany(line => line.Clusters).ToArray();

        Assert.Equal((1, 3), (emojiCluster.StartOffset, emojiCluster.EndOffset));
        Assert.Contains(transformedClusters, cluster => cluster.Rune.Value == 'A' && cluster.StartOffset == 0);
        Assert.Contains(transformedClusters, cluster => cluster.Rune.Value == ' ' &&
            cluster.StartOffset == 1 && cluster.EndOffset == 4);
        Assert.Contains(transformedClusters, cluster => cluster.Rune.Value == 'B' && cluster.StartOffset == 4);
    }

    [Fact]
    public void TrailingNewlineKeepsFinalEmptyLineAtSourceEnd()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        using var scope = TextLayoutProviderContext.Push(provider);
        var layout = new TextLayout("line\n", new Font("Segoe UI", 16))
        {
            WhiteSpace = TextWhiteSpaceMode.Pre
        };

        var snapshot = AssertSnapshot(layout);

        Assert.True(snapshot.Lines.Count >= 2);
        Assert.Equal(layout.Text.Length, snapshot.Lines[^1].StartOffset);
        Assert.Equal(layout.Text.Length, snapshot.Lines[^1].EndOffset);
    }

    [Fact]
    public void MixedAutoDirectionParagraphsUseSquareFallback()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        using var scope = TextLayoutProviderContext.Push(provider);
        var layout = new TextLayout("abc\nאבג", new Font("Segoe UI", 16))
        {
            Direction = BidiDirection.Auto,
            WhiteSpace = TextWhiteSpaceMode.Pre
        };

        Assert.False(layout.TryGetAuthoritativeSnapshot(out _));
    }

    [Fact]
    public async Task CustomAliasesWithSameInternalFamilyKeepSeparateFormats()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var provider = DirectWriteTextLayoutProvider.Shared;
        provider.ClearCaches();
        using var scope = TextLayoutProviderContext.Push(provider);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Inter-Regular.ttf");
        var first = new FontFace("Square DirectWrite Alias One", path);
        var second = new FontFace("Square DirectWrite Alias Two", path);
        FontFaceSet.Default.Add(first);
        FontFaceSet.Default.Add(second);
        await first.LoadAsync();
        await second.LoadAsync();

        AssertSnapshot(new TextLayout("alias one", new Font(first.Family, 16)));
        AssertSnapshot(new TextLayout("alias two", new Font(second.Family, 16)));

        Assert.Equal(2, provider.FormatCacheCount);
        Assert.Equal(2, provider.LayoutCacheCount);
    }

    private static ITextLayoutSnapshot AssertSnapshot(TextLayout layout)
    {
        Assert.True(layout.TryGetAuthoritativeSnapshot(out var snapshot));
        Assert.NotEmpty(snapshot.Lines);
        return snapshot;
    }
}
