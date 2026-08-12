using Square.Compiler.LanguageServices;
using Square.Compiler.Parser;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class LanguageServiceRecoveryTests
{
    [Fact]
    public void TolerantSqxParseKeepsCompletedRootWhenFollowingElementIsUnclosed()
    {
        const string source = "<template><View /><Panel><Text /></template>";

        var result = SquareDocumentService.ParseTolerant(source, "Editing.sqx");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SQX0001", diagnostic.Id);
        Assert.False(result.IsSuccess);

        var document = Assert.IsType<SqxDocument>(result.ParsedSqxDocument);
        Assert.Collection(
            document.Template.Roots,
            root => Assert.Equal("View", Assert.IsType<SqxElement>(root).TagName),
            root => Assert.Equal("Panel", Assert.IsType<SqxElement>(root).TagName));
    }

    [Fact]
    public void StrictSqxParseStillRejectsTheSameUnclosedElement()
    {
        const string source = "<template><View /><Panel><Text /></template>";

        var result = SquareDocumentService.Parse(source, "Strict.sqx");

        Assert.False(result.IsSuccess);
        Assert.Null(result.ParsedSqxDocument);
        Assert.Equal("SQX0001", Assert.Single(result.Diagnostics).Id);
    }

    [Fact]
    public void TolerantSqvParseKeepsSyntaxDiagnosticWithoutFabricatingAst()
    {
        const string source = "<template><View>{{ Title</View></template>";

        var result = SquareDocumentService.ParseTolerant(source, "Editing.sqv");

        Assert.False(result.IsSuccess);
        Assert.Equal("SQV0001", Assert.Single(result.Diagnostics).Id);
        Assert.Null(result.ParsedSqxDocument);
    }
}
