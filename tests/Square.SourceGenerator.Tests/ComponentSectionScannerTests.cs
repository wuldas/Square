using Square.Compiler.Syntax;
using Square.Compiler.LanguageServices;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class ComponentSectionScannerTests
{
    [Fact]
    public void ScanBuildsIndependentSectionsWithAbsoluteRanges()
    {
        const string source = "<template>\r\n  <View />\r\n</template>\r\n" +
            "<script lang=\"csharp\" name=\"Card\">\r\n  public int Count;\r\n</script>\r\n" +
            "<style>\r\n  .card { gap: 8px; }\r\n</style>";

        var result = ComponentSectionScanner.Scan(
            source,
            "Card.sqx",
            ComponentDialect.Sqx,
            tolerant: false);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(ComponentDialect.Sqx, result.Document.Dialect);
        Assert.Equal("Card.sqx", result.Document.SourcePath);
        Assert.Equal(source, result.Document.SourceText);

        var template = Assert.IsType<TemplateSectionSyntax>(result.Document.Template);
        AssertRange(source, template.FullRange, "<template>\r\n  <View />\r\n</template>");
        AssertRange(source, template.OpeningTagRange, "<template>");
        AssertRange(source, template.ContentRange, "\r\n  <View />\r\n");
        AssertRange(source, template.ClosingTagRange, "</template>");
        Assert.True(template.IsClosed);

        var script = Assert.IsType<ScriptSectionSyntax>(result.Document.Script);
        AssertRange(source, script.OpeningTagRange, "<script lang=\"csharp\" name=\"Card\">");
        AssertRange(source, script.ContentRange, "\r\n  public int Count;\r\n");
        AssertRange(source, script.ClosingTagRange, "</script>");
        Assert.Equal("\r\n  public int Count;\r\n", script.ContentText);

        var style = Assert.IsType<StyleSectionSyntax>(result.Document.Style);
        AssertRange(source, style.OpeningTagRange, "<style>");
        AssertRange(source, style.ContentRange, "\r\n  .card { gap: 8px; }\r\n");
        AssertRange(source, style.ClosingTagRange, "</style>");
        Assert.Equal("\r\n  .card { gap: 8px; }\r\n", style.ContentText);
    }

    [Fact]
    public void ScanPreservesEmptyContentAndQuotedOpeningTagRanges()
    {
        const string source = "<!-- before -->\r\n<template><View /></template>\r\n" +
            "<script lang=\"csharp\" note=\"a>b\"></script>\r\n<style></style>";

        var result = ComponentSectionScanner.Scan(source, "Empty.sqx", ComponentDialect.Sqx, tolerant: false);

        Assert.Empty(result.Diagnostics);
        AssertRange(source, result.Document.Script.OpeningTagRange, "<script lang=\"csharp\" note=\"a>b\">");
        Assert.Equal(0, result.Document.Script.ContentRange.Length);
        Assert.Equal(0, result.Document.Style.ContentRange.Length);
    }

    [Fact]
    public void ScanIgnoresSectionTextInsideTemplateComments()
    {
        const string source = "<template><!-- </template> --><View /></template>";

        var result = ComponentSectionScanner.Scan(source, "Comment.sqx", ComponentDialect.Sqx, tolerant: false);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("<!-- </template> --><View />", result.Document.Template.ContentText);
    }

    [Fact]
    public void SqxAndSqvUseTheSameUnicodeSectionRanges()
    {
        const string source = "<template>\r\n<Text text=\"你好😀\" />\r\n</template>\r\n<style>.页面 { gap: 8px; }</style>";

        var sqx = ComponentSectionScanner.Scan(source, "Unicode.sqx", ComponentDialect.Sqx, tolerant: false);
        var sqv = ComponentSectionScanner.Scan(source, "Unicode.sqv", ComponentDialect.Sqv, tolerant: false);

        Assert.Empty(sqx.Diagnostics);
        Assert.Empty(sqv.Diagnostics);
        Assert.Equal(sqx.Document.Template.FullRange, sqv.Document.Template.FullRange);
        Assert.Equal(sqx.Document.Template.ContentRange, sqv.Document.Template.ContentRange);
        Assert.Equal(sqx.Document.Style.FullRange, sqv.Document.Style.FullRange);
        Assert.Equal(sqx.Document.Style.ContentRange, sqv.Document.Style.ContentRange);
    }

    [Fact]
    public void ScanKeepsNestedVueTemplateInsideTheTemplateSection()
    {
        const string source = "<template><Card><template #header><Text /></template></Card></template>" +
            "<style>.card { color: red; }</style>";

        var result = ComponentSectionScanner.Scan(
            source,
            "Card.sqv",
            ComponentDialect.Sqv,
            tolerant: false);

        Assert.Empty(result.Diagnostics);
        AssertRange(
            source,
            result.Document.Template.FullRange,
            "<template><Card><template #header><Text /></template></Card></template>");
        Assert.NotNull(result.Document.Style);
    }

    [Fact]
    public void ScanIgnoresClosingTagTextInsideScriptAndStyleStrings()
    {
        const string source = "<template><View /></template>" +
            "<script>private const string End = \"</script>\";</script>" +
            "<style>.label::after { content: \"</style>\"; }</style>";

        var result = ComponentSectionScanner.Scan(
            source,
            "Strings.sqx",
            ComponentDialect.Sqx,
            tolerant: false);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("private const string End = \"</script>\";", result.Document.Script.ContentText);
        Assert.Equal(".label::after { content: \"</style>\"; }", result.Document.Style.ContentText);
    }

    [Theory]
    [InlineData("sqx", "<Text text={\"</template>\"} />")]
    [InlineData("sqv", "<Text text=\"</template>\" />")]
    public void ScanIgnoresClosingTagTextInsideTemplateAttributes(string extension, string child)
    {
        var source = "<template>" + child + "</template><style>.page { gap: 8px; }</style>";
        var dialect = extension == "sqx" ? ComponentDialect.Sqx : ComponentDialect.Sqv;

        var result = ComponentSectionScanner.Scan(
            source,
            "Strings." + extension,
            dialect,
            tolerant: false);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(child, result.Document.Template.ContentText);
        Assert.NotNull(result.Document.Style);
    }

    [Theory]
    [InlineData("sqx", "{ \"</template>\" }")]
    [InlineData("sqv", "{{ \"</template>\" }}")]
    public void ScanIgnoresClosingTagTextInsideTemplateExpressions(string extension, string expression)
    {
        var source = "<template>" + expression + "<View /></template>";
        var dialect = extension == "sqx" ? ComponentDialect.Sqx : ComponentDialect.Sqv;

        var result = ComponentSectionScanner.Scan(
            source,
            "Expressions." + extension,
            dialect,
            tolerant: false);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(expression + "<View />", result.Document.Template.ContentText);
    }

    [Fact]
    public void SectionNameCaseSensitivityFollowsTheDocumentDialect()
    {
        const string source = "<Template><View /></Template>";

        var sqx = ComponentSectionScanner.Scan(source, "Case.sqx", ComponentDialect.Sqx, tolerant: false);
        var sqv = ComponentSectionScanner.Scan(source, "Case.sqv", ComponentDialect.Sqv, tolerant: false);

        Assert.Contains(sqx.Diagnostics, diagnostic => diagnostic.Kind == ComponentSectionDiagnosticKind.UnknownSection);
        Assert.Empty(sqv.Diagnostics);
        Assert.NotNull(sqv.Document.Template);
    }

    [Fact]
    public void TolerantScanRecoversFollowingSectionsAfterUnclosedTemplate()
    {
        const string source = "<template>\n  <View />\n" +
            "<script lang=\"csharp\">public int Count;</script>\n" +
            "<style>.page { gap: 8px; }</style>";

        var result = ComponentSectionScanner.Scan(
            source,
            "Recover.sqx",
            ComponentDialect.Sqx,
            tolerant: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ComponentSectionDiagnosticKind.UnclosedSection, diagnostic.Kind);
        Assert.False(result.Document.Template.IsClosed);
        Assert.Equal("\n  <View />\n", result.Document.Template.ContentText);
        Assert.NotNull(result.Document.Script);
        Assert.NotNull(result.Document.Style);
    }

    [Theory]
    [InlineData("<style></style>", "MissingTemplate", "<style>")]
    [InlineData("<template></template><template></template>", "DuplicateSection", "<template>")]
    [InlineData("<template></template><unknown></unknown>", "UnknownSection", "<unknown")]
    [InlineData("<template></template>outside", "UnexpectedContent", "outside")]
    [InlineData("<template><View />", "UnclosedSection", "<template>")]
    public void ScanReportsDocumentContractDiagnosticAtTheResponsibleToken(
        string source,
        string expectedKind,
        string marker)
    {
        var result = ComponentSectionScanner.Scan(
            source,
            "Broken.sqx",
            ComponentDialect.Sqx,
            tolerant: false);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(expectedKind, diagnostic.Kind.ToString());
        Assert.Equal(source.LastIndexOf(marker, StringComparison.Ordinal), diagnostic.Range.Offset);
    }

    private static void AssertRange(string source, SquareSourceRange range, string expected)
    {
        Assert.Equal(expected, source.Substring(range.Offset, range.Length));
    }
}
