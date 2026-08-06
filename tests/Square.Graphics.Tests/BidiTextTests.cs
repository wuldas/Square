using System.Text;
using Square.Graphics;
using Xunit;

namespace Square.Graphics.Tests;

public class BidiTextTests
{
    [Fact]
    public void ClassifiesStrongNumbersWhitespaceAndNeutralRunes()
    {
        Assert.Equal(BidiCharacterClass.Ltr, BidiText.Classify(new Rune('A')));
        Assert.Equal(BidiCharacterClass.Rtl, BidiText.Classify(new Rune('\u05e9')));
        Assert.Equal(BidiCharacterClass.Rtl, BidiText.Classify(new Rune('\u0645')));
        Assert.Equal(BidiCharacterClass.EuropeanNumber, BidiText.Classify(new Rune('7')));
        Assert.Equal(BidiCharacterClass.ArabicNumber, BidiText.Classify(new Rune('\u0667')));
        Assert.Equal(BidiCharacterClass.Whitespace, BidiText.Classify(new Rune(' ')));
        Assert.Equal(BidiCharacterClass.Neutral, BidiText.Classify(new Rune(',')));
    }

    [Fact]
    public void LtrParagraphReversesOnlyTheRtlRun()
    {
        var layout = BidiText.Layout("abc שלום 123");

        Assert.Equal(BidiDirection.Ltr, layout.BaseDirection);
        Assert.Collection(
            layout.VisualRuns,
            run => Assert.Equal(new BidiTextRun(0, 4, BidiDirection.Ltr, 0), run),
            run => Assert.Equal(new BidiTextRun(4, 4, BidiDirection.Rtl, 1), run),
            run => Assert.Equal(new BidiTextRun(8, 4, BidiDirection.Ltr, 0), run));

        Assert.Equal(new[] { 0, 1, 2, 3, 7, 6, 5, 4, 8, 9, 10, 11 }, layout.VisualToLogical);
        Assert.Equal(new[] { 0, 1, 2, 3, 7, 6, 5, 4, 8, 9, 10, 11 }, layout.LogicalToVisual);
    }

    [Fact]
    public void RtlParagraphPlacesEmbeddedLatinAndNumbersAsAnLtrRun()
    {
        var layout = BidiText.Layout("שלום abc 123");

        Assert.Equal(BidiDirection.Rtl, layout.BaseDirection);
        Assert.Collection(
            layout.VisualRuns,
            run => Assert.Equal(new BidiTextRun(5, 7, BidiDirection.Ltr, 2), run),
            run => Assert.Equal(new BidiTextRun(0, 5, BidiDirection.Rtl, 1), run));

        Assert.Equal(new[] { 5, 6, 7, 8, 9, 10, 11, 4, 3, 2, 1, 0 }, layout.VisualToLogical);
        Assert.Equal(new[] { 11, 10, 9, 8, 7, 0, 1, 2, 3, 4, 5, 6 }, layout.LogicalToVisual);
    }

    [Fact]
    public void ArabicIndicNumberRunKeepsLogicalDigitOrderInAnRtlParagraph()
    {
        var layout = BidiText.Layout("שלום \u0661\u0662\u0663");

        Assert.Collection(
            layout.VisualRuns,
            run => Assert.Equal(new BidiTextRun(5, 3, BidiDirection.Ltr, 2), run),
            run => Assert.Equal(new BidiTextRun(0, 5, BidiDirection.Rtl, 1), run));
        Assert.Equal(new[] { 5, 6, 7, 4, 3, 2, 1, 0 }, layout.VisualToLogical);
    }

    [Fact]
    public void EmbedAddsAnEmbeddingLevelWithoutOverridingCharacterDirection()
    {
        var layout = BidiText.Layout(
            "abc שלום",
            new BidiTextOptions(BidiDirection.Ltr, BidiTextMode.Embed));

        Assert.Equal(BidiDirection.Ltr, layout.BaseDirection);
        Assert.Collection(
            layout.VisualRuns,
            run => Assert.Equal(new BidiTextRun(0, 4, BidiDirection.Ltr, 2), run),
            run => Assert.Equal(new BidiTextRun(4, 4, BidiDirection.Rtl, 3), run));
        Assert.Equal(new[] { 0, 1, 2, 3, 7, 6, 5, 4 }, layout.VisualToLogical);
    }

    [Fact]
    public void BidiOverrideForcesOneDirectionalRunIncludingNumbersAndNeutralCharacters()
    {
        var layout = BidiText.Layout(
            "abc שלום 123",
            new BidiTextOptions(BidiDirection.Rtl, BidiTextMode.BidiOverride));

        Assert.Equal(BidiDirection.Rtl, layout.BaseDirection);
        Assert.Collection(
            layout.VisualRuns,
            run => Assert.Equal(new BidiTextRun(0, 12, BidiDirection.Rtl, 1), run));
        Assert.Equal(
            new[] { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 },
            layout.VisualToLogical);
    }

    [Fact]
    public void RunsKeepUtf16OffsetsForSupplementaryRunes()
    {
        var layout = BidiText.Layout("A 😀 שלום");

        Assert.Collection(
            layout.VisualRuns,
            run => Assert.Equal(new BidiTextRun(0, 5, BidiDirection.Ltr, 0), run),
            run => Assert.Equal(new BidiTextRun(5, 4, BidiDirection.Rtl, 1), run));
        Assert.Equal(new[] { 0, 1, 2, 3, 7, 6, 5, 4 }, layout.VisualToLogical);
    }
}
