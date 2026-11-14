using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler.Parser;
using Square.Compiler.Syntax;
using Square.Compiler.Template.Ir;
using Square.Compiler.Template.Lowering;
using Square.Runtime.Binding;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class SqvConditionalWhitespaceTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\n  ")]
    [InlineData("\r\n\t  ")]
    [InlineData("\t\f ")]
    [InlineData("<!-- branch -->")]
    [InlineData("\n  <!-- first -->\n  <!-- second -->\n  ")]
    public void ConditionalBranchesIgnoreWhitespaceAndComments(string separator)
    {
        const int baseOffset = 23;
        var source = "<Text v-if=\"First\">first</Text>" + separator +
            "<Text v-else-if=\"Second\">second</Text>" + separator +
            "<Text v-else-if=\"Third\">third</Text>" + separator +
            "<Text v-else>fallback</Text>";
        var syntax = SqvTemplateSyntaxParser.Parse(source, baseOffset, tolerant: false);

        var chain = Assert.IsType<TemplateIrIfChain>(Assert.Single(SqvTemplateLowerer.Lower(syntax).Roots));

        Assert.Equal(baseOffset, chain.Origin.Offset);
        Assert.Collection(chain.Branches,
            branch => AssertBranch(branch, "First", "first"),
            branch => AssertBranch(branch, "Second", "second"),
            branch => AssertBranch(branch, "Third", "third"),
            branch => AssertBranch(branch, null, "fallback"));
        Assert.Equal(new[] { "v-if=\"First\"", "v-else-if=\"Second\"", "v-else-if=\"Third\"", "v-else" },
            chain.Branches.Select(branch => source.Substring(branch.Origin.Offset - baseOffset, branch.Origin.Length)));
    }

    [Theory]
    [InlineData("View")]
    [InlineData("div")]
    public void IndentedConditionalChainLowersInsideNativeAndHtmlElements(string tagName)
    {
        var source = "<" + tagName + ">\n  <Text v-if=\"Ready\">yes</Text>\n  " +
            "<Text v-else>no</Text>\n  <Text>after</Text>\n</" + tagName + ">";

        var root = Assert.IsType<TemplateIrElement>(Assert.Single(Lower(source).Roots));

        Assert.Collection(root.Children,
            node => Assert.Equal(2, Assert.IsType<TemplateIrIfChain>(node).Branches.Count),
            node => Assert.Equal("after", Assert.IsType<TemplateIrText>(
                Assert.Single(Assert.IsType<TemplateIrElement>(node).Children)).Text));
    }

    [Theory]
    [InlineData("pre", false)]
    [InlineData("pre", true)]
    [InlineData("textarea", false)]
    [InlineData("textarea", true)]
    public void WhitespaceOutsideTheChainRemainsInPreformattedContent(string tagName, bool hasElse)
    {
        var source = "<" + tagName + "> \n<Text v-if=\"Ready\">yes</Text>" +
            (hasElse ? "\n  <Text v-else>no</Text>" : string.Empty) +
            "\r\n\t<Text>after</Text>\n  </" + tagName + ">";

        var root = Assert.IsType<TemplateIrElement>(Assert.Single(Lower(source).Roots));

        Assert.Collection(root.Children,
            node => Assert.Equal(" \n", Assert.IsType<TemplateIrText>(node).Text),
            node => Assert.Equal(hasElse ? 2 : 1, Assert.IsType<TemplateIrIfChain>(node).Branches.Count),
            node => Assert.Equal("\r\n\t", Assert.IsType<TemplateIrText>(node).Text),
            node => Assert.Equal("Text", Assert.IsType<TemplateIrElement>(node).TagName),
            node => Assert.Equal("\n  ", Assert.IsType<TemplateIrText>(node).Text));
    }

    [Theory]
    [InlineData("content")]
    [InlineData("\u3000")]
    [InlineData("\u00a0")]
    [InlineData("\u000b")]
    [InlineData("\n  \u3000\n  ")]
    [InlineData("\n  content\n  ")]
    [InlineData("\n  {{ Value }}\n  ")]
    [InlineData("\n  <Text />\n  ")]
    [InlineData("\n  <Text v-for=\"item in Items\">{{ item }}</Text>\n  ")]
    [InlineData("\n  <!-- comment -->content<!-- comment -->\n  ")]
    public void ConditionalChainDoesNotCrossContentOrOtherElements(string separator)
    {
        foreach (var directive in new[] { "v-else", "v-else-if=\"Other\"" })
        {
            var source = "<Text v-if=\"Ready\">yes</Text>" + separator + "<Text " + directive + ">no</Text>";

            AssertOrphanedBranch(source, directive);
        }
    }

    [Theory]
    [InlineData("\n  <!-- comment -->\n  ")]
    [InlineData("<View><Text v-if=\"Ready\" /></View>\n  ")]
    [InlineData("<Text v-if=\"Ready\" />\n  <Text v-else />\n  ")]
    [InlineData("<Text v-if=\"Ready\" /><Text v-else />")]
    public void OrphanedBranchesAndBranchesAfterElseStillReportSqv0004(string prefix)
    {
        foreach (var directive in new[] { "v-else", "v-else-if=\"Other\"" })
        {
            AssertOrphanedBranch(prefix + "<Text " + directive + " />", directive);
        }
    }

    [Theory]
    [InlineData("\u3000")]
    [InlineData("\u00a0")]
    [InlineData("\u000b")]
    [InlineData("\u3000content\u3000")]
    public void NativeTextTrimsOnlyTemplateWhitespace(string content)
    {
        var root = Assert.IsType<TemplateIrElement>(Assert.Single(Lower("<Text> \t" + content + "\r\n</Text>").Roots));

        Assert.Equal(content, Assert.IsType<TemplateIrText>(Assert.Single(root.Children)).Text);
    }

    [Theory]
    [InlineData("\u3000")]
    [InlineData("\u00a0")]
    [InlineData("\u000b")]
    public void HtmlTextRetainsSignificantUnicodeWhitespace(string content)
    {
        var text = " \t" + content + "\r\n";
        var root = Assert.IsType<TemplateIrElement>(Assert.Single(Lower("<div>" + text + "</div>").Roots));

        Assert.Equal(text, Assert.IsType<TemplateIrText>(Assert.Single(root.Children)).Text);
    }

    [Theory]
    [InlineData("<!-- one -->", 1, 13)]
    [InlineData("<!-- one\n  two -->", 2, 10)]
    [InlineData("<!-- one\r\n  two -->\r\n  ", 3, 3)]
    public void CommentsAdvanceFollowingTokenLineAndColumn(string comment, int line, int column)
    {
        var tokens = new SqvLexer(comment + "<Text v-else />").Tokenize();

        var openTag = Assert.Single(tokens, token => token.Type == SqvTokenType.OpenTag);
        Assert.Equal(comment.Length, openTag.Offset);
        Assert.Equal(line, openTag.Line);
        Assert.Equal(column, openTag.Column);
    }

    [Fact]
    public void OrphanedBranchAfterMultilineCommentReportsItsSourceLineAndColumn()
    {
        const string source = """
            <template>
              <!-- first
                   second -->
              <Text v-else />
            </template>
            """;
        var compilation = CreateCompilation();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("ConditionalWhitespace.sqv", source)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);

        var diagnostic = Assert.Single(driver.RunGenerators(compilation).GetRunResult().Diagnostics
            .Where(diagnostic => diagnostic.Id == "SQV0004"));

        Assert.Equal(new LinePosition(3, 8), diagnostic.Location.GetLineSpan().StartLinePosition);
        Assert.Equal("v-else", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    [Fact]
    public void WhitespaceDoesNotMergeIndependentConditionalChains()
    {
        const string source = """
            <Text v-if="First">first</Text>
            <Text v-else>first fallback</Text>

            <Text v-if="Second">second</Text>
            <Text v-else>second fallback</Text>
            """;

        Assert.Collection(Lower(source).Roots,
            node => Assert.Collection(Assert.IsType<TemplateIrIfChain>(node).Branches,
                branch => AssertBranch(branch, "First", "first"),
                branch => AssertBranch(branch, null, "first fallback")),
            node => Assert.Collection(Assert.IsType<TemplateIrIfChain>(node).Branches,
                branch => AssertBranch(branch, "Second", "second"),
                branch => AssertBranch(branch, null, "second fallback")));
    }

    [Fact]
    public void IndentedConditionalBranchesPreserveTheirEnclosingKeyedLoop()
    {
        const string source = """
            <View v-for="(item, index) in Items" :key="item.Id">
              <Text v-if="item.Active">active</Text>
              <Text v-else-if="item.Pending">pending</Text>
              <Text v-else>inactive</Text>
            </View>
            """;

        var loop = Assert.IsType<TemplateIrFor>(Assert.Single(Lower(source).Roots));

        Assert.Equal("Items", loop.SourceExpression);
        Assert.Equal("item", loop.ItemName);
        Assert.Equal("index", loop.IndexName);
        Assert.Equal("item.Id", loop.KeyExpression);
        var view = Assert.IsType<TemplateIrElement>(Assert.Single(loop.Children));
        Assert.Empty(view.Attributes);
        var chain = Assert.IsType<TemplateIrIfChain>(Assert.Single(view.Children));
        Assert.Collection(chain.Branches,
            branch => AssertBranch(branch, "item.Active", "active"),
            branch => AssertBranch(branch, "item.Pending", "pending"),
            branch => AssertBranch(branch, null, "inactive"));
    }

    [Fact]
    public void IndentedSqvConditionalChainsGenerateCompilableComponents()
    {
        const string source = """
            <template>
              <View>
                <Text v-if="Visible">shown</Text>
                <!-- choose a fallback -->
                <Text v-else-if="Pending">pending</Text>
                <Text v-else>hidden</Text>
                <View v-for="(item, index) in Items" :key="item">
                  <Text v-if="item.Length > 0">{{ item }}</Text>
                  <Text v-else>empty</Text>
                </View>
              </View>
            </template>
            <script namespace="TestApp">
              public bool Visible = true;
              public bool Pending = false;
              public ObservableCollection<string> Items = new();
            </script>
            """;
        var compilation = CreateCompilation();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("ConditionalWhitespace.sqv", source)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.NotNull(output.GetTypeByMetadataName("TestApp.ConditionalWhitespace"));
    }

    [Theory]
    [InlineData("\u3000", "\u3000")]
    [InlineData("\u00a0", "\u00a0")]
    [InlineData("\u3000text", "\u3000text")]
    [InlineData("\u00a0text", "\u00a0text")]
    [InlineData(" \t\u3000text\u00a0\r\n", "\u3000text\u00a0")]
    [InlineData(" \ttext\r\n", "text")]
    public void GeneratedTextContentAndTextNodesRetainSignificantWhitespace(string content, string nativeExpected)
    {
        var source = "<template><View><Text>" + content + "</Text><View>" + content +
            "</View><div>" + content + "</div></View></template><script namespace=\"TestApp\"></script>";

        var nodes = GenerateComponentNodes(source);

        var textContent = Assert.Single(nodes.OfType<InvocationExpressionSyntax>(), invocation =>
            invocation.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "SetProperty" &&
            invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax property &&
            property.Token.ValueText == "TextContent");
        Assert.Equal(nativeExpected,
            Assert.IsType<LiteralExpressionSyntax>(textContent.ArgumentList.Arguments[1].Expression).Token.ValueText);
        var nativeText = Assert.Single(nodes.OfType<ObjectCreationExpressionSyntax>(), creation =>
            creation.Type.ToString() == "global::Square.Controls.Text" && creation.ArgumentList?.Arguments.Count == 1);
        Assert.Equal(nativeExpected,
            Assert.IsType<LiteralExpressionSyntax>(nativeText.ArgumentList!.Arguments[0].Expression).Token.ValueText);
        var htmlText = Assert.Single(nodes.OfType<ObjectCreationExpressionSyntax>(), creation =>
            creation.Type.ToString() == "global::Square.UI.Text" && creation.ArgumentList?.Arguments.Count == 1);
        Assert.Equal(content,
            Assert.IsType<LiteralExpressionSyntax>(htmlText.ArgumentList!.Arguments[0].Expression).Token.ValueText);
    }

    [Fact]
    public void GeneratedComponentsOmitIndentationOnlyText()
    {
        const string source = "<template><View> \t\r\n\f<Text> \t\r\n\f</Text><div> \t\r\n\f</div></View></template>" +
            "<script namespace=\"TestApp\"></script>";

        var nodes = GenerateComponentNodes(source);

        Assert.DoesNotContain(nodes.OfType<InvocationExpressionSyntax>(), invocation =>
            invocation.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "SetProperty" &&
            invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax property &&
            property.Token.ValueText == "TextContent");
        Assert.DoesNotContain(nodes.OfType<ObjectCreationExpressionSyntax>(), creation =>
            (creation.Type.ToString() is "global::Square.Controls.Text" or "global::Square.UI.Text") &&
            creation.ArgumentList?.Arguments.Count == 1);
    }

    private static SyntaxNode[] GenerateComponentNodes(string source)
    {
        var compilation = CreateCompilation();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("ConditionalWhitespace.sqv", source)],
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.NotNull(output.GetTypeByMetadataName("TestApp.ConditionalWhitespace"));
        return driver.GetRunResult().GeneratedTrees.SelectMany(tree => tree.GetRoot().DescendantNodes()).ToArray();
    }

    private static CSharpCompilation CreateCompilation()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var references = (trustedPlatformAssemblies ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
        if (references.All(reference => reference.Display != typeof(PropAttribute).Assembly.Location))
            references.Add(MetadataReference.CreateFromFile(typeof(PropAttribute).Assembly.Location));
        return CSharpCompilation.Create(
            "ConditionalWhitespaceTests",
            [CSharpSyntaxTree.ParseText("public sealed class Placeholder { }")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static TemplateIrDocument Lower(string source) =>
        SqvTemplateLowerer.Lower(SqvTemplateSyntaxParser.Parse(source, tolerant: false));

    private static void AssertBranch(TemplateIrIfBranch branch, string? condition, string text)
    {
        Assert.Equal(condition, branch.Condition);
        Assert.Equal(condition == null, branch.IsElse);
        var element = Assert.IsType<TemplateIrElement>(Assert.Single(branch.Children));
        Assert.Equal("Text", element.TagName);
        Assert.Empty(element.Attributes);
        Assert.Equal(text, Assert.IsType<TemplateIrText>(Assert.Single(element.Children)).Text);
    }

    private static void AssertOrphanedBranch(string source, string directive)
    {
        const int baseOffset = 17;
        var syntax = SqvTemplateSyntaxParser.Parse(source, baseOffset, tolerant: false);

        var exception = Assert.Throws<SqxParseException>(() => SqvTemplateLowerer.Lower(syntax));

        Assert.Equal("SQV0004", exception.DiagnosticId);
        Assert.Equal(baseOffset + source.LastIndexOf(directive, StringComparison.Ordinal), exception.Position);
        Assert.Equal(directive.StartsWith("v-else-if", StringComparison.Ordinal) ? 9 : 6, exception.Length);
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(content);
    }
}
