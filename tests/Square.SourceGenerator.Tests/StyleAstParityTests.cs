using Square.Compiler.Syntax;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Xunit;

namespace Square.Compiler.Tests;

public sealed class StyleAstParityTests
{
    [Theory]
    [InlineData(".page { color: red; gap: 8px !important; }")]
    [InlineData("Button:hover, .primary { background: rgb(1, 2, 3); opacity: 0.5; }")]
    [InlineData(".page { width: var(--size, calc(100% - 8px)); }")]
    public void CompilerAndRuntimeCssParsersAgreeOnDeclarations(string css)
    {
        var compiler = CssSyntaxParser.Parse(css, 0);
        var runtime = new CssParser(new CssTokenizer(css).Tokenize()).Parse();

        Assert.Empty(compiler.Diagnostics);
        var compilerDeclarations = compiler.Rules
            .SelectMany(rule => rule.Declarations)
            .Select(declaration => (declaration.Property, declaration.Value, declaration.Important))
            .Distinct()
            .OrderBy(item => item.Property)
            .ToArray();
        var runtimeDeclarations = runtime.Rules
            .SelectMany(rule => rule.Declarations)
            .Select(declaration => (declaration.Property, declaration.Value, declaration.Important))
            .Distinct()
            .OrderBy(item => item.Property)
            .ToArray();
        Assert.Equal(runtimeDeclarations, compilerDeclarations);
    }

    [Fact]
    public void CompilerAndRuntimeCssParsersAgreeOnSupportedAtRuleKinds()
    {
        const string css = "@import \"theme.css\";" +
            "@media screen { .page { gap: 8px; } }" +
            "@keyframes fade { from { opacity: 0; } to { opacity: 1; } }" +
            "@font-face { font-family: Demo; src: url(demo.woff2); }";

        var compiler = CssSyntaxParser.Parse(css, 0);
        var runtime = new CssParser(new CssTokenizer(css).Tokenize()).Parse();

        Assert.Empty(compiler.Diagnostics);
        Assert.Equal(runtime.Imports.Count, compiler.AtRules.Count(rule => rule.Name == "import"));
        Assert.Equal(runtime.MediaRules.Count, compiler.AtRules.Count(rule => rule.Name == "media"));
        Assert.Equal(runtime.KeyFrames.Count, compiler.AtRules.Count(rule => rule.Name == "keyframes"));
        Assert.Equal(
            runtime.AtRules.Single(rule => rule.Name == "font-face").Declarations.Count,
            compiler.AtRules.Single(rule => rule.Name == "font-face").Declarations.Count);
        Assert.Equal(
            runtime.MediaRules.Single().Rules.Single().Declarations.Single().Property,
            compiler.AtRules.Single(rule => rule.Name == "media").Rules.Single().Declarations.Single().Property);
        Assert.Equal(
            runtime.KeyFrames.Single().Stops.Count,
            compiler.AtRules.Single(rule => rule.Name == "keyframes").Rules.Count);
    }

    [Fact]
    public void CompilerAndRuntimeCssParsersAgreeOnStructuredSelectors()
    {
        const string css = "View > .item[data-kind^=\"x\" i]:hover + #next { color: red; }";

        var compiler = Assert.Single(Assert.Single(CssSyntaxParser.Parse(css, 0).Rules).Selectors);
        var runtime = Assert.Single(new CssParser(new CssTokenizer(css).Tokenize()).Parse().Rules).Selector;

        Assert.Equal(runtime.Steps.Count, compiler.Steps.Count);
        for (var stepIndex = 0; stepIndex < runtime.Steps.Count; stepIndex++)
        {
            var runtimeStep = runtime.Steps[stepIndex];
            var compilerStep = compiler.Steps[stepIndex];
            Assert.Equal(runtimeStep.Combinator.ToString(), compilerStep.Combinator.ToString());
            Assert.Equal(runtimeStep.Selector.Parts.Count, compilerStep.Parts.Count);
            for (var partIndex = 0; partIndex < runtimeStep.Selector.Parts.Count; partIndex++)
            {
                var runtimePart = runtimeStep.Selector.Parts[partIndex];
                var compilerPart = compilerStep.Parts[partIndex];
                Assert.Equal(runtimePart.Kind.ToString(), compilerPart.Kind.ToString());
                Assert.Equal(runtimePart.Name, compilerPart.Name);
                Assert.Equal(runtimePart.AttributeOperator.ToString(), compilerPart.AttributeOperator.ToString());
                Assert.Equal(runtimePart.AttributeValue, compilerPart.AttributeValue);
                Assert.Equal(runtimePart.AttributeCaseSensitivity.ToString(), compilerPart.AttributeCaseSensitivity.ToString());
            }
        }
    }
}
