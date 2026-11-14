using Square.Graphics;
using Xunit;

namespace Square.Graphics.Tests;

public sealed class UnicodeWhitespaceTests
{
    [Theory]
    [InlineData('\u3000')]
    [InlineData('\u00a0')]
    [InlineData('\u2002')]
    [InlineData('\u202f')]
    public void UnicodeSpacesKeepTheirAdvanceAndSourceOffsets(char space)
    {
        var text = new string(space, 2);
        foreach (var mode in new[] { TextWhiteSpaceMode.Normal, TextWhiteSpaceMode.Nowrap, TextWhiteSpaceMode.PreLine })
        {
            var measuredRunes = new List<int>();
            var lines = TextWrapping.Wrap(text, 100, (_, rune) =>
            {
                measuredRunes.Add(rune.Value);
                return rune.Value == space ? 12f : 4f;
            }, new TextWrappingOptions(mode, CollapseNewlines: true));

            var line = Assert.Single(lines);
            Assert.Equal(24, line.Width);
            Assert.Equal(0, line.StartOffset);
            Assert.Equal(2, line.EndOffset);
            Assert.Equal(new[] { (int)space, space }, measuredRunes);
        }
    }

    [Theory]
    [InlineData(TextWhiteSpaceMode.Normal)]
    [InlineData(TextWhiteSpaceMode.Nowrap)]
    [InlineData(TextWhiteSpaceMode.Pre)]
    [InlineData(TextWhiteSpaceMode.PreWrap)]
    public void NonBreakingSpaceKeepsWordSpacingWithoutCollapsing(TextWhiteSpaceMode mode)
    {
        var line = Assert.Single(TextWrapping.Wrap("A\u00a0B", 100, (_, _) => 10f,
            new TextWrappingOptions(mode, WordSpacing: 3, CollapseNewlines: true)));

        Assert.Equal(33, line.Width);
        Assert.Equal(0, line.StartOffset);
        Assert.Equal(3, line.EndOffset);
    }

    [Theory]
    [InlineData(TextWhiteSpaceMode.Normal)]
    [InlineData(TextWhiteSpaceMode.Nowrap)]
    [InlineData(TextWhiteSpaceMode.PreLine)]
    public void CssSpacesStillCollapseAndTrim(TextWhiteSpaceMode mode)
    {
        var line = Assert.Single(TextWrapping.Wrap(" \tA\t  B\t ", 100, (_, _) => 10f,
            new TextWrappingOptions(mode, CollapseNewlines: true)));

        Assert.Equal(30, line.Width);
        Assert.Equal(2, line.StartOffset);
        Assert.Equal(7, line.EndOffset);
    }

    [Theory]
    [InlineData(TextWhiteSpaceMode.Pre)]
    [InlineData(TextWhiteSpaceMode.PreWrap)]
    public void PreservedModesKeepAsciiAndUnicodeSpaces(TextWhiteSpaceMode mode)
    {
        var line = Assert.Single(TextWrapping.Wrap(" \u3000\u3000 ", 100, (_, rune) => rune.Value == 0x3000 ? 12f : 4f,
            new TextWrappingOptions(mode, CollapseNewlines: true)));

        Assert.Equal(32, line.Width);
        Assert.Equal(0, line.StartOffset);
        Assert.Equal(4, line.EndOffset);
    }
}
