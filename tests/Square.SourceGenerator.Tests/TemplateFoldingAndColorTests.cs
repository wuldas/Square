using Square.Compiler.LanguageServices;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class TemplateFoldingAndColorTests
{
    [Fact]
    public void FoldsNestedTemplateElementsAndStyleSection()
    {
        const string source = """
            <template>
              <View>
                <Button />
              </View>
            </template>
            <style>
              .page { color: #cccccc; }
            </style>
            """;

        var ranges = TemplateFoldingService.GetRanges(source, "Fold.sqx");

        Assert.Contains(ranges, range => range.StartLine == 0 && range.EndLine == 4);
        Assert.Contains(ranges, range => range.StartLine == 1 && range.EndLine == 3);
        Assert.Contains(ranges, range => range.StartLine == 5 && range.EndLine == 7);
    }

    [Fact]
    public void FoldingUsesExactRangeForNestedElementsWithTheSameTag()
    {
        const string source = """
            <template>
              <View>
                <View>
                  <Text />
                </View>
              </View>
            </template>
            """;

        var ranges = TemplateFoldingService.GetRanges(source, "Nested.sqx");

        Assert.Contains(ranges, range => range.StartLine == 1 && range.EndLine == 5);
        Assert.Contains(ranges, range => range.StartLine == 2 && range.EndLine == 4);
    }

    [Fact]
    public void FindsCssHexColorsAndPresentsRgb()
    {
        const string source = "<style>.page { color: #2a2d2e; }</style>";

        var colors = TemplateColorService.GetColors(source);
        var color = Assert.Single(colors);
        Assert.Equal("#2a2d2e", source.Substring(color.Start, color.Length));

        var presentations = TemplateColorService.GetPresentations(
            source, color.Start, color.Length, color.Red, color.Green, color.Blue, color.Alpha);
        Assert.Contains(presentations, item => item.Label.Equals("#2A2D2E", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(presentations, item => item.Label.StartsWith("rgb(", StringComparison.Ordinal));
    }

    [Fact]
    public void FindsSqvInlineStyleColorAfterDirectiveAttribute()
    {
        const string source = "<template><Button @click=\"Save\" style=\"color: #abcdef\" /></template>";

        var color = Assert.Single(TemplateColorService.GetColors(source, "Colors.sqv"));

        Assert.Equal("#abcdef", source.Substring(color.Start, color.Length));
    }

    [Fact]
    public void FoldsCssAtRulesAndNestedRulesFromStyleAst()
    {
        const string source = """
            <template><View /></template>
            <style>
              @media screen {
                .page {
                  color: #cccccc;
                }
              }
            </style>
            """;

        var ranges = TemplateFoldingService.GetRanges(source, "StyleFold.sqx");

        Assert.Contains(ranges, range => range.StartLine == 2 && range.EndLine == 6);
        Assert.Contains(ranges, range => range.StartLine == 3 && range.EndLine == 5);
    }

    [Fact]
    public void FoldsCSharpMethodAndNestedBlocksFromScriptAst()
    {
        const string source = """
            <template><View /></template>
            <script>
            private void Save()
            {
                if (true)
                {
                    return;
                }
            }
            </script>
            """;

        var ranges = TemplateFoldingService.GetRanges(source, "ScriptFold.sqx");

        Assert.Contains(ranges, range => range.StartLine == 3 && range.EndLine == 8);
        Assert.Contains(ranges, range => range.StartLine == 5 && range.EndLine == 7);
    }
}
