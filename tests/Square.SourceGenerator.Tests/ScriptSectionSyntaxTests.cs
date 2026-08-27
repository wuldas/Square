using Square.Compiler.LanguageServices;
using Square.Compiler.Syntax;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class ScriptSectionSyntaxTests
{
    [Fact]
    public void ScriptMetadataPreservesValuesAndAbsoluteAttributeRanges()
    {
        const string source = "<template><View /></template>\r\n" +
            "<script lang=\"csharp\" namespace=\"App.Components\" name=\"Card\" access=\"internal\">\r\n" +
            "  public int Count;\r\n</script>";

        var result = ComponentSectionScanner.Scan(
            source,
            "Card.sqx",
            ComponentDialect.Sqx,
            tolerant: false);
        var metadata = result.Document.Script.Metadata;

        Assert.Empty(metadata.Diagnostics);
        Assert.Equal("csharp", metadata.Language);
        Assert.Equal("App.Components", metadata.Namespace);
        Assert.Equal("Card", metadata.ComponentName);
        Assert.Equal("internal", metadata.Access);
        Assert.Collection(
            metadata.Attributes,
            attribute => AssertAttribute(source, attribute, "lang", "csharp"),
            attribute => AssertAttribute(source, attribute, "namespace", "App.Components"),
            attribute => AssertAttribute(source, attribute, "name", "Card"),
            attribute => AssertAttribute(source, attribute, "access", "internal"));
    }

    [Fact]
    public void ScriptMetadataUsesStableDefaults()
    {
        const string source = "<template><View /></template><script></script>";

        var result = ComponentSectionScanner.Scan(
            source,
            "Defaults.sqv",
            ComponentDialect.Sqv,
            tolerant: false);
        var metadata = result.Document.Script.Metadata;

        Assert.Empty(metadata.Diagnostics);
        Assert.Equal("csharp", metadata.Language);
        Assert.Null(metadata.Namespace);
        Assert.Null(metadata.ComponentName);
        Assert.Equal("public", metadata.Access);
        Assert.Empty(metadata.Attributes);
    }

    [Fact]
    public void ScriptMetadataReportsInvalidValuesAndDuplicateOrUnknownAttributes()
    {
        const string source = "<template><View /></template>" +
            "<script lang=\"typescript\" namespace=\"Bad-Namespace\" name=\"9Card\" " +
            "access=\"private\" name=\"Other\" custom=\"value\"></script>";

        var result = ComponentSectionScanner.Scan(
            source,
            "Invalid.sqx",
            ComponentDialect.Sqx,
            tolerant: false);
        var diagnostics = result.Document.Script.Metadata.Diagnostics;
        var kinds = diagnostics.Select(diagnostic => diagnostic.Kind.ToString()).ToArray();

        Assert.Contains("UnsupportedLanguage", kinds);
        Assert.Contains("InvalidNamespace", kinds);
        Assert.Contains("InvalidComponentName", kinds);
        Assert.Contains("InvalidAccess", kinds);
        Assert.Contains("DuplicateAttribute", kinds);
        Assert.Contains("UnknownAttribute", kinds);
        Assert.Equal(
            "9Card",
            Slice(source, Assert.Single(diagnostics, diagnostic =>
                diagnostic.Kind.ToString() == "InvalidComponentName").Range));
    }

    [Fact]
    public void CSharpScriptSyntaxParsesUsingsAndMembersWithDocumentRanges()
    {
        const string source = "<template><View /></template>\r\n<script>\r\n" +
            "using System;\r\nusing Square.Controls;\r\n\r\n" +
            "private int _count;\r\n" +
            "[Prop(Required = true)]\r\npublic string 标题 { get; } = \"Card\";\r\n" +
            "private void OnSave() { }\r\n</script>";

        var result = ComponentSectionScanner.Scan(
            source,
            "ScriptAst.sqx",
            ComponentDialect.Sqx,
            tolerant: false);
        var script = result.Document.Script.CSharp;

        Assert.Empty(script.Diagnostics);
        Assert.Equal(new[] { "System", "Square.Controls" },
            script.Usings.Select(usingDirective => usingDirective.Name!.ToString()));
        Assert.Collection(
            script.Members,
            member => Assert.IsType<FieldDeclarationSyntax>(member),
            member => Assert.IsType<PropertyDeclarationSyntax>(member),
            member => Assert.IsType<MethodDeclarationSyntax>(member));
        Assert.StartsWith("private int _count;", script.BodyText.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("[Prop(Required = true)]", script.BodyText, StringComparison.Ordinal);

        var property = Assert.IsType<PropertyDeclarationSyntax>(script.Members[1]);
        var propertyRange = script.SourceMap.ToDocumentRange(property.Span);
        Assert.Contains("public string 标题", Slice(source, propertyRange), StringComparison.Ordinal);
        Assert.InRange(propertyRange.Offset, result.Document.Script.ContentRange.Offset, result.Document.Script.ContentRange.End);
    }

    [Fact]
    public void EmptyScriptProducesAnEmptyCSharpAst()
    {
        const string source = "<template><View /></template><script></script>";

        var result = ComponentSectionScanner.Scan(source, "Empty.sqx", ComponentDialect.Sqx, tolerant: false);
        var script = result.Document.Script.CSharp;

        Assert.Empty(script.Diagnostics);
        Assert.Empty(script.Usings);
        Assert.Empty(script.Members);
        Assert.Equal(string.Empty, script.BodyText);
    }

    [Fact]
    public void CSharpDiagnosticsMapToTheOriginalScriptWithoutWrapperText()
    {
        const string source = "<template><View /></template>\r\n<script>\r\n" +
            "public string 标题 { get; }\r\nprivate void Save( { }\r\n</script>";

        var result = ComponentSectionScanner.Scan(source, "BrokenScript.sqx", ComponentDialect.Sqx, tolerant: false);
        var scriptSection = result.Document.Script;
        var diagnostics = scriptSection.CSharp.Diagnostics;

        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.InRange(diagnostic.Range.Offset, scriptSection.ContentRange.Offset, scriptSection.ContentRange.End);
            Assert.InRange(diagnostic.Range.End, scriptSection.ContentRange.Offset, scriptSection.ContentRange.End);
            Assert.DoesNotContain("__SquareScriptSyntax", diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("__Component", diagnostic.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("sqx")]
    [InlineData("sqv")]
    public void SharedDocumentServiceCarriesTheSectionSyntaxTree(string extension)
    {
        const string source = "<template><View /></template>" +
            "<script namespace=\"App.Components\">using System; public int Count;</script>";

        var result = SquareDocumentService.ParseSyntaxTree(source, "Card." + extension);
        var document = Assert.IsType<Square.Compiler.Parser.SqxDocument>(result.ParsedSqxDocument);

        Assert.True(result.IsSuccess);
        Assert.NotNull(document.Syntax);
        Assert.Equal(extension == "sqx" ? ComponentDialect.Sqx : ComponentDialect.Sqv, document.Syntax.Dialect);
        Assert.Equal("App.Components", document.Syntax.Script.Metadata.Namespace);
        Assert.Single(document.Syntax.Script.CSharp.Usings);
        Assert.Single(document.Syntax.Script.CSharp.Members);
    }

    [Theory]
    [InlineData("sqx", "SQX0001")]
    [InlineData("sqv", "SQV0001")]
    public void SharedDocumentServiceReportsScriptMetadataValueRange(string extension, string expectedId)
    {
        const string source = "<template><View /></template>" +
            "<script namespace=\"Bad-Namespace\" name=\"9Card\"></script>";

        var result = SquareDocumentService.Parse(source, "Invalid." + extension);
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(expectedId, diagnostic.Id);
        Assert.Equal("Invalid script namespace 'Bad-Namespace'", diagnostic.Message);
        Assert.Equal(source.IndexOf("Bad-Namespace", StringComparison.Ordinal), diagnostic.Range.Offset);
        Assert.Equal("Bad-Namespace".Length, diagnostic.Range.Length);
    }

    [Theory]
    [InlineData("sqx", "SQX0001")]
    [InlineData("sqv", "SQV0001")]
    public void SharedDocumentServiceReportsCSharpSyntaxAtTheOriginalRange(string extension, string expectedId)
    {
        const string source = "<template><View /></template><script>private void Save( { }</script>";
        var scriptStart = source.IndexOf("private", StringComparison.Ordinal);
        var scriptEnd = source.IndexOf("</script>", StringComparison.Ordinal);

        var result = SquareDocumentService.Parse(source, "BrokenScript." + extension);
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(expectedId, diagnostic.Id);
        Assert.InRange(diagnostic.Range.Offset, scriptStart, scriptEnd);
        Assert.InRange(diagnostic.Range.End, scriptStart, scriptEnd);
        Assert.DoesNotContain("__SquareScriptSyntax", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("__Component", diagnostic.Message, StringComparison.Ordinal);
    }

    private static void AssertAttribute(
        string source,
        ScriptAttributeSyntax attribute,
        string expectedName,
        string expectedValue)
    {
        Assert.Equal(expectedName, attribute.Name);
        Assert.Equal(expectedValue, attribute.Value);
        Assert.Equal(expectedName, Slice(source, attribute.NameRange));
        Assert.Equal(expectedValue, Slice(source, attribute.ValueRange));
        Assert.Contains(expectedName + "=", Slice(source, attribute.FullRange), StringComparison.Ordinal);
    }

    private static string Slice(string source, SquareSourceRange range) =>
        source.Substring(range.Offset, range.Length);
}
