using Square.Compiler.LanguageServices;
using Square.Compiler.Syntax;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class StyleSectionSyntaxTests
{
    [Fact]
    public void StyleAstPreservesSelectorsDeclarationsAndAbsoluteRanges()
    {
        const string source = "<template><View class=\"page\" /></template>\r\n" +
            "<style>\r\n.page, Button:hover {\r\n  color: #fff;\r\n  gap: 8px !important;\r\n}\r\n</style>";

        var result = ComponentSectionScanner.Scan(source, "Style.sqx", ComponentDialect.Sqx, tolerant: false);
        var style = result.Document.Style.Css;

        Assert.Empty(style.Diagnostics);
        var rule = Assert.Single(style.Rules);
        AssertRange(source, rule.FullRange, ".page, Button:hover {\r\n  color: #fff;\r\n  gap: 8px !important;\r\n}");
        Assert.Collection(
            rule.Selectors,
            selector => AssertRange(source, selector.Range, ".page"),
            selector => AssertRange(source, selector.Range, "Button:hover"));
        Assert.Collection(
            rule.Declarations,
            declaration =>
            {
                Assert.Equal("color", declaration.Property);
                Assert.Equal("#fff", declaration.Value);
                Assert.False(declaration.Important);
                AssertRange(source, declaration.PropertyRange, "color");
                AssertRange(source, declaration.ValueRange, "#fff");
            },
            declaration =>
            {
                Assert.Equal("gap", declaration.Property);
                Assert.Equal("8px", declaration.Value);
                Assert.True(declaration.Important);
                AssertRange(source, declaration.ValueRange, "8px");
            });
    }

    [Fact]
    public void StyleAstParsesImportMediaAndKeyframesAtRules()
    {
        const string source = "<template><View /></template><style>" +
            "@import \"theme.css\";" +
            "@media screen and (min-width: 600px) { .page { gap: 8px; } }" +
            "@keyframes fade { from { opacity: 0; } to { opacity: 1; } }" +
            "</style>";

        var result = ComponentSectionScanner.Scan(source, "AtRules.sqx", ComponentDialect.Sqx, tolerant: false);
        var style = result.Document.Style.Css;

        Assert.Empty(style.Diagnostics);
        Assert.Collection(
            style.AtRules,
            importRule =>
            {
                Assert.Equal("import", importRule.Name);
                Assert.Equal("\"theme.css\"", importRule.Prelude);
                Assert.Empty(importRule.Rules);
                AssertRange(source, importRule.FullRange, "@import \"theme.css\";");
            },
            mediaRule =>
            {
                Assert.Equal("media", mediaRule.Name);
                Assert.Equal("screen and (min-width: 600px)", mediaRule.Prelude);
                var nested = Assert.Single(mediaRule.Rules);
                Assert.Equal(".page", Assert.Single(nested.Selectors).Text);
                Assert.Equal("8px", Assert.Single(nested.Declarations).Value);
            },
            keyframesRule =>
            {
                Assert.Equal("keyframes", keyframesRule.Name);
                Assert.Equal("fade", keyframesRule.Prelude);
                Assert.Collection(
                    keyframesRule.Rules,
                    from => Assert.Equal("from", Assert.Single(from.Selectors).Text),
                    to => Assert.Equal("to", Assert.Single(to.Selectors).Text));
            });
    }

    [Fact]
    public void StyleAstIgnoresCommentsInsideDeclarationBlocks()
    {
        const string source = "<template><View /></template>" +
            "<style>.page { color: red; /* fake: value; } */ gap: 8px; }</style>";

        var result = ComponentSectionScanner.Scan(source, "Comments.sqx", ComponentDialect.Sqx, tolerant: false);
        var style = result.Document.Style.Css;

        Assert.Empty(style.Diagnostics);
        var declarations = Assert.Single(style.Rules).Declarations;
        Assert.Collection(
            declarations,
            color => Assert.Equal("color", color.Property),
            gap => Assert.Equal("gap", gap.Property));
    }

    [Fact]
    public void DocumentColorsComeFromStyleDeclarationValuesOnly()
    {
        const string source = "<template><Text text=\"#ffffff\" style=\"border-color: #abcdef\" /></template>" +
            "<style>/* #000000 */ .page { color: #123456; }</style>";

        var colors = TemplateColorService.GetColors(source);

        Assert.Collection(
            colors,
            inline => Assert.Equal(source.IndexOf("#abcdef", StringComparison.Ordinal), inline.Start),
            style => Assert.Equal(source.IndexOf("#123456", StringComparison.Ordinal), style.Start));
        Assert.All(colors, color => Assert.Equal("#123456".Length, color.Length));
    }

    [Fact]
    public void MalformedCssProducesBoundedPartialAst()
    {
        const string css = ".good { color: red; } .broken { color: \"red;";

        var style = CssSyntaxParser.Parse(css, 100);

        Assert.NotEmpty(style.Diagnostics);
        Assert.Equal(2, style.Rules.Count);
        Assert.Equal(".good", Assert.Single(style.Rules[0].Selectors).Text);
        Assert.All(style.Diagnostics, diagnostic =>
            Assert.InRange(diagnostic.Range.End, 100, 100 + css.Length));
    }

    private static void AssertRange(string source, SquareSourceRange range, string expected) =>
        Assert.Equal(expected, source.Substring(range.Offset, range.Length));
}
